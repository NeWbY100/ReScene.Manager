# Multi-Set SRR Creation (Spec 1: Video Releases) Implementation Plan

> **STATUS: EXECUTABLE — codex plan review r5 verdict APPROVE-WITH-FIXES; the single fix
> (no-skip Step 4 wording, codex r5 f1) is applied. Execution proceeds under the Execution
> Regime per the standing user approval (2026-07-19). Review logs: r1/r2b beside this file.**
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

**Spec (normative):** `docs/superpowers/specs/2026-07-18-multiset-srr-creation-design.md` (rev 5a,
codex-APPROVED) + `docs/superpowers/specs/pyrescene-rules-excerpt.txt` (rule source of truth; EXTENDED with
rar_file_blacklist, similar_to_good_name, fixed_resolution_cover, is_storable_fix,
create_srr_for_subs, and the generate_srr tail; pyrescene PINNED at
`04da213cef6765ed98e0d1735683822a41ea0103`).

## Global Constraints

- File-input behavior stays byte-identical; existing suites stay green (lib ~912+, App.Core 513+,
  Manager 15+); forced-rebuild gate 0 warnings / 0 errors (`-p:BaseOutputPath=bin2/`, delete after).
- Folder-input output byte-identical to pyrescene golden fixtures after app-name normalization.
- Every ported rule cites its excerpt lines in a comment; divergences carry `[DIVERGENCE]` tags
  copied from the spec.
- One top-level type per file (docs/coding-guidelines.md); scanner in App.Core, writer in Lib.
- Review regime (codex r1 AMENDED, adopted): sequential subagent-driven execution; per task ONE
  consolidated fix/re-review loop covering the task reviewer AND a codex diff review; ledger is
  `.superpowers/sdd-multiset/progress.md`; implementer dispatches for Tasks 2-7 and 9 include the
  spec + full excerpt paths as required reading.
- RED discipline (codex r1 f18 / r3 f18, BINDING FOR EVERY TASK 1-11 whether or not an explicit
  'Step 1b' appears in its step list): before the recorded RED run, add COMPILING stubs for every
  new type/member the task introduces (throwing NotImplementedException) and any new test fixture
  helpers, so RED shows assertion-level failures per contract row — never compile errors. Then
  targeted GREEN; then the full suite. Record all three outputs in the task report. Implementer
  dispatches MUST restate this rule.

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
  `public static string ResolveSfvEntry(string sfvDirectory, string entryName)`,
  `public static string CanonicalizeLogicalName(string logicalName)` — normalizes separators to
  `/`, rejects rooted/empty/`.`/`..`-containing names with `SrrNameException` (codex r1 f10;
  Task 2 runs EVERY StoredFileEntry.StoredName through it);
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
        // NTFS JUNCTIONS need no privilege (unlike symlinks) — runs unconditionally on
        // Windows (codex r2b f1 / r4 f1; xUnit 2.9.3 has no Assert.Skip, none needed).
        CreateJunction(link, target);
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

    [Theory]
    [InlineData("CD1\\a.sfv", "CD1/a.sfv")]
    public void CanonicalizeLogicalName_NormalizesBackslashes(string input, string expected) =>
        Assert.Equal(expected, SrrNameCanonicalizer.CanonicalizeLogicalName(input));

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/../b.nfo")]
    [InlineData("C:/abs/x.nfo")]
    [InlineData("a//b.nfo")]
    public void CanonicalizeLogicalName_Degenerate_Throws(string bad) =>
        Assert.Throws<SrrNameException>(() => SrrNameCanonicalizer.CanonicalizeLogicalName(bad));

    private static void CreateJunction(string link, string target)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(
            "cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        proc.WaitForExit();
        Assert.Equal(0, proc.ExitCode); // junction creation must succeed — never skipped
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

- [ ] **Step 1b (stubs):** add `SrrNameCanonicalizer`/`SrrNameException` with every declared
  member throwing `NotImplementedException` — the suite COMPILES (codex r1 f18).
- [ ] **Step 2: Run to verify assertion-level RED**

Run: `dotnet test ReScene.Lib/ReScene.Tests/ReScene.Tests.csproj --filter SrrNameCanonicalizer`
Expected: FAIL — every test fails with `NotImplementedException` (recorded per contract row),
not compile errors.

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
            // POSIX realpath equivalence: resolve EVERY ancestor (codex r1 f1 / r4 f1).
            return ResolveAncestorChain(path);
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

    // POSIX final-path helper: resolves each existing component while walking down from
    // the filesystem root (codex r1 f1 / r4 f1 — real compiled member).
    private static string ResolveAncestorChain(string path)
    {
        string full = Path.GetFullPath(path);
        string current = Path.GetPathRoot(full)!;
        foreach (string seg in Path.GetRelativePath(current, full).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, seg);
            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current) : new FileInfo(current);
            FileSystemInfo resolved = info.ResolveLinkTarget(returnFinalTarget: true) ?? info;
            current = resolved.FullName;
        }

        return current;
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

    public static string CanonicalizeLogicalName(string logicalName)
    {
        string name = logicalName.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name)
            || name == "." || name.Split('/').Any(seg => seg is "." or ".." or ""))
        {
            throw new SrrNameException($"Invalid stored logical name: {logicalName}");
        }

        return name;
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

- [ ] **Step 4:** re-run the filter → PASS (every test, junction included, runs unconditionally
  on Windows — no skip path exists; codex r5 f1).
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

1. Zero inputs + stored files → storage-only SRR (header + stored blocks). Zero inputs + zero
   stored files → HEADER-ONLY SRR, `Success = true` (spec §5 pyrescene parity; codex r2b f9 —
   emptiness is NEVER an error in this overload).
2. Per input, in list order: `.sfv` → parse entries (reuse the existing SFV line parser), resolve
   each via `ResolveSfvEntry(sfvDir, entryName)`, keep `RARVolumeIdentifier.IsRARVolume` matches,
   then GROUP BY CHAIN (codex r1 f2 / r2b f2): chain key = the volume's DIRECTORY plus base
   archive name (strip the volume suffix `.rar`/`.rNN`/`.partN.rar` with N of ANY digit count,
   OrdinalIgnoreCase) — same basename in different directories is a DIFFERENT chain; chains keep
   FIRST-SEEN order from the SFV entry sequence; volumes sort with
   `RARVolumeNameComparer.Instance` ONLY within their chain (a global sort would interleave
   `a.rar, b.rar, a.r00` — tested with two interleaved chains AND cross-directory same-basename
   chains). First-volume rule: plain `.rar` with no `.partN` numbering, or the LOWEST-numbered
   `.partN.rar` present (`.part1.rar`, `.part01.rar`, `.part001.rar` all covered by numeric
   parse, OrdinalIgnoreCase); a lone `.rNN` is NOT a first volume. Tests cover part1/part01/
   part001 first-volume acceptance and `.part02.rar` rejection. `.rar` → walk its
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
   (same directory); success → `File.Move(tmp, outputPath, overwrite: true)`; failure → error
   result; cancellation → OperationCanceledException PROPAGATES (one contract, matches the test;
   legacy wrappers keep their existing behavior — codex r1 f9). Both paths delete tmp
   best-effort and leave a pre-existing destination untouched. Reject outputPath whose `GetFinalPath` (when it exists — else its
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
        RarFixtures.WriteStoreModeRarSet(d, baseName, volumeCount: 2, payloadBytes: 64);
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
        SRRFile srr = SRRFile.Load(_out);
        Assert.Equal(["CD1/a.sfv", "CD2/b.sfv"], srr.StoredFiles.Select(f => f.FileName));
        Assert.Equal(["CD1/a.rar", "CD1/a.r00", "CD2/b.rar", "CD2/b.r00"],
            srr.RARFiles.Select(f => f.FileName));
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
        SRRFile srr = SRRFile.Load(_out);
        Assert.Single(srr.StoredFiles);
        Assert.Empty(srr.RARFiles);
    }

    [Fact]
    public async Task ZeroInputs_ZeroStored_WritesHeaderOnlySrr()
    {
        SRRCreationResult r = await _writer.CreateFromInputsAsync(_out, [], null, false);
        Assert.Null(r.ErrorMessage);
        SRRFile srr = SRRFile.Load(_out);
        Assert.Empty(srr.StoredFiles);
        Assert.Empty(srr.RARFiles);
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

(New test helper `RarFixtures.WriteStoreModeRarSet(dir, baseName, volumeCount, payloadBytes)`
in `ReScene.Lib/ReScene.Tests/RarFixtures.cs`: EXTENDS the proven `CreateMinimalRAR4File` idiom
from `SRRWriterTests.cs` to emit N store-mode volumes named `{base}.rar`, `{base}.r00`, …; add
it as this task's FIRST stub so the RED phase compiles (codex r1 f11/f18). Reader API verified
2026-07-19: `SRRFile.Load` → `SRRFile` with `StoredFiles`/`RARFiles` of blocks exposing
`FileName`; results report `Success`. Additional matrix rows: legacy single-SFV and direct-RAR
byte-equality vs pre-change outputs; additionalFiles order + identical-source dedup; overwrite of
existing destination on success; failure AFTER tmp exists (inject via a stored file deleted
mid-run) leaves destination + no tmp.)

- [ ] **Step 1b (stubs):** add the `CreateFromInputsAsync` stub (throws NotImplementedException)
  and the `RarFixtures` helper so everything COMPILES.
- [ ] **Step 2:** `dotnet test ... --filter SRRWriterMultiInput` → assertion-level RED
  (NotImplementedException per row), not compile errors.
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
  correct-CRC SFVs, `release.nfo`,
  and at most ONE excluded SFV (`Subs/subs.sfv` + its single RAR); NO Sample/ dir in the
  writer-only trees (generated artifacts join in the post-Task-9 full-pipeline golden — codex r1
  f4). Then: assert `git -C E:\git\extern\pyrescene rev-parse HEAD` ==
  `04da213cef6765ed98e0d1735683822a41ea0103` (abort otherwise); run
  `python bin/pyrescene.py --no-srs --no-isdb --output <tmp> <tree-2disc>` (flags verified
  present; they disable SRS generation and ISDb hashes for determinism) with
  `PYTHONPATH=TestData/multiset/compat` where `compat/imghdr.py` is a vendored copy of the
  removed stdlib module (Python 3.14 dropped imghdr; pyrescene imports it — codex r1 f17); copy
  the resulting `.srr` to `golden-2disc.srr`. Same for `tree-storageonly/` (nfo only). README
  records: pinned hash, python version, exact commands, shim provenance, regeneration steps.
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
  category-pass order (release.nfo, then the SFVs) — written longhand so the lib test has no
  App.Core dependency. Add the equivalent storage-only test. `NormalizeAppName` is validated
  FIRST against hand-built byte vectors (no app-name flag; differing name lengths; truncated
  header; trailing bytes preserved) so a symmetric normalizer bug cannot mask real diffs (codex
  r1 f18). A `FullPipelineGoldenTests` placeholder is added DISABLED here and enabled in Task 9:
  it regenerates a golden WITH samples/subs via pyrescene WITHOUT --no-srs and compares the
  complete folder-mode output (nested-SRR app-name fields normalized identically) — codex r1 f4.
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
  `public sealed record TraversalIssue(string Path, string Message);`
  `public sealed record TraversalResult(IReadOnlyList<string> Files, IReadOnlyList<TraversalIssue> Issues, bool RootFailed);`
  `public static TraversalResult EnumerateFiles(string root, CancellationToken ct = default)` —
  full paths in deterministic order; per-directory failures become ordered Issues (root failure
  sets RootFailed — Task 5 maps Issues to Warnings and RootFailed to the Warnings-only result,
  codex r1 f12); directory reparse points are NOT descended (pyrescene's os.walk default);
  ct checked per directory; and
  `public static IReadOnlyList<string> FilterByExtension(IReadOnlyList<string> files, string extension)`
  — preserves traversal order, OrdinalIgnoreCase extension match. Tests add: root ACL-deny →
  RootFailed; descendant deny → Issue + remaining files intact; symlinked dir not followed
  (skip-guarded like Task 1); pre-cancelled ct → OperationCanceledException.

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

        var files = ReleaseTraversal.EnumerateFiles(TempDir).Files
            .Select(f => Path.GetRelativePath(TempDir, f).Replace('\\', '/'))
            .ToList();

        Assert.Equal(["A.txt", "b.txt", "CD10/z.sfv", "CD2/y.sfv", "CD2/sub/q.txt"], files);
    }

    [Fact]
    public void EnumerateFiles_CaseOnlyNames_TotallyOrdered()
    {
        Make("a.nfo");
        Make("A.nfo1");           // distinct names differing in case sort deterministically
        var files = ReleaseTraversal.EnumerateFiles(TempDir).Files.Select(Path.GetFileName).ToList();
        Assert.Equal(["A.nfo1", "a.nfo"], files);
    }

    [Fact]
    public void FilterByExtension_PreservesOrder_IgnoresCase()
    {
        Make("CD2", "b.SFV");
        Make("CD1", "a.sfv");
        Make("CD1", "x.nfo");
        var all = ReleaseTraversal.EnumerateFiles(TempDir).Files;
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
    public static TraversalResult EnumerateFiles(string root, CancellationToken ct = default)
    {
        var files = new List<string>();
        var issues = new List<TraversalIssue>();
        try
        {
            _ = Directory.GetFiles(root); // probe: root failure is fatal (codex r1 f12)
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new TraversalResult([], [new TraversalIssue(root, e.Message)], RootFailed: true);
        }

        Walk(root, files, issues, ct);
        return new TraversalResult(files, issues, RootFailed: false);
    }

    private static void Walk(string dir, List<string> files, List<TraversalIssue> issues, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string[] dirFiles;
        string[] subdirs;
        try
        {
            dirFiles = Directory.GetFiles(dir);
            subdirs = Directory.GetDirectories(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            issues.Add(new TraversalIssue(dir, e.Message)); // scanner maps to Warnings
            return;
        }

        Array.Sort(dirFiles, StringComparer.Ordinal);
        Array.Sort(subdirs, StringComparer.Ordinal);
        files.AddRange(dirFiles);
        foreach (string sub in subdirs)
        {
            // pyrescene's os.walk does not follow directory reparse points (codex r1 f12).
            if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            Walk(sub, files, issues, ct);
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
  public production ctor `public ReleaseScanner()` PLUS internal test ctor
  `internal ReleaseScanner(Func<string, IReadOnlyList<string>>? sfvEntryReader, Func<string, ProofRarFacts>? proofRarReader)`
  (codex r1 f14). `public sealed record ProofRarFacts(bool Readable, bool HasPackedBlocks, bool AnyImage, bool LastPackedIsImage)`
  and the production inspector `public static class RarProofInspector { public static ProofRarFacts Inspect(string rarPath); }`
  live in **ReScene.Lib** (`ReScene.Lib/ReScene/RAR/ProofRarFacts.cs` + `RarProofInspector.cs`) —
  the lib owns RAR parsing and `RARHeaderReader` is lib-internal, so App.Core consumes the PUBLIC
  inspector (codex r2b f3). The inspector's own tests (real fixture RARs via RarFixtures) live in
  ReScene.Tests and are added in THIS task's lib companion change; App.Core.Tests drive the
  injectable seam with fact literals only. Rule 4 consumes
  `LastPackedIsImage` (last-block-wins), the independent proof-RAR pass consumes `AnyImage` —
  one seam serves both distinct predicates (codex r1 f3). Production reader implements it over
  the lib `RARHeaderReader`; the lib companion change in THIS task (Task 5) adds the ReScene.Tests test routing a REAL
  fixture RAR (RarFixtures) through `RarProofInspector.Inspect` — the seam is not a circular
  oracle and no cross-test-assembly reference exists (codex r4 f3); Task 7 only consumes the
  already-tested public API.

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
| `Proof/p.sfv` listing exactly `p.rar`, facts.LastPackedIsImage=true | rule 4: SFV+RAR → StoredFiles, not a set |
| `Proof/p.sfv` listing `p.rar`, facts: HasPacked, last block NOT image (earlier one is) | NOT proof (last-block-wins) → continues to rules 5-7 |
| `Proof/p.sfv` listing `p.rar`, facts.Readable=false | warning + excluded (treated proof) |
| `Proof/p.sfv` listing `p.RAR` (uppercase) | singleton entry not ending lowercase `.rar` → excluded as proof (excerpt casing check) |
| `Proof/p.sfv` listing `p.rar`, RAR file MISSING on disk | warning + excluded (excerpt missing-proof branch) |
| `Proof/p.sfv`, facts.HasPackedBlocks=false | last-block predicate false → NOT proof → continues |
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
    TraversalResult traversal = ReleaseTraversal.EnumerateFiles(releaseRoot, ct);
    if (traversal.RootFailed)
    {
        return ReleaseScanResult.RootError(releaseRoot, traversal.Issues[0].Message);
    }

    IReadOnlyList<string> all = traversal.Files;
    var warnings = new List<string>(
        traversal.Issues.Select(i => $"Unreadable: {i.Path} ({i.Message})")); // codex r3 f12

    string releaseName = Path.GetFileName(Path.TrimEndingDirectorySeparator(releaseRoot));
    string lcRelease = releaseName.ToLowerInvariant();
    var sfvs = ReleaseTraversal.FilterByExtension(all, ".sfv");

    var main = new List<string>(); var subs = new List<string>();
    var stored = new List<string>(); // warnings list created above from traversal issues

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
| `CD1/a.sfv` (rars) + root `x.mp3.sfv` listing `t.mp3` | BOTH survive rules 1-7 → BOTH MainSets (codex r1 f7: rescue does not apply; the mp3 set contributes zero volume blocks in Spec 1 and a warning notes it) |
| ONLY `grp-subs.sfv` listing `t.mp3` (excluded by rule 2, rescue fires) | rescue: music-entry SFV → `MusicSfvs` + warning [DIVERGENCE]; zero MainSets |
| ONLY `grp-subs.sfv` listing `a.rar b.rar` (excluded, rescue fires) | rescue: >1 entry → re-admitted MAIN |
| `Sample/clip.avi` | phase 1 (dir contains sample) → SampleFiles |
| `movie.sample.mkv` in root | phase 1 (name) → SampleFiles |
| `clip.avi` + sibling `clip.sfv` | phase 1 literal-slice sibling → SampleFiles |
| `clip.m2ts` + sibling `clip.sfv` | NOT phase-1-by-sibling: the slice computes `clip..sfv` (codex r1 f7), which does not exist |
| `clip.m2ts` + sibling `clip..sfv` (double dot, actually created) | phase 1 POSITIVE via the quirky computed name |
| `t.MP3`-listing SFV in rescue | NOT music (case-sensitive endswith) → >1-entry rule decides |
| any SFV present anywhere + loose `CD9/x.rar` | loose-RAR discovery DISABLED (sfvs exist) — x.rar not a set |
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
- Consume: `ReScene.RAR.RarProofInspector` / `ProofRarFacts` (PUBLIC lib API from Task 5's lib
  companion change — nothing App.Core-local; codex r3 f3)
- Test: `ReScene.App.Core.Tests/ReleaseScannerStoredTests.cs`

**Interfaces:**
- Consumes: Tasks 4-6 internals.
- Produces: populated `StoredFiles` and, with Task 9, ONE authoritative merge algorithm
  (codex r1 f6) mirroring the excerpt's generate_srr sequence:
  1) nfo pass, 2) m3u, 3) proof images+RARs, 4) log, 5) cue,
  6) GENERATED artifacts in sample traversal order (SRS or failure .txt or VOB nested SRR),
  7) remaining pre-existing .srs not superseded by 6,
  8) conditional fix RAR (`is_storable_fix` — excerpt),
  9) subtitle nested SRRs + their stored subtitle SFVs,
  10) FINAL SFV pass: input SFVs appended; any nested/proof `.srr` AND any proof `.rar` whose
     stem matches an SFV is MOVED immediately before that SFV (excerpt tail L1243: extension
     check is `('.srr', '.rar')` — codex r2b f6).
  Task 7 implements passes 1-5 + the pass-10 skeleton for input SFVs; Task 9 splices 6-9 and the
  full pass-10 reordering. A mixed-tree test in Task 9 asserts the COMPLETE ordered logical-name
  list.

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
  production delegate = `RarProofInspector.Inspect` — the PUBLIC lib API; unreadable →
  warning + not stored) → stored. Conditional fix RAR: transcribe the excerpt's fix-RAR storage condition.

- [ ] **Step 1: failing tests** — `ReleaseScannerStoredTests.cs` matrix:

| Item | Expectation |
|---|---|
| `release.nfo`, `imdb.nfo`, `TVMAZE.NFO` | only `release.nfo` stored |
| `playlist.m3u`, `rip.log`, `disc.cue`, `old.srs` | all stored in category order after nfo |
| `Proof/Folder.jpg` | STORED (keyword bypass precedes always_skip) |
| root `Folder.jpg`, `MyFolder.png`, `AlbumArtSmall.jpg`, `AlbumArt_{guid}_Large.jpg`, `has space.jpg` | all skipped (always_skip) |
| root `AlbumArtLarge.jpg` 150KB similar-named | STORED (predicate is `albumartsmall` contains + `albumart_{` prefix only) |
| root `00-cover.jpg` 5KB | stored (prefix accept, size irrelevant) |
| sfv `grp-movienight.sfv` (stem 14 chars), root image `grp-movienight-front.jpg` 150KB | stored — `similar_to_good_name` compares TEN-character slices (excerpt), so the shared prefix must be >= 10 chars (codex r4 f13) |
| nfo `00-grp-movienight.nfo` (only good name), root image `grp-movienight-front.jpg` 150KB | stored — good stem strip_zeros-normalized to `grp-movienight` before the 10-char compare |
| only `grp-movienight.m3u`, root image `grp-movienight-front.jpg` 150KB | stored (M3U-only similarity, same 10-char branch) |
| sfv `grp-movienight.sfv`, root image `unrelated-shot9.jpg` 150KB | skipped + warning (negative control) |
| sfv `grp-movie.sfv` (stem 9 chars), root image `grp-movie-front.jpg` 150KB | SKIPPED — shared prefix under 10 chars fails the slice compare (boundary negative — codex r4 f13) |
| root `big-cover.jpg` exactly 630x1200 px, similar-named, 150KB | skipped (fixed_resolution_cover) |
| `rip.log` matching the excerpt's log blacklist | not stored; non-blacklisted `x.log` stored |
| `no.nfo` sized per the excerpt's byte condition vs off-by-one | stored/skipped per exact excerpt predicate |
| fix-RAR gates (`is_storable_fix` true/false release names) | RAR stored only when gate passes |
| images `.png` before `.jpg` in same dir | order follows traversal (per-extension ordering row) |
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
- Modify: `ReScene.App.Core/ViewModels/MainWindowViewModel.cs` (both CreatorViewModel sites get
  the shared `new ReleaseScanner()` — the composition root for these VMs lives HERE, so DI is
  complete in this task; codex r2b f14)
- Test: `ReScene.App.Core.Tests/CreatorViewModelFolderModeTests.cs`

**Interfaces:**
- Consumes: Task 2's `CreateFromInputsAsync` (via service), Task 5's `IReleaseScanner` +
  `ReleaseScanResult`/`ReleaseSetInput`.
- Produces (Tasks 9-10 depend on): VM members
  `public ObservableCollection<ReleaseSetInput> DetectedSets { get; }`,
  `[ObservableProperty] public partial bool IsScanning { get; set; }`,
  `public IAsyncRelayCommand BrowseInputFolderCommand`,
  ctor gains `IReleaseScanner releaseScanner`. ATOMICALLY in THIS task (codex r1 f14): update
  `MainWindowViewModel` (both sites: Creator + wizard CreateSRRWizard, passing a shared
  `new ReleaseScanner()`), the `BeginnerShellViewModel` wizard construction, and EVERY test
  factory (grep `new CreatorViewModel(` across App.Core.Tests + Manager.Tests; tests pass
  `StubReleaseScanner`). Task 10 then touches ONLY markup/bindings.

Behavior (spec §3):
- EVERY InputPath change — file path, blank, or nonexistent — bumps the generation and cancels
  any in-flight folder scan (stale completions discard against the CURRENT generation regardless
  of the new input kind); `CanCreate` = not scanning AND not music-only AND existing gates, with
  `NotifyCanExecuteChanged` on every transition; ALL scan warnings kept ordered (status shows
  the count; the full ordered list goes to the existing log/details surface) — codex r2b f15.
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
  _releaseRoot, storeRelativePaths: true, additionalFiles: StoredFiles snapshot mapped to
  StoredFileEntry(StoredName, FullPath), options, ct)` (parameter name matches Task 2 — codex r1
  f14). File branch unchanged (byte-identity).

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
  - file-mode Create still calls the old single-SFV path (regression);
  - EVERY input change (file, blank, nonexistent) cancels/invalidates an older folder scan
    (stale folder-scan completion after switching to FILE input is discarded);
  - `CanCreate` false while IsScanning and for music-only, with command notification asserted;
  - result paths are absolute; logical StoredNames derive from the release root;
  - ALL warnings surfaced in order (status shows count, tooltip/log lists all — not just first);
  - storage-only tree → Create enabled → header-only/storage-only writer call captured;
  - filesystem-root and trailing-separator inputs → error status, no auto OutputPath;
  - exact service arguments captured for a mixed main+music tree (music excluded from inputs).
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
- Unique INJECTED temp working dir (`Func<string> workDirFactory`, default GetTempPath+GUID —
  codex r1 f8), cleaned in `finally` WITHOUT swallowing OperationCanceledException; artifact
  logical name = root-relative source path with extension swapped (`Sample/x.mkv` →
  `Sample/x.srs`); collision keying by FULL RELATIVE STEM; same-stem collision keeps full ext.
- SRS failure → failure file named `basename(sample) + ".txt"` (`clip.mkv.txt`), stored ONLY when
  non-empty (excerpt); RAR-backed lowercase-`.vob` sample (leading bytes `Rar!`, case-sensitive
  checks per excerpt) → keeps its generated SRS AND adds a nested SRR — BOTH artifacts (codex r1
  f8, not a replacement); subtitle processing returns an ORDERED COLLECTION
  (`IReadOnlyList<string>` of produced SRR paths) — one SFV may yield several; each excluded
  subtitle SFV is itself stored (merge pass 9).
- Generated `.srs` SUPERSEDES a same-relative-path pre-existing `.srs` in the stored list (replace
  entry, no collision error).
- Cancellation/failure → delete the working dir best-effort; destination untouched (writer's
  transaction covers the SRR itself).
- ENABLE `FullPipelineGoldenTests` (added disabled in Task 3 — codex r2b f4): regenerate the
  full-pipeline golden over a tree WITH `Sample/` and `Subs/` using
  `python bin/pyrescene.py --vobsub-srr --output <tmp> <tree>` (NO `--no-srs`; `--no-isdb`
  retained), PYTHONPATH shim as in Task 3; nested-SRR app-name fields normalized with the SAME
  `NormalizeAppName` applied to each stored `.srr` payload; assert complete byte equality of the
  folder-mode VM output.
- Executable pass-10 reorder step (codex r2b f6): after assembling the stored list, move every
  nested/proof `.srr` AND proof `.rar` whose stem matches an SFV to immediately before that SFV
  (excerpt tail L1243); the mixed-tree test asserts the COMPLETE ordered logical-name list.
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
- Test: `ReScene.Manager.Tests/CreatorViewFolderBindingTests.cs` — RED-first headless binding
  test using the repo's existing Avalonia.Headless idiom (codex r1 f16): instantiate
  `CreatorView` with a VM whose `BrowseInputFolderCommand` is a recording stub; find the
  'Browse folder…' button; assert command binds and executes; assert the DetectedSets
  ItemsControl binds `RelativeName`. Plus the build gate and Task 11 E2E.

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
- [ ] **Step 1 (RED):** write `CreatorViewFolderBindingTests` AND `CreateSRRWizardBodyBindingTests`
  (same recording-stub pattern); run
  `dotnet test ReScene.Manager.Tests/... --filter FolderBinding` → both FAIL (button absent).
- [ ] **Step 2:** markup edits on BOTH surfaces per the contract above.
- [ ] **Step 3 (GREEN):** re-run the filter → PASS; then the full Manager suite.
- [ ] **Step 4:** forced-rebuild gate (`-p:BaseOutputPath=bin2/`, 0/0, delete bin2) → commit
  `feat(ui): folder input chrome on Creator + wizard (a11y contract)`.

### Task 11: E2E verification + final review

- [ ] **Step 1:** synthesize `%TEMP%\e2e-2disc\` release folder (reuse Task 3's generator tree
  layout: CD1/CD2 sets + nfo + Sample). Via agent bridge: launch worktree app → Advanced SRR
  Creator tab → CLICK 'Browse folder…' via `ava_input` (live command binding; dialog dismissed
  via Escape `ava_key`) THEN `ava_input text` the folder path; the WIZARD pass in Step 3 clicks
  its own 'Browse folder…' the same way before typing (both surfaces exercised — codex r2b f16) → verify DetectedSets shows `CD1/a.sfv` and
  `CD2/b.sfv` (`ava_search by text`), status summary Ok, automation names present (`ava_props`
  on both browse buttons + list) → dispatch CreateSRRCommand → assert output exists.
- [ ] **Step 2:** load the created SRR in the Inspector (typed path + `ava_key Enter`): tree shows
  both sets' volume blocks; Reconstructor import shows both discs in the Set column.
- [ ] **Step 3:** wizard surface: Beginner mode → Create an SRR → type folder on step 1 → steps
  2-3 pre-populated (bridge-verify list contents) → cancel (no creation needed twice).
- [ ] **Step 4:** full gates: lib + App.Core + Manager suites, forced-rebuild gate 0/0.
- [ ] **Step 5:** final whole-branch review: `BASE=$(git merge-base main HEAD)`, then the
  Execution Regime's review-package invocation with that `$BASE` (no placeholders — codex r4
  f17) + codex whole-branch review (runner-script pattern, scoped prompt); dispatch ONE fix
  subagent for the complete findings list; re-verify; commit.

## Execution Regime (binding)

Per the standing approval in the header: subagent-driven development
(superpowers:subagent-driven-development), fresh implementer per task, task-reviewer per task,
PLUS a codex diff review per task (runner-script pattern with fidelity-scoped prompts; the
scanner tasks' prompts must point codex at the excerpt file). Ledger: `.superpowers/sdd-multiset/progress.md` (plan-specific — codex r1 f17). review-package invocation (script verified to exist 2026-07-19); BASE is COMPUTED, never a
placeholder (codex r3 f17): per task, `BASE=$(git rev-parse HEAD)` recorded IMMEDIATELY BEFORE
dispatching that task's implementer; for the Task 11 whole-branch review,
`BASE=$(git merge-base main HEAD)`. Then:
`bash "$HOME/.claude/plugins/cache/claude-plugins-official/superpowers/6.1.1/skills/subagent-driven-development/scripts/review-package" "$BASE" HEAD`
(if the plugin version dir moved, locate with
`ls "$HOME/.claude/plugins/cache/claude-plugins-official/superpowers"`; fallback:
`git log --oneline "$BASE"..HEAD && git diff --stat "$BASE" HEAD && git diff -U10 "$BASE" HEAD`
redirected to one file, per the sdd skill).
Bridge provisioning: the worktree Debug build carries AvaDevBridge (dotnet run via ava_launch);
no extra install. Model selection per the sdd skill's
guidance; Task 5 and Task 7 implementers get the excerpt path in their dispatch as required
reading alongside the task brief.


