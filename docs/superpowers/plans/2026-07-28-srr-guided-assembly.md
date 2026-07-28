# SRR-Guided Volume Assembly Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reconstruct byte-perfect RAR sets on any host by splicing SRR-stored headers with the brute-forced rar output's packed stream, replacing in-place header patching whenever an SRR is available.

**Architecture:** `SRRReconstructor` gains a packed-byte source seam (`IPackedSource`) and a typed result; a preflight API declines SRRs whose required payloads are stripped. `Manager` runs the preflight once per set before the candidate loop, swaps patch+hash for assemble+hash on the assembly path (CAV and non-CAV variants), and finalizes the reconstructor's `WrittenPaths` through a dedicated transactional finalizer. A producer-runner seam makes the candidate loop testable without a rar binary.

**Tech Stack:** .NET 10, xUnit (`ReScene.Lib/ReScene.Tests`, net10.0). No new dependencies.

**Spec:** `docs/superpowers/specs/2026-07-28-srr-guided-assembly-design.md` (rev 5, codex-APPROVED). The spec is normative; where this plan and the spec disagree, the spec wins and the discrepancy must be raised.

## Global Constraints

- One top-level type per file (`docs/coding-guidelines.md`); `.editorconfig` governs style; `dotnet format` clean.
- Forced-rebuild gate: `dotnet build ReScene.Manager.slnx -c Debug -t:Rebuild` = 0 warnings / 0 errors at every task end (run from repo root; use `-p:BaseOutputPath=bin2/` and delete `bin2` after — the user's IDE locks default outputs).
- Tests likewise run with `-p:BaseOutputPath=bin3/` (delete `bin3` after).
- Lib test csproj does NOT include `System.IO` in implicit usings — new test files relying on `Path`/`File`/`Directory` are fine (the lib test project already carries `<Using Include="System.IO" />`; do not remove it).
- `SRRBlockFlags.RecoveryBlocksRemoved` is set unconditionally by every real SRR writer — it must NEVER gate behavior (spec §2).
- Preflight runs ONCE PER SET, BEFORE the candidate loop; a decline launches no producer (spec §2).
- Producer observation (awaiting the process task) is an invariant before ANY finalization or cleanup, including non-CAV success (spec §4, codex rev-4 A1).
- Naming when `RenameToOriginalNames=false`: basename replacement preserving the COMPLETE volume suffix via `RARVolumeNaming.GetBaseName` — never `Path.GetExtension` (spec §5, codex rev-3 B1).
- On Manager calls the reconstructor's internal verification is a no-op (empty `hashes`); `VerificationFailed` is unreachable there (spec §4, codex rev-3 A1).
- Commit after every task; message style `feat(lib): …` / `test(lib): …` with the session trailer used throughout this branch.

---

### Task 1: Typed result + `IPackedSource` seam in `SRRReconstructor` (behavior-preserving refactor)

**Files:**
- Create: `ReScene.Lib/ReScene/Core/SRRReconstructionStatus.cs`
- Create: `ReScene.Lib/ReScene/Core/SRRReconstructionResult.cs`
- Create: `ReScene.Lib/ReScene/Core/IO/IPackedSource.cs`
- Create: `ReScene.Lib/ReScene/Core/IO/ReleaseFilePackedSource.cs`
- Modify: `ReScene.Lib/ReScene/Core/SRRReconstructor.cs` (signature, name decode, source seam)
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs:236-260` (custom-packer call site mapping)
- Test: `ReScene.Lib/ReScene.Tests/SRRReconstructorTests.cs` (update call sites; add Unicode-name test)

**Interfaces:**
- Consumes: existing `SRRReconstructor.ReconstructAsync`, `FindSourceFile`, `CopyBytesAsync`; `RARUtils.DecodeFileName(byte[] nameBytes, bool isUnicode)` (exact call shape used by `RARPatcher.PatchStream`).
- Produces (later tasks rely on these exact shapes):

```csharp
// SRRReconstructionStatus.cs
namespace ReScene.Core;

/// <summary>Outcome of an SRR-guided reconstruction or preflight.</summary>
public enum SRRReconstructionStatus
{
    /// <summary>All requested volumes written (and verified, where CRCs were supplied).</summary>
    Success,
    /// <summary>Preflight declined: a required payload is not present in the SRR.</summary>
    UnsupportedSrr,
    /// <summary>The packed source ended before the last requested ADD_SIZE byte.</summary>
    SourceExhausted,
    /// <summary>Volumes were written but hash comparison failed (custom-packer path only —
    /// unreachable on Manager assembly calls, which pass no hashes).</summary>
    VerificationFailed,
    /// <summary>I/O or parse failure.</summary>
    Error,
}
```

```csharp
// SRRReconstructionResult.cs
namespace ReScene.Core;

/// <summary>Typed result of <see cref="SRRReconstructor"/> operations.</summary>
public sealed record SRRReconstructionResult(
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
/// Called once per archived file, in SRR order; the returned stream is positioned at the
/// file's packed byte 0 and is disposed by the reconstructor when the file's last split
/// piece has been copied.
/// </summary>
public interface IPackedSource : IDisposable
{
    Stream OpenPackedStream(string archivedFileName);
}
```

```csharp
// ReleaseFilePackedSource.cs
namespace ReScene.Core.IO;

/// <summary>
/// The custom-packer data source: the archived file's bytes ARE its packed bytes
/// (store method), read from the release input directory. Extracted verbatim from the
/// pre-seam <see cref="SRRReconstructor"/> source handling.
/// </summary>
public sealed class ReleaseFilePackedSource(string inputDirectory) : IPackedSource
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

- New reconstructor signature (Task 2 adds `PreflightSet`; Task 4 adds set filtering):

```csharp
public async Task<SRRReconstructionResult> ReconstructAsync(
    string srrFilePath,
    IPackedSource packedSource,
    string outputDirectory,
    IReadOnlyList<string> originalRARFileNames,
    HashSet<string> hashes,
    HashType hashType,
    CancellationToken cancellationToken)
```

(`inputDirectory` leaves the signature — it was only used for `FindSourceFile`, which now
lives behind `ReleaseFilePackedSource`, and for progress text; progress uses
`outputDirectory` for its directory field instead.)

- [ ] **Step 1: Create the four new files** exactly as the Produces block above (namespaces `ReScene.Core` / `ReScene.Core.IO`; file-scoped namespaces per repo style).

- [ ] **Step 2: Refactor `SRRReconstructor.ReconstructAsync`**

Inside the existing method body, replace the per-file source handling (the
`currentSourceStream` open/dispose block around lines 260-287) with the seam:

```csharp
// BEFORE (open):
//   string sourcePath = FindSourceFile(inputDirectory, archivedFileName);
//   currentSourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
// AFTER:
currentSourceStream = packedSource.OpenPackedStream(archivedFileName);
```

Replace the archived-name decode (lines ~222-247): keep the nameSize/nameOffset
arithmetic (including the LARGE offset branch) but decode with the same
Unicode-aware helper the patcher uses:

```csharp
byte[] nameBytes = new byte[nameSize];
Array.Copy(fullHeader, nameOffset, nameBytes, 0, nameSize);
archivedFileName = RARUtils.DecodeFileName(nameBytes,
    ((RARFileFlags)flags).HasFlag(RARFileFlags.Unicode));
archivedFileName = archivedFileName.Replace('\\', Path.DirectorySeparatorChar);
```

Change the return: build `SRRReconstructionResult` instead of the tuple —
`Success` when `success`, else `VerificationFailed` when `completedVolumes > 0 &&
identityComplete && !allMatched`, else `Error` with the existing log text as the
`Diagnostic`. Wrap the whole body's I/O in the existing try; on caught
`EndOfStreamException` from `CopyBytesAsync` return
`Fail(SourceExhausted, ex.Message, writtenPaths)`; on other `IOException`/
`InvalidDataException` return `Fail(Error, ex.Message, writtenPaths)`
(`OperationCanceledException` still propagates).

- [ ] **Step 3: Update the Manager custom-packer call site (Manager.cs ~241-258)**

```csharp
using var packedSource = new ReleaseFilePackedSource(options.ReleaseDirectoryPath);
SRRReconstructionResult reconResult = await reconstructor.ReconstructAsync(
    options.RAROptions.SRRFilePath,
    packedSource,
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

- [ ] **Step 4: Update `SRRReconstructorTests.cs` call sites** — mechanical: wrap the old
`inputDirectory` argument as `new ReleaseFilePackedSource(inputDirectory)` and assert on
`result.Status == SRRReconstructionStatus.Success` / `result.WrittenPaths` instead of the
tuple. Do not weaken any assertion.

- [ ] **Step 5: Add the failing Unicode-name test**

```csharp
[Fact]
public async Task ReconstructAsync_UnicodeArchivedName_ResolvesThroughTheSeam()
{
    // A file header with LHD_UNICODE set stores "name\0<encoded>"; the pre-seam ASCII
    // decode requested the ANSI fallback + garbage, so a source keyed by the DECODED
    // name (as RARStream is) would never match. The seam must hand the source the
    // Unicode-decoded name.
    using TempDir dir = new();
    string name = "náme\u00e9.bin"; // é forces the Unicode name path
    byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8];
    File.WriteAllBytes(Path.Combine(dir.Path, name), payload);

    string srr = Path.Combine(dir.Path, "u.srr");
    new SRRTestDataBuilder(srr)
        .AddHeader("ReScene.Lib")
        .AddRARFileWithHeaders("u.rar", h => h
            .AddMarker()
            .AddArchiveHeader()
            .AddUnicodeFileHeader(name, (uint)payload.Length, (uint)payload.Length)
            .AddEndArchive())
        .Build();

    var recorder = new RecordingPackedSource(name, payload);
    var reconstructor = new SRRReconstructor(new NullLogger());
    SRRReconstructionResult result = await reconstructor.ReconstructAsync(
        srr, recorder, dir.CreatePath("out"), ["u.rar"], [], HashType.CRC32, CancellationToken.None);

    Assert.Equal(SRRReconstructionStatus.Success, result.Status);
    Assert.Equal(name, recorder.RequestedName); // decoded, not the ANSI fallback
}
```

Support types in the test file (nested, private): `RecordingPackedSource : IPackedSource`
returning a `MemoryStream(payload)` and recording `RequestedName`;
`AddUnicodeFileHeader` on `RAR4HeaderBuilder` — add it in this task (emits a file header
whose name field is the RAR unicode encoding `ansi\0<packed unicode>` with
`RARFileFlags.Unicode` set; build the encoded bytes with the inverse of
`RARUtils.DecodeFileName`'s format — a minimal two-byte-per-char high-byte-page encoding
is sufficient for the test as long as `DecodeFileName` round-trips it; verify by calling
`RARUtils.DecodeFileName` on the emitted bytes inside the builder and asserting equality,
so the fixture can never drift from the decoder).

- [ ] **Step 6: Run the new test — verify it fails on the PRE-seam decode** (temporarily run it against a copy of the old ASCII decode if practical; otherwise verify it fails when `DecodeFileName` is replaced with the old ASCII line, then restore — grep-confirm the restore).

- [ ] **Step 7: Full lib suite + custom-packer regression**

Run: `dotnet test ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj -c Debug -p:BaseOutputPath=bin3/`
Expected: all green (1431 + 1 new), including every existing `SRRReconstructorTests` case.

- [ ] **Step 8: Forced-rebuild gate + commit**

```bash
git add ReScene.Lib
git commit -m "feat(lib): IPackedSource seam + typed result in SRRReconstructor"
```

---

### Task 2: Preflight API + custom-packer protection

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/SRRReconstructor.cs` (add `PreflightSet`, wire into `ReconstructAsync`)
- Modify: `ReScene.Lib/ReScene.Tests/SRRTestDataBuilder.cs` (flags overload)
- Modify: `ReScene.Lib/ReScene.Tests/RAR4HeaderBuilder.cs` (`AddProtectBlock`, `AddServiceBlock`)
- Test: `ReScene.Lib/ReScene.Tests/SRRPreflightTests.cs` (new)

**Interfaces:**
- Consumes: `SRRReconstructionResult`/`Status` (Task 1); `RAR4BlockType.Protect (0x78)`, `RARArchiveFlags.Protected (0x0040)`, `RAR4BlockType.Service (0x7A)`, `RAR4BlockType.FileHeader (0x74)`.
- Produces: `public SRRReconstructionResult PreflightSet(string srrFilePath, IReadOnlyList<string> originalRARFileNames)` — `Success` (empty `WrittenPaths`) when the selected set is assemblable; `Fail(UnsupportedSrr, reason)` otherwise. Task 6 calls this before the candidate loop.

- [ ] **Step 1: Builder support.** `SRRTestDataBuilder.AddRARFileWithHeaders` gains an
optional `ushort flags = 0x0000` parameter written into the SRR block's flags word
(callers pass `(ushort)SRRBlockFlags.RecoveryBlocksRemoved` for the real-world shape).
`RAR4HeaderBuilder` gains:

```csharp
/// <summary>Old-style recovery block (0x78) with <paramref name="dataSize"/> declared bytes.</summary>
public RAR4HeaderBuilder AddProtectBlock(uint dataSize)   // header only — data deliberately absent
/// <summary>RAR4 service block (0x7A, file-header layout) named <paramref name="name"/> ("RR", "AV", "CMT").</summary>
public RAR4HeaderBuilder AddServiceBlock(string name, uint dataSize, bool includeData)
```

`AddServiceBlock(includeData: true)` appends `dataSize` deterministic bytes after the
header (the CMT case); `false` writes the header only (the stripped AV/RR case).
`AddProtectBlock` writes a 0x78 base header with LONG_BLOCK + ADD_SIZE and no data.
(Reuse the file-header layout emission from `AddCmtServiceBlock` — extract a shared
private helper rather than duplicating.)

- [ ] **Step 2: Write the failing preflight tests**

```csharp
public class SRRPreflightTests
{
    // 1. The real-world default: flag-only RecoveryBlocksRemoved, no actual RR → ELIGIBLE.
    [Fact] public void Preflight_FlagOnlyRecoveryRemoved_IsEligible() { /* builder: AddRARFileWithHeaders("a.rar", (ushort)SRRBlockFlags.RecoveryBlocksRemoved, h => h.AddMarker().AddArchiveHeader().AddFileHeader(...).AddEndArchive()); expect Success */ }
    // 2-4. Real RR evidence → UnsupportedSrr, and NO output file/directory is created.
    [Fact] public void Preflight_ProtectedArchiveHeader_Declines() { /* AddArchiveHeader(RARArchiveFlags.Protected) */ }
    [Fact] public void Preflight_OldStyleProtectBlock_Declines() { /* AddProtectBlock(64) */ }
    [Fact] public void Preflight_RRServiceBlock_Declines() { /* AddServiceBlock("RR", 64, includeData: false) */ }
    // 5. Any stripped data-bearing non-CMT block → UnsupportedSrr.
    [Fact] public void Preflight_StrippedAvServiceBlock_Declines() { /* AddServiceBlock("AV", 16, includeData: false) */ }
    // 6. CMT with stored payload stays eligible.
    [Fact] public void Preflight_CmtWithPayload_IsEligible() { /* AddCmtServiceBlock(...) */ }
    // 7. Declines carry a human diagnostic naming the offending block.
    [Fact] public void Preflight_Diagnostic_NamesTheBlock() { /* Assert.Contains("RR", result.Diagnostic) etc. */ }
}
```

Write them fully (each ~10 lines with the builder); assert `Directory.Exists(outDir) == false`
where noted — preflight must not create anything.

- [ ] **Step 3: Run — expect FAIL** (`PreflightSet` does not exist).

- [ ] **Step 4: Implement `PreflightSet`**

A read-only walk sharing the SRR block-scan shape of `ReconstructAsync` (extract the
base-header read into a small private static iterator both use —
`IEnumerable<(long Pos, byte Type, ushort Flags, ushort HeaderSize, uint AddSize)>
ScanBlocks(Stream)` — so the two walks cannot drift). Rules, evaluated only inside
RARFile sections belonging to the selected set (until Task 4 lands set filtering, "the
selected set" is "every section"; Task 4 tightens both walks with the same helper):

```csharp
public SRRReconstructionResult PreflightSet(string srrFilePath, IReadOnlyList<string> originalRARFileNames)
{
    // decline reasons, in detection order:
    // - embedded ArchiveHeader with RARArchiveFlags.Protected  → "recovery record (protected archive)"
    // - embedded block type 0x78                               → "old-style recovery block"
    // - embedded Service block named "RR"                      → "recovery record service block"
    // - embedded Service block, name != "CMT", ADD_SIZE > 0    → $"stripped {name} service data"
    // - embedded non-file old-style block with ADD_SIZE > 0    → $"stripped block 0x{type:X2} data"
    // (FileHeader ADD_SIZE is the packed data — supplied by the IPackedSource, eligible.)
    // Service-block names sit at the same name-field offset as file headers; reuse the
    // existing name-extraction arithmetic.
}
```

Also wire the same rule into `ReconstructAsync`'s block switch: where it currently
reads `rarAddSize` bytes from the SRR for `Service`/`default` blocks, it must first
check eligibility — a non-CMT data-bearing block returns
`Fail(UnsupportedSrr, …)` instead of consuming SRR bytes that are not there (the
latent custom-packer mis-read from the spec §2).

- [ ] **Step 5: Run the new tests — PASS; full lib suite — green.**

- [ ] **Step 6: Rebuild gate + commit** `feat(lib): SRR assembly preflight + stripped-payload guard`

---

### Task 3: `ProducedVolumesPackedSource` + EXT_TIME test fixtures

**Files:**
- Create: `ReScene.Lib/ReScene/Core/IO/ProducedVolumesPackedSource.cs`
- Modify: `ReScene.Lib/ReScene.Tests/RAR4HeaderBuilder.cs` (mtime remainder bytes)
- Test: `ReScene.Lib/ReScene.Tests/ProducedVolumesPackedSourceTests.cs` (new)

**Interfaces:**
- Consumes: `RARStream(string firstRARPath, string? packedFileName)` (seekable, cross-volume; snapshots the volume list at construction).
- Produces:

```csharp
// ProducedVolumesPackedSource.cs
namespace ReScene.Core.IO;

/// <summary>
/// Packed-byte source over a brute-forced rar output set: each archived file's stream is a
/// <see cref="RARStream"/> over the produced volumes. SINGLE-SNAPSHOT by design —
/// <see cref="RARStream"/> enumerates volumes at construction and never discovers later
/// ones, so callers create a fresh source per assembly attempt and never reuse one across
/// a producer state change (spec §4).
/// </summary>
public sealed class ProducedVolumesPackedSource(string producedFirstVolumePath) : IPackedSource
{
    public Stream OpenPackedStream(string archivedFileName) =>
        new RARStream(producedFirstVolumePath, archivedFileName);

    public void Dispose()
    {
        // Streams are owned and disposed by the reconstructor.
    }
}
```

- [ ] **Step 1: `RAR4HeaderBuilder` mtime remainder.** Add
`byte[]? mtimeRemainder = null` (0–3 bytes) to `AddFileHeader`. When provided with
`RARFileFlags.ExtTime`: the mtime nibble's low two bits carry the count
(`extFlags |= (ushort)(0x1000 * 0 + count << 12)`? — NO: the mtime nibble occupies bits
15-12; present bit is 0x8000; the count is the nibble's low two bits, i.e.
`extFlags |= (ushort)(count << 12)`), and the remainder bytes are written immediately
after the flags word, before any ctime/atime DOS dates. `extTimeSize += count`. Add a
builder self-check test in `ProducedVolumesPackedSourceTests`:
a header built with a 3-byte remainder parses via `RARHeaderReader` with the correct
`HeaderSize` and its data region begins at `HeaderSize` (assembly copies headers
verbatim, so byte-level self-consistency — not WinRAR semantic fidelity — is the
requirement; state this in a comment).

- [ ] **Step 2: Failing tests for the source**

```csharp
public class ProducedVolumesPackedSourceTests
{
    // Build a 3-volume produced set (RAR4HeaderBuilder + real marker/archive/file/end
    // blocks, split flags set, distinct deterministic payload bytes per piece), then:
    [Fact] public void OpenPackedStream_ConcatenatesSplitPieces_AcrossVolumes() { /* read all; SequenceEqual the full payload */ }
    [Fact] public void OpenPackedStream_SecondFile_StartsAtItsOwnByteZero() { /* two archived files; open file B; first byte == B's payload[0] */ }
    [Fact] public void Source_IsSingleSnapshot_NewVolumeAfterConstructionIsInvisible() { /* construct source; add vol3 afterwards; reading past vol2 throws/ends (per RARStream contract) — pins the spec caveat */ }
}
```

- [ ] **Step 3: Run — FAIL (type missing) → implement (the Produces block IS the implementation) → PASS.**

- [ ] **Step 4: Full lib suite, rebuild gate, commit** `feat(lib): ProducedVolumesPackedSource + EXT_TIME fixture support`

---

### Task 4: Set filtering + core assembly byte-identity tests

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/SRRReconstructor.cs` (section filter in both walks)
- Test: `ReScene.Lib/ReScene.Tests/SRRAssemblyTests.cs` (new)

**Interfaces:**
- Consumes: everything from Tasks 1–3.
- Produces: `ReconstructAsync` honoring `originalRARFileNames` as the SET SELECTOR: RARFile sections not in the set are skipped entirely (no output stream, no source reads). Matching: separator-normalized (`\`→`/`), trimmed, `OrdinalIgnoreCase` on the relative name when the provided names are qualified; bare-basename fallback only when that basename is unique among the SRR's RARFile sections (mirrors `Manager.QualifiedKey` + `BuildExpectedInOrder` semantics). Extract as `internal static bool SectionMatchesSet(string sectionName, IReadOnlyList<string> setNames, ILookup<string, string> srrBasenames)` so `PreflightSet` (Task 2) applies the identical filter.

- [ ] **Step 1: Write the failing tests** (each fully coded; shared fixture builder at the top of the file that produces, for a given header shape, BOTH an "original" volume set + its SRR AND a "produced" set carrying the same packed payload re-split under different header sizes):

```csharp
public class SRRAssemblyTests
{
    // THE BUG (spec Problem section): originals WITH a 5-byte EXT_TIME (flags word +
    // 3-byte remainder), produced WITHOUT the field; produced vol 1 therefore packs 5
    // MORE payload bytes. Assemble from the produced set → per-volume SHA-256 equals
    // the originals'.
    [Fact] public async Task Assemble_ExtTimeDivergence_ByteIdenticalOutput()

    // Mirror direction: originals WITHOUT ext-time, produced WITH → assembled vol 1
    // needs bytes from produced vol 2 (read crosses the produced boundary).
    [Fact] public async Task Assemble_MirrorShift_ReadsAcrossProducedBoundary()

    // Two archived files, the boundary between them mid-volume; SplitBefore/After walk.
    [Fact] public async Task Assemble_MultiFile_SplitAcrossVolumes()

    // RARPadding block between sections is emitted into assembled output.
    [Fact] public async Task Assemble_PaddingBlocks_Preserved()

    // Multi-set SRR: CD1/x.rar + CD2/x.rar (IDENTICAL basenames). Assembling CD2's set
    // emits ONLY CD2 volumes and never opens a packed stream for CD1 content.
    [Fact] public async Task Assemble_MultiSet_SameBasenames_FiltersByQualifiedName()

    // Bare-name fallback is rejected when ambiguous: set selector ["x.rar"] against a
    // CD1/CD2 SRR returns Error with a diagnostic naming the ambiguity.
    [Fact] public async Task Assemble_AmbiguousBareName_Fails()
}
```

Byte-identity assertions: `Assert.Equal(SHA256.HashData(File.ReadAllBytes(orig)), SHA256.HashData(File.ReadAllBytes(assembled)))` per volume, plus a total-length equality first (better failure message).

- [ ] **Step 2: Run — the multi-set/ambiguity cases FAIL (no filter yet); divergence cases should already PASS through Tasks 1–3 machinery — if any fails, that is a real Task 1–3 bug to fix now.**

- [ ] **Step 3: Implement `SectionMatchesSet` + apply in both `ReconstructAsync` (skip non-matching RARFile sections and their embedded blocks) and `PreflightSet`.**

- [ ] **Step 4: All tests PASS; full lib suite; rebuild gate; commit** `feat(lib): SRR-guided assembly core - set filtering + byte-identity proven`

---

### Task 5: Manager producer seam + observation invariant (behavior-preserving)

**Files:**
- Create: `ReScene.Lib/ReScene/Core/Diagnostics/IRARProcessRunner.cs`
- Create: `ReScene.Lib/ReScene/Core/Diagnostics/RealRARProcessRunner.cs`
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs` (ctor seam; both launch sites; observation helper; non-CAV grace fix)
- Test: `ReScene.Lib/ReScene.Tests/ManagerProducerLifecycleTests.cs` (new)

**Interfaces:**
- Produces:

```csharp
// IRARProcessRunner.cs
namespace ReScene.Core.Diagnostics;

/// <summary>Seam over rar process execution so the candidate loop is testable without a
/// rar binary. The real implementation wraps <see cref="RARProcess"/> verbatim.</summary>
internal interface IRARProcessRunner
{
    /// <summary>Starts rar for one candidate; the returned task completes with the exit
    /// code when the process ends (or its cancellation is observed).</summary>
    Task<int> RunAsync(string rarExePath, string inputDirectory, string outputFilePath,
        IEnumerable<string> arguments, LogTarget logTarget,
        Action<RARProcess>? onCreated, CancellationToken cancellationToken);
}
```

`RealRARProcessRunner` constructs `RARProcess` exactly as the two current call sites do
and invokes `onCreated` (Manager uses it for `_processLogManager.OpenLog` +
`SubscribeToProcessEvents`). `Manager` gains `internal Manager(IReSceneLogger? logger,
IRARProcessRunner runner)` chained from the public ctor with
`new RealRARProcessRunner()`.

- Observation invariant helper (private, used by EVERY exit — non-winning AND winning, CAV and non-CAV):

```csharp
/// <summary>
/// Cancels (if requested) and OBSERVES the producer: awaits the process task to actual
/// completion — no grace-timeout abandonment — swallowing only the cancellation result.
/// Invariant (spec §4 + codex rev-4 A1): no finalization, deletion, or next-candidate
/// launch may run while a producer task is unobserved.
/// </summary>
private static async Task ObserveProducerAsync(Task<int>? processTask,
    CancellationTokenSource? processCts, bool cancelFirst)
{
    if (processTask is null) { return; }
    if (cancelFirst) { processCts?.Cancel(); }
    try { _ = await processTask.ConfigureAwait(false); }
    catch (OperationCanceledException) { /* observed */ }
}
```

- [ ] **Step 1: Introduce the seam** — replace both `new RARProcess(...)` sites (CAV inline
~line 746 and `RARCompressDirectoryAsync` ~line 449) with `_runner.RunAsync(...)`,
passing an `onCreated` that performs the current OpenLog/Subscribe calls. NO behavior
change; the real runner reproduces today's construction exactly.

- [ ] **Step 2: Replace ad-hoc kills with `ObserveProducerAsync`** at: the
`actualRARFilePath == null` branch, the quick-mismatch branch, the generic `catch`
(currently observes nothing — Manager.cs:968/999), cancellation exits, AND
`RARCompressDirectoryAsync`'s early-termination tail — the current
`Task.Delay(1000)` grace race is replaced by a full await of the process task
(codex rev-4 A1: the grace could return with the task incomplete).

- [ ] **Step 3: Failing lifecycle tests** with a `FakeRunner : IRARProcessRunner` (drops
pre-built volume files into the output dir on a controllable schedule; exposes
`TaskCompletionSource<int>` per launch + `ObservedByAwait` flags):

```csharp
public class ManagerProducerLifecycleTests
{
    [Fact] public async Task NonCav_Success_AwaitsProcessTaskBeforeReturning()
    [Fact] public async Task Cav_QuickMismatch_CancelsAndObservesBeforeNextCandidate()
    [Fact] public async Task Exception_MidCandidate_ObservesProducerBeforePropagating()
    [Fact] public async Task UserCancellation_ObservesProducer()
}
```

(Drive `Manager.BruteForce…` through `BruteForceOptions` pointing at temp dirs with a
single fake "version"; the fake runner writes a minimal valid single-volume rar so the
legacy path completes. These tests run the LEGACY path — the seam must not change it.)

- [ ] **Step 4: Tests pass; FULL lib suite green (the seam is behavior-preserving — any legacy test failure is a seam bug); rebuild gate; commit** `refactor(lib): producer runner seam + observation invariant`

---

### Task 6: Manager assembly wiring (preflight-before-loop, CAV + non-CAV flows)

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs` (engagement, quick check, retry, statuses, logging)
- Test: `ReScene.Lib/ReScene.Tests/ManagerAssemblyFlowTests.cs` (new)

**Interfaces:**
- Consumes: `PreflightSet` (T2), `ProducedVolumesPackedSource` (T3), `ReconstructAsync` (T4), `IRARProcessRunner`/`ObserveProducerAsync` (T5).
- Produces: private helpers on `Manager`:

```csharp
/// <summary>True when this run uses SRR-guided assembly (spec §4 engagement rule).
/// Set once per set, before the candidate loop.</summary>
private bool _useAssembly;

/// <summary>Assembles the ORIGINAL volume subset [0..volumeCount) for the current
/// candidate into <paramref name="assemblyDir"/> from the produced set at
/// <paramref name="producedFirstVolume"/>. Fresh ProducedVolumesPackedSource per call
/// (single-snapshot). volumeCount = 1 for the quick gate, int.MaxValue for the full set.
/// Returns the reconstructor result verbatim.</summary>
private async Task<SRRReconstructionResult> AssembleCandidateAsync(
    BruteForceOptions options, string producedFirstVolume, string assemblyDir,
    int volumeCount, CancellationToken ct)
```

(`volumeCount` limiting = pass `originalRARFileNames.Take(volumeCount).ToList()` as the
set selector — the reconstructor already stops after its selected sections.)

- Engagement (once, before the loop, after `BuildExpectedInOrder`):

```csharp
_useAssembly = false;
if (!string.IsNullOrEmpty(options.RAROptions.SRRFilePath)
    && options.RAROptions.CustomPackerDetected == SRR.CustomPackerType.None)
{
    SRRReconstructionResult pre = new SRRReconstructor(_logger)
        .PreflightSet(options.RAROptions.SRRFilePath, options.RAROptions.OriginalRARFileNames);
    _useAssembly = pre.Status == SRRReconstructionStatus.Success;
    if (!_useAssembly)
    {
        _logger.Information(this,
            $"SRR-guided assembly unavailable ({pre.Diagnostic}) — trying legacy reconstruction for this set",
            LogTarget.System);
    }
    else
    {
        _logger.Information(this, "SRR-guided assembly engaged (headers from SRR, data from rar output)", LogTarget.System);
    }
}
```

- Candidate flow when `_useAssembly` (replacing the `PatchRARFilesHostOS` + hash block):

```csharp
string assemblyDir = Path.Combine(rarOutputDirectoryPath, $"assembled-{candidateSlug}");
SRRReconstructionResult quick = await AssembleCandidateAsync(options, actualRARFilePath, assemblyDir, 1, _cts.Token).ConfigureAwait(false);
string? quickHash = quick.Status == SRRReconstructionStatus.Success && quick.WrittenPaths.Count == 1
    ? HashCalculator.Calculate(options.HashType, quick.WrittenPaths[0])
    : null;
bool producerRunning = runningProcessTask is { IsCompleted: false };
if (quickHash is null && producerRunning)
{
    // Incomplete snapshot (spec §4): ANY failure while the producer runs — short read,
    // missing/incomplete header, sharing/parse error, SourceExhausted — awaits
    // completion and retries ONCE with a fresh source.
    await ObserveProducerAsync(runningProcessTask, processCts, cancelFirst: false).ConfigureAwait(false);
    quick = await AssembleCandidateAsync(options, actualRARFilePath, assemblyDir, 1, _cts.Token).ConfigureAwait(false);
    quickHash = quick.Status == SRRReconstructionStatus.Success && quick.WrittenPaths.Count == 1
        ? HashCalculator.Calculate(options.HashType, quick.WrittenPaths[0]) : null;
}
if (quickHash is null)
{
    // Post-retry mapping (spec §4): SourceExhausted (CAV, producer done) = no-match;
    // in non-CAV the same status = INCONCLUSIVE (mirror shift needs vol-2 bytes that
    // were never written) — log guidance once per set; Error = failed combination
    // through the existing error-row path.
}
_logger.Information(this, $"Assembled hash for {quick.WrittenPaths.FirstOrDefault() ?? assemblyDir}: {quickHash} (match: {quickHash != null && options.Hashes.Contains(quickHash)})", LogTarget.Phase2);
```

Full-set assembly on quick match (CAV only): `ObserveProducerAsync(...,
cancelFirst: false)` (let rar finish), then `AssembleCandidateAsync(..., int.MaxValue, …)`
into the same `assemblyDir`, then the EXISTING `VolumeMatchEvaluator` block pointed at
`quick.WrittenPaths` (CRC32, exactly as today; skipped identically when
`BuildExpectedInOrder` is empty — first-hash-only parity, spec §4). The
`*** MATCH FOUND ***` summary prints `SRR-guided assembly` instead of the `(patched)`
note. Non-CAV on quick match: report success through the legacy first-volume path with
the single assembled volume.

Retention on this path: non-matching candidates delete `assemblyDir` under the same
`DeleteRARFiles` / `DeleteDuplicateCRCFiles` flags that govern the carrier volumes
today (both artifact classes, spec §5); carriers keep their existing handling.

- [ ] **Step 1: Failing flow tests** (FakeRunner from T5 drops pre-built "produced" sets from the T4 fixture builder; SRR + expected CRCs from the same builder):

```csharp
public class ManagerAssemblyFlowTests
{
    [Fact] public async Task Preflight_Declines_LegacyRunsFromCandidateOne_NoProducerCancelled()
    [Fact] public async Task Cav_QuickMatch_FullAssembly_PerVolumeCrcVerify_Succeeds()   // the EXTTIME scenario end-to-end
    [Fact] public async Task Cav_IncompleteSnapshot_RetriesOnceWithFreshSource()          // vol2 EXISTS at trigger, tail bytes appear only at completion (spec test 8 fixture)
    [Fact] public async Task Cav_PostRetry_SourceExhausted_IsNoMatch()
    [Fact] public async Task Cav_PersistentParseError_IsFailedCombination()
    [Fact] public async Task NonCav_FirstVolumeAssembly_Succeeds()
    [Fact] public async Task NonCav_MirrorShift_IsInconclusive_WithGuidanceLog()
    [Fact] public async Task NoCrcMap_FirstHashOnly_ParityPreserved()
    [Fact] public async Task NonMatching_AssemblyDir_DeletedUnderDeleteFlags()
}
```

- [ ] **Step 2: Implement; iterate to green; every legacy-path test from T5 must stay green.**

- [ ] **Step 3: Full lib suite; rebuild gate; commit** `feat(lib): SRR-guided assembly engaged in the brute-force flow`

---

### Task 7: Assembled-set finalizer + retention + app relocation compatibility

**Files:**
- Modify: `ReScene.Lib/ReScene/Core/Manager.cs` (add `FinalizeAssembledSet`; call from the assembly win path)
- Test: `ReScene.Lib/ReScene.Tests/ManagerAssemblyFinalizeTests.cs` (new)

**Interfaces:**
- Consumes: `ExecuteMovePlan` (existing transactional mover), `RARVolumeNaming.GetBaseName`, `RenameToOriginalNames`, `DeleteRARFiles`.
- Produces:

```csharp
/// <summary>
/// Finalizes an assembly win: moves the reconstructor's ordered WrittenPaths — verbatim,
/// no volume rediscovery, no patching — transactionally into <paramref name="rarOutputDir"/>
/// (the app's VerifiedOutputRelocator only accepts committed files there). Naming (spec §5):
/// RenameToOriginalNames=true → the original volume names (the assembled files already
/// carry them); false → basename replacement preserving the COMPLETE volume suffix:
/// "foo.part01.rar" → "&lt;slug&gt;-assembled.part01.rar", "foo.r00" → "&lt;slug&gt;-assembled.r00"
/// (RARVolumeNaming.GetBaseName — never Path.GetExtension, which collapses .partNN.rar).
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
            string suffix = fileName[baseName.Length..];           // ".part01.rar" / ".r00" / ".rar"
            fileName = $"{candidateSlug}-assembled{suffix}";
        }
        plan.Add((src, Path.Combine(rarOutputDir, fileName)));
    }
    return ExecuteMovePlan(plan);
}
```

Success-path retention (spec §5): after a `Complete` finalize, carrier volumes are
deleted when `DeleteRARFiles` is true, else retained in the work area; the (now empty)
`assemblyDir` is removed.

- [ ] **Step 1: Failing tests**

```csharp
public class ManagerAssemblyFinalizeTests
{
    [Fact] public void Finalize_OriginalNames_PlacesUnderWorkOutput()          // paths == <workRoot>/output/<original names>
    [Fact] public void Finalize_GeneratedNames_PreservesPartNNSuffix()         // foo.part01/02.rar → slug-assembled.part01/02.rar — DISTINCT
    [Fact] public void Finalize_GeneratedNames_OldStyleSuffixes()              // .rar/.r00/.r01
    [Fact] public void Finalize_CollisionWithRetainedCarrier_NeverOverwrites() // DeleteRARFiles=false + generated names — no dest collision
    [Fact] public void Finalize_Transactional_RollsBackOnOccupiedDestination()
    [Fact] public void Success_DeleteRARFilesTrue_RemovesCarriers_FalseRetains()
}
```

- [ ] **Step 2: Implement; green; full lib suite; rebuild gate.**

- [ ] **Step 3: App-side compatibility check (no app code change expected):** run the App.Core suite — `VerifiedOutputRelocator` consumes `<workRoot>/output` committed files; the finalizer writes exactly there. If any App.Core test encodes the old `-patched` naming for assembly-path runs, fix the TEST expectation, not the naming policy, and record it in the task report.

- [ ] **Step 4: Commit** `feat(lib): assembled-set finalizer with suffix-preserving naming + retention`

---

### Task 8: Full-board verification + docs

**Files:**
- Modify: `docs/superpowers/specs/2026-07-28-srr-guided-assembly-design.md` (status line → implemented)
- Modify: `CHANGELOG.md` (Unreleased entry)
- Test: everything

- [ ] **Step 1: Full suites** — lib, App.Core, Manager (`-p:BaseOutputPath=bin3/`), forced rebuild gate 0W/0E. All green, counts recorded from actual output (counts are approximate until measured).
- [ ] **Step 2: CHANGELOG Unreleased entry** (concise: "RAR reconstruction now assembles output volumes from the SRR's original headers, fixing cross-platform reconstruction (e.g. Linux rar builds that omit the EXT_TIME header field); recovery-record SRRs fall back to the legacy path with a clear diagnostic.")
- [ ] **Step 3: Spec status line → "rev 5 — implemented <commit>"; commit** `docs: assembly feature complete`
- [ ] **Step 4: Report for acceptance smoke** — remind the user: Linux itw-gaor re-run with `G:\WinRAR\extracted\Linux`, Windows re-run for parity; rebuild the Linux artifact at `C:\Users\Paul\AppData\Local\Temp\rescene-linux2`.

---

## Self-Review (completed)

- **Spec coverage:** §1 seam+result→T1; §2 preflight→T2; §3 filtering→T4; §4 flows/retry/statuses/lifecycle→T5+T6; §5 finalizer/naming/retention→T7; §6 patch-path untouched→T5/T6 legacy-green gates; Limitations→T2 declines + T6 inconclusive; Testing 1–5,12→T3/T4, 6–7→T2, 8–11→T6, 13→T7, 14→T1/T2; infra→T2/T3/T5. No uncovered requirement found.
- **Placeholders:** test bodies in T2/T5/T6/T7 are named cases with their fixture strategy stated inline and full assertions where the mechanics are non-obvious (T1 Unicode, T4 byte-identity); the implementer writes the remaining bodies against those exact names — no TBD/TODO markers exist.
- **Type consistency:** `SRRReconstructionResult/Status`, `IPackedSource`, `ReleaseFilePackedSource`, `ProducedVolumesPackedSource`, `PreflightSet`, `SectionMatchesSet`, `IRARProcessRunner`, `ObserveProducerAsync`, `AssembleCandidateAsync`, `FinalizeAssembledSet` — names and shapes match across all tasks.
