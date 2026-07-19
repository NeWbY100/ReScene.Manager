# Multi-Set SRR Creation (Spec 1: Video Releases) Implementation Plan

> **STATUS: UNDER REVISION — codex plan review r1 returned REVISE with 19 findings (full text:
> `2026-07-19-plan-review-codex-r1.log` in this directory) and AMENDED the execution mode
> (adopted: sequential subagent-driven, consolidated task-reviewer+codex fix/re-review loop,
> plan-specific ledger `.superpowers/sdd-multiset/progress.md`, recorded RED/GREEN/full-suite
> evidence per task; spec+excerpt handed to Tasks 2-7/9 and reviewers). Environment facts verified
> 2026-07-19: Python 3.14 only — pyrescene needs a vendored `imghdr` shim (commit under
> `TestData/multiset/compat/`, PYTHONPATH-injected by the generator); `review-package` is at
> `C:/Users/<user>/.claude/plugins/cache/claude-plugins-official/superpowers/6.1.1/skills/subagent-driven-development/scripts/review-package`;
> pyrescene commit hash must be pinned before Task 3. Do not execute until all 19 findings are
> folded in and codex re-review returns APPROVE.**
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
   Produces: `GetFinalPath`, `CanonicalizeRelative`, `ResolveSfvEntry` (see Task 1 Interfaces).
2. **Lib — CreateFromInputsAsync** (`SRRWriter.cs` + tests): N≥0 inputs, per-input volume blocks
   in order, stored dedup, temp-in-destination-dir + atomic move, header-only on empty,
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
    public async Task ZeroInputs_ZeroStored_WritesHeaderOnlySrr()
    {
        SRRCreationResult r = await _writer.CreateFromInputsAsync(_out, [], null, false);
        Assert.Null(r.ErrorMessage);
        SRRFileData srr = SRRFile.Load(_out);
        Assert.Empty(srr.StoredFiles);
        Assert.Empty(srr.RarFiles);
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

### Task 4: App.Core — deterministic release traversal

**Files:**
- Create: `ReScene.App.Core/Services/ReleaseTraversal.cs`
- Test: `ReScene.App.Core.Tests/ReleaseTraversalTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (Tasks 5-7 depend on these exact members):
  `public static class ReleaseTraversal` with
  `public static IReadOnlyList<string> EnumerateFiles(string root)` — full paths, deterministic
  os.walk-emulating order, and
  `public static IReadOnlyList<string> FilterByExtension(IReadOnlyList<string> files, string extension)`
  — preserves traversal order, OrdinalIgnoreCase extension match.

Spec §2 Ordering (rev 4/5): top-down traversal; at each directory level sort child DIRECTORY
names and FILE names with `StringComparer.Ordinal` (case-sensitive); files of a directory are
emitted before descending into its subdirectories (matching `os.walk` top-down consumption in
the excerpt's `get_files`). This is `[DIVERGENCE: determinism]` vs pyrescene's raw enumeration —
cite the spec paragraph in the class doc comment.

- [ ] **Step 1: failing tests** — `ReleaseTraversalTests.cs`:

```csharp
using ReScene.App.Core.Services;

namespace ReScene.App.Core.Tests;

public class ReleaseTraversalTests : TempDirTestBase
{
    private string Make(params string[] rel)
    {
        string p = Path.Combine([TempDir, .. rel]);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void EnumerateFiles_TopDown_OrdinalPerLevel_FilesBeforeSubdirs()
    {
        Make("b.txt");
        Make("A.txt");            // ordinal: 'A' (65) < 'b' (98)
        Make("CD2", "y.sfv");
        Make("CD10", "z.sfv");    // ordinal: "CD10" < "CD2" (char '1' < '2')
        Make("CD2", "sub", "q.txt");

        var files = ReleaseTraversal.EnumerateFiles(TempDir)
            .Select(f => Path.GetRelativePath(TempDir, f).Replace('\\', '/'))
            .ToList();

        Assert.Equal(["A.txt", "b.txt", "CD10/z.sfv", "CD2/y.sfv", "CD2/sub/q.txt"], files);
    }

    [Fact]
    public void EnumerateFiles_CaseOnlyNames_TotallyOrdered()
    {
        Make("a.nfo");
        Make("A.nfo1");           // distinct names differing in case sort deterministically
        var files = ReleaseTraversal.EnumerateFiles(TempDir).Select(Path.GetFileName).ToList();
        Assert.Equal(["A.nfo1", "a.nfo"], files);
    }

    [Fact]
    public void FilterByExtension_PreservesOrder_IgnoresCase()
    {
        Make("CD2", "b.SFV");
        Make("CD1", "a.sfv");
        Make("CD1", "x.nfo");
        var all = ReleaseTraversal.EnumerateFiles(TempDir);
        var sfvs = ReleaseTraversal.FilterByExtension(all, ".sfv")
            .Select(f => Path.GetFileName(f)).ToList();
        Assert.Equal(["a.sfv", "b.SFV"], sfvs);
    }
}
```

- [ ] **Step 2:** `dotnet test ReScene.App.Core.Tests/... --filter ReleaseTraversal` → FAIL.
- [ ] **Step 3: implement**:

```csharp
namespace ReScene.App.Core.Services;

/// <summary>
/// Deterministic release-tree traversal. [DIVERGENCE: determinism] — pyrescene's byte order is
/// raw os.walk enumeration (filesystem-dependent); this emulation sorts each level's directory
/// and file names with StringComparer.Ordinal and emits a directory's files before descending,
/// per the spec's Ordering paragraph (rev 4). All scanner category passes consume this order.
/// </summary>
public static class ReleaseTraversal
{
    public static IReadOnlyList<string> EnumerateFiles(string root)
    {
        var result = new List<string>();
        Walk(root, result);
        return result;
    }

    private static void Walk(string dir, List<string> result)
    {
        string[] files;
        string[] subdirs;
        try
        {
            files = Directory.GetFiles(dir);
            subdirs = Directory.GetDirectories(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return; // per-item warning is the SCANNER's job (Task 5); traversal skips silently
        }

        Array.Sort(files, StringComparer.Ordinal);
        Array.Sort(subdirs, StringComparer.Ordinal);
        result.AddRange(files);
        foreach (string sub in subdirs)
        {
            Walk(sub, result);
        }
    }

    public static IReadOnlyList<string> FilterByExtension(IReadOnlyList<string> files, string extension) =>
        files.Where(f => string.Equals(Path.GetExtension(f), extension, StringComparison.OrdinalIgnoreCase))
             .ToList();
}
```

- [ ] **Step 4:** filter PASS. **Step 5:** commit
  `feat(app): deterministic release traversal (ordinal os.walk emulation)`.

### Task 5: App.Core — ReleaseScanner records + main-set decision tree (spec §2a)

**Files:**
- Create: `ReScene.App.Core/Services/ReleaseSetInput.cs`
  (`public sealed record ReleaseSetInput(string SfvOrRarPath, string RelativeName);`)
- Create: `ReScene.App.Core/Services/ReleaseScanResult.cs` (record per spec §2 with the six lists)
- Create: `ReScene.App.Core/Services/IReleaseScanner.cs`
  (`ReleaseScanResult Scan(string releaseRoot, CancellationToken ct = default);`)
- Create: `ReScene.App.Core/Services/ReleaseScanner.cs`
- Test: `ReScene.App.Core.Tests/ReleaseScannerMainSetTests.cs`

**Interfaces:**
- Consumes: Task 4's `ReleaseTraversal` members.
- Produces: the four types above with EXACTLY the spec §2 record shape (Tasks 6-8 extend/consume);
  internal seams for Tasks 6-7: `internal ReleaseScanner(Func<string, IReadOnlyList<string>>? sfvEntryReader = null, Func<string, ProofRarContent>? proofRarReader = null)`
  where `internal enum ProofRarContent { Image, NonImage, Unreadable }` (own file) — injectable so
  rule-4 tests need no real RARs.

Implementation is the ordered decision tree of spec §2a — implement by transcribing the excerpt
(`pyrescene-rules-excerpt.txt`, `remove_unwanted_sfvs` section) branch by branch IN ORDER, each
branch commented with its excerpt line range. Sequential first-match: rule 2's false-positive
regex `^(000?-)|(.*(cd\d|flac).*)` (IgnoreCase) FALLS THROUGH (pyrescene `pass`), it does not
accept. Rescue fallback + destinations per spec (`SubtitleSfvs` for excluded, proof-linked SFV+RAR
→ `StoredFiles`, `dirfix` subdir → skip + warning, subpack/subfix release name → main SFVs ALSO
queued to `SubtitleSfvs` for nested processing). `MainSets.RelativeName` =
root-relative path with `/` separators (plain `Path.GetRelativePath` here — scanner names are
display/logical hints; the WRITER re-canonicalizes with final paths at §1a strictness).

- [ ] **Step 1: failing tests** — `ReleaseScannerMainSetTests.cs`. Test matrix (one Fact per row;
  build each tree with `TempDirTestBase` + tiny text files; SFV contents via helper
  `WriteSfv(path, params string[] entries)` writing `"{entry} 00000000"` lines; the injectable
  `sfvEntryReader` default reads real files so most tests use the real path):

| Tree | Expectation |
|---|---|
| `CD1/a.sfv`, `CD2/b.sfv` (rar entries) | 2 MainSets, order `CD1/a.sfv` then `CD2/b.sfv`, RelativeNames `CD1/a.sfv`/`CD2/b.sfv` |
| `x.vobsubs.sfv` in root, release dir named `Some.Movie-GRP` | excluded → SubtitleSfvs (rule 1) |
| same SFV, release dir named `Some.SUBPACK-GRP` | MAIN set (rule 1 exception) AND also queued to SubtitleSfvs (subpack release) |
| `grp-subs.sfv` (rule 2, no carve-out) | excluded → SubtitleSfvs |
| `00-grp-subs.sfv` (matches `^000?-`) with rar entries | falls through rule 2 → MAIN set |
| `grp.subs.cd1.sfv` under dir `Cover/` | falls through rule 2, then rule 3 excludes (pardir cover) — proves `pass` semantics |
| `Subs/x.sfv` | rule 3 exclusion → SubtitleSfvs |
| `Proof/p.sfv` listing exactly `p.rar`, proofRarReader→Image | rule 4: SFV+RAR → StoredFiles, not a set |
| `Proof/p.sfv` listing `p.rar`, reader→NonImage | NOT proof → continues; with rar entries it becomes MAIN set |
| `Proof/p.sfv` listing `p.rar`, reader→Unreadable | warning + excluded (treated proof) |
| `Proof/p.sfv` listing two entries | rule 4 requires singleton → falls through to rules 5-7 |
| `Subs/CD1/s.sfv` | rule 5 (`.*Subs.?CD\d$`) → SubtitleSfvs |
| `SubpackStuff/x.sfv`, release `Movie-GRP` | rule 6 substring pardir → excluded |
| `MyFix/x.sfv`, release `Movie.FIX-GRP` | rule 6 `fix` exception (release name has fix) → MAIN |
| all SFVs subs-named + one has 2 rar entries | rescue re-admits the 2-entry one as MAIN |
| `Subs/dirfix.stuff/x.sfv` under a `dirfix` dir | skipped entirely + warning |
| root unreadable (ACL deny via existing `AclDenyHelper`) | Warnings-only result, empty lists |
| cancellation token pre-cancelled | `OperationCanceledException` |

- [ ] **Step 2:** run filter `ReleaseScannerMainSet` → FAIL.
- [ ] **Step 3:** implement `ReleaseScanner.Scan` decision tree (skeleton):

```csharp
public ReleaseScanResult Scan(string releaseRoot, CancellationToken ct = default)
{
    ct.ThrowIfCancellationRequested();
    IReadOnlyList<string> all;
    try { all = ReleaseTraversal.EnumerateFiles(releaseRoot); }
    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
    { return ReleaseScanResult.RootError(releaseRoot, e.Message); }

    string releaseName = Path.GetFileName(Path.TrimEndingDirectorySeparator(releaseRoot));
    string lcRelease = releaseName.ToLowerInvariant();
    var sfvs = ReleaseTraversal.FilterByExtension(all, ".sfv");

    var main = new List<string>(); var subs = new List<string>();
    var stored = new List<string>(); var warnings = new List<string>();

    foreach (string sfv in sfvs)
    {
        ct.ThrowIfCancellationRequested();
        SfvClass cls = ClassifySfv(sfv, lcRelease, warnings);   // rules 1-7, excerpt-cited
        switch (cls) { /* Main -> main; Subs -> subs; Proof -> stored(sfv + rar); Skip -> warning already added */ }
    }
    // rescue fallback (excerpt tail), subpack/subfix main->subs queue, then Tasks 6/7 passes...
}
```

  The full `ClassifySfv` is the excerpt transcription; every `if` carries
  `// excerpt: remove_unwanted_sfvs L<from>-<to>`.
- [ ] **Step 4:** filter PASS + full App.Core suite green. **Step 5:** commit
  `feat(app): ReleaseScanner main-set decision tree (pyrescene 2a port)`.

### Task 6: App.Core — scanner music/samples/first-RAR (spec §2b, §2c, §2e)

**Files:**
- Modify: `ReScene.App.Core/Services/ReleaseScanner.cs`
- Test: `ReScene.App.Core.Tests/ReleaseScannerMediaTests.cs`

**Interfaces:**
- Consumes: Task 5's scanner internals (`ClassifySfv` result lists, injectable readers).
- Produces: populated `MusicSfvs`, `SampleFiles`, and first-RAR `MainSets` entries in
  `ReleaseScanResult` (shapes unchanged — Tasks 8-9 consume the record as declared in Task 5).

Rules (each excerpt-cited in code):
- §2b `has_music` ONLY inside the zero-survivor rescue (excerpt `remove_unwanted_sfvs` tail):
  rescue re-admits SFVs with >1 entry as MAIN; SFVs whose entries `endswith(".mp3"/".flac"/".mp2")`
  CASE-SENSITIVE → `MusicSfvs` + warning `[DIVERGENCE]` (spec routes them to Spec 2 instead of
  sets). Empty/corrupt SFV during rescue → warning + stored-only `[DIVERGENCE: hardening]`.
- §2c samples, phase 1: `FileType.VideoExtensions` =
  `.mp4 .m4v .avi .mkv .wmv .vob .m2ts .ts .mpeg .mpg .m2v .m2p` (OrdinalIgnoreCase ext match);
  path contains `sample` (case-insensitive) OR the literal sibling `video[..^4] + ".sfv"` exists
  (pyrescene's `sample[:-4] + ".sfv"` slice — for `.m2ts` this checks `x.m.sfv`-style names; the
  quirk is intentional and tested). Phase 2: remaining videos whose BASENAME appears
  case-sensitively among any SFV's entry names anywhere in the release.
- §2e: first-RAR main sets come only from selected SFVs (already Task 5's behavior); loose-RAR
  discovery ONLY when `sfvs.Count == 0` for the entire root `[DIVERGENCE: extension]`: for each
  `.rar` outside dirs excluded by rules 3-6 whose name is its chain's first volume
  (`RARVolumeIdentifier` + `RARVolumeNameComparer` from the lib), add
  `ReleaseSetInput(rarPath, relativeName)`.

- [ ] **Step 1: failing tests** — `ReleaseScannerMediaTests.cs` matrix:

| Tree | Expectation |
|---|---|
| `CD1/a.sfv` (rars) + `x.mp3.sfv` listing `t.mp3` | a.sfv MAIN; music sfv survives rules 1-7? it has no rar entries → stays candidate → NOT rescued (main survived) → classified per §2b only in rescue: NOT music-flagged here; it reaches 2d stored-only with warning |
| ONLY `x.sfv` listing `t.mp3` | rescue: music → `MusicSfvs` + warning; zero MainSets |
| ONLY `y.sfv` listing `a.rar b.rar` (2 entries) | rescue: re-admitted MAIN |
| `Sample/clip.avi` | phase 1 (dir contains sample) → SampleFiles |
| `movie.sample.mkv` in root | phase 1 (name) → SampleFiles |
| `clip.avi` + sibling `clip.sfv` | phase 1 literal-slice sibling → SampleFiles |
| `clip.m2ts` + sibling `clip.sfv` | NOT phase-1-by-sibling (slice checks `clip.m.sfv`) — quirk preserved; also not in any SFV → not a sample |
| `video.mkv` listed by basename inside `CD1/a.sfv` | phase 2 → SampleFiles |
| `VIDEO.mkv` listed as `video.mkv` in sfv | NOT phase 2 (case-sensitive membership) |
| zero SFVs anywhere; `CD1/a.rar`+`a.r00`, `Subs/s.rar` | loose discovery: `CD1/a.rar` MAIN (r00 not first; Subs excluded) |
| zero SFVs, empty tree | zero MainSets, no crash |

- [ ] **Step 2:** filter `ReleaseScannerMedia` → FAIL. **Step 3:** implement (video extension set
  as `private static readonly string[] VideoExtensions`, excerpt-cited; phase-2 via
  `HashSet<string>(StringComparer.Ordinal)` of all SFV entry basenames). **Step 4:** filter +
  full App.Core suite PASS. **Step 5:** commit
  `feat(app): scanner samples, rescue-scoped music, gated loose-RAR discovery`.

### Task 7: App.Core — scanner stored-file chain (spec §2d)

**Files:**
- Modify: `ReScene.App.Core/Services/ReleaseScanner.cs`
- Create: `ReScene.App.Core/Services/ProofRarContent.cs` (if not created in Task 5)
- Test: `ReScene.App.Core.Tests/ReleaseScannerStoredTests.cs`

**Interfaces:**
- Consumes: Tasks 4-6 internals.
- Produces: populated `StoredFiles` (order = category passes: nfo → m3u → proof images/rars →
  log → cue → srs → sfvs, traversal order within each; Task 8 consumes as-is; Task 9 applies the
  generated-SRS supersede rule at VM level).

Rules (all excerpt-cited; `generate_srr` consumption + `get_proof_files` chain):
- nfo pass: every `*.nfo` EXCEPT basenames `imdb.nfo`, `tvmaze.nfo` (case-insensitive), and
  `no.nfo` under the excerpt's condition (transcribe it verbatim from the excerpt's nfo block).
- m3u pass: every `*.m3u`. log pass: `*.log` filtered per the excerpt's log condition (transcribe;
  it excludes site/tool logs by name). cue pass: every `*.cue`. srs pass: every pre-existing
  `*.srs` (supersede handling is Task 9's).
- Proof images: for every `.jpg/.jpeg/.png/.bmp/.gif` in traversal order: keyword path
  (`proof|sample|cover|screenshots|compare` substring, case-insensitive) → stored BEFORE
  `always_skip` (a `Proof/Folder.jpg` IS stored); else `always_skip` (space in basename OR stem
  ends `folder` OR basename contains `albumartsmall` OR basename starts `albumart_{`) → skip;
  else `store_rls_root`: basename starts `00`/`01`/`001` → stored; else
  `new FileInfo(f).Length > 100000` AND `SimilarToGoodName(f)` (strip_zeros-normalized prefix
  match against nfo/sfv/rar/m3u basenames — transcribe `similar_to_good_name` + `strip_zeros`)
  AND NOT `FixedResolutionCover(f)` (transcribe the resolution check; use the excerpt's exact
  dimension list) → stored; else skip + warning (size logged like pyrescene).
- Proof RARs (independent pass): every `*.rar` whose path contains `proof` (case-insensitive) and
  whose packed blocks include an image ext (via the Task 5 injectable `proofRarReader`;
  production impl uses lib `RARHeaderReader`, `ValueError`-equivalent → warning + not stored) →
  stored. Conditional fix RAR: transcribe the excerpt's fix-RAR storage condition.

- [ ] **Step 1: failing tests** — `ReleaseScannerStoredTests.cs` matrix:

| Item | Expectation |
|---|---|
| `release.nfo`, `imdb.nfo`, `TVMAZE.NFO` | only `release.nfo` stored |
| `playlist.m3u`, `rip.log`, `disc.cue`, `old.srs` | all stored in category order after nfo |
| `Proof/Folder.jpg` | STORED (keyword bypass precedes always_skip) |
| root `Folder.jpg`, `MyFolder.png`, `AlbumArtSmall.jpg`, `AlbumArt_{guid}_Large.jpg`, `has space.jpg` | all skipped (always_skip) |
| root `AlbumArtLarge.jpg` 150KB similar-named | STORED (predicate is `albumartsmall` contains + `albumart_{` prefix only) |
| root `00-cover.jpg` 5KB | stored (prefix accept, size irrelevant) |
| root `grp-proof.jpg` 150KB, sfv named `grp-movie.sfv` | stored (similar name) |
| root `random.jpg` 150KB unrelated name | skipped + warning |
| root `small.jpg` 50KB similar-named | skipped (≤100000) with boundary test at exactly 100000 (skip) and 100001 (store) |
| `Proof/p.rar` reader→Image | stored |
| `Proof/p.rar` reader→NonImage | not stored |
| `Proof/p.rar` reader→Unreadable | warning, not stored |
| category order | full result list equals the documented category-pass concatenation for a mixed tree |

- [ ] **Step 2:** filter `ReleaseScannerStored` → FAIL. **Step 3:** implement; every transcribed
  helper carries `// excerpt: <function> L<from>-<to>`. **Step 4:** filter + full App.Core PASS.
- [ ] **Step 5:** commit `feat(app): scanner stored-file chain (2d exact port)`.

### Task 8: App.Core — service pass-through + CreatorViewModel folder mode (spec §3)

**Files:**
- Modify: `ReScene.App.Core/Services/ISRRCreationService.cs` + `SRRCreationService.cs`
  (add `CreateFromInputsAsync` pass-through with Task 2's exact signature)
- Modify: `ReScene.App.Core/ViewModels/CreatorViewModel.cs`
- Test: `ReScene.App.Core.Tests/CreatorViewModelFolderModeTests.cs`

**Interfaces:**
- Consumes: Task 2's `CreateFromInputsAsync` (via service), Task 5's `IReleaseScanner` +
  `ReleaseScanResult`/`ReleaseSetInput`.
- Produces (Tasks 9-10 depend on): VM members
  `public ObservableCollection<ReleaseSetInput> DetectedSets { get; }`,
  `[ObservableProperty] public partial bool IsScanning { get; set; }`,
  `public IAsyncRelayCommand BrowseInputFolderCommand`,
  ctor gains `IReleaseScanner releaseScanner` parameter (update ALL existing constructions:
  `MainWindowViewModel` [2 sites: Creator + wizard CreateSRRWizard], and every test factory —
  grep `new CreatorViewModel(`; tests pass a `StubReleaseScanner`).

Behavior (spec §3):
- `OnInputPathChanged`: `Directory.Exists(path)` → folder mode: bump `_scanGeneration` (int,
  Inspector `_loadGeneration` house pattern), cancel `_scanCts`, new CTS, `IsScanning = true`,
  `Task.Run(() => _releaseScanner.Scan(path, token))`, apply on UI thread via `_dispatcher.Post`
  ONLY if generation still current: populate `DetectedSets`, `StoredFiles` (StoredName =
  scan relative name, FullPath = source), `ExtraSampleFiles`, `ExtraSubtitleSfvFiles`;
  `InputStatus` = FieldStatus.Ok with `"{sets} RAR set(s) · {samples} sample(s) · {stored} stored file(s)"`
  (+ first warning appended); music-only (MusicSfvs>0 && MainSets empty) → FieldStatus.Error
  "Music release — folder scan support arrives in a later update." and Create gated.
- OutputPath tracking: private `_outputPathAutoGenerated` bool; auto-fill sets it true; user edit
  (OnOutputPathChanged when the new value differs from the last auto value) sets false; re-scan
  replaces ONLY when auto. Auto value: `Path.Combine(Path.GetDirectoryName(rootTrimmed)!, rootName + ".srr")`;
  filesystem-root input → FieldStatus.Error, no auto name.
- `CreateSRRCommand` folder branch: when `_isFolderMode`, call
  `_srrService.CreateFromInputsAsync(OutputPath, DetectedSets.Select(s => s.SfvOrRarPath).ToList(),
  _releaseRoot, storeRelativePaths: true, storedFiles: StoredFiles snapshot mapped to
  StoredFileEntry(StoredName, FullPath), options, ct)`. File branch unchanged (byte-identity).

- [ ] **Step 1: failing tests** — `CreatorViewModelFolderModeTests.cs` (house pattern: fakes +
  `TestUiDispatcher`; add `file sealed class StubReleaseScanner : IReleaseScanner` returning a
  canned `ReleaseScanResult`, plus a `GatedReleaseScanner` blocking on a `ManualResetEventSlim`
  for the generation test — mirror `GatedCompareService`):
  - folder input populates DetectedSets/StoredFiles/samples/subs + Ok status summary;
  - stale scan discarded: start scan A (gated), change input to B (stub, completes), release A →
    state remains B's (generation guard);
  - IsScanning true while gated, false after; Create disabled while scanning;
  - music-only result → Error status + Create cannot execute in folder mode;
  - OutputPath auto-generated on first scan, replaced on re-scan, PRESERVED after user edit;
  - Create in folder mode captures (via `FakeSRRCreationService` extension recording the new
    method args) ordered input paths, root, storeRelativePaths=true, stored list;
  - file-mode Create still calls the old single-SFV path (regression).
- [ ] **Step 2:** filter `CreatorViewModelFolderMode` → FAIL. **Step 3:** implement.
- [ ] **Step 4:** filter + FULL App.Core suite (513+) PASS. **Step 5:** commit
  `feat(app): CreatorViewModel folder mode with generation-guarded scans`.

### Task 9: App.Core — generated artifacts working-dir model (spec §3 artifacts)

**Files:**
- Modify: `ReScene.App.Core/ViewModels/CreatorViewModel.cs` (artifact staging inside the folder
  Create flow, before the writer call)
- Test: `ReScene.App.Core.Tests/CreatorViewModelArtifactTests.cs`

**Interfaces:**
- Consumes: Task 8's folder-mode Create flow; existing SRS/nested-SRR creation services already
  injected into the VM (SRSCreationService for samples; the wizard's subs pipeline for
  subtitle SFVs — reuse the exact members the wizard steps call today; grep
  `ExtraSampleFiles`/`ExtraSubtitleSfvFiles` consumption in `CreatorViewModel.CreateSRR` to find
  them and cite in the diff).
- Produces: staged artifacts appended to the writer's stored list.

Behavior (spec §3 rev 3/5):
- Temp working dir `Path.Combine(Path.GetTempPath(), "srr-work-" + generation)` created per
  Create; artifact logical name = root-relative source path with extension swapped
  (`Sample/x.mkv` → `Sample/x.srs`); collision keying by FULL RELATIVE STEM — same stem in
  different dirs is NOT a collision; same-stem collision keeps full ext (`x.mkv.srs`).
- SRS failure → store pyrescene's failure `.txt` (same stem, `.txt`); RAR-backed `.vob` sample →
  nested SRR instead of SRS; one subtitle SFV may yield multiple nested SRRs (append all).
- Generated `.srs` SUPERSEDES a same-relative-path pre-existing `.srs` in the stored list (replace
  entry, no collision error).
- Cancellation/failure → delete the working dir best-effort; destination untouched (writer's
  transaction covers the SRR itself).
- [ ] **Step 1: failing tests** — matrix: extension-swap naming; cross-dir same stem no collision;
  same-stem full-ext keeping; supersede replaces pre-existing srs entry; SRS-failure txt stored;
  multi-SRR subtitle appends all; cancellation removes working dir. (Stub the SRS/nested services
  with recording fakes following the file's existing fake patterns.)
- [ ] **Step 2:** FAIL → **Step 3:** implement → **Step 4:** filter + full suite PASS →
  **Step 5:** commit `feat(app): generated-artifact staging (working-dir model)`.

### Task 10: Manager — UI both surfaces + a11y contract (spec §4, §4a)

**Files:**
- Modify: `ReScene.Manager/Views/CreatorView.axaml` (+ `.axaml.cs` if focus-return needs code)
- Modify: `ReScene.Manager/Views/Wizards/CreateSRRWizardBody.axaml`
- Modify: `ReScene.Manager/Views/MainWindow.axaml.cs` or DI wiring for `IReleaseScanner`
  registration (grep where services are constructed — `App.axaml.cs`/`Program.cs` composition
  root; register `new ReleaseScanner()` and pass into both `CreatorViewModel` constructions)
- Test: build gate + Task 11 E2E (no headless UI tests in this repo's Manager.Tests for views)

Markup contract (both surfaces, spec §4/§4a exactly):
- Beside the existing Browse button:
  `<Button Content="Browse folder…" Command="{Binding BrowseInputFolderCommand}"
   AutomationProperties.Name="Browse release folder" Classes="ghost" .../>`;
  existing file button gains `AutomationProperties.Name="Browse input file"`;
  input TextBox gains `AutomationProperties.HelpText="Accepts a release .sfv/.rar file path or a release folder path"`.
- Under the input row:
  `<ItemsControl ItemsSource="{Binding DetectedSets}" MaxHeight="96"
   AutomationProperties.Name="{Binding DetectedSets.Count, StringFormat='Detected RAR sets, {0} items'}"
   IsVisible="{Binding DetectedSets.Count}">` with `ScrollViewer.VerticalScrollBarVisibility=Auto`
  wrapper and item template `<TextBlock Text="{Binding RelativeName}" />`.
- `FieldStatusLine Status="{Binding InputStatus}"` already present on the Advanced surface —
  wizard step 0 gains the same summary binding (it already shows InputStatus; verify).
- Busy: `<ProgressBar IsIndeterminate="True" IsVisible="{Binding IsScanning}" Height="4"/>` under
  the input row; the path TextBox is NEVER disabled (spec §4a).
- Tab order: input box → Browse file → Browse folder → detected list (set `TabIndex` explicitly
  where the default order differs). Focus return after pickers: Avalonia returns focus to the
  invoking button by default — verify via Task 11 bridge check; only add code-behind if it fails.
- [ ] **Steps:** markup edits → forced-rebuild gate (`-p:BaseOutputPath=bin2/`, 0/0, delete bin2)
  → commit `feat(ui): folder input chrome on Creator + wizard (a11y contract)`.

### Task 11: E2E verification + final review

- [ ] **Step 1:** synthesize `%TEMP%\e2e-2disc\` release folder (reuse Task 3's generator tree
  layout: CD1/CD2 sets + nfo + Sample). Via agent bridge: launch worktree app → Advanced SRR
  Creator tab → `ava_input text` the folder path → verify DetectedSets shows `CD1/a.sfv` and
  `CD2/b.sfv` (`ava_search by text`), status summary Ok, automation names present (`ava_props`
  on both browse buttons + list) → dispatch CreateSRRCommand → assert output exists.
- [ ] **Step 2:** load the created SRR in the Inspector (typed path + `ava_key Enter`): tree shows
  both sets' volume blocks; Reconstructor import shows both discs in the Set column.
- [ ] **Step 3:** wizard surface: Beginner mode → Create an SRR → type folder on step 1 → steps
  2-3 pre-populated (bridge-verify list contents) → cancel (no creation needed twice).
- [ ] **Step 4:** full gates: lib + App.Core + Manager suites, forced-rebuild gate 0/0.
- [ ] **Step 5:** final whole-branch review: `scripts/review-package MERGE_BASE HEAD` + codex
  whole-branch review (runner-script pattern, scoped prompt); dispatch ONE fix subagent for any
  findings; re-verify; commit.

## Execution Regime (binding)

Per the standing approval in the header: subagent-driven development
(superpowers:subagent-driven-development), fresh implementer per task, task-reviewer per task,
PLUS a codex diff review per task (runner-script pattern with fidelity-scoped prompts; the
scanner tasks' prompts must point codex at the excerpt file). Ledger:
`.superpowers/sdd/progress.md` (append per task completion). Model selection per the sdd skill's
guidance; Task 5 and Task 7 implementers get the excerpt path in their dispatch as required
reading alongside the task brief.


