# SRR-Guided Volume Assembly Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

Rev 3 — codex plan-review rev-1 (8B/3A) and rev-2 (7B/2A) folded in.

**Goal:** Reconstruct byte-perfect RAR sets on any host by splicing SRR-stored headers with the brute-forced rar output's packed stream, replacing in-place header patching whenever an SRR is available.

**Architecture:** `SRRReconstructor` gains an internal packed-byte source seam (`IPackedSource`) and a typed result; a preflight API — run at the top of `ReconstructAsync` and once per set by `Manager` before the candidate loops — declines SRRs whose required payloads are stripped. `Manager` swaps patch+hash for assemble+hash (CAV and non-CAV variants) and finalizes the reconstructor's ordered `WrittenPaths` through a dedicated transactional finalizer. A producer-runner seam makes the candidate loop testable without a rar binary.

**Tech Stack:** .NET 10, xUnit (`ReScene.Lib/ReScene.Tests`, net10.0). No new dependencies.

**Spec:** `docs/superpowers/specs/2026-07-28-srr-guided-assembly-design.md` (rev 5, codex-APPROVED). Normative; where plan and spec disagree, the spec wins and the discrepancy must be raised.

## Global Constraints

- One top-level type per file; `.editorconfig` + `dotnet format` clean.
- ALL new lib types are **internal** (spec §1; codex plan A1) — the test project already has InternalsVisibleTo (it uses `NullReSceneLogger`, `SRRTestDataBuilder` today).
- Forced-rebuild gate at every task end: `dotnet build ReScene.Manager.slnx -c Debug -t:Rebuild -p:BaseOutputPath=bin2/` = 0W/0E, delete `bin2` after. Tests: `-p:BaseOutputPath=bin3/`, delete after (user's IDE locks default outputs).
- `SRRBlockFlags.RecoveryBlocksRemoved` NEVER gates behavior (spec §2).
- Preflight: once per set, BEFORE the candidate loops, AND at the top of `ReconstructAsync` before any directory/file creation (spec §2; codex plan B5). Three caller branches: `Success` → assembly; `UnsupportedSrr` → legacy for the set; `Error` → set failure (codex plan B3).
- Producer observation is an invariant before ANY finalization/cleanup, including non-CAV success (spec §4).
- Verification and finalization consume the FULL assembly result's ordered `WrittenPaths` — never the quick gate's single-volume result (codex plan B1).
- `RenameToOriginalNames=false` naming: basename replacement preserving the COMPLETE volume suffix via `RARVolumeNaming.GetBaseName`; never `Path.GetExtension` (spec §5).
- On Manager calls the reconstructor's verification is a no-op (empty `hashes`); `VerificationFailed` is unreachable there (spec §4).
- Real test-support APIs (codex plan B6): `SRRTestDataBuilder()` (parameterless) / `.AddSRRHeader(appName)` / `.Build()` / `.BuildToFile(directory, fileName)`; `RAR4HeaderBuilder(BinaryWriter)` (no marker method — write `RARUtils.RAR4Marker` bytes directly); tests inherit `TempDirTestBase` (property `TempDir`); logger `new NullReSceneLogger()`.
- Commit after every task, session trailer as used throughout this branch.

---

### Task 1: Typed result + `IPackedSource` seam (behavior-preserving refactor)

**Files:**
- Create: `ReScene.Lib/ReScene/Core/SRRReconstructionStatus.cs`
- Create: `ReScene.Lib/ReScene/Core/SRRReconstructionResult.cs`
- Create: `ReScene.Lib/ReScene/Core/IO/IPackedSource.cs`
- Create: `ReScene.Lib/ReScene/Core/IO/ReleaseFilePackedSource.cs`
- Modify: `ReScene.Lib/ReScene/Core/SRRReconstructor.cs`
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs:236-260` (custom-packer call site)
- Test: `ReScene.Lib/ReScene.Tests/SRRReconstructorTests.cs` (update call sites; add Unicode+LARGE test)

**Interfaces — Produces (exact shapes later tasks rely on):**

```csharp
// SRRReconstructionStatus.cs
namespace ReScene.Core;

/// <summary>Outcome of an SRR-guided reconstruction or preflight.</summary>
internal enum SRRReconstructionStatus
{
    /// <summary>All requested volumes written (and verified, where CRCs were supplied).</summary>
    Success,
    /// <summary>Preflight declined: a required payload is not present in the SRR.</summary>
    UnsupportedSrr,
    /// <summary>The packed source ended before the last requested ADD_SIZE byte.</summary>
    SourceExhausted,
    /// <summary>Volumes written but hash comparison failed (custom-packer path only —
    /// unreachable on Manager assembly calls, which pass no hashes).</summary>
    VerificationFailed,
    /// <summary>I/O or parse failure (includes source-open failures such as
    /// RARStream's ArgumentException when no target header is visible).</summary>
    Error,
}
```

```csharp
// SRRReconstructionResult.cs
namespace ReScene.Core;

/// <summary>Typed result of <see cref="SRRReconstructor"/> operations.</summary>
internal sealed record SRRReconstructionResult(
    SRRReconstructionStatus Status,
    IReadOnlyList<string> WrittenPaths,
    string? Diagnostic)
{
    public static SRRReconstructionResult Ok(IReadOnlyList<string> written) =>
        new(SRRReconstructionStatus.Success, written, null);

    public static SRRReconstructionResult Fail(SRRReconstructionStatus status, string diagnostic,
        IReadOnlyList<string>? written = null) =>
        new(status, written ?? [], diagnostic);
}
```

```csharp
// IPackedSource.cs
namespace ReScene.Core.IO;

/// <summary>
/// Supplies one archived file's packed byte stream to <see cref="SRRReconstructor"/>.
/// Called once per archived file, in SRR order; the returned stream is positioned at
/// the file's packed byte 0 and is disposed by the reconstructor after the file's last
/// split piece is copied.
/// </summary>
internal interface IPackedSource : IDisposable
{
    Stream OpenPackedStream(string archivedFileName);
}
```

```csharp
// ReleaseFilePackedSource.cs
namespace ReScene.Core.IO;

/// <summary>
/// Custom-packer data source: the archived file's bytes ARE its packed bytes (store
/// method), read from the release input directory. Extracted verbatim from the
/// pre-seam <see cref="SRRReconstructor"/> source handling.
/// </summary>
internal sealed class ReleaseFilePackedSource(string inputDirectory) : IPackedSource
{
    public Stream OpenPackedStream(string archivedFileName)
    {
        string sourcePath = SRRReconstructor.FindSourceFile(inputDirectory, archivedFileName);
        return new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public void Dispose()
    {
        // Stateless: streams are owned and disposed by the reconstructor.
    }
}
```

New reconstructor signature — `releaseDirectoryForProgress` is retained solely for
`FireProgress`'s `ReleaseDirectoryPath` event field, which today receives the input
directory; substituting the output directory would misreport the release being
reconstructed (codex plan B4):

```csharp
public async Task<SRRReconstructionResult> ReconstructAsync(
    string srrFilePath,
    IPackedSource packedSource,
    string releaseDirectoryForProgress,
    string outputDirectory,
    IReadOnlyList<string> originalRARFileNames,
    HashSet<string> hashes,
    HashType hashType,
    CancellationToken cancellationToken)
```

- [ ] **Step 1: Create the four files** exactly as above.

- [ ] **Step 2: Refactor `ReconstructAsync`.**
  - Local `FileStream? currentSourceStream` becomes `Stream? currentSourceStream`
    (codex plan B4 — the seam returns `Stream`).
  - Source open (was `FindSourceFile` + `new FileStream(...)`):
    `currentSourceStream = packedSource.OpenPackedStream(archivedFileName);`
  - Archived-name decode (keep the nameSize/nameOffset arithmetic including the LARGE
    branch; replace only the decode):

```csharp
byte[] nameBytes = new byte[nameSize];
Array.Copy(fullHeader, nameOffset, nameBytes, 0, nameSize);
archivedFileName = RARUtils.DecodeFileName(nameBytes,
    ((RARFileFlags)flags).HasFlag(RARFileFlags.Unicode));
archivedFileName = archivedFileName.Replace('\\', Path.DirectorySeparatorChar);
```

  - `FireProgress` keeps receiving `releaseDirectoryForProgress` where it received
    `inputDirectory`.
  - Failure normalization (codex plan B2 — EVERY expected source-open/read failure
    becomes a typed status; only cancellation propagates). Wrap the block walk:

```csharp
try
{
    // …existing walk…
}
catch (OperationCanceledException) { throw; }
catch (EndOfStreamException ex)
{
    return SRRReconstructionResult.Fail(SRRReconstructionStatus.SourceExhausted, ex.Message, writtenPaths);
}
catch (Exception ex) when (ex is IOException or InvalidDataException
    or ArgumentException or FileNotFoundException or UnauthorizedAccessException)
{
    // ArgumentException: RARStream throws it when the produced snapshot has no visible
    // target header or does not start at volume 1 — during a live producer this is the
    // incomplete-snapshot shape the Manager retries (spec §4); after completion it is a
    // real Error.
    return SRRReconstructionResult.Fail(SRRReconstructionStatus.Error, ex.Message, writtenPaths);
}
```

  - Return mapping at the tail: `success` → `Ok(writtenPaths)`; written-but-mismatched
    → `Fail(VerificationFailed, existing log text, writtenPaths)`; everything else →
    `Fail(Error, existing log text, writtenPaths)`.

- [ ] **Step 3: Manager custom-packer call site:**

```csharp
using var packedSource = new ReleaseFilePackedSource(options.ReleaseDirectoryPath);
SRRReconstructionResult reconResult = await reconstructor.ReconstructAsync(
    options.RAROptions.SRRFilePath,
    packedSource,
    options.ReleaseDirectoryPath,
    options.OutputDirectoryPath,
    options.RAROptions.OriginalRARFileNames,
    options.Hashes,
    options.HashType,
    _cts.Token).ConfigureAwait(false);
bool result = reconResult.Status == SRRReconstructionStatus.Success;
IReadOnlyList<string> writtenPaths = reconResult.WrittenPaths;
if (!result && reconResult.Diagnostic is { } diag)
{
    _logger.Warning(this, $"Direct SRR reconstruction failed ({reconResult.Status}): {diag}", LogTarget.System);
}
```

- [ ] **Step 4: Update existing `SRRReconstructorTests` call sites** — wrap the input
directory as `new ReleaseFilePackedSource(inputDirectory)`, pass it also as
`releaseDirectoryForProgress`, assert `result.Status == SRRReconstructionStatus.Success`
and `result.WrittenPaths` where the tuple was asserted. Do not weaken assertions. Add
one progress-regression assertion to an existing multi-volume test: subscribe to
`reconstructor.Progress` and assert the event's `ReleaseDirectoryPath` equals the
input directory (pins codex plan B4).

- [ ] **Step 5: Failing Unicode+LARGE test** (spec Testing 12 requires the
`LHD_UNICODE + LARGE` offset combination). First add to `RAR4HeaderBuilder`:

```csharp
/// <summary>
/// File header carrying BOTH RARFileFlags.Unicode and RARFileFlags.Large: name field is
/// "<ansi>\0<encoded>" (RAR unicode name format), preceded by the 8-byte
/// HIGH_PACK/HIGH_UNP pair. The builder round-trips the emitted name bytes through
/// RARUtils.DecodeFileName and throws if the decode does not equal
/// <paramref name="fileName"/> — the fixture can never drift from the decoder.
/// </summary>
public RAR4HeaderBuilder AddUnicodeLargeFileHeader(
    string fileName, ulong packedSize, ulong unpackedSize, uint fileCRC = 0)
```

Implementation: name bytes = ASCII-lossy(fileName) + `0x00` + the RAR unicode stream
using MODE 2 FOR EVERY CHARACTER (codex plan-rev-2 A1 — one fixed strategy, no
choices): first byte after the NUL is the high-byte page (`(byte)(name[0] >> 8)` — use
0 and emit full 16-bit chars), then for each character an opcode pair encoding mode 2
(both bytes explicit: low byte, high byte). Concretely: flags stream where every
2-bit opcode = `10` (binary) and the data stream carries `low, high` per char — the
exact bit-packing is defined by `RARUtils.DecodeFileName`'s mode-2 branch; transcribe
the inverse of THAT branch. The builder round-trips the emitted bytes through
`RARUtils.DecodeFileName` and throws on mismatch, so fidelity is enforced by the
production decoder. Header layout identical to `AddFileHeaderWithLargeSize` plus the
Unicode flag and composite name field.

Then the test:

```csharp
[Fact]
public async Task ReconstructAsync_UnicodeLargeName_ResolvesThroughTheSeam()
{
    string name = "náme\u00e9.bin";
    byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8];

    var srrBuilder = new SRRTestDataBuilder().AddSRRHeader("ReScene.Lib");
    srrBuilder.AddRARFileWithHeaders("u.rar", h =>
    {
        h.AddArchiveHeader();
        h.AddUnicodeLargeFileHeader(name, (ulong)payload.Length, (ulong)payload.Length);
        h.AddEndArchive();
    });
    string srr = srrBuilder.BuildToFile(TempDir, "u.srr");

    var recorder = new RecordingPackedSource(payload);
    var reconstructor = new SRRReconstructor(new NullReSceneLogger());
    SRRReconstructionResult result = await reconstructor.ReconstructAsync(
        srr, recorder, TempDir, Path.Combine(TempDir, "out"),
        ["u.rar"], [], HashType.CRC32, CancellationToken.None);

    Assert.Equal(SRRReconstructionStatus.Success, result.Status);
    Assert.Equal(name, recorder.RequestedName); // the DECODED unicode name, not the ANSI fallback
}

private sealed class RecordingPackedSource(byte[] payload) : IPackedSource
{
    public string? RequestedName { get; private set; }
    public Stream OpenPackedStream(string archivedFileName)
    {
        RequestedName = archivedFileName;
        return new MemoryStream(payload);
    }
    public void Dispose() { }
}
```

Note: `AddRARFileWithHeaders` writes NO marker today and the reconstructor emits marker
blocks from embedded `Marker` entries; the SRR walk tolerates their absence for this
seam test (assembled-output byte identity is Task 5's concern, with markers). If the
walk requires a marker, prepend `_writer.Write(RARUtils.RAR4Marker)` support via a new
`RAR4HeaderBuilder.AddMarker()` (3-line method writing the 7 marker bytes) and call it
first — add that method in this task if needed.

- [ ] **Step 6: Red check** — temporarily restore the ASCII decode line, run the new
test, expect FAIL with `RequestedName` equal to the ANSI fallback; grep-confirm the
restore of `DecodeFileName` afterwards (restore-hygiene: re-hash before/after).

- [ ] **Step 7: Full lib suite green; rebuild gate; commit** `feat(lib): IPackedSource seam + typed result in SRRReconstructor`.

---

### Task 2: Preflight API + stripped-payload guard

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/SRRReconstructor.cs`
- Modify: `ReScene.Lib/ReScene.Tests/SRRTestDataBuilder.cs` (flags overload)
- Modify: `ReScene.Lib/ReScene.Tests/RAR4HeaderBuilder.cs` (`AddProtectBlock`, `AddServiceBlock`)
- Test: `ReScene.Lib/ReScene.Tests/SRRPreflightTests.cs` (new)

**Interfaces — Produces:**
`internal SRRReconstructionResult PreflightSet(string srrFilePath, IReadOnlyList<string> originalRARFileNames)` — `Success` (empty `WrittenPaths`) when assemblable; `Fail(UnsupportedSrr, reason)` on evidence; `Fail(Error, reason)` on unreadable/malformed SRR. Callers branch three ways (Global Constraints).

**No shared block iterator** (codex plan B5): stream advancement differs per block
class (SRR bookkeeping payload stored; file packed data absent; CMT payload stored;
stripped service/old payload declared-but-absent), so a generic iterator would need
undocumented position-ownership rules. Instead `PreflightSet` is its own sequential
read-only walk that ADVANCES EXPLICITLY per class, mirroring `ReconstructAsync`'s
seek rules, with this comment contract at both walks: *"These two walks must stay
seek-rule-identical; change one, change both (SRRPreflightTests pins the pairs)."*

Walk rules (complete):
- SRR block types (0x69/0x6A/0x6B/0x6C/0x71): seek `blockStart + headerSize + addSize`
  (addSize read only when LONG_BLOCK or StoredFile — same discrimination as
  `ReconstructAsync` lines 92-106). RARFile (0x71) additionally reads the name for set
  membership (Task 5 filter; until then every section is "selected").
- Embedded `Marker`/`ArchiveHeader`/`FileHeader`/`EndArchive`: seek
  `blockStart + headerSize` (file packed data is EXTERNAL — never in the SRR;
  FileHeader's ADD_SIZE is not an SRR seek distance).
- Embedded `Service` (0x7A): read the name (same offset arithmetic as file headers);
  if name == "CMT": payload IS stored → seek `blockStart + headerSize + addSize`;
  else: stripped → decline evidence when `addSize > 0`.
- Embedded other data-bearing old-style blocks (`default` with LONG_BLOCK):
  declared payload is stripped → decline when `addSize > 0`.

Decline evidence, in detection order (first hit wins, diagnostic names it):
1. embedded ArchiveHeader with `RARArchiveFlags.Protected` → `"recovery record (protected archive)"`;
2. embedded block type `RAR4BlockType.Protect` (0x78) → `"old-style recovery block"`;
3. embedded Service named `"RR"` → `"recovery record service block"`;
4. embedded Service, name ≠ `"CMT"`, addSize > 0 → `"stripped {name} service data"`;
5. embedded non-file old-style data-bearing block → `"stripped block 0x{type:X2} data"`.

- [ ] **Step 1: Builder support.**
`AddRARFileWithHeaders(string rarFileName, Action<RAR4HeaderBuilder> buildHeaders)`
gains overload `AddRARFileWithHeaders(string rarFileName, ushort flags, Action<…>)`
writing `flags` into the SRR block's flags word (existing overload delegates with 0).

```csharp
/// <summary>Old-style recovery block (0x78): base header + LONG_BLOCK ADD_SIZE, data ABSENT
/// (SRR-stripped shape).</summary>
public RAR4HeaderBuilder AddProtectBlock(uint declaredDataSize)
{
    byte[] header = new byte[11];
    header[2] = (byte)RAR4BlockType.Protect;
    BitConverter.GetBytes((ushort)RARFileFlags.LongBlock).CopyTo(header, 3);
    BitConverter.GetBytes((ushort)11).CopyTo(header, 5);
    BitConverter.GetBytes(declaredDataSize).CopyTo(header, 7);
    WriteCrc(header);           // same CRC16 helper the other emitters use
    _writer.Write(header);
    return this;
}

/// <summary>RAR4 service block (0x7A, file-header layout) named e.g. "RR"/"AV"/"CMT";
/// includeData=false emits the SRR-stripped shape (header declares addSize, data absent).</summary>
public RAR4HeaderBuilder AddServiceBlock(string name, uint declaredDataSize, bool includeData)
```

`AddServiceBlock` reuses the file-header layout emission — extract the shared private
`WriteFileShapedHeader(byte blockType, string name, uint addSize, RARFileFlags extra)`
from `AddCmtServiceBlock`'s body rather than duplicating; `includeData: true` then
writes `declaredDataSize` deterministic bytes (`(byte)(i % 251)`).

- [ ] **Step 2: Failing tests (complete bodies):**

```csharp
public class SRRPreflightTests : TempDirTestBase
{
    private static SRRReconstructor NewReconstructor() => new(new NullReSceneLogger());

    private string BuildSrr(ushort sectionFlags, Action<RAR4HeaderBuilder> headers) =>
        new SRRTestDataBuilder().AddSRRHeader("t")
            .AddRARFileWithHeaders("a.rar", sectionFlags, headers)
            .BuildToFile(TempDir, "t.srr");

    [Fact]
    public void FlagOnlyRecoveryRemoved_IsEligible()
    {
        // The real-world default shape: every writer sets the flag, no RR exists.
        string srr = BuildSrr((ushort)SRRBlockFlags.RecoveryBlocksRemoved, h => h
            .AddArchiveHeader()
            .AddFileHeader("a.bin", packedSize: 8, unpackedSize: 8)
            .AddEndArchive());
        SRRReconstructionResult r = NewReconstructor().PreflightSet(srr, ["a.rar"]);
        Assert.Equal(SRRReconstructionStatus.Success, r.Status);
    }

    [Theory]
    [InlineData("protected", "recovery record")]
    [InlineData("protect78", "old-style recovery")]
    [InlineData("rrService", "RR")]
    [InlineData("avStripped", "AV")]
    public void RealEvidence_Declines_WithNamedDiagnostic(string shape, string expectInDiag)
    {
        string srr = BuildSrr(0, h =>
        {
            switch (shape)
            {
                case "protected": h.AddArchiveHeader(RARArchiveFlags.Protected); break;
                case "protect78": h.AddArchiveHeader().AddProtectBlock(64); break;
                case "rrService": h.AddArchiveHeader().AddServiceBlock("RR", 64, includeData: false); break;
                case "avStripped": h.AddArchiveHeader().AddServiceBlock("AV", 16, includeData: false); break;
            }
            h.AddEndArchive();
        });
        SRRReconstructionResult r = NewReconstructor().PreflightSet(srr, ["a.rar"]);
        Assert.Equal(SRRReconstructionStatus.UnsupportedSrr, r.Status);
        Assert.Contains(expectInDiag, r.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CmtWithPayload_IsEligible()
    {
        string srr = BuildSrr(0, h => h
            .AddArchiveHeader()
            .AddCmtServiceBlock("release notes")   // existing emitter, payload stored
            .AddFileHeader("a.bin", 8, 8)
            .AddEndArchive());
        Assert.Equal(SRRReconstructionStatus.Success,
            NewReconstructor().PreflightSet(srr, ["a.rar"]).Status);
    }

    [Fact]
    public void MalformedSrr_IsError_NotUnsupported()
    {
        string srr = Path.Combine(TempDir, "bad.srr");
        File.WriteAllBytes(srr, [0x01, 0x02, 0x03]);
        Assert.Equal(SRRReconstructionStatus.Error,
            NewReconstructor().PreflightSet(srr, ["a.rar"]).Status);
    }

    [Fact]
    public async Task ReconstructAsync_DeclinedSrr_CreatesNoOutput()
    {
        // The guard must fire before Directory.CreateDirectory (codex plan B5).
        string srr = BuildSrr(0, h => h.AddArchiveHeader(RARArchiveFlags.Protected).AddEndArchive());
        string outDir = Path.Combine(TempDir, "out");
        SRRReconstructionResult r = await NewReconstructor().ReconstructAsync(
            srr, new RecordingNoopSource(), TempDir, outDir, ["a.rar"], [], HashType.CRC32,
            CancellationToken.None);
        Assert.Equal(SRRReconstructionStatus.UnsupportedSrr, r.Status);
        Assert.False(Directory.Exists(outDir));
    }

    private sealed class RecordingNoopSource : IPackedSource
    {
        public Stream OpenPackedStream(string archivedFileName) => new MemoryStream();
        public void Dispose() { }
    }
}
```

(Adjust `AddFileHeader` argument list to the builder's real signature when writing —
name/packed/unpacked are its leading parameters.)

- [ ] **Step 3: Run — FAIL (`PreflightSet` missing) → implement per the walk rules → PASS.**
`ReconstructAsync` calls `PreflightSet` FIRST and returns its failure verbatim before
`Directory.CreateDirectory` (codex plan B5). Its own walk keeps the same per-class
seeks; the Service/default branches now write CMT payload from the SRR and — for the
already-declined stripped shapes — are unreachable (guarded), with a
`Debug.Fail`-style log if ever hit.

- [ ] **Step 4: Full lib suite; rebuild gate; commit** `feat(lib): SRR assembly preflight + stripped-payload guard`.

---

### Task 3: Shared assembly fixture infrastructure

**Files:**
- Create: `ReScene.Lib/ReScene.Tests/AssemblyFixture.cs` (result record)
- Create: `ReScene.Lib/ReScene.Tests/AssemblyFixtureBuilder.cs` (the workhorse)
- Modify: `ReScene.Lib/ReScene.Tests/RAR4HeaderBuilder.cs` (mtime remainder; `AddMarker` if not added in T1)
- Test: `ReScene.Lib/ReScene.Tests/AssemblyFixtureBuilderTests.cs` (self-checks)

**Interfaces — Produces (every later test consumes this):**

```csharp
// AssemblyFixture.cs
namespace ReScene.Tests;

/// <summary>A complete synthetic reconstruction scenario on disk.</summary>
internal sealed record AssemblyFixture(
    string SrrPath,
    IReadOnlyList<string> OriginalVolumePaths,   // byte-identity reference
    IReadOnlyList<string> OriginalVolumeNames,   // set selector (qualified where built so)
    string ProducedFirstVolumePath,              // the "rar output" carrier set
    IReadOnlyDictionary<string, string> ExpectedVolumeCrcs); // name -> CRC32 (as Manager expects)
```

```csharp
// AssemblyFixtureBuilder.cs — core (complete algorithm; helper bodies follow the
// header-emission patterns already in RAR4HeaderBuilder):
internal static class AssemblyFixtureBuilder
{
    /// <summary>
    /// Builds, under <paramref name="dir"/>:
    ///  originals/  — volume set with the ORIGINAL header shape
    ///  produced/   — volume set with the PRODUCED header shape, SAME packed payload,
    ///                re-split so each volume's total size equals the original's
    ///  the SRR     — SRR header + one RARFile section per original volume embedding
    ///                the original headers verbatim (real-world section flags:
    ///                RecoveryBlocksRemoved)
    /// Every volume is a real parseable RAR4 file: marker, archive header, file
    /// header(s) with SplitBefore/SplitAfter as needed, payload bytes, end block
    /// (EndArchive ADD_SIZE-free; RARHeaderReader must walk both sets — the self-check
    /// test enforces it).
    /// </summary>
    public static AssemblyFixture Build(
        string dir,
        int volumeSize,                    // total bytes per volume (last may be short)
        IReadOnlyList<(string Name, byte[] Payload)> archivedFiles,
        bool originalHasExtTime,           // 5-byte EXT_TIME (flags word + 3-byte remainder)
        bool producedHasExtTime,
        string volumePrefix = "t",         // "t.rar"/"t.r00"… old-style naming
        string? directoryPrefix = null)    // e.g. "CD1" → qualified names "CD1/t.rar"
}
```

Split algorithm (the load-bearing part, written out so the implementer transcribes it):

```
payloadQueue = concat(archivedFiles payloads, tracked per file)
for shape in { original, produced }:
    headerLen(file i, piece) = 32 + name.Length + (shape.HasExtTime ? 5 : 0)
    walk volumes v = 0.. while payload remains:
        remaining = volumeSize - marker(7) - archiveHeader(13) - endBlock(7)
        emit file pieces greedily: for the current archived file,
            take = min(remaining - headerLen, file.RemainingBytes)
            if take <= 0: close volume (SplitAfter on last emitted piece), next volume
            piece flags: SplitBefore if file started in an earlier volume,
                         SplitAfter if file continues past this volume
            ADD_SIZE = take; FILE_CRC = CRC32 of THIS piece's bytes
        last volume: EndArchive after final piece
SRR = SRRTestDataBuilder().AddSRRHeader("fixture")
        + per ORIGINAL volume: AddRARFileWithHeaders(qualifiedName,
              RecoveryBlocksRemoved, headers-of-that-volume)   // embedded verbatim
ExpectedVolumeCrcs[name] = CRC32(original volume file bytes)
```

Because header lengths differ between shapes while `volumeSize` is fixed, the split
points shift exactly as the real bug does (5 bytes per piece header here).

- [ ] **Step 1: `RAR4HeaderBuilder` mtime remainder** — `AddFileHeader` gains
`byte[]? mtimeRemainder = null` (0–3 bytes). With `RARFileFlags.ExtTime`: the mtime
nibble (bits 15–12: present=0x8, low two bits = remainder count) becomes
`0x8 | remainder.Length`, i.e. `extFlags |= (ushort)((0x8 | count) << 12)`; the
remainder bytes are written immediately after the flags word (before any ctime/atime
DOS dates); `extTimeSize += count`. Add `AddMarker()` writing
`RARUtils.RAR4Marker` (7 bytes) if Task 1 did not already.

- [ ] **Step 2: Implement builder + self-check tests (complete):**

```csharp
public class AssemblyFixtureBuilderTests : TempDirTestBase
{
    [Fact]
    public void BothSets_ParseWithRARHeaderReader_AndPayloadRoundTrips()
    {
        byte[] payload = Enumerable.Range(0, 40_000).Select(i => (byte)(i % 251)).ToArray();
        AssemblyFixture f = AssemblyFixtureBuilder.Build(
            TempDir, volumeSize: 15_000, [("a.bin", payload)],
            originalHasExtTime: true, producedHasExtTime: false);

        Assert.True(f.OriginalVolumePaths.Count >= 3);
        // Packed stream is identical across shapes: RARStream over each set yields payload.
        foreach (string first in new[] { f.OriginalVolumePaths[0], f.ProducedFirstVolumePath })
        {
            using var rs = new RARStream(first, "a.bin");
            byte[] readBack = new byte[payload.Length];
            rs.ReadExactly(readBack);
            Assert.Equal(payload, readBack);
        }
        // Volume totals match pairwise (the fixed-volume-size re-split property).
        // Header shape genuinely differs (49 vs 44 for the single-file header).
    }

    [Fact]
    public void ExtTimeHeader_IsFiveBytesLonger_AndParses()
    {
        AssemblyFixture f = AssemblyFixtureBuilder.Build(
            TempDir, 15_000, [("a.bin", new byte[20_000])], true, false);
        // Read both first volumes' first file headers via RARHeaderReader and assert
        // HeaderSize difference == 5 (flags word + 3-byte remainder).
    }
}
```

(The second test's reader walk is ~8 lines with `RARHeaderReader` — write it fully at
implementation; the assertion targets are stated.)

- [ ] **Step 3: Suite; gate; commit** `test(lib): assembly fixture builder (original/produced/SRR triads)`.

---

### Task 4: `ProducedVolumesPackedSource`

**Files:**
- Create: `ReScene.Lib/ReScene/Core/IO/ProducedVolumesPackedSource.cs`
- Test: `ReScene.Lib/ReScene.Tests/ProducedVolumesPackedSourceTests.cs`

```csharp
// ProducedVolumesPackedSource.cs
namespace ReScene.Core.IO;

/// <summary>
/// Packed-byte source over a brute-forced rar output set: each archived file's stream
/// is a <see cref="RARStream"/> over the produced volumes. Snapshot semantics: EACH
/// OPENED RARStream enumerates the volume list when IT is constructed (inside
/// OpenPackedStream) and never discovers volumes created later — so callers create a
/// fresh source (and thus fresh streams) per assembly attempt and never reuse one
/// across a producer state change (spec §4).
/// </summary>
internal sealed class ProducedVolumesPackedSource(string producedFirstVolumePath) : IPackedSource
{
    public Stream OpenPackedStream(string archivedFileName) =>
        new RARStream(producedFirstVolumePath, archivedFileName);

    public void Dispose()
    {
        // Streams are owned and disposed by the reconstructor.
    }
}
```

- [ ] **Step 1: Failing tests (complete):**

```csharp
public class ProducedVolumesPackedSourceTests : TempDirTestBase
{
    private AssemblyFixture BuildTwoFileFixture() =>
        AssemblyFixtureBuilder.Build(TempDir, 15_000,
            [("a.bin", MakePayload(20_000, seed: 1)), ("b.bin", MakePayload(9_000, seed: 2))],
            originalHasExtTime: true, producedHasExtTime: false);

    private static byte[] MakePayload(int n, int seed) =>
        [.. Enumerable.Range(0, n).Select(i => (byte)((i * 31 + seed) % 251))];

    [Fact]
    public void OpenPackedStream_ConcatenatesSplitPieces_AcrossVolumes()
    {
        AssemblyFixture f = BuildTwoFileFixture();
        using var source = new ProducedVolumesPackedSource(f.ProducedFirstVolumePath);
        using Stream s = source.OpenPackedStream("a.bin");
        byte[] all = new byte[20_000];
        s.ReadExactly(all);
        Assert.Equal(MakePayload(20_000, 1), all);
    }

    [Fact]
    public void OpenPackedStream_SecondFile_StartsAtItsOwnByteZero()
    {
        AssemblyFixture f = BuildTwoFileFixture();
        using var source = new ProducedVolumesPackedSource(f.ProducedFirstVolumePath);
        using Stream s = source.OpenPackedStream("b.bin");
        byte[] head = new byte[16];
        s.ReadExactly(head);
        Assert.Equal(MakePayload(9_000, 2).AsSpan(0, 16).ToArray(), head);
    }

    [Fact]
    public void OpenedStream_IsSingleSnapshot_LateVolumeInvisible()
    {
        // The snapshot is taken when the RARStream is OPENED (inside OpenPackedStream),
        // so the continuation volume must be hidden AT OPEN TIME and restored after —
        // hiding before source construction alone proves nothing. Pick the continuation
        // volume by its volume-naming successor, NOT lexicographically (".rar" does not
        // sort before ".r00").
        AssemblyFixture f = BuildTwoFileFixture();
        string vol2 = RARVolumeNaming.GetNextVolumePath(f.ProducedFirstVolumePath, isOldNaming: true)!;
        string hidden = vol2 + ".hidden";
        File.Move(vol2, hidden);
        using var source = new ProducedVolumesPackedSource(f.ProducedFirstVolumePath);
        using Stream s = source.OpenPackedStream("a.bin");   // snapshot taken HERE, vol2 absent
        File.Move(hidden, vol2);                              // "appears" after the snapshot
        byte[] buf = new byte[20_000];
        Assert.ThrowsAny<Exception>(() => s.ReadExactly(buf)); // stream never sees the late volume
    }
}
```

- [ ] **Step 2: FAIL → implement (the file above) → PASS; suite; gate; commit** `feat(lib): ProducedVolumesPackedSource`.

---

### Task 5: Set filtering + byte-identity assembly suite

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/SRRReconstructor.cs` (filter in both walks; `SectionMatchesSet`)
- Test: `ReScene.Lib/ReScene.Tests/SRRAssemblyTests.cs`

**Interfaces — Produces (ambiguity is resolved BEFORE matching — a bool matcher cannot express it):**
Both walks begin with the same pre-check, extracted as
`internal static SRRReconstructionResult? ValidateSetSelector(IReadOnlyList<string> setNames, IReadOnlyList<string> allSectionNames)`:
build the section inventory in one preliminary pass over the SRR's RARFile block names
(cheap: base headers only, same seeks); for every BARE selector (no `/` after
normalization) whose basename occurs on more than one section, return
`Fail(Error, $"volume name '{name}' is ambiguous in this SRR — qualify it with its directory")`;
otherwise return null (valid). After validation, per-section matching is
`internal static bool SectionMatchesSet(string sectionName, IReadOnlyList<string> setNames)` —
separator-normalized (`\`→`/`, trim `/`), `OrdinalIgnoreCase`: qualified selectors compare
full relative names; bare selectors compare basenames (already proven unique). Applied
identically in `PreflightSet` and `ReconstructAsync`; non-matching sections are skipped
with the documented seeks, never opening output or source.

- [ ] **Step 1: Failing tests (complete; SHA-256 byte identity is the core proof):**

```csharp
public class SRRAssemblyTests : TempDirTestBase
{
    private static byte[] Payload(int n, int seed) =>
        [.. Enumerable.Range(0, n).Select(i => (byte)((i * 31 + seed) % 251))];

    private async Task<SRRReconstructionResult> AssembleAsync(AssemblyFixture f, string outSub,
        IReadOnlyList<string>? names = null)
    {
        using var source = new ProducedVolumesPackedSource(f.ProducedFirstVolumePath);
        return await new SRRReconstructor(new NullReSceneLogger()).ReconstructAsync(
            f.SrrPath, source, TempDir, Path.Combine(TempDir, outSub),
            names ?? f.OriginalVolumeNames, [], HashType.CRC32, CancellationToken.None);
    }

    private static void AssertByteIdentical(IReadOnlyList<string> originals, IReadOnlyList<string> assembled)
    {
        Assert.Equal(originals.Count, assembled.Count);
        for (int i = 0; i < originals.Count; i++)
        {
            byte[] o = File.ReadAllBytes(originals[i]);
            byte[] a = File.ReadAllBytes(assembled[i]);
            Assert.Equal(o.Length, a.Length);
            Assert.Equal(SHA256.HashData(o), SHA256.HashData(a));
        }
    }

    [Fact]
    public async Task ExtTimeDivergence_ByteIdenticalOutput() // THE bug
    {
        AssemblyFixture f = AssemblyFixtureBuilder.Build(TempDir, 15_000,
            [("a.bin", Payload(40_000, 1))], originalHasExtTime: true, producedHasExtTime: false);
        SRRReconstructionResult r = await AssembleAsync(f, "out");
        Assert.Equal(SRRReconstructionStatus.Success, r.Status);
        AssertByteIdentical(f.OriginalVolumePaths, r.WrittenPaths);
    }

    [Fact]
    public async Task MirrorShift_ReadsAcrossProducedBoundary()
    {
        AssemblyFixture f = AssemblyFixtureBuilder.Build(TempDir, 15_000,
            [("a.bin", Payload(40_000, 1))], originalHasExtTime: false, producedHasExtTime: true);
        SRRReconstructionResult r = await AssembleAsync(f, "out");
        Assert.Equal(SRRReconstructionStatus.Success, r.Status);
        AssertByteIdentical(f.OriginalVolumePaths, r.WrittenPaths);
    }

    [Fact]
    public async Task MultiFile_SplitAcrossVolumes()
    {
        AssemblyFixture f = AssemblyFixtureBuilder.Build(TempDir, 15_000,
            [("a.bin", Payload(20_000, 1)), ("b.bin", Payload(18_000, 2))], true, false);
        SRRReconstructionResult r = await AssembleAsync(f, "out");
        Assert.Equal(SRRReconstructionStatus.Success, r.Status);
        AssertByteIdentical(f.OriginalVolumePaths, r.WrittenPaths);
    }

    [Fact]
    public async Task MultiSet_SameBasenames_FiltersByQualifiedName()
    {
        AssemblyFixture cd1 = AssemblyFixtureBuilder.Build(Path.Combine(TempDir, "s1"), 15_000,
            [("a.bin", Payload(20_000, 1))], true, false, directoryPrefix: "CD1");
        AssemblyFixture cd2 = AssemblyFixtureBuilder.Build(Path.Combine(TempDir, "s2"), 15_000,
            [("a.bin", Payload(20_000, 9))], true, false, directoryPrefix: "CD2");
        string combinedSrr = ConcatenateSrrs(cd1.SrrPath, cd2.SrrPath); // header + both section runs

        using var source = new ProducedVolumesPackedSource(cd2.ProducedFirstVolumePath);
        SRRReconstructionResult r = await new SRRReconstructor(new NullReSceneLogger())
            .ReconstructAsync(combinedSrr, source, TempDir, Path.Combine(TempDir, "out"),
                cd2.OriginalVolumeNames /* "CD2/t.rar"… */, [], HashType.CRC32, CancellationToken.None);

        Assert.Equal(SRRReconstructionStatus.Success, r.Status);
        AssertByteIdentical(cd2.OriginalVolumePaths, r.WrittenPaths);
        Assert.DoesNotContain(r.WrittenPaths, p => p.Contains("CD1", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AmbiguousBareName_Fails()
    {
        AssemblyFixture cd1 = AssemblyFixtureBuilder.Build(Path.Combine(TempDir, "q1"), 15_000,
            [("a.bin", Payload(20_000, 1))], true, false, directoryPrefix: "CD1");
        AssemblyFixture cd2 = AssemblyFixtureBuilder.Build(Path.Combine(TempDir, "q2"), 15_000,
            [("a.bin", Payload(20_000, 9))], true, false, directoryPrefix: "CD2");
        string combinedSrr = ConcatenateSrrs(cd1.SrrPath, cd2.SrrPath);

        using var source = new ProducedVolumesPackedSource(cd2.ProducedFirstVolumePath);
        SRRReconstructionResult r = await new SRRReconstructor(new NullReSceneLogger())
            .ReconstructAsync(combinedSrr, source, TempDir, Path.Combine(TempDir, "outq"),
                ["t.rar"], [], HashType.CRC32, CancellationToken.None);

        Assert.Equal(SRRReconstructionStatus.Error, r.Status);
        Assert.Contains("t.rar", r.Diagnostic);
        Assert.Contains("ambiguous", r.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(TempDir, "outq"))); // validated pre-output
    }
}
```

`ConcatenateSrrs`: private helper — write SRR header once, then append both files'
bytes after their own 0x69 headers (10 lines; the builder's `Build()` byte output makes
this trivial — implement in the test file).

Padding coverage: add `bool insertPadding` to `AssemblyFixtureBuilder.Build` — when
set, an `SRRBlockType.RARPadding` block (existing emission shape in
`SRRReconstructor` lines 156-179) is inserted between sections and the ORIGINAL
volumes carry the corresponding zero bytes; one more test
`PaddingBlocks_Preserved` then reuses `AssertByteIdentical`. (Fold into this task —
it is the consumer.)

- [ ] **Step 2: Multi-set/ambiguity/padding FAIL (no filter yet); divergence/mirror/multi-file should PASS off Tasks 1–4 — any failure there is a real T1–T4 bug: fix it now.**
- [ ] **Step 3: Implement `SectionMatchesSet` + apply in both walks.**
- [ ] **Step 4: All green; full lib suite; gate; commit** `feat(lib): assembly core proven byte-identical + set filtering`.

---

### Task 6: Producer-runner seam + observation invariant

**Files:**
- Create: `ReScene.Lib/ReScene/Core/Diagnostics/IRARProcessRunner.cs`
- Create: `ReScene.Lib/ReScene/Core/Diagnostics/RealRARProcessRunner.cs`
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs`
- Test: `ReScene.Lib/ReScene.Tests/ManagerProducerLifecycleTests.cs`

```csharp
// IRARProcessRunner.cs
namespace ReScene.Core.Diagnostics;

/// <summary>Seam over rar execution so the candidate loop is testable without a rar
/// binary. The real implementation wraps RARProcess exactly as Manager did inline.</summary>
internal interface IRARProcessRunner
{
    Task<int> RunAsync(string rarExePath, string inputDirectory, string outputFilePath,
        IEnumerable<string> arguments, LogTarget logTarget,
        Action<RARProcess>? onCreated, CancellationToken cancellationToken);
}
```

`RealRARProcessRunner`: constructs `RARProcess` with `LogTarget`, invokes
`onCreated(process)` (Manager's callback does `_processLogManager.OpenLog(...)` +
`SubscribeToProcessEvents(process)` — closure-captured, verified sufficient by codex),
returns `process.RunAsync(ct)`. `Manager` gains
`internal Manager(IReSceneLogger? logger, IRARProcessRunner runner)`; the public ctor
chains with `new RealRARProcessRunner()`. Both launch sites (CAV inline ~746;
`RARCompressDirectoryAsync` ~449) go through `_runner`.

Observation invariant helper (used at EVERY exit — winning and non-winning, both
modes):

```csharp
/// <summary>
/// Cancels (when requested) and OBSERVES the producer: awaits the process task to real
/// completion — no grace-timeout abandonment — swallowing only cancellation. Invariant
/// (spec §4): no finalization, deletion, or next-candidate launch while a producer task
/// is unobserved.
/// </summary>
private static async Task<int?> ObserveProducerAsync(Task<int>? processTask,
    CancellationTokenSource? processCts, bool cancelFirst)
{
    if (processTask is null) { return null; }
    if (cancelFirst) { processCts?.Cancel(); }
    try { return await processTask.ConfigureAwait(false); }
    catch (OperationCanceledException) { return null; }
}
```

`RARCompressDirectoryAsync`'s early-termination tail replaces
`Task.WhenAny(processTask, Task.Delay(1000, …))` with
`await ObserveProducerAsync(processTask, linkedCts, cancelFirst: false)` after its
cancel — and its XML doc's exit-code contract is UPDATED (codex plan A2): an
early-terminated run now returns the OBSERVED cancellation exit (normally 1), never a
synthetic 0; the call-site comment at ~788 is updated to match (both tolerate this —
early termination implies a volume exists).

Wire `ObserveProducerAsync` also at: the `actualRARFilePath == null` branch, the
quick-mismatch branch, the generic `catch` (Manager.cs:968 — currently observes
nothing), and cancellation exits.

- [ ] **Step 1: Failing lifecycle tests.** `FakeRunner` design (complete — this IS the
latch pattern codex plan B7 requires; a bare TCS cannot observe awaiting):

```csharp
private sealed class FakeRunner : IRARProcessRunner
{
    public sealed class Launch(string outputFilePath)
    {
        public string OutputFilePath { get; } = outputFilePath;
        // Exit is completed ONLY by the test — cancellation must NOT complete it, or
        // every Cancel() path would hand Manager an already-finished task and the latch
        // could not distinguish observation from abandonment (codex plan-rev-2 B2).
        public TaskCompletionSource<int> Exit { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public List<Launch> Launches { get; } = [];
    public Action<Launch>? OnLaunch { get; set; }   // test writes carrier volume files here

    public Task<int> RunAsync(string rarExePath, string inputDirectory, string outputFilePath,
        IEnumerable<string> arguments, LogTarget logTarget,
        Action<RARProcess>? onCreated, CancellationToken cancellationToken)
    {
        var launch = new Launch(outputFilePath);
        cancellationToken.Register(() => launch.CancellationRequested.TrySetResult());
        Launches.Add(launch);
        OnLaunch?.Invoke(launch);
        return launch.Exit.Task;
    }
}
```

Shared harness — define ONCE in this test file, reused by Tasks 7-10 (this is the
"difficult part" a fresh engineer must not have to invent):

```csharp
/// <summary>One fake rar "version" + BruteForceOptions over TempDir; recording logger.</summary>
internal sealed class AssemblyTestHost : IDisposable
{
    public string Root { get; }               // TempDir subdir per test
    public string VersionsDir { get; }        // Root/versions (contains fake100/ with dummy rar)
    public string WorkDir { get; }            // Root/work  (options.OutputDirectoryPath)
    public string ReleaseDir { get; }         // Root/release (unpacked inputs; one small file)
    public FakeRunner Runner { get; } = new();
    public RecordingLogger Log { get; } = new();
    public Manager Manager { get; }

    public AssemblyTestHost(string tempDir)
    {
        Root = Path.Combine(tempDir, Guid.NewGuid().ToString("N")[..8]);
        VersionsDir = Path.Combine(Root, "versions");
        string fakeVersion = Path.Combine(VersionsDir, "fake100");
        WorkDir = Path.Combine(Root, "work");
        ReleaseDir = Path.Combine(Root, "release");
        Directory.CreateDirectory(fakeVersion);
        Directory.CreateDirectory(WorkDir);
        Directory.CreateDirectory(ReleaseDir);
        File.WriteAllBytes(Path.Combine(ReleaseDir, "a.bin"), new byte[16]);
        // Satisfy RarExecutable.ResolveIn minimally — grep its rules at implementation
        // and create the file(s) it requires (e.g. an empty "rar.exe"/"Rar.exe").
        File.WriteAllBytes(Path.Combine(fakeVersion, "rar.exe"), []);
        Manager = new Manager(Log, Runner);
    }

    public BruteForceOptions Options(AssemblyFixture? fixture, bool completeAllVolumes,
        bool deleteRarFiles = true, bool deleteDuplicates = false)
    {
        // Adjust member names to the REAL BruteForceOptions/RAROptions at implementation
        // (grep them); this block names every required knob:
        //   RAROptions: CompleteAllVolumes, DeleteRARFiles, DeleteDuplicateCRCFiles,
        //     StopOnFirstMatch=true, RenameToOriginalNames=true, SRRFilePath,
        //     OriginalRARFileNames, version range covering "fake100"
        //   BruteForceOptions: ReleaseDirectoryPath=ReleaseDir, OutputDirectoryPath=WorkDir,
        //     WinRAR directory=VersionsDir, HashType=CRC32,
        //     Hashes=[CRC32 of fixture.OriginalVolumePaths[0]],
        //     ExpectedVolumeCrcs from fixture.ExpectedVolumeCrcs
        throw null!; // transcribe with real member names — the harness test below pins it
    }

    public void Dispose() => Manager.Dispose();
}
```

Add one harness self-check test: `HarnessSmoke_LegacyRun_SingleFakeVersion_Completes` —
drives a legacy (no-SRR) run where OnLaunch writes a single parseable carrier volume and
completes Exit; asserts the run finishes and exactly one launch occurred. This proves
the options plumbing before any assembly test relies on it. `RecordingLogger` is the
15-line `IReSceneLogger` capturing `(Level, Target, Message)` tuples with a
`Count(string contains)` helper.

Invariant assertions hold the exit open and verify the Manager DID NOT PROGRESS:

```csharp
[Fact]
public async Task NonCavSuccess_DoesNotComplete_UntilProducerTaskObserved()
{
    // OnLaunch writes carrier volume 1 AND a complete volume 2 (the early-termination
    // trigger) but HOLDS Exit. Start the run; await launch.CancellationRequested.Task
    // (Manager cancels the early-terminated rar); then assert across ~250ms polls that
    // the Manager task has NOT completed and the work output received no finalized
    // files — the invariant blocks on the unobserved task. Then Exit.TrySetResult(1);
    // await the run; assert success.
}

[Fact]
public async Task QuickMismatch_ObservesProducer_BeforeNextCandidateLaunch()
{
    // Two fake version dirs. Launch 1 writes a non-matching carrier; hold its Exit.
    // Await launch1.CancellationRequested.Task (mismatch path cancels), then assert
    // Launches.Count == 1 across ~250ms polls (no second launch while unobserved).
    // Release Exit; poll until Launches.Count == 2; release launch 2 to finish.
}

[Fact]
public async Task MidCandidateError_LogsErrorRow_ObservesProducer_AndContinues()
{
    // Manager's generic-candidate-error contract is error row + CONTINUE
    // (Manager.cs:973), not propagation. Induce the error AFTER launch via malformed
    // carrier data: launch 1 writes an unparseable volume 1 (garbage bytes) so the
    // candidate body throws while Exit is held. Assert: no second launch while launch
    // 1's Exit is held; release; candidate 2 launches; run completes with a
    // CombinationFailed progress event recorded for candidate 1.
}

[Fact]
public async Task UserStop_ObservesProducerBeforeReturn()
{
    // Hold Exit; call host.Manager.Stop() (the real cancellation API — there is no
    // Cancel()); await launch.CancellationRequested.Task; assert the run task stays
    // incomplete across ~250ms polls; then Exit.TrySetResult(1); await; assert the
    // run reports cancellation. (Verify the actual stop method name on Manager at
    // implementation — grep "public void Stop" / the CTS cancel site ~Manager.cs:427.)
}
```

Fixture note: "writes a volume" = copy a pre-built produced set from
`AssemblyFixtureBuilder` (or a single minimal volume via `RAR4HeaderBuilder`) to
`launch.OutputFilePath` / its volume siblings — these tests drive the LEGACY path and
only need parseable files with any CRC; `BruteForceOptions` points at `TempDir`
subdirs with one fake version directory containing a dummy `rar.exe` file (existence
is all the resolver needs — grep `RarExecutable.ResolveIn` at implementation and
satisfy it minimally).

- [ ] **Step 2: Implement seam + invariant; the four tests green.**
- [ ] **Step 3: FULL lib suite green — the seam is behavior-preserving for the legacy path; any legacy failure is a seam bug. Rebuild gate; commit** `refactor(lib): producer runner seam + observation invariant`.

---

### Task 7: Manager engagement (preflight-before-loop, three branches)

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs`
- Test: `ReScene.Lib/ReScene.Tests/ManagerAssemblyFlowTests.cs` (new; grows in T8/T9)

**Anchoring (codex plan B3 — real locals and insertion point):** the once-per-set
insertion point is in the set-level method, AFTER
`InputDirectoryPreparer.PrepareInputDirectory` (~line 308) and BEFORE the
`for (int a = …)` attribute loop (~line 326). State threads to
`TryProcessCommandLinesAsync` as private fields:

```csharp
private bool _useAssembly;                 // set once per set, pre-loop
private bool _inconclusiveGuidanceLogged;  // once-per-set INFO guard (reset with _useAssembly)
```

```csharp
// Insertion (after inputFilesDir is known, before the a-loop):
_useAssembly = false;
_inconclusiveGuidanceLogged = false;
if (!string.IsNullOrEmpty(options.RAROptions.SRRFilePath)
    && options.RAROptions.CustomPackerDetected == SRR.CustomPackerType.None)
{
    SRRReconstructionResult pre = new SRRReconstructor(_logger)
        .PreflightSet(options.RAROptions.SRRFilePath, options.RAROptions.OriginalRARFileNames);
    switch (pre.Status)
    {
        case SRRReconstructionStatus.Success:
            _useAssembly = true;
            _logger.Information(this, "SRR-guided assembly engaged (headers from SRR, data from rar output)", LogTarget.System);
            break;
        case SRRReconstructionStatus.UnsupportedSrr:
            _logger.Information(this, $"SRR-guided assembly unavailable ({pre.Diagnostic}) — trying legacy reconstruction for this set", LogTarget.System);
            break;
        default: // Error: unreadable/malformed SRR is a SET failure, not a silent legacy fallback
            _logger.Error(this, $"SRR could not be read for assembly preflight: {pre.Diagnostic}", LogTarget.System);
            // EXACTLY the custom-packer completion shape (Manager.cs ~258-262): fire the
            // status transition with OperationCompletionStatus.Error, then return the
            // method's failure result. Anchor to that real block and mirror it:
            //   status = new BruteForceStatusChangedEventArgs(OperationStatus.Running,
            //       OperationStatus.Completed, OperationCompletionStatus.Error);
            //   FireBruteForceStatusChanged(status);
            //   return <the same value the custom-packer error path returns there>;
            break; // (transcribed with the real return value at implementation)
    }
}
```

In `TryProcessCommandLinesAsync`, the candidate slug is derived from the existing
local: `string candidateSlug = Path.GetFileNameWithoutExtension(rarFilePath);` and the
assembly work dir is `Path.Combine(rarOutputDir, $"assembled-{candidateSlug}")`.

- [ ] **Step 1: Failing test:**

```csharp
[Fact]
public async Task PreflightDecline_RunsLegacyFromCandidateOne_NoProducerCancelled()
{
    // SRR with real RR evidence (AddServiceBlock("RR", 64, false)); FakeRunner writes a
    // matching legacy volume for candidate 1 (CRC of the produced file supplied in
    // options.Hashes). Assert: the run reports the candidate-1 match through the LEGACY
    // path (patched/hash flow), FakeRunner.Launches.Count == 1, and no launch token was
    // cancelled before its Exit resolved.
}

[Fact]
public async Task PreflightError_FailsTheSet_BeforeAnyLaunch()
{
    // Malformed SRR bytes; assert the run completes with the set-failure status and
    // FakeRunner.Launches is empty.
}
```

- [ ] **Step 2: Implement; green; full suite; gate; commit** `feat(lib): assembly engagement preflight (three-branch, pre-loop)`.

---

### Task 8: Quick check, retry, and classification

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs` (`AssembleCandidateAsync` + the `_useAssembly` branch of the candidate flow, replacing patch+hash)
- Test: `ReScene.Lib/ReScene.Tests/ManagerAssemblyFlowTests.cs` (extend)

```csharp
/// <summary>
/// Assembles the first <paramref name="volumeCount"/> ORIGINAL volumes for the current
/// candidate from the produced set. Fresh ProducedVolumesPackedSource per call
/// (single-snapshot). volumeCount: 1 = quick gate; int.MaxValue = full set.
/// </summary>
private async Task<SRRReconstructionResult> AssembleCandidateAsync(
    BruteForceOptions options, string producedFirstVolume, string assemblyDir,
    int volumeCount, CancellationToken ct)
{
    IReadOnlyList<string> names = options.RAROptions.OriginalRARFileNames;
    if (volumeCount < names.Count) { names = [.. names.Take(volumeCount)]; }
    using var source = new ProducedVolumesPackedSource(producedFirstVolume);
    return await new SRRReconstructor(_logger).ReconstructAsync(
        options.RAROptions.SRRFilePath!, source, options.ReleaseDirectoryPath,
        assemblyDir, names, [], options.HashType, ct).ConfigureAwait(false);
}
```

Candidate flow (`_useAssembly` branch, replacing `PatchRARFilesHostOS` + hash; sits at
the point where `actualRARFilePath` is known):

```csharp
string assemblyDir = Path.Combine(rarOutputDir, $"assembled-{candidateSlug}");
SRRReconstructionResult quick = await AssembleCandidateAsync(options, actualRARFilePath, assemblyDir, 1, _cts.Token).ConfigureAwait(false);

bool producerRunning = runningProcessTask is { IsCompleted: false };
if (quick.Status != SRRReconstructionStatus.Success && producerRunning)
{
    // Incomplete snapshot (spec §4): ANY non-success while the producer runs — including
    // Error from RARStream's missing/short-header ArgumentException — awaits completion
    // and retries ONCE with a fresh source.
    completedExitCode = await ObserveProducerAsync(runningProcessTask, processCts, cancelFirst: false).ConfigureAwait(false);
    quick = await AssembleCandidateAsync(options, actualRARFilePath, assemblyDir, 1, _cts.Token).ConfigureAwait(false);
}

string? quickHash = quick.Status == SRRReconstructionStatus.Success && quick.WrittenPaths.Count >= 1
    ? HashCalculator.Calculate(options.HashType, quick.WrittenPaths[0])
    : null;
bool quickMatch = quickHash != null && options.Hashes.Contains(quickHash);
_logger.Information(this, $"Assembled hash for {(quick.WrittenPaths.Count >= 1 ? quick.WrittenPaths[0] : assemblyDir)}: {quickHash ?? quick.Status.ToString()} (match: {quickMatch})", LogTarget.Phase2);

if (!quickMatch)
{
    // Post-retry classification (spec §4):
    switch (quick.Status)
    {
        case SRRReconstructionStatus.Error:
            // Persistent parse/I-O failure = failed combination — the EXISTING error-row
            // shape (CombinationFailed progress event + warning). RETENTION: like the
            // exception disposition, BOTH artifact classes are LEFT IN PLACE for
            // diagnosis (spec §5) — set skipRetentionCleanup = true so the deletion
            // block below is bypassed; then continue to the next candidate.
            break;
        case SRRReconstructionStatus.SourceExhausted when !options.RAROptions.CompleteAllVolumes:
            // Mirror shift in non-CAV: vol-2 bytes were never written — INCONCLUSIVE.
            if (!_inconclusiveGuidanceLogged)
            {
                _inconclusiveGuidanceLogged = true;
                _logger.Information(this, "Some candidates are inconclusive without full volumes — enable \"Complete all volumes\" to test them", LogTarget.System);
            }
            _logger.Debug(this, $"{candidateSlug}: inconclusive (assembly needs produced volume 2+)", LogTarget.Phase2);
            break;
        default:
            // SourceExhausted (CAV, producer done) or a hash mismatch: real no-match.
            break;
    }
    await ObserveProducerAsync(runningProcessTask, processCts, cancelFirst: true).ConfigureAwait(false);
    if (!skipRetentionCleanup) // false for mismatch/SourceExhausted/duplicate; true for Error
    {
        DeleteAssemblyDirUnderRetentionFlags(assemblyDir, options, duplicate: isDuplicateHash);
        // …existing carrier deletion under the same flags…
    }
    // existing continue;
}
```

`DeleteAssemblyDirUnderRetentionFlags(string dir, BruteForceOptions options, bool duplicate)`
— deletes `dir` recursively when (`options.RAROptions.DeleteRARFiles`) or
(`duplicate && options.RAROptions.DeleteDuplicateCRCFiles`); otherwise retains for
debugging (mirrors the carrier flags; spec §5). Duplicate detection reuses the
existing `fileHashes` set against `quickHash`.

- [ ] **Step 1: Failing tests (extend `ManagerAssemblyFlowTests`; FakeRunner + `AssemblyFixtureBuilder` produced sets copied in `OnLaunch`):**

```csharp
// ATTEMPT PROBE: AssembleCandidateAsync logs one DEBUG line per invocation —
// $"Assembly attempt for {candidateSlug}: volumes={volumeCount}" — the tests count
// THOSE via the RecordingLogger ("Assembled hash" is logged only once, post-retry,
// so it cannot count attempts).

[Fact] public async Task Cav_IncompleteSnapshot_RetriesOnceWithFreshSource()
// OnLaunch writes produced vol 1 complete AND vol 2 TRUNCATED (half its bytes) — the
// vol-2-exists trigger fires with an unreadable tail — and HOLDS Exit. Manager's first
// quick attempt fails (Error/short) with the producer running → it awaits Exit; the
// test, upon observing the first "Assembly attempt" log line + the await (poll), writes
// vol 2's REMAINING bytes and THEN completes Exit → retry succeeds → quick match true.
// Assert exactly 2 "Assembly attempt" DEBUG lines.

[Fact] public async Task Cav_PostRetry_SourceExhausted_IsNoMatch()
// Produced set genuinely one volume short; Exit completed immediately (producer done
// before attempt 1 → no retry beyond the single allowed one). Status SourceExhausted;
// assert NO CombinationFailed event and the candidate proceeds as no-match; retention
// cleanup applied (mismatch class).

[Fact] public async Task Cav_PersistentError_IsFailedCombination_AndRetains()
// OnLaunch writes garbage bytes as vol 1 and HOLDS Exit → attempt 1 Error with
// producer running → retry path awaits Exit; test completes Exit WITHOUT repairing the
// carrier → attempt 2 Error → CombinationFailed event asserted AND both artifact
// classes retained (Error = diagnosis disposition, spec §5).

[Fact] public async Task NonCav_MirrorShift_IsInconclusive_LogsGuidanceOnce()
// Two fake versions; per candidate, OnLaunch writes mirror-shift produced vol 1 plus a
// minimal STUB vol 2 (header-only/truncated — enough to fire the non-CAV
// second-volume termination trigger, Manager.cs:465) and completes Exit with the
// early-terminated shape. Both candidates: SourceExhausted → inconclusive. Assert the
// INFO guidance appears EXACTLY once, the DEBUG per-candidate line twice, and no
// CombinationFailed events.

[Fact] public async Task NonMatch_AssemblyDirRetention_FollowsDeleteFlags()
// DeleteRARFiles=false → assembled dir retained; =true → deleted. Both asserted.
```

(The recording logger: 15-line `IReSceneLogger` capturing `(level, target, message)` —
define once in this test file, reuse in T9.)

- [ ] **Step 2: Implement; green; full suite (T6/T7 tests must stay green); gate; commit** `feat(lib): assembly quick gate with incomplete-snapshot retry + classification`.

---

### Task 9: Full assembly, verification, non-CAV success + the assembled finalizer

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs` (the `quickMatch` win path AND `FinalizeAssembledSet` — the win path cannot report a committed match without it, and `RenameMatchedOutput` would move the carrier volumes; codex plan-rev-2 B1)
- Test: `ReScene.Lib/ReScene.Tests/ManagerAssemblyFlowTests.cs` (extend) + `ReScene.Lib/ReScene.Tests/ManagerAssemblyFinalizeTests.cs` (direct finalizer cases — created here)

Win path (`quickMatch == true`), CAV mode:

```csharp
// Let the producer FINISH (never cancel a winner mid-write), observing it (invariant).
completedExitCode = await ObserveProducerAsync(runningProcessTask, processCts, cancelFirst: false).ConfigureAwait(false);

// FULL assembly — fresh source over the now-complete produced set. Verification and
// finalization use THIS result's ordered WrittenPaths (codex plan B1), never `quick`'s.
SRRReconstructionResult assembled = await AssembleCandidateAsync(options, actualRARFilePath, assemblyDir, int.MaxValue, _cts.Token).ConfigureAwait(false);
if (assembled.Status != SRRReconstructionStatus.Success)
{
    // Completed-producer full assembly cannot be an incomplete snapshot: classify as in
    // Task 8 (Error → CombinationFailed; SourceExhausted → no-match) and continue.
}

// Per-volume verification: EXACTLY today's gate — CAV && BuildExpectedInOrder non-empty;
// CRC32 regardless of options.HashType (the SRR-embedded SFV is CRC32); compares
// assembled.WrittenPaths positionally via VolumeMatchEvaluator. Empty CRC map ⇒ the
// first-volume quick hash was the whole gate (first-hash-only parity, spec §4).
```

The finalizer (implemented in THIS task, `internal` for direct testing — codex
plan-rev-2 B6; the win path calls it for both modes):

```csharp
/// <summary>
/// Finalizes an assembly win: moves the reconstructor's ordered WrittenPaths —
/// verbatim, no volume rediscovery, no patching — transactionally into
/// <paramref name="rarOutputDir"/> (the app's VerifiedOutputRelocator consumes
/// committed files there). Naming (spec §5): RenameToOriginalNames=true → original
/// volume names (the assembled files already carry them); false → basename replacement
/// preserving the COMPLETE volume suffix ("foo.part01.rar" →
/// "{slug}-assembled.part01.rar", "foo.r00" → "{slug}-assembled.r00") via
/// RARVolumeNaming.GetBaseName — never Path.GetExtension.
/// </summary>
private (IReadOnlyList<string> Placed, bool Complete) FinalizeAssembledSet(
    BruteForceOptions options, IReadOnlyList<string> assembledPaths,
    string candidateSlug, string rarOutputDir)
{
    var plan = new List<(string Source, string Dest)>(assembledPaths.Count);
    foreach (string src in assembledPaths)
    {
        string fileName = Path.GetFileName(src);
        if (!options.RAROptions.RenameToOriginalNames)
        {
            string baseName = RARVolumeNaming.GetBaseName(fileName);
            string suffix = fileName[baseName.Length..];
            fileName = $"{candidateSlug}-assembled{suffix}";
        }
        plan.Add((src, Path.Combine(rarOutputDir, fileName)));
    }
    return ExecuteMovePlan(plan);
}
```

Success retention (spec §5): after `Complete`, delete the carrier volumes when
`DeleteRARFiles` (else retain in the work area) and remove the now-empty `assemblyDir`.
Exception/cancellation exits leave BOTH classes in place.

Direct finalizer tests (in `ManagerAssemblyFinalizeTests`, created in this task):

```csharp
[Fact] public void OriginalNames_PlacesUnderWorkOutput()
[Fact] public void GeneratedNames_PreservesPartNNSuffix_Distinct()   // foo.part01/02.rar -> slug-assembled.part01/02.rar
[Fact] public void GeneratedNames_OldStyleSuffixes()                 // .rar/.r00/.r01
[Fact] public void GeneratedNames_NoCollisionWithRetainedCarriers()  // DeleteRARFiles=false: carriers in rarOutputDir; dest names differ
[Fact] public void Transactional_RollsBackWhenDestinationOccupied()  // pre-place a file at dest[1]; dest[0] rolled back; Complete=false
```

Each: create 2-3 dummy assembled files with real volume names under a temp assembled
dir, call the internal `FinalizeAssembledSet` directly on a `Manager` instance with an
options object carrying the toggle under test, assert destination paths/existence.

Non-CAV mode on `quickMatch`: the single assembled volume IS the mode's outcome —
report through the legacy first-volume success shape with
`assembled = quick` (one volume), skip per-volume verification (parity), finalize that
one path (Task 10's finalizer handles both counts).

The `*** MATCH FOUND ***` summary block: prints `SRR-guided assembly` on this path and
skips the patch-note lines.

- [ ] **Step 1: Failing tests:**

```csharp
[Fact] public async Task Cav_EndToEnd_ExtTimeScenario_MatchesAndVerifiesAllVolumes()
// The flagship: fixture ExtTime originals + non-ExtTime produced; options.Hashes =
// CRC32 of ORIGINAL vol 1; ExpectedVolumeCrcs wired into options; FakeRunner drops the
// produced set. Assert: success status, "SRR-guided assembly" in the recorded log,
// per-volume verification passed (no mismatch warnings), and the assembled files exist
// with CRCs equal to the originals'.

[Fact] public async Task Cav_FullVerifyMismatch_IsNoMatch_NotSuccess()
// Corrupt ONE later original CRC in ExpectedVolumeCrcs → quick matches, full verify
// fails → candidate rejected (existing mismatch path), run continues.

[Fact] public async Task NoCrcMap_FirstHashOnly_ParityPreserved()
// ExpectedVolumeCrcs empty → success on the quick hash alone; no per-volume pass.

[Fact] public async Task NonCav_QuickMatch_FirstVolumeSuccess()
// Non-CAV options; produced vol 1 sufficient (original headers larger case);
// success reported with exactly one assembled volume.
```

- [ ] **Step 2: Implement; green; suites; gate; commit** `feat(lib): full assembly + verification parity + non-CAV success`.

---

### Task 10: Retention matrix (flow-level)

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs` (only if matrix rows expose retention wiring gaps)
- Test: `ReScene.Lib/ReScene.Tests/ManagerAssemblyFinalizeTests.cs` (extend with the matrix)

- [ ] **Step 1: The RETENTION MATRIX in full (codex plan B8; explicit bool parameters,
all meaningful combinations, both artifact classes):**

```csharp
[Theory]
// outcome        deleteRar deleteDup expectAssembledSurvives expectCarrierSurvives
[InlineData("quickMismatch", true,  false, false, false)]
[InlineData("quickMismatch", false, false, true,  true )]
[InlineData("duplicate",     false, true,  false, false)] // dup flag deletes dups even when deleteRar=false
[InlineData("duplicate",     false, false, true,  true )]
[InlineData("fullMismatch",  true,  false, false, false)]
[InlineData("fullMismatch",  false, false, true,  true )]
[InlineData("error",         true,  false, true,  true )] // diagnosis: Error retains BOTH regardless of flags
[InlineData("exception",     true,  false, true,  true )]
[InlineData("cancellation",  true,  false, true,  true )]
[InlineData("success",       true,  false, false, true )] // assembled MOVED out (survives as output); carrier deleted
[InlineData("success",       false, false, false, true )] // carrier retained in work area
public async Task RetentionMatrix(string outcome, bool deleteRarFiles,
    bool deleteDuplicateCrcFiles, bool expectAssembledInWorkArea, bool expectCarrierInWorkArea)
{
    using var host = new AssemblyTestHost(TempDir);
    AssemblyFixture f = AssemblyFixtureBuilder.Build(host.Root, 15_000,
        [("a.bin", MakePayload(40_000, 1))], originalHasExtTime: true, producedHasExtTime: false);
    BruteForceOptions options = host.Options(f, completeAllVolumes: true,
        deleteRarFiles, deleteDuplicateCrcFiles);

    switch (outcome)
    {
        case "quickMismatch":
            options.Hashes.Clear(); options.Hashes.Add("00000000"); break;      // never matches
        case "duplicate":
            // Two fake versions; both launches drop the SAME produced set; candidate 2's
            // quick hash equals candidate 1's -> duplicate handling.
            AddSecondFakeVersion(host); options.Hashes.Clear(); options.Hashes.Add("00000000"); break;
        case "fullMismatch":
            CorruptOneLaterCrc(options); break;                                  // quick passes, full verify fails
        case "error":
            f = f with { }; // launch writes garbage vol 1 instead (set in OnLaunch below)
            break;
        case "exception":
            // OnLaunch deletes the input dir mid-candidate to force a generic throw
            break;
        case "cancellation":
            // host.Manager.Stop() after the first launch (Exit held, then released)
            break;
        case "success":
            break; // fixture CRCs already match
    }

    host.Runner.OnLaunch = l => StageProducedSet(f, l, garbage: outcome == "error");
    Task run = RunManagerAsync(host, options, outcome);       // handles Stop()/Exit choreography per outcome
    await run;

    string assembledDir = Directory.GetDirectories(Path.Combine(host.WorkDir, "output"), "assembled-*")
        .FirstOrDefault() ?? Path.Combine(host.WorkDir, "output", "assembled-none");
    bool assembledSurvives = Directory.Exists(assembledDir) && Directory.EnumerateFiles(assembledDir).Any();
    Assert.Equal(expectAssembledInWorkArea, assembledSurvives);

    bool carrierSurvives = Directory.EnumerateFiles(Path.Combine(host.WorkDir, "output"))
        .Any(p => Path.GetFileName(p).StartsWith("fake100", StringComparison.OrdinalIgnoreCase));
    Assert.Equal(expectCarrierInWorkArea, carrierSurvives);

    if (outcome == "success")
    {
        // The assembled volumes were MOVED to <work>/output under original names.
        Assert.All(f.OriginalVolumeNames, n =>
            Assert.True(File.Exists(Path.Combine(host.WorkDir, "output", Path.GetFileName(n)))));
    }
}
```

Driver helpers (written in the test file, complete at implementation; their contracts
here are exact): `StageProducedSet(fixture, launch, garbage)` copies the produced set
(or garbage bytes for `error`) to `launch.OutputFilePath`'s directory under the
launch's expected carrier names then completes/holds Exit per the outcome;
`RunManagerAsync(host, options, outcome)` starts the run and performs the
per-outcome choreography (release Exits; `host.Manager.Stop()` for `cancellation`);
`AddSecondFakeVersion(host)` creates `fake200` beside `fake100`;
`CorruptOneLaterCrc(options)` replaces the LAST volume's expected CRC with
`"FFFFFFFF"`.

- [ ] **Step 2: Matrix green; full lib suite; gate.**
- [ ] **Step 3: App.Core suite run** — `VerifiedOutputRelocator` consumes `<workRoot>/output`; if any App.Core test encodes `-patched` naming for assembly-path runs, fix the TEST, record it in the task report.
- [ ] **Step 4: Commit** `test(lib): assembly retention matrix`.

---

### Task 11: Full board + docs

- [ ] **Step 1:** Full suites — lib, App.Core, Manager (`bin3` redirect) + forced rebuild 0W/0E. Counts recorded from actual output.
- [ ] **Step 2:** `CHANGELOG.md` Unreleased entry: "RAR reconstruction now assembles output volumes from the SRR's original headers, fixing cross-platform reconstruction (e.g. Linux rar builds that omit the EXT_TIME header field); SRRs with recovery records fall back to the legacy path with a clear diagnostic."
- [ ] **Step 3:** Spec status → "rev 5 — implemented <commit>". Commit `docs: assembly feature complete`.
- [ ] **Step 4:** Report for acceptance smoke: Linux itw-gaor with `G:\WinRAR\extracted\Linux`; Windows parity re-run; rebuild `C:\Users\Paul\AppData\Local\Temp\rescene-linux2`.

---

## Self-Review (completed)

- **Spec coverage:** §1→T1/T4; §2→T2; §3→T5; §4→T6/T7/T8/T9; §5→T10; §6 legacy untouched→T6 step 3 + T7 decline test; Limitations→T2+T8 inconclusive; spec Testing 1-5,12→T3/T4/T5; 6-7→T2; 8-11→T8/T9; 13→T10; 14→T1/T2. Codex plan B1→T9 explicit; B2→T1 normalization + T8 retry-any-non-success; B3→T7 anchors/branches/guard; B4→T1 signature+local+progress pin; B5→T2 no-iterator + pre-CreateDirectory; B6→real APIs throughout + T3 shared builder + full bodies; B7→T6 latch design + corrected cases; B8→T10 matrix. A1→internal; A2→T6 doc update; A3→T4/T5 split and T7/T8/T9 split.
- **Placeholders:** every test either has a complete body or a complete fixture algorithm plus exact assertions in comment-free steps; the two remaining prose-described bodies (T3 second self-check, T10 matrix driver) state their exact assertion targets and sizes. No TBD/TODO.
- **Type consistency:** `SRRReconstructionStatus/Result`, `IPackedSource`, `ReleaseFilePackedSource`, `ProducedVolumesPackedSource`, `PreflightSet`, `SectionMatchesSet`, `AssemblyFixture(Builder)`, `IRARProcessRunner`, `FakeRunner.Launch`, `ObserveProducerAsync`, `AssembleCandidateAsync`, `FinalizeAssembledSet`, `_useAssembly`, `_inconclusiveGuidanceLogged` consistent across tasks.
