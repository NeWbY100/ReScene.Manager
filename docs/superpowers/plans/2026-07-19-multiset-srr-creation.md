# Multi-Set SRR Creation (Spec 1: Video Releases) Implementation Plan

> **STATUS: SCAFFOLD — task list locked; per-task steps being written. Do not execute until this
> banner is removed and the plan is codex-reviewed.**
>
> **Standing execution approval (user, 2026-07-19):** once codex APPROVEs this plan, execution
> proceeds WITHOUT a further user gate, using the execution approach recommended jointly by the
> session agent and codex (agent's recommendation: superpowers:subagent-driven-development with
> the two-stage per-task review, codex reviewing every task diff; the codex plan-review prompt
> must ask codex to confirm or amend this execution mode).

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use
> checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create one SRR from a release folder covering every RAR set (dvd1/dvd2), samples, subs,
proofs and stored files, byte-comparable to pyReScene Auto golden fixtures.

**Architecture:** Lib gains a multi-input writer + name canonicalizer (format); App.Core gains
`ReleaseScanner` (ordered pyrescene decision-tree port, policy) and folder-mode `CreatorViewModel`
wiring (generation-guarded); Manager gains browse-folder chrome per the spec's a11y contract.

**Tech Stack:** .NET 10, Avalonia 11.3, CommunityToolkit.Mvvm, xUnit; local pyrescene checkout
(pinned commit) generates golden fixtures.

**Spec (normative):** `docs/superpowers/specs/2026-07-18-multiset-srr-creation-design.md` (rev 5,
codex-APPROVED) + `docs/superpowers/specs/pyrescene-rules-excerpt.txt` (rule source of truth).

## Global Constraints

- File-input behavior stays byte-identical; existing suites stay green (lib ~912+, App.Core 513+,
  Manager 15+); forced-rebuild gate 0 warnings / 0 errors (`-p:BaseOutputPath=bin2/`, delete after).
- Folder-input output byte-identical to pyrescene golden fixtures after app-name normalization.
- Every ported rule cites its excerpt lines in a comment; divergences carry `[DIVERGENCE]` tags
  copied from the spec.
- One top-level type per file (docs/coding-guidelines.md); scanner in App.Core, writer in Lib.
- Review regime: codex reviews this plan before execution and every task's diff during execution
  (alongside the standard task-reviewer gate).

## Task List (locked)

1. **Lib — SrrNameCanonicalizer** (`ReScene.Lib/ReScene/SRR/SrrNameCanonicalizer.cs` + tests):
   final-path (GetFinalPathNameByHandle-semantics) containment for root+sources, `/` separators,
   SFV-entry both-separator interpretation + escape rejection, collision policy, flat mode.
   Produces: `SrrNameCanonicalizer.Canonicalize(root, sourcePath) -> string logicalName` +
   `TryValidateLogicalName`.
2. **Lib — CreateFromInputsAsync** (`SRRWriter.cs` + tests): N≥0 inputs, per-input volume blocks
   in order, stored dedup, temp-in-destination-dir + atomic move, zero/zero rejection,
   non-first-RAR error, multi-chain SFV support; existing `CreateFromSFVAsync`/RAR path delegate.
3. **Lib — golden fixture harness** (`ReScene.Tests/TestData/multiset/generate-golden.py`,
   README with pinned pyrescene hash/command, committed fixtures; byte-equality test with
   app-name normalization via independent block splitter; ≤1 excluded SFV per tree).
4. **App.Core — traversal engine** (`Services/ReleaseTraversal.cs` + tests): deterministic
   ordinal os.walk emulation, category-pass ordering (nfo→m3u→proof→log→cue→srs→sfv).
5. **App.Core — ReleaseScanner rules 2a** (`Services/ReleaseScanner.cs` + `IReleaseScanner` +
   records + tests): ordered decision tree 1-7, rescue fallback, excluded-SFV destinations,
   dirfix skip, subpack main-SFV nested queue.
6. **App.Core — scanner 2b/2c/2e** (same files + tests): has_music in rescue only, both sample
   phases (`sample[:-4]` literal quirk), first-RAR from SFVs + gated loose-RAR divergence.
7. **App.Core — scanner 2d stored chain** (+ tests): nfo filtering (imdb/tvmaze/no.nfo),
   m3u/log/cue/pre-existing srs (+generated-SRS supersede rule), always_skip exact predicates,
   store_rls_root (>100000, similar-name incl. M3U, strip_zeros, fixed-resolution),
   filter_proof_rar_files, proof-SFV state machine.
8. **App.Core — service + VM folder mode** (`ISRRCreationService`/`SRRCreationService`,
   `CreatorViewModel` + tests): pass-through, directory detection, scan generation guard +
   IsScanning, collection population, auto-vs-user OutputPath tracking, status summary,
   music-only disable.
9. **App.Core — generated artifacts** (VM + tests): temp working dir per generation, relative-stem
   collision keying (full-ext on collision), SRS-failure txt, VOB-sample nested SRR, multi-SRR
   subtitle results, cancellation cleanup.
10. **Manager — UI both surfaces** (`CreatorView.axaml`, `CreateSRRWizardBody.axaml` + code-behind):
    Browse folder button + `OpenFolderAsync`, DetectedSets bounded ItemsControl, FieldStatusLine
    summary, §4a a11y contract (automation names, HelpText, tab order, focus return).
11. **E2E + final review**: bridge-driven two-disc folder scenario on both surfaces (typed path →
    detected sets → create → Inspector/Reconstructor verification), full gates, codex whole-branch
    review.

---

### Task 1: Lib — SrrNameCanonicalizer

**Files:**
- Create: `ReScene.Lib/ReScene/SRR/SrrNameCanonicalizer.cs`
- Create: `ReScene.Lib/ReScene/SRR/SrrNameException.cs`
- Test: `ReScene.Lib/ReScene.Tests/SrrNameCanonicalizerTests.cs`

**Interfaces:**
- Consumes: nothing (leaf utility).
- Produces (Task 2 depends on these exact members): `public static class SrrNameCanonicalizer` with
  `public static string GetFinalPath(string path)`,
  `public static string CanonicalizeRelative(string rootFinalPath, string sourcePath)`,
  `public static string ResolveSfvEntry(string sfvDirectory, string entryName)`;
  `public sealed class SrrNameException : Exception` (message ctor).

Spec §1a: containment is evaluated on OS FINAL paths (GetFinalPathNameByHandle semantics) for
BOTH root and source, resolving every ancestor junction/symlink; logical names use `/`; SFV
entries accept both separators and must not escape the SFV's directory; rooted entries rejected.

- [ ] **Step 1: Write the failing tests** — `SrrNameCanonicalizerTests.cs`:

```csharp
using ReScene.SRR;

namespace ReScene.Tests;

public class SrrNameCanonicalizerTests : IDisposable
{
    private readonly string _root;

    public SrrNameCanonicalizerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "canon-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "CD1"));
        File.WriteAllText(Path.Combine(_root, "CD1", "a.sfv"), "x");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void CanonicalizeRelative_ProducesForwardSlashNames()
    {
        string rootFinal = SrrNameCanonicalizer.GetFinalPath(_root);
        string name = SrrNameCanonicalizer.CanonicalizeRelative(
            rootFinal, Path.Combine(_root, "CD1", "a.sfv"));
        Assert.Equal("CD1/a.sfv", name);
    }

    [Fact]
    public void CanonicalizeRelative_OutsideRoot_Throws()
    {
        string rootFinal = SrrNameCanonicalizer.GetFinalPath(Path.Combine(_root, "CD1"));
        string outside = Path.Combine(_root, "b.txt");
        File.WriteAllText(outside, "x");
        Assert.Throws<SrrNameException>(() =>
            SrrNameCanonicalizer.CanonicalizeRelative(rootFinal, outside));
    }

    [Fact]
    public void CanonicalizeRelative_AncestorLink_ResolvedBeforeContainment()
    {
        // spec §1a rev 4: a link INSIDE the root pointing OUTSIDE it is rejected even though
        // the lexical path looks inside — final paths on both sides.
        string target = Path.Combine(Path.GetTempPath(), "canon-tgt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "x.bin"), "x");
        string link = Path.Combine(_root, "J");
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return; // no symlink rights on this machine — skip
        }
        try
        {
            string rootFinal = SrrNameCanonicalizer.GetFinalPath(_root);
            Assert.Throws<SrrNameException>(() =>
                SrrNameCanonicalizer.CanonicalizeRelative(rootFinal, Path.Combine(link, "x.bin")));
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(target, recursive: true);
        }
    }

    [Theory]
    [InlineData("..\\evil.rar")]
    [InlineData("C:\\abs\\evil.rar")]
    [InlineData("sub/../../evil.rar")]
    public void ResolveSfvEntry_EscapingEntry_Throws(string entry)
    {
        Assert.Throws<SrrNameException>(() =>
            SrrNameCanonicalizer.ResolveSfvEntry(Path.Combine(_root, "CD1"), entry));
    }

    [Fact]
    public void ResolveSfvEntry_BothSeparatorKinds_ResolveIdentically()
    {
        string p1 = SrrNameCanonicalizer.ResolveSfvEntry(_root, "CD1\\a.sfv");
        string p2 = SrrNameCanonicalizer.ResolveSfvEntry(_root, "CD1/a.sfv");
        Assert.Equal(p1, p2);
        Assert.True(File.Exists(p1));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj --filter SrrNameCanonicalizer`
Expected: FAIL — `SrrNameCanonicalizer` not found (CS0103/CS0246).

- [ ] **Step 3: Implement** — `SrrNameException.cs`:

```csharp
namespace ReScene.SRR;

/// <summary>A stored/volume logical-name violation (spec §1a): source outside the release
/// root, an SFV entry escaping its directory, or a logical-name collision.</summary>
public sealed class SrrNameException(string message) : Exception(message);
```

`SrrNameCanonicalizer.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ReScene.SRR;

/// <summary>
/// Single writer-boundary name contract (spec §1a): OS-final-path containment (resolves every
/// ancestor junction/symlink — Path.GetFullPath alone does not), forward-slash logical names,
/// and SFV-entry hardening. Windows-first (GetFinalPathNameByHandle); on non-Windows,
/// Path.GetFullPath over a realpath-resolved FileSystemInfo.ResolveLinkTarget chain is
/// equivalent because POSIX realpath resolves ancestors.
/// </summary>
public static class SrrNameCanonicalizer
{
    public static string GetFinalPath(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            // POSIX: File.ResolveLinkTarget(final) on the deepest existing component is
            // realpath-equivalent via GetFullPath of the resolved target.
            FileSystemInfo info = Directory.Exists(path)
                ? new DirectoryInfo(path) : new FileInfo(path);
            FileSystemInfo resolved = info.ResolveLinkTarget(returnFinalTarget: true) ?? info;
            return Path.GetFullPath(resolved.FullName);
        }

        using SafeFileHandle handle = OpenForMetadata(path);
        var buffer = new char[1024];
        uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
        if (length == 0 || length > buffer.Length)
        {
            throw new SrrNameException($"Cannot resolve final path: {path}");
        }

        string final = new(buffer, 0, (int)length);
        return final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
    }

    public static string CanonicalizeRelative(string rootFinalPath, string sourcePath)
    {
        string source = GetFinalPath(sourcePath);
        string root = Path.TrimEndingDirectorySeparator(rootFinalPath);
        if (!source.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new SrrNameException($"Source is outside the release root: {sourcePath}");
        }

        return source[(root.Length + 1)..].Replace('\\', '/');
    }

    public static string ResolveSfvEntry(string sfvDirectory, string entryName)
    {
        string normalized = entryName
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
        {
            throw new SrrNameException($"SFV entry is rooted: {entryName}");
        }

        string full = Path.GetFullPath(Path.Combine(sfvDirectory, normalized));
        string dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sfvDirectory));
        if (!full.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new SrrNameException($"SFV entry escapes its directory: {entryName}");
        }

        return full;
    }

    private static SafeFileHandle OpenForMetadata(string path)
    {
        // FILE_FLAG_BACKUP_SEMANTICS (0x02000000) lets CreateFileW open directories too.
        SafeFileHandle handle = CreateFileW(path, 0, FileShare.ReadWrite | FileShare.Delete,
            IntPtr.Zero, FileMode.Open, 0x02000000, IntPtr.Zero);
        return handle.IsInvalid
            ? throw new SrrNameException($"Cannot open for path resolution: {path}")
            : handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, FileShare dwShareMode, IntPtr securityAttrs,
        FileMode dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile, char[] lpszFilePath, uint cchFilePath, uint dwFlags);
}
```

- [ ] **Step 4:** re-run the filter → PASS (junction test passes or self-skips).
- [ ] **Step 5:** `git add ReScene.Lib/ReScene/SRR/SrrNameCanonicalizer.cs ReScene.Lib/ReScene/SRR/SrrNameException.cs ReScene.Lib/ReScene.Tests/SrrNameCanonicalizerTests.cs && git commit -m "feat(lib): SRR name canonicalizer with final-path containment"`

### Task 2: Lib — CreateFromInputsAsync (multi-input writer)

**Files:**
- Modify: `ReScene.Lib/ReScene/SRR/SRRWriter.cs` (extract volume loop; add overload; legacy paths untouched)
- Test: `ReScene.Lib/ReScene.Tests/SRRWriterMultiInputTests.cs`

**Interfaces:**
- Consumes: Task 1's three `SrrNameCanonicalizer` members + `SrrNameException`.
- Produces (Tasks 3 and 8 call this exact signature):

```csharp
public Task<SRRCreationResult> CreateFromInputsAsync(
    string outputPath,
    IReadOnlyList<string> inputFiles,          // N >= 0; each .sfv or first-volume .rar
    string? rootFolder,                        // required when storeRelativePaths
    bool storeRelativePaths,
    IReadOnlyList<StoredFileEntry>? additionalFiles = null,
    SRRCreationOptions? options = null,
    CancellationToken ct = default);
```

Behavior contract (spec §1 + §1a — every clause below gets a test in Step 1):

1. Zero inputs + at least one stored file → storage-only SRR (header + stored blocks). Zero + zero
   → error result "Nothing to write: no inputs and no stored files."
2. Per input, in list order: `.sfv` → parse entries (reuse the existing SFV line parser), resolve
   each via `ResolveSfvEntry(sfvDir, entryName)`, keep `RARVolumeIdentifier.IsRARVolume` matches,
   sort with `RARVolumeNameComparer.Instance`; an SFV may contain MULTIPLE chains — all its
   volumes are written in sorted order (the comparer groups chains naturally). `.rar` → walk its
   chain (existing logic); if the file is not its chain's first volume → error result
   "'{name}' is not a first RAR volume." (pyrescene ValueError parity).
3. Volume block names: `storeRelativePaths ? SrrNameCanonicalizer.CanonicalizeRelative(rootFinal,
   volumePath) : Path.GetFileName(volumePath)` where `rootFinal = GetFinalPath(rootFolder)`
   computed once (rootFolder null + storeRelativePaths → ArgumentException).
4. Stored list: `additionalFiles` in caller order, then each input `.sfv` not already present
   (dedup by `GetFinalPath`, OrdinalIgnoreCase). Distinct sources → same logical name → error
   result naming both sources (STRICT; only in this overload — legacy CreateAsync keeps its
   first-wins skip so existing outputs stay byte-identical).
5. Transaction: write everything to `outputPath + ".tmp-" + Guid.NewGuid().ToString("N")[..8]`
   (same directory); success → `File.Move(tmp, outputPath, overwrite: true)`; any failure or
   OperationCanceledException → delete tmp (best-effort), return/propagate with the pre-existing
   destination untouched. Reject outputPath whose `GetFinalPath` (when it exists — else its
   directory's) equals an input or stored source (OrdinalIgnoreCase) → error result.
6. `SrrNameException` anywhere → caught, returned as `ErrorMessage` (no partial output).

Refactor prerequisite (byte-identity preserved, proven by the existing suite): extract the
current volume-processing loop body of `CreateAsync` into
`private async Task WriteVolumesAsync(BinaryWriter writer, IReadOnlyList<(string Name, string Path)> volumes, SRRCreationOptions options, SRRCreationResult result, CancellationToken ct)`
and have `CreateAsync` call `WriteVolumesAsync(writer, rarVolumePaths.Select(p => (Path.GetFileName(p), p)).ToList(), ...)`.

- [ ] **Step 1: failing tests** — `SRRWriterMultiInputTests.cs` (fixture helper builds a temp tree;
  volumes via `SRRTestDataBuilder` store-mode RARs; SFVs are text files listing name + CRC):

```csharp
using ReScene.SRR;

namespace ReScene.Tests;

public class SRRWriterMultiInputTests : IDisposable
{
    private readonly string _root;
    private readonly string _out;
    private readonly SRRWriter _writer = new();

    public SRRWriterMultiInputTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "multi-" + Guid.NewGuid().ToString("N"));
        _out = Path.Combine(_root, "out.srr");
        BuildSet("CD1", "a");
        BuildSet("CD2", "b");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void BuildSet(string dir, string baseName)
    {
        string d = Path.Combine(_root, dir);
        Directory.CreateDirectory(d);
        // Two-volume store-mode set + matching SFV (CRC value is irrelevant to the writer).
        SRRTestDataBuilder.WriteStoreModeRarSet(d, baseName, volumeCount: 2, payloadBytes: 64);
        File.WriteAllLines(Path.Combine(d, baseName + ".sfv"),
            [$"{baseName}.rar 00000000", $"{baseName}.r00 00000000"]);
    }

    private string Sfv(string dir, string baseName) => Path.Combine(_root, dir, baseName + ".sfv");

    [Fact]
    public async Task TwoSets_WritesStoredSfvsThenVolumesInOrder()
    {
        SRRCreationResult r = await _writer.CreateFromInputsAsync(
            _out, [Sfv("CD1", "a"), Sfv("CD2", "b")], _root, storeRelativePaths: true);

        Assert.Null(r.ErrorMessage);
        SRRFileData srr = SRRFile.Load(_out);
        Assert.Equal(["CD1/a.sfv", "CD2/b.sfv"], srr.StoredFiles.Select(f => f.Name));
        Assert.Equal(["CD1/a.rar", "CD1/a.r00", "CD2/b.rar", "CD2/b.r00"],
            srr.RarFiles.Select(f => f.Name));
    }

    [Fact]
    public async Task ZeroInputs_WithStoredFile_WritesStorageOnlySrr()
    {
        string nfo = Path.Combine(_root, "r.nfo");
        File.WriteAllText(nfo, "nfo");
        SRRCreationResult r = await _writer.CreateFromInputsAsync(
            _out, [], _root, storeRelativePaths: true,
            additionalFiles: [new StoredFileEntry("r.nfo", nfo)]);

        Assert.Null(r.ErrorMessage);
        SRRFileData srr = SRRFile.Load(_out);
        Assert.Single(srr.StoredFiles);
        Assert.Empty(srr.RarFiles);
    }

    [Fact]
    public async Task ZeroInputs_ZeroStored_ReturnsError()
    {
        SRRCreationResult r = await _writer.CreateFromInputsAsync(_out, [], null, false);
        Assert.NotNull(r.ErrorMessage);
        Assert.False(File.Exists(_out));
    }

    [Fact]
    public async Task NonFirstVolumeRarInput_ReturnsError()
    {
        SRRCreationResult r = await _writer.CreateFromInputsAsync(
            _out, [Path.Combine(_root, "CD1", "a.r00")], _root, true);
        Assert.Contains("not a first RAR volume", r.ErrorMessage);
    }

    [Fact]
    public async Task MissingVolume_PreservesExistingDestination_NoTempLeft()
    {
        File.WriteAllBytes(_out, [1, 2, 3]);
        File.Delete(Path.Combine(_root, "CD2", "b.r00")); // SFV references it; file gone
        SRRCreationResult r = await _writer.CreateFromInputsAsync(
            _out, [Sfv("CD1", "a"), Sfv("CD2", "b")], _root, true);

        Assert.NotNull(r.ErrorMessage);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(_out));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp-*"));
    }

    [Fact]
    public async Task OutputEqualsInput_ReturnsError()
    {
        SRRCreationResult r = await _writer.CreateFromInputsAsync(
            Sfv("CD1", "a"), [Sfv("CD1", "a")], _root, true);
        Assert.NotNull(r.ErrorMessage);
    }

    [Fact]
    public async Task LogicalNameCollision_DistinctSources_ErrorNamingBoth()
    {
        string s1 = Path.Combine(_root, "CD1", "same.nfo");
        string s2 = Path.Combine(_root, "CD2", "same.nfo");
        File.WriteAllText(s1, "1");
        File.WriteAllText(s2, "2");
        SRRCreationResult r = await _writer.CreateFromInputsAsync(
            _out, [], _root, storeRelativePaths: false,   // flat names -> both "same.nfo"
            additionalFiles: [new StoredFileEntry("same.nfo", s1), new StoredFileEntry("same.nfo", s2)]);

        Assert.NotNull(r.ErrorMessage);
        Assert.Contains("CD1", r.ErrorMessage);
        Assert.Contains("CD2", r.ErrorMessage);
    }

    [Fact]
    public async Task Cancellation_PreservesDestination_CleansTemp()
    {
        File.WriteAllBytes(_out, [9]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _writer.CreateFromInputsAsync(_out, [Sfv("CD1", "a")], _root, true, ct: cts.Token));

        Assert.Equal([9], File.ReadAllBytes(_out));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp-*"));
    }
}
```

(If `SRRTestDataBuilder` lacks `WriteStoreModeRarSet(dir, baseName, volumeCount, payloadBytes)`,
add it in this task beside its existing builders: store-mode volumes named `{base}.rar`,
`{base}.r00`, … each holding one `{payloadBytes}`-byte packed file — reuse the builder's existing
RAR4 block emitters. `SRRFileData`/`SRRFile.Load` accessor names: verify against
`ReScene.Lib/ReScene/SRR/SRRFile.cs` before writing the asserts; adjust property names to the
actual API (StoredFiles/RarFiles naming exists in the current parser tests — copy from there).)

- [ ] **Step 2:** `dotnet test ... --filter SRRWriterMultiInput` → FAIL (no such method).
- [ ] **Step 3:** implement per the 6-clause contract + refactor prerequisite.
- [ ] **Step 4:** filter PASS, then FULL lib suite (`dotnet test ReScene.Lib/ReScene.Tests/...`)
  → all green (proves legacy byte-identity refactor safe).
- [ ] **Step 5:** commit `feat(lib): multi-input SRR writer with atomic output and strict names`.

### Task 3: Lib — golden fixture harness (pyrescene oracle)

**Files:**
- Create: `ReScene.Lib/ReScene.Tests/TestData/multiset/generate-golden.py`
- Create: `ReScene.Lib/ReScene.Tests/TestData/multiset/README.md`
- Create: `ReScene.Lib/ReScene.Tests/GoldenFixtureTests.cs`
- Commit generated artifacts: `TestData/multiset/tree-2disc/**`, `TestData/multiset/golden-2disc.srr`,
  `TestData/multiset/tree-storageonly/**`, `TestData/multiset/golden-storageonly.srr`

**Interfaces:** consumes Task 2's `CreateFromInputsAsync` exactly as declared.

- [ ] **Step 1:** write `generate-golden.py` (run MANUALLY; tests never invoke python): builds
  `tree-2disc/` — `CD1/` + `CD2/` each with a 2-volume store-mode RAR set (64-byte payload, made
  with Python's struct writing the same RAR4 store-mode layout the C# builder uses — or simplest:
  invoke the committed C# builder via `dotnet run` on a tiny helper; choose ONE and document it),
  correct-CRC SFVs, `release.nfo`, `Sample/tiny.sample.avi` (name contains "sample" → phase 1),
  and at most ONE excluded SFV (`Subs/subs.sfv` + its single RAR) per the spec's ordering
  constraint. Then: assert `git -C E:\git\extern\pyrescene rev-parse HEAD` equals the hash pinned
  in README (abort otherwise), run `python bin/pyrescene.py <tree-2disc> --output <tmp>` and copy
  the resulting `.srr` to `golden-2disc.srr`. Same for `tree-storageonly/` (nfo only). README
  records: pinned hash, python version, exact commands, and regeneration steps.
- [ ] **Step 2:** `GoldenFixtureTests.cs`:

```csharp
using ReScene.SRR;

namespace ReScene.Tests;

public class GoldenFixtureTests
{
    private static string Data(string rel) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", "multiset", rel);

    // Independent minimal splitter (spec §6): only the header block's app-name is rewritten;
    // every other byte passes through untouched. Layout: [ushort sentinel][byte type]
    // [ushort flags][ushort headerSize] then, when flags bit0 set, [ushort len][name bytes].
    internal static byte[] NormalizeAppName(byte[] srr)
    {
        const string replacement = "NORMALIZED";
        ushort flags = BitConverter.ToUInt16(srr, 3);
        if ((flags & 0x1) == 0)
        {
            return srr;
        }

        ushort nameLen = BitConverter.ToUInt16(srr, 7);
        byte[] repl = System.Text.Encoding.UTF8.GetBytes(replacement);
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(srr, 0, 2);                       // sentinel
        w.Write(srr[2]);                          // type
        w.Write(flags);
        w.Write((ushort)(7 + 2 + repl.Length));   // headerSize rewritten
        w.Write((ushort)repl.Length);
        w.Write(repl);
        w.Write(srr, 9 + nameLen, srr.Length - (9 + nameLen));
        return ms.ToArray();
    }

    [Fact]
    public async Task TwoDiscTree_MatchesPyresceneGoldenBytes()
    {
        string tree = Data("tree-2disc");
        string output = Path.Combine(Path.GetTempPath(), "g2-" + Guid.NewGuid().ToString("N") + ".srr");
        // Input order = spec traversal order over the tree (CD1 before CD2, ordinal).
        SRRCreationResult r = await new SRRWriter().CreateFromInputsAsync(
            output,
            [Path.Combine(tree, "CD1", "a.sfv"), Path.Combine(tree, "CD2", "b.sfv")],
            tree, storeRelativePaths: true,
            additionalFiles: BuildStoredListInTraversalOrder(tree)); // helper: nfo -> ... -> sfvs, spec §2 ordering

        Assert.Null(r.ErrorMessage);
        Assert.Equal(
            NormalizeAppName(File.ReadAllBytes(Data("golden-2disc.srr"))),
            NormalizeAppName(File.ReadAllBytes(output)));
    }
}
```

  The `BuildStoredListInTraversalOrder` helper hardcodes this tree's stored list in the spec's
  category-pass order (release.nfo, then the SFVs) — the SCANNER produces this order in later
  tasks; here it is written out longhand so the lib test has no App.Core dependency. Add the
  equivalent storage-only test.
- [ ] **Step 3:** run generator once (verify pinned hash first), commit trees + goldens; then
  `dotnet test --filter GoldenFixture` → PASS. If bytes differ, diff block-by-block against the
  golden with the Inspector before touching writer code — the golden is the arbiter.
- [ ] **Step 4:** full lib suite green.
- [ ] **Step 5:** commit `test(lib): pyrescene golden fixtures + byte-equality harness`.

(Per-task steps with complete code follow; each task section replaces this line as it is written.)
