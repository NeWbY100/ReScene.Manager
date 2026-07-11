# RAR Reconstruction Correctness Fixes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all 25 verified codex findings in the RAR reconstruction subsystem, restoring the common single-set workflow and closing the multi-set correctness/safety gaps.

**Architecture:** Lib-first (two additive `ReScene.Lib` changes), then a root-cause redesign of the app's work-root/relocation path (uniform scratch-then-relocate with a containment guard), then mechanical correctness fixes grouped by file. Every task is TDD: failing test → minimal fix → green → commit.

**Tech Stack:** .NET 10 · CommunityToolkit.Mvvm 8.4 · xUnit · `ReScene.App.Core` (UI-agnostic) + `ReScene.Lib` submodule. Spec: `docs/superpowers/specs/2026-07-11-reconstruction-fixes-design.md`.

## Global Constraints

- **Single-set output contract:** a single-set run must produce byte-identical `.rar` output at `OutputPath\output\<name>` (same bytes, same location). The uniform relocation satisfies this via a same-volume rename; assert the option-builder + final paths in a test.
- **No destructive delete outside the output tree:** every `Directory.Delete`/`File.Delete` on a reconstruction path first canonicalizes its target and asserts it is at or under `Path.GetFullPath(Path.Combine(OutputPath, "output"))`.
- **Honest reporting:** a set reports success only when its own verified output is placed at its final location — never while output is stranded, missing, or a sibling's output was destroyed.
- **TDD, small commits:** one failing test per fix first; commit per task.
- **Build gate (every task):** `dotnet build ReScene.Manager.slnx -c Debug -p:BaseOutputPath=bin2/` → 0 warnings / 0 errors; relevant `dotnet test` green; delete `bin2/` after.
- **Lib-first:** `ReScene.Lib` changes (Tasks 1–2) land and the submodule pointer is bumped before app tasks build against them. No flat `SRRFile` field is removed.
- **Deferred:** recovery-record (`-rr`) — no `-rr` switch exists; Task 7 leaves a documented `// TODO(-rr)` where it would attach.
- **Commit trailer:** end every commit message with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

## File Structure

**Lib (`ReScene.Lib/ReScene/`):** `SRR/SRRArchiveSet.cs` (per-set dirs), `SRR/SRRFileParser.cs` (route dir records), `Core/Manager.cs` (dir-qualified CRC key).
**App (`ReScene.App.Core/ViewModels/Reconstruction/`):** `OutputPathGuard.cs` (new), `ArchiveSetPlanner.cs`, `SRRSwitchMapper.cs`, `RARCommandLineBuilder.cs`, `ReconstructionProgressTracker.cs`, `ImportedSRRStateMapper.cs`, `SRRImportParser.cs`, `ReconstructorFieldGuidance.cs`; `ViewModels/ReconstructorViewModel.cs`.
**Tests:** `ReScene.App.Core.Tests/` and `ReScene.Lib/ReScene.Tests/`.

Line numbers below are anchors from the review; re-confirm with a quick read before editing (the file may have shifted by earlier tasks).

---

## Task 1: Lib — per-set directory tracking (#7)

**Files:** Modify `ReScene.Lib/ReScene/SRR/SRRArchiveSet.cs`, `ReScene.Lib/ReScene/SRR/SRRFileParser.cs` (directory-record site ~708-715); Test `ReScene.Lib/ReScene.Tests/SRRArchiveSetTests.cs`.

**Interfaces — Produces:** `SRRArchiveSet.ArchivedDirectories` (`IReadOnlyList<string>`), `SRRArchiveSet.ArchivedDirectoryTimestamps` (`IReadOnlyDictionary<string, DateTime>`) — the set's own in-archive directory records; the flat `SRRFile.ArchivedDirectories` union is unchanged.

- [ ] **Step 1 — failing test:** In `SRRArchiveSetTests`, build a synthetic two-set SRR (via `SRRTestDataBuilder`, RAR4) where set A contains an in-archive directory `SubsA` and set B contains `SubsB`. Assert `sets[0].ArchivedDirectories` contains only `SubsA` and `sets[1].ArchivedDirectories` only `SubsB`.
- [ ] **Step 2 — run, expect FAIL** (property does not exist / directories flattened): `dotnet test ReScene.Lib/ReScene.Tests/ --filter SRRArchiveSet -p:BaseOutputPath=bin2/`.
- [ ] **Step 3 — implement:** Add a mutable backing collection (`_archivedDirectories`, `_archivedDirectoryTimestamps`) + read-only exposers to `SRRArchiveSet` (mirror the existing `ArchivedFiles`/`ArchivedFileTimestamps` members). In `SRRFileParser` at the directory branch (`if (isDirectory)` ~708-713), after adding to the flat `srr.ArchivedDirectories`, also add to `srr.CurrentArchiveSet?.` the per-set collection (and its timestamp), mirroring how file records already route to `set?.ArchivedFiles`.
- [ ] **Step 4 — run, expect PASS.**
- [ ] **Step 5 — commit** (`fix(lib): track in-archive directories per archive set (#7)`).

## Task 2: Lib — directory-qualified per-volume CRC key (#9) + bump submodule pointer

**Files:** Modify `ReScene.Lib/ReScene/Core/Manager.cs` (`BuildExpectedInOrder` ~160-166); Test `ReScene.Lib/ReScene.Tests/` (Manager/VolumeMatch area).

**Interfaces — Produces:** `Manager.BuildExpectedInOrder` keys expected volumes by a directory-qualified relative path (normalized `\`→`/`) rather than bare `Path.GetFileName`, consistent with the app-side `ArchiveSetPlanner.BuildExpectedVolumeCrcs` change in Task 8.

- [ ] **Step 1 — failing test:** expected CRC map with two entries `CD1/release.rar` and `CD2/release.rar` (different CRCs) → `BuildExpectedInOrder` for a set whose volumes are `CD2\release.rar` picks CRC_B, not CRC_A. Assert the ordered expected list matches CD2's CRCs.
- [ ] **Step 2 — run, expect FAIL** (bare basename collides on `release.rar`).
- [ ] **Step 3 — implement:** normalize keys to dir-qualified relative paths on both the expected-map build and the produced-volume lookup; where produced volumes carry only basenames, map the set's dir-qualified names down to basenames for the final lookup. Keep behavior identical for the common distinct-basename case.
- [ ] **Step 4 — run, expect PASS**; run the full lib suite to confirm no regression: `dotnet test ReScene.Lib/ReScene.Tests/ -p:BaseOutputPath=bin2/`.
- [ ] **Step 5 — commit in the submodule** (`fix(lib): directory-qualified per-volume CRC matching (#9)`), then **bump the pointer**: from the superproject worktree root `git add ReScene.Lib && git commit -m "chore: bump ReScene.Lib (per-set dirs #7, dir-qualified CRC #9)"` (+ trailer). Delete `bin2/`.

---

## Task 3: App — OutputPathGuard containment helper (#1 foundation)

**Files:** Create `ReScene.App.Core/ViewModels/Reconstruction/OutputPathGuard.cs`; Test `ReScene.App.Core.Tests/Reconstruction/OutputPathGuardTests.cs`.

**Interfaces — Produces:** `static bool OutputPathGuard.IsUnderOutput(string outputPath, string candidateFullPath)` — true iff `Path.GetFullPath(candidateFullPath)` is equal to or nested under `Path.GetFullPath(Path.Combine(outputPath, "output"))` (case-correct per OS); and `static string OutputPathGuard.ResolveFinalDir(string outputPath, string setDirectory)` — returns the canonical `output\<setDirectory>` path and **throws `InvalidOperationException`** if it escapes the output tree.

- [ ] **Step 1 — failing tests:**
```csharp
[Theory]
[InlineData("", "release.rar")]          // root of output tree
[InlineData("DVD1", "DVD1")]             // one level down
public void ResolveFinalDir_StaysUnderOutput(string setDir, string expectedTail)
{
    string outputPath = OperatingSystem.IsWindows() ? @"C:\out" : "/out";
    string resolved = OutputPathGuard.ResolveFinalDir(outputPath, setDir);
    Assert.True(OutputPathGuard.IsUnderOutput(outputPath, resolved));
    Assert.EndsWith(expectedTail, resolved.TrimEnd(Path.DirectorySeparatorChar));
}

[Fact]
public void ResolveFinalDir_RejectsTraversal()
{
    string outputPath = OperatingSystem.IsWindows() ? @"C:\out" : "/out";
    Assert.Throws<InvalidOperationException>(
        () => OutputPathGuard.ResolveFinalDir(outputPath, "../../Documents"));
}
```
- [ ] **Step 2 — run, expect FAIL** (class does not exist).
- [ ] **Step 3 — implement** `OutputPathGuard`: `IsUnderOutput` canonicalizes both sides with `Path.GetFullPath`, appends a trailing separator, and compares with `StringComparison.OrdinalIgnoreCase` on Windows / `Ordinal` elsewhere (equal OR `candidate.StartsWith(outputRoot)`). `ResolveFinalDir` computes `Path.GetFullPath(Path.Combine(outputPath, "output", setDirectory.Replace('/', Path.DirectorySeparatorChar)))` and throws if `!IsUnderOutput`.
- [ ] **Step 4 — run, expect PASS.**
- [ ] **Step 5 — commit** (`feat: OutputPathGuard containment helper for reconstruction deletes`).

## Task 4: App — uniform WorkRootFor + Sanitize hardening (#3 part 1)

**Files:** Modify `ReScene.App.Core/ViewModels/Reconstruction/ArchiveSetPlanner.cs` (`WorkRootFor` ~182-185, `Sanitize` ~265); Test `ReScene.App.Core.Tests/Reconstruction/ArchiveSetPlannerTests.cs`.

**Interfaces — Produces:** `WorkRootFor(shared, set)` now returns `Path.Combine(OutputPath, ".rescene-work", Sanitize(setKeyOrRelease))` for **every** set (single or multi); `Sanitize` additionally strips `..`/rooted segments.

- [ ] **Step 1 — failing test:** for a single set with a **non-empty** key (`MakeSet("release", "", ...)`), assert `WorkRootFor` returns a path under `OutputPath\.rescene-work\release` (today it returns `OutputPath`). Add: `Sanitize("../evil")` contains no `..`.
- [ ] **Step 2 — run, expect FAIL** (returns `OutputPath` for the non-empty single set).
- [ ] **Step 3 — implement:**
```csharp
public static string WorkRootFor(SharedReconstructionSettings shared, SRRArchiveSet set) =>
    Path.Combine(shared.OutputPath, ".rescene-work", Sanitize(string.IsNullOrEmpty(set.Key) ? "release" : set.Key));
```
Harden `Sanitize` to also replace/strip `..` and any rooted prefix (e.g. split on `/\`, drop `..`/empty/root segments, re-join with `_`).
- [ ] **Step 4 — run, expect PASS.**
- [ ] **Step 5 — commit** (`fix: single-set reconstruction uses a scratch work-root like multi-set (#3)`).

## Task 5: App — uniform relocation + guard + custom-packer + clear-once (#3 part 2, #1, #4, #5)

**Files:** Modify `ReScene.App.Core/ViewModels/ReconstructorViewModel.cs` (`RelocateVerifiedOutput` ~1855-1898, `CleanupWorkRoot` ~1904-1928, run loop ~1625-1637, and the pre-run cleanup ~1417-1446); Test `ReScene.App.Core.Tests/` (Reconstructor VM headless tests).

**Interfaces — Consumes:** `OutputPathGuard` (Task 3), `WorkRootFor` (Task 4). **Produces:** `RelocateVerifiedOutput(workRoot, set, setCount)` relocates for **all** sets; no `setCount <= 1` early-return.

- [ ] **Step 1 — failing tests (headless VM/seam, no rar.exe):** (a) single set → after a simulated verified run, files land at `OutputPath\output\<name>` and `.rescene-work` is gone; (b) two sets sharing `Directory="DVD1"` → both sets' outputs survive (no sibling deletion); (c) a set with `Directory="../../x"` → relocation throws/records failure and performs **no** delete outside `output`; (d) custom-packer layout (volumes at `<workRoot>` root, no `\output`) → relocated to `output\<dir>` and reported success; (e) missing output entirely → set reported **failed**, not success.
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** remove both `setCount <= 1` early-returns. In `RelocateVerifiedOutput`: `sourceDir = Directory.Exists(Path.Combine(workRoot,"output")) ? that : workRoot` (custom-packer volumes at root); if the chosen source has no `.rar`/`.rNN` volumes → return `false` (failure). Compute `targetDir = OutputPathGuard.ResolveFinalDir(OutputPath, set.Directory)` (throws on traversal → caught, return `false`). Do **not** recursively delete a shared `targetDir`; only `Directory.CreateDirectory(targetDir)` then move this set's files. Clear `OutputPath\output` **once** at run start (in the pre-run cleanup block, replacing per-set recursive deletes). `CleanupWorkRoot`: delete only `workRoot` (guarded), never a shared `output\<dir>`.
- [ ] **Step 4 — run, expect PASS.**
- [ ] **Step 5 — commit** (`fix: uniform verified-output relocation with containment guard (#3,#1,#4,#5)`).

## Task 6: App — cancellation cleans the in-flight work-root (#17)

**Files:** Modify `ReScene.App.Core/ViewModels/ReconstructorViewModel.cs` (run loop `catch (OperationCanceledException)` ~1601-1623); Test `ReScene.App.Core.Tests/`.

- [ ] **Step 1 — failing test:** simulate a cancel thrown from a set's run (copy/CRC phase) in a multi-set run → assert the set's `<workRoot>` is removed before the exception propagates.
- [ ] **Step 2 — run, expect FAIL** (scratch dir remains).
- [ ] **Step 3 — implement:** wrap the per-set body in `try { … } finally { if (cancellation or failure && !committed) CleanupWorkRoot(options.OutputDirectoryPath, set, sets.Count); }`, or call `CleanupWorkRoot` inside the `catch (OperationCanceledException)` before `throw`. Ensure a committed (successfully relocated) set is **not** cleaned.
- [ ] **Step 4 — run, expect PASS.**
- [ ] **Step 5 — commit** (`fix: clean in-flight work-root on cancellation (#17)`).

## Task 7: App — per-set command/version matrices (#6)

**Files:** Modify `ReScene.App.Core/ViewModels/Reconstruction/ArchiveSetPlanner.cs` (`BuildOptionsForSet` ~99-168); Test `ArchiveSetPlannerTests`.

- [ ] **Step 1 — failing test:** two sets, A metadata `CompressionMethod=0 (store), IsSolid=false`, B `CompressionMethod=5 (best), IsSolid=true`, both within the user's selected switches → assert set A's `CommandLineArguments` contain a `-m0`/`-s-` combo and set B's contain `-m5`/`-s`, and each set's `RARVersions` derive from its own `RARVersion`. (Today both reuse the identical global matrix.)
- [ ] **Step 2 — run, expect FAIL** (both sets identical).
- [ ] **Step 3 — implement:** when `set` carries metadata (`CompressionMethod`/`DictionarySize`/`RARVersion`/`IsSolid` non-null), build a per-set matrix by constraining the shared switch settings to the set's values before calling `RARCommandLineBuilder.BuildCommandLineArguments`/`BuildVersionRanges`; when the set has none (flat no-SRR set), fall back to `shared.CommandLineArguments`/`shared.RARVersions`. Add `// TODO(-rr): thread set.HasRecoveryRecord once a -rr switch exists (deferred).`
- [ ] **Step 4 — run, expect PASS.**
- [ ] **Step 5 — commit** (`fix: per-set command/version matrices from set metadata (#6)`).

## Task 8: App — per-set hash gate, basename, dir-qualified CRC, per-set dirs (#8, #10, #9, #7-app)

**Files:** Modify `ReScene.App.Core/ViewModels/Reconstruction/ArchiveSetPlanner.cs` (`BuildOptionsForSet` hash loop ~153-156 and dirs ~122; `BuildExpectedVolumeCrcs` ~67-95); Test `ArchiveSetPlannerTests`.

**Interfaces — Consumes:** `SRRArchiveSet.ArchivedDirectories` (Task 1), the dir-qualified key convention (Task 2).

- [ ] **Step 1 — failing tests:** (#8) set B's `options.Hashes` excludes set A's first-volume CRC when a combined verification file covers both; (#10) `BuildExpectedVolumeCrcs` matches `DVD1\release.rar` against a flat `release.rar` SFV entry on any separator (run with `\`); (#9) two sets with identical basenames in `CD1\`/`CD2\` get distinct CRCs; (#7-app) `options.RAROptions.ArchiveDirectoryPaths` for a set equals that set's `ArchivedDirectories`, not the release union.
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** (#8) filter `shared.VerificationHashes` to this set's volume filenames before adding to `options.Hashes` (reuse the `wanted` set logic). (#10) replace `Path.GetFileName(...)` at lines 70/77 with a helper `LastSegment(string p) => p.Replace('\\','/').TrimEnd('/').Split('/')[^1]`. (#9) key `BuildExpectedVolumeCrcs` by dir-qualified relative path to agree with Task 2's `Manager`. (#7-app) source directories from `set.ArchivedDirectories` (fallback to `shared.ArchiveDirectories` for the flat set).
- [ ] **Step 4 — run, expect PASS.**
- [ ] **Step 5 — commit** (`fix: per-set hash gate, cross-platform basename, dir-qualified CRC, per-set dirs (#8,#10,#9,#7)`).

## Task 9: App — RAR5 compression method normalization (#11)

**Files:** Modify `ReScene.App.Core/ViewModels/Reconstruction/SRRSwitchMapper.cs` (`MapCompression` 58-72) and `ReScene.App.Core/ViewModels/Reconstruction/SRRImportParser.cs` (`DescribeCompression` ~97-107); Test `ReScene.App.Core.Tests/Reconstruction/SRRSwitchMapperTests.cs`.

- [ ] **Step 1 — failing test:** an `SRRFile` whose `CompressionMethod == 0x35` (RAR5 `-m5`) → `Map(srr).Compression` is `CompressionMap(5, "Best")` (today: null). Add `DescribeCompression(0x35) == "Best"`.
- [ ] **Step 2 — run, expect FAIL** (0x35 > 5 → null).
- [ ] **Step 3 — implement:** normalize before the range check:
```csharp
int method = srr.CompressionMethod.Value;
if (method >= 0x30) { method -= 0x30; }   // RAR5 stores ASCII 0x30..0x35; RAR4 already 0..5
if (method is < 0 or > 5) { return null; }
return new CompressionMap(method, _compressionNames[method]);
```
Apply the identical `if (method >= 0x30) method -= 0x30;` normalization in `SRRImportParser.DescribeCompression`.
- [ ] **Step 4 — run, expect PASS.**
- [ ] **Step 5 — commit** (`fix: map RAR5 compression method 0x30-0x35 (#11)`).

## Task 10: App — dictionary sizes 8 MiB–1 GiB (#12)

**Files:** Modify `ReScene.App.Core/ViewModels/Reconstruction/SRRSwitchMapper.cs` (`DictionarySwitch` enum 25-35, `MapDictionary` 74-98) and `ReScene.App.Core/ViewModels/ReconstructorViewModel.cs` (`ApplySwitchDiff` dictionary cases ~2466-2496); Test `SRRSwitchMapperTests`.

- [ ] **Step 1 — discover exact toggles:** grep the VM for `SwitchMD` properties to enumerate the large-dictionary toggles that exist (e.g. `SwitchMD8M`…`SwitchMD1G`): `rg "SwitchMD\w+" ReScene.App.Core/ViewModels/ReconstructorViewModel.cs`.
- [ ] **Step 2 — failing test:** `Map(srr)` for `DictionarySize == 1048576` (1 GiB) → `DictionaryMap(DictionarySwitch.MD1G, 1048576)` (today: `None`).
- [ ] **Step 3 — run, expect FAIL.**
- [ ] **Step 4 — implement:** extend the `DictionarySwitch` enum and the `MapDictionary` `size switch` with the discovered sizes (`8192→MD8M`, `16384→MD16M`, `32768→MD32M`, `65536→MD64M`, `131072→MD128M`, `262144→MD256M`, `524288→MD512M`, `1048576→MD1G` — include exactly the sizes whose `SwitchMDxx` toggles exist), and add matching cases to `ApplySwitchDiff` mirroring the existing MD64K…MD4096K wiring. Remove the "deliberately not mapped" comment.
- [ ] **Step 5 — run, expect PASS; commit** (`fix: map large RAR dictionary sizes 8M-1G (#12)`).

## Task 11: App — retire stale auto-SFV on every import (#15)

**Files:** Modify `ReScene.App.Core/ViewModels/ReconstructorViewModel.cs` (`TryExtractStoredSFV` ~2576-2612); Test `ReScene.App.Core.Tests/`.

- [ ] **Step 1 — failing test:** import A (embedded SFV → `VerificationPath` set under `_sfvTempDir`), then import B with `StoredFiles.Count == 0` → assert `VerificationPath` is cleared and `_sfvTempDir` is null (today it retains A's SFV).
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** move the retire block (clear `VerificationPath` when it starts with `_sfvTempDir`, `_tempDir.Cleanup(_sfvTempDir)`, `_sfvTempDir = null`) to run **before** the `if (srr.StoredFiles.Count == 0) return;` early-return (or unconditionally at method entry).
- [ ] **Step 4 — run, expect PASS; commit** (`fix: retire previous auto-extracted SFV on no-stored-files import (#15)`).

## Task 12: App — persist/restore ArchiveSets in saved config (#22)

**Files:** Modify `ReScene.App.Core/ViewModels/Reconstruction/ImportedSRRStateMapper.cs` (~44) and its DTO; Test `ReScene.App.Core.Tests/`.

- [ ] **Step 1 — failing test:** round-trip a two-set imported state through the config mapper (to-DTO then from-DTO) → `ArchiveSets.Count == 2` with the correct per-set volumes (today: sets lost, one merged set synthesized).
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** add a per-set DTO list to the persisted config record and map `_import.ArchiveSets` to/from it. If a legacy config lacks sets but names an existing `SRRFilePath`, leave it to `ResolveSets`' re-parse fallback (no change needed there).
- [ ] **Step 4 — run, expect PASS; commit** (`fix: persist per-set archive sets in saved config (#22)`).

## Task 13: App — clamp/overflow-guard the -mt matrix + build off the UI thread (#13)

**Files:** Modify `ReScene.App.Core/ViewModels/Reconstruction/RARCommandLineBuilder.cs` (267-268, 284) and `ReScene.App.Core/ViewModels/ReconstructorViewModel.cs` (matrix build in `BuildSharedSettings` ~1689 and the `SwitchMTStart/End` setters ~714-715); Test `ReScene.App.Core.Tests/Reconstruction/RARCommandLineBuilderTests.cs`.

- [ ] **Step 1 — failing test:** `RARSwitchSettings { SwitchMT = true, SwitchMTStart = 1, SwitchMTEnd = int.MaxValue }` → `BuildCommandLineArguments` returns without OOM/overflow and the produced `-mtN` values are all `≤ 64` (today: inclusive loop wraps at `int.MaxValue`).
- [ ] **Step 2 — run, expect FAIL** (hang/overflow).
- [ ] **Step 3 — implement:** clamp both ends to WinRAR's real max in the builder:
```csharp
int mtLo = s.SwitchMT ? Math.Clamp(Math.Min(s.SwitchMTStart, s.SwitchMTEnd), 1, 64) : 0;
int mtHi = s.SwitchMT ? Math.Clamp(Math.Max(s.SwitchMTStart, s.SwitchMTEnd), 1, 64) : 0;
```
(The `≤ 64` clamp makes the inclusive `for` loop overflow-safe.) Also clamp in the VM `SwitchMTStart`/`SwitchMTEnd` `partial` setters (`Math.Clamp(value, 1, 64)`), and wrap the matrix build in `BuildSharedSettings` in `await Task.Run(() => …, token)` so the Cartesian expansion runs off the UI thread under the run's `CancellationToken`.
- [ ] **Step 4 — run, expect PASS; commit** (`fix: clamp and off-thread the -mt option matrix (#13)`).

## Task 14: App — volume-size fallback does not reinterpret the unit (#21)

**Files:** Modify `ReScene.App.Core/ViewModels/Reconstruction/RARCommandLineBuilder.cs` (`BuildVolumeArgument` 370-388); Test `RARCommandLineBuilderTests`.

- [ ] **Step 1 — failing test:** `RARSwitchSettings { VolumeSize = "", VolumeSizeUnitIndex = 3 /* GB */ }` → `BuildVolumeArgument` returns `-v15000k` (today: `-v15000000000`, ~15 TB).
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** return the fixed KB fallback directly before the unit switch:
```csharp
if (!long.TryParse(s.VolumeSize, out long sizeValue) || sizeValue <= 0)
{
    return $"-v{DefaultVolumeSizeKb}k";
}
```
- [ ] **Step 4 — run, expect PASS; commit** (`fix: volume-size fallback ignores the selected unit (#21)`).

## Task 15: App — snapshot verification hashes before output cleanup (#14)

**Files:** Modify `ReScene.App.Core/ViewModels/ReconstructorViewModel.cs` (pre-run cleanup ~1417-1446, `LoadVerificationHashes` ~1722-1733, and add the overlap guard for `VerificationPath` under `OutputPath`); Test `ReScene.App.Core.Tests/`.

- [ ] **Step 1 — failing test:** `VerificationPath` = a `.sfv` inside `OutputPath`, output non-empty (cleanup runs) → the run still gates on the parsed hashes (they are not silently emptied by the delete).
- [ ] **Step 2 — run, expect FAIL** (file deleted → empty hashes).
- [ ] **Step 3 — implement:** load and cache the parsed verification hashes into an in-memory field **before** the output-cleanup block runs, and have `LoadVerificationHashes` prefer the cached snapshot; additionally extend the Start-time overlap guard to reject `VerificationPath` equal-to/under `OutputPath` (mirror `PathsOverlap`).
- [ ] **Step 4 — run, expect PASS; commit** (`fix: snapshot verification hashes before output cleanup (#14)`).

## Task 16: App — guard preflight directory enumeration (#18)

**Files:** Modify `ReScene.App.Core/ViewModels/ReconstructorViewModel.cs` (~1302 and ~1417); Test `ReScene.App.Core.Tests/`.

- [ ] **Step 1 — failing test:** a fake directory service (or a path) whose enumeration throws `UnauthorizedAccessException` at the preflight step → Start surfaces a validation error via the dialog service, `IsRunning` stays false, no exception escapes.
- [ ] **Step 2 — run, expect FAIL** (exception escapes to the global handler).
- [ ] **Step 3 — implement:** wrap the two `Directory.Enumerate*` preflight calls in `try { … } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { <show validation error>; return; }`, mirroring the guards already at 1405/1427.
- [ ] **Step 4 — run, expect PASS; commit** (`fix: guard preflight directory enumeration (#18)`).

## Task 17: App — link-resolved, case-correct path-overlap guard (#2, #26)

**Files:** Modify `ReScene.App.Core/ViewModels/Reconstruction/ReconstructorFieldGuidance.cs` (`PathsOverlap` 138-159); Test `ReScene.App.Core.Tests/Reconstruction/ReconstructorFieldGuidanceTests.cs`.

- [ ] **Step 1 — failing tests:** (#26) on a case-sensitive comparison, `/data/Release` vs `/data/release` are **not** overlapping (assert via an injected comparer or `OperatingSystem` branch); (#2) a resolve helper: two distinct lexical paths resolving to the same real directory **are** overlapping (test the resolve+compare helper directly; skip actual symlink creation if the platform/CI can't).
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** pick the comparer once — `OperatingSystem.IsWindows() ? OrdinalIgnoreCase : Ordinal` — and use it for all three compares. Before comparing, resolve real targets: when a path exists, use `Directory.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? Path.GetFullPath(path)`; fall back to lexical `Path.GetFullPath` when the path does not exist or resolution throws.
- [ ] **Step 4 — run, expect PASS; commit** (`fix: link-resolved and case-correct path-overlap guard (#2,#26)`).

## Task 18: App — set-aware progress rows + wall-clock ETA (#23, #25)

**Files:** Modify `ReScene.App.Core/ViewModels/Reconstruction/ReconstructionProgressTracker.cs` (row identity ~158, ETA tick ~193); Test `ReScene.App.Core.Tests/Reconstruction/ReconstructionProgressTrackerTests.cs`.

- [ ] **Step 1 — failing tests:** (#23) set 1 succeeds with combo C, set 2 seeds C then fails → set 2's row is not marked "Match"; (#25) after a progress event reporting 100 s remaining, advancing the injected clock by 5 s without a new event decreases remaining to ~95 s (today it stays 100 s and pushes ETA forward).
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** include the active set in the row key (reset/stamp rows on `SetActiveSet`) and finalize each row from its own `SetOutcome`, not a global "any succeeded"; cache the estimate's wall-clock timestamp (via the existing injected time source) and subtract elapsed on each tick.
- [ ] **Step 4 — run, expect PASS; commit** (`fix: set-aware progress rows and wall-clock ETA (#23,#25)`).

## Task 19: App — single timestamp summary, batched log, current-set progress label (#19, #20, #24)

**Files:** Modify `ReScene.App.Core/ViewModels/ReconstructorViewModel.cs` (timestamp-failure display ~2293, log append ~2338-2354, progress mapping ~2244); Test `ReScene.App.Core.Tests/`.

- [ ] **Step 1 — failing tests:** (#19) two sets each producing a timestamp failure → the warning dialog is shown exactly once, after the run; (#20) appending N log events performs at most K UI dispatches and preserves (bounded) content — assert via a counting `IUiDispatcher`; (#24) progress across two sets is labeled "Set X of N" and does not appear to regress within a set.
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** accumulate `_timestampFailures` and show/clear one summary after `RunArchiveSetsAsync` returns; buffer log lines and coalesce UI updates (bounded builder, single throttled dispatcher post) instead of rebuilding the whole immutable string per event; label the progress bar/counters as current-set (`Set X of N`).
- [ ] **Step 4 — run, expect PASS; commit** (`fix: single timestamp summary, batched log, current-set progress (#19,#20,#24)`).

---

## Coverage check (all 25 findings → task)

#1 T3+T5 · #2 T17 · #3 T4+T5 · #4 T5 · #5 T5 · #6 T7 · #7 T1+T8 · #8 T8 · #9 T2+T8 · #10 T8 · #11 T9 · #12 T10 · #13 T13 · #14 T15 · #15 T11 · #17 T6 · #18 T16 · #19 T19 · #20 T19 · #21 T14 · #22 T12 · #23 T18 · #24 T19 · #25 T18 · #26 T17. (#16 is the verified false positive — no task.)

## Final verification (after all tasks)

- [ ] Clean non-incremental build `-p:BaseOutputPath=bin2/` → 0 warnings / 0 errors.
- [ ] Full suites green: `ReScene.App.Core.Tests`, `ReScene.Manager.Tests`, `ReScene.Lib/ReScene.Tests`.
- [ ] Delete `bin2/` (worktree + `E:/Projects/avalonia-agent-mcp`).
- [ ] Manual smoke (needs WinRAR + a real single-set SRR): reconstruct a single-set release; confirm verified volumes land in the chosen `OutputPath\output\` — the definitive proof of #3.
