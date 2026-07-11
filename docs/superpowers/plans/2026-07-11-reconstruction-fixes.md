# RAR Reconstruction Correctness Fixes — Implementation Plan (rev. 2, post-codex-review)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix all 25 verified codex findings in the RAR reconstruction subsystem, restoring the common single-set workflow and closing the multi-set correctness/safety gaps — with the shared infrastructure the fixes depend on built first, and no task leaving the build broken.

**Architecture:** Lib-first (per-set directories + backward-compatible dir-qualified CRC). Then app-side shared infrastructure (metadata normalization, a named verification snapshot, containment guards, a `TimeProvider` seam, an immutable switch snapshot). Then the atomic work-root/relocation redesign. Then the remaining per-set, robustness, path-guard and progress fixes. Every task is TDD.

**Tech Stack:** .NET 10 · CommunityToolkit.Mvvm 8.4 · xUnit · `ReScene.App.Core` (UI-agnostic) + `ReScene.Lib` submodule. Spec: `docs/superpowers/specs/2026-07-11-reconstruction-fixes-design.md`. This revision incorporates a full codex review of rev. 1 (all 15 concerns adopted).

## Global Constraints

- **Single-set output contract:** a single-set run produces byte-identical `.rar` output at `OutputPath\output\<name>` — same bytes AND same location (no `<dir>` subfolder even when the set has a non-empty `Key`/`Directory`). Assert final path + option equivalence in tests.
- **Delete safety — two guarded roots:** every destructive delete/move canonicalizes its target with real link resolution and asserts it is a strict descendant of exactly one reserved root — the **output tree** (`OutputPath\output`) or the **scratch tree** (`OutputPath\.rescene-work`). Untrusted `set.Directory`/`set.Key` (from SRR volume names) can never widen or redirect a delete. When safety cannot be established for an existing path, **fail closed** (abort with a validation error), never delete.
- **Honest reporting:** a set reports success only when its own **complete** verified volume set is placed at its final location. Never report success while output is stranded, incomplete, missing, or a sibling's output was destroyed.
- **Full-volume verification must never be silently disabled:** any change to expected-CRC keying must keep the expected map populated for the common case; an empty map (which makes `Manager` fall back to first-volume-only) is a failure to fix, not an acceptable outcome.
- **TDD, small commits:** one failing test per fix first; commit per task; each task leaves the build green.
- **Build gate (every task):** `dotnet build ReScene.Manager.slnx -c Debug -p:BaseOutputPath=bin2/` → 0 warnings / 0 errors; relevant `dotnet test` green; delete `bin2/` after.
- **Lib-first & backward-compatible:** `ReScene.Lib` changes (Tasks 1–2) land + pointer bumped before app tasks build against them; the dir-qualified CRC key keeps a legacy basename fallback so the app keeps working across the T2→T9 gap. No flat `SRRFile` field removed; update `PublicApi.ReScene.approved.txt` for new public members.
- **Deferred:** recovery-record (`-rr`) — no `-rr` switch exists; Task 8 leaves a documented `// TODO(-rr)`.
- **Commit trailer:** end every commit message with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

## File Structure

**Lib (`ReScene.Lib/ReScene/`):** `SRR/SRRArchiveSet.cs` (per-set dir membership + 3 time maps), `SRR/SRRFileParser.cs` (route dir records to current set), `Core/Manager.cs` (`BuildExpectedInOrder` qualified-first + basename fallback + `LastSegment`), `ReScene.Tests/PublicApi.ReScene.approved.txt`.
**App new files (`ReScene.App.Core/ViewModels/Reconstruction/`):** `RarMetadataNormalizer.cs`, `VerificationSnapshot.cs`, `ReconstructionPathGuard.cs`.
**App modified:** `SRRSwitchMapper.cs`, `SRRImportParser.cs`, `ArchiveSetPlanner.cs`, `SharedReconstructionSettings.cs`, `RARCommandLineBuilder.cs`, `ReconstructionProgressTracker.cs`, `ImportedSRRStateMapper.cs`, `Models/ImportedSRRState.cs`, `ReconstructorFieldGuidance.cs`, `ViewModels/ReconstructorViewModel.cs`; `ReScene.Manager/Views/Wizards/BeginnerWizardFactory.cs` (confirm surface).
**Tests:** `ReScene.App.Core.Tests/`, `ReScene.Manager.Tests/`, `ReScene.Lib/ReScene.Tests/`.

Line numbers are review anchors — re-confirm with a quick read before editing.

---

## Phase A — Lib (submodule first)

### Task 1: Lib — per-set directory membership + all three time maps (#7)

**Files:** Modify `ReScene.Lib/ReScene/SRR/SRRArchiveSet.cs`, `ReScene.Lib/ReScene/SRR/SRRFileParser.cs` (directory branch ~708-715), `ReScene.Lib/ReScene.Tests/PublicApi.ReScene.approved.txt`; Test `ReScene.Lib/ReScene.Tests/SRRArchiveSetTests.cs`.

**Interfaces — Produces:** `SRRArchiveSet.ArchivedDirectories` (`IReadOnlyList<string>`), `.ArchivedDirectoryTimestamps` / `.ArchivedDirectoryCreationTimes` / `.ArchivedDirectoryAccessTimes` (`IReadOnlyDictionary<string, DateTime>`) — the set's own in-archive directory records + all three time maps. A public/init construction seam so the config restore (Task 11) can rebuild a set (add an `internal` add-method or `init` collections; do NOT leave the backing collections write-inaccessible to `ReScene.App.Core`).

- [ ] **Step 1 — failing test:** synthetic two-set SRR (via `SRRTestDataBuilder`, RAR4) where set A archives dir `SubsA` (with a distinct modified time) and set B archives `SubsB`. Assert `sets[0].ArchivedDirectories == ["SubsA"]`, `sets[1].ArchivedDirectories == ["SubsB"]`, and each set's three time maps contain only its own dir with the right values.
- [ ] **Step 2 — run, expect FAIL** (members absent / flattened).
- [ ] **Step 3 — implement:** add the membership list + three time dictionaries to `SRRArchiveSet` (mirror the existing `ArchivedFiles`/`ArchivedFileTimestamps`/`…CreationTimes`/`…AccessTimes` members). In `SRRFileParser`'s `isDirectory` branch, after the existing flat `srr.ArchivedDirectories.Add`, also route the record + all three times to `srr.CurrentArchiveSet?`.
- [ ] **Step 4 — run, expect PASS;** regenerate/approve `PublicApi.ReScene.approved.txt` for the new members.
- [ ] **Step 5 — commit** (`fix(lib): per-set in-archive directory membership + times (#7)`).

### Task 2: Lib — dir-qualified CRC with basename fallback (#9, #10-lib) + bump pointer

**Files:** Modify `ReScene.Lib/ReScene/Core/Manager.cs` (`BuildExpectedInOrder` 157-169); Test `ReScene.Lib/ReScene.Tests/` (Manager/VolumeMatch).

**Interfaces — Produces:** `Manager.BuildExpectedInOrder` looks up each ordered volume by **directory-qualified relative path first** (normalized `\`→`/` via a `LastSegment`/normalize helper), then **falls back to bare basename** when no qualified key exists. It must **never** return empty when the previous basename logic would have matched (no silent loss of full-volume verification).

- [ ] **Step 1 — failing tests:** (a) collision — expected map has `CD1/release.rar`→CRC_A and `CD2/release.rar`→CRC_B; a set whose `OriginalRARFileNames` are `CD2\release.rar` → picks CRC_B. (b) common case — expected map keyed by bare `release.r00` (flat SFV) with volumes `DVD1\release.r00` → still matches via basename fallback (map NOT empty). (c) mixed separators resolve identically.
- [ ] **Step 2 — run, expect FAIL** on (a); (b)/(c) guard against regressing them.
- [ ] **Step 3 — implement:** add a private `static string LastSegment(string p) => p.Replace('\\','/').TrimEnd('/').Split('/')[^1];` and normalize helper; build the lookup to try the qualified relative key, then `LastSegment`. Store both keys when building `ExpectedVolumeCrcs` consumers agree (coordinated with Task 9). Keep the positional iteration over `OriginalRARFileNames`.
- [ ] **Step 4 — run, expect PASS;** full lib suite green.
- [ ] **Step 5 — commit** (`fix(lib): directory-qualified CRC matching with basename fallback (#9,#10)`), then bump the superproject pointer: `git add ReScene.Lib && git commit -m "chore: bump ReScene.Lib (per-set dirs #7, dir-qualified CRC #9)"` (+ trailer). Delete `bin2/`.

---

## Phase B — App shared infrastructure

### Task 3: App — shared RAR-metadata normalization (#11, #12)

**Files:** Create `ReScene.App.Core/ViewModels/Reconstruction/RarMetadataNormalizer.cs`; Modify `SRRSwitchMapper.cs` (`MapCompression` 58-72, `DictionarySwitch` enum 25-35, `MapDictionary` 74-98) and `SRRImportParser.cs` (`DescribeCompression` ~97-107); Test `ReScene.App.Core.Tests/Reconstruction/RarMetadataNormalizerTests.cs`, `SRRSwitchMapperTests.cs`.

**Interfaces — Produces:** `static int RarMetadataNormalizer.NormalizeCompressionMethod(int raw)` (maps RAR5 ASCII `0x30..0x35`→`0..5`, leaves `0..5` unchanged, returns `-1` for anything else); `static DictionarySwitch RarMetadataNormalizer.DictionarySwitchFor(int sizeKb)` covering `64…1048576` KB → `MD64K…MD1G` (used by both the mapper and, later, Task 8's planner).

- [ ] **Step 1 — failing tests:** `NormalizeCompressionMethod(0x35) == 5`, `(3) == 3`, `(0x99) == -1`; `DictionarySwitchFor(1048576) == MD1G`, `(4096) == MD4096K`; end-to-end `Map(srr)` for `CompressionMethod==0x35` → `CompressionMap(5,"Best")` and `DictionarySize==1048576` → `DictionaryMap(MD1G,1048576)`; `DescribeCompression(0x35)` returns the RAR5 "Best" text.
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** add `RarMetadataNormalizer`. Extend `DictionarySwitch` enum with `MD8M,MD16M,MD32M,MD64M,MD128M,MD256M,MD512M,MD1G` (VM toggles already exist, lines 670-677). Rewrite `MapCompression` to `int m = RarMetadataNormalizer.NormalizeCompressionMethod(srr.CompressionMethod.Value); if (m < 0) return null; return new(m, _compressionNames[m]);` and `MapDictionary` to use `DictionarySwitchFor`. Route `DescribeCompression` through the normalizer too. In `ReconstructorViewModel.ApplySwitchDiff` (~2098+) add the eight new `DictionarySwitch` set-cases (the clear-all already covers the toggles).
- [ ] **Step 4 — run, expect PASS.**
- [ ] **Step 5 — commit** (`fix: normalize RAR5 compression + large dictionaries via shared helper (#11,#12)`).

### Task 4: App — named verification snapshot parsed before cleanup (#14, enables #8)

**Files:** Create `ReScene.App.Core/ViewModels/Reconstruction/VerificationSnapshot.cs`; Modify `SharedReconstructionSettings.cs` (add the snapshot), `ReconstructorViewModel.cs` (`LoadVerificationHashes` 1722-1740, the run-start ordering ~1449, the overlap guard); Test `ReScene.App.Core.Tests/`.

**Interfaces — Produces:** `sealed record VerificationSnapshot(HashType HashType, IReadOnlyList<(string Name, string Hash)> Entries)` with `IReadOnlyCollection<string> AllHashes` and `IReadOnlyList<string> HashesForVolumes(IEnumerable<string> volumeNames)` (qualified-first, basename fallback). Carried on `SharedReconstructionSettings.Verification` (replacing the values-only `VerificationHashes` for filtering; keep `AllHashes` for the SHA1/no-CRC gate).

- [ ] **Step 1 — failing test:** `VerificationPath` is a `.sfv` **inside** `OutputPath`, output non-empty (cleanup runs) → the run still gates on the parsed named entries (snapshot captured before cleanup, so the delete cannot empty it); and `HashesForVolumes(["DVD1\\x.r00"])` matches a flat `x.r00` SFV entry.
- [ ] **Step 2 — run, expect FAIL** (file deleted → empty hashes; and no named data exists).
- [ ] **Step 3 — implement:** parse the verification file **once into a `VerificationSnapshot`** at Start, **before** the output-cleanup block (~1417-1447); carry it on `SharedReconstructionSettings`; `LoadVerificationHashes` returns `snapshot.AllHashes`. Add the Start-time guard rejecting `VerificationPath` equal-to/under `OutputPath` (via Task 6's guard once it lands; here, a lexical check is acceptable and hardened in Task 15).
- [ ] **Step 4 — run, expect PASS.**
- [ ] **Step 5 — commit** (`fix: snapshot verification file into a named map before cleanup (#14)`).

### Task 5: App — reconstruction path guards (output tree + scratch tree) (#1, #2, #26 foundation)

**Files:** Create `ReScene.App.Core/ViewModels/Reconstruction/ReconstructionPathGuard.cs`; Test `ReScene.App.Core.Tests/Reconstruction/ReconstructionPathGuardTests.cs`.

**Interfaces — Produces:**
- `static string ResolveReal(string path)` — canonical real path: resolve the **deepest existing ancestor** via `Directory.ResolveLinkTarget(..., returnFinalTarget:true)` / final-path, then re-append the non-existent suffix; lexical `Path.GetFullPath` only when nothing on the chain exists.
- `static bool IsStrictDescendant(string root, string candidate)` — real-resolves both; compares with a **filesystem-appropriate** comparer (case-insensitive on Windows/macOS default volumes; case-sensitive where the FS is; when it cannot be determined for an existing path, **throw** so callers fail closed).
- `static string ResolveOutputChild(string outputPath, string relative)` and `static string ResolveScratchChild(string outputPath, string setKey)` — return the canonical child and **throw `InvalidOperationException`** if not a strict descendant of `OutputPath\output` / `OutputPath\.rescene-work` respectively. `ResolveScratchChild` sanitizes `setKey` (strip `..`/rooted/`/\`) and appends a short stable hash of the raw key for collision resistance.

- [ ] **Step 1 — failing tests:** `ResolveOutputChild(out,"DVD1")` under `out\output`; `ResolveOutputChild(out,"../../x")` throws; `ResolveScratchChild(out,"release")` under `out\.rescene-work` and `!= out\.rescene-work` itself; two different raw keys sanitizing to the same base still yield distinct scratch dirs (hash suffix); a linked-ancestor case where the real target escapes → `IsStrictDescendant` false; case-sensitivity honored per platform.
- [ ] **Step 2 — run, expect FAIL** (class absent).
- [ ] **Step 3 — implement** `ReconstructionPathGuard` per the interfaces above.
- [ ] **Step 4 — run, expect PASS.**
- [ ] **Step 5 — commit** (`feat: reconstruction path guards for output + scratch trees (#1 foundation)`).

---

## Phase C — Work-root / relocation core (atomic)

### Task 6: App — uniform work-root + robust relocation + clear-once + cancel cleanup (#3, #1, #4, #5, #17)

Single atomic task (per codex: switching the work root and fixing relocation must land together, or single-set output strands between commits).

**Files:** Modify `ArchiveSetPlanner.cs` (`WorkRootFor` 182-185, `Sanitize` ~265), `ReconstructorViewModel.cs` (`RelocateVerifiedOutput` 1855-1898, `CleanupWorkRoot` 1904-1928, run loop 1601-1637, pre-run cleanup 1417-1447), `ReScene.Manager/Views/Wizards/BeginnerWizardFactory.cs` (confirm surface ~204-219); Consumes `ReconstructionPathGuard` (Task 5), `RARVolumeIdentifier.IsRARVolume` (lib). Test `ReScene.App.Core.Tests/`, `ReScene.Manager.Tests/` (wizard confirm text).

**Interfaces — Produces:** `WorkRootFor` always returns `ReconstructionPathGuard.ResolveScratchChild(OutputPath, set.Key or "release")`. Relocation target = **output root** for `setCount == 1`, `output\<set.Directory>` for multi-set.

- [ ] **Step 1 — failing tests (headless seam, no rar.exe):**
  (a) **single set, non-empty key + `Directory="DVD1"`** → final files at `OutputPath\output\<name>` (NOT `output\DVD1\`); `.rescene-work` removed. (single-set location contract)
  (b) two sets sharing `Directory="DVD1"` → both survive (no sibling recursive delete).
  (c) `set.Directory="../../x"` → relocation aborts that set (records failure), **no** delete outside `output`.
  (d) custom-packer layout (volumes at `<workRoot>` root, and nested `<workRoot>\DVD1\`) → complete volume set detected via `RARVolumeIdentifier.IsRARVolume` (incl `.sNN`/`.001`) and relocated; success.
  (e) incomplete/missing output (fewer volumes than expected) → set reported **failed**.
  (f) a pre-existing file at a destination → move-without-overwrite + preflight; a mid-move failure rolls back only this set's moved files, leaving siblings intact.
  (g) cancel thrown mid-set → `<workRoot>` removed (scratch guard) before propagation; a committed set is not cleaned.
  (h) confirm text (VM + `BeginnerWizardFactory`) names the **output subtree**, and unrelated root files under `OutputPath` survive cleanup.
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:**
  - `WorkRootFor` → `ReconstructionPathGuard.ResolveScratchChild(shared.OutputPath, string.IsNullOrEmpty(set.Key) ? "release" : set.Key)`; harden `Sanitize` (dead once the guard sanitizes, but keep for its other callers).
  - Rewrite `RelocateVerifiedOutput(workRoot, set, setCount)`: enumerate produced volumes across both layouts (`<workRoot>\output` and `<workRoot>` root, including nested subdirs) filtering with `RARVolumeIdentifier.IsRARVolume`; require the **complete expected volume set** (else return `false`). Compute `targetDir = setCount == 1 ? ReconstructionPathGuard.ResolveOutputChild(OutputPath, "") : ReconstructionPathGuard.ResolveOutputChild(OutputPath, set.Directory)` (throws on traversal → catch → `false`). `Directory.CreateDirectory(targetDir)`; preflight that no destination file exists (else fail); `File.Move(src, dst, overwrite:false)` tracking moved dests; on any exception, move them back (rollback) and return `false`. Never recursively delete a shared `targetDir`.
  - Remove the `setCount <= 1` early-returns in relocation/cleanup.
  - `CleanupWorkRoot`: delete only `workRoot`, guarded by `ReconstructionPathGuard.ResolveScratchChild` (strict scratch descendant); never a shared `output\<dir>`.
  - Pre-run cleanup (1417-1447): clear only the **output subtree** + the reserved **scratch tree** (each via its guard), preserve unrelated `OutputPath` root files; update the confirm message text (VM + wizard) to say the *output and working folders'* contents will be cleared; reject Start when a still-needed input (imported SRR / verification file / WinRAR dir) resolves under either reserved root.
  - Run loop: wrap the per-set body so cancellation/failure calls `CleanupWorkRoot` in a `finally` for any uncommitted set; a committed (relocated) set is left intact.
- [ ] **Step 4 — run, expect PASS.**
- [ ] **Step 5 — commit** (`fix: uniform scratch work-root + guarded transactional relocation (#3,#1,#4,#5,#17)`).

---

## Phase D — Per-set correctness

### Task 7: App — per-set command/version matrices (#6)

**Files:** Modify `SharedReconstructionSettings.cs` (add an immutable switch snapshot), `ArchiveSetPlanner.cs` (`BuildOptionsForSet` 99-168), `ReconstructorViewModel.cs` (`BuildSharedSettings`); Consumes `RarMetadataNormalizer` (Task 3). Test `ArchiveSetPlannerTests`.

**Interfaces — Consumes:** `SharedReconstructionSettings.SwitchSnapshot` (new — an immutable copy of the user's `RARSwitchSettings`) so a per-set matrix can be built by constraining a copy. **Produces:** per-set `CommandLineArguments`/`RARVersions` derived from each set's normalized metadata.

- [ ] **Step 1 — failing test:** two sets, A `{CompressionMethod:0, IsSolid:false, RARVersion:29}`, B `{CompressionMethod:0x35→5, IsSolid:true, RARVersion:50}` within the user's selected switches → set A's args carry `-m0`/`-s-`, set B's `-m5`/`-s`; each set's `RARVersions` intersect the user's selected version folders with the set's format (RAR4 vs RAR5), **not** a raw range built from the unpack-version int.
- [ ] **Step 2 — run, expect FAIL** (both sets identical global matrix).
- [ ] **Step 3 — implement:** carry `RARSwitchSettings SwitchSnapshot` on `SharedReconstructionSettings`. In `BuildOptionsForSet`, when `set` carries metadata, clone the snapshot, constrain compression (via `RarMetadataNormalizer.NormalizeCompressionMethod`), dictionary (`DictionarySwitchFor`), solid, and archive **format** (RAR4/RAR5 from `set.RARVersion` treated as an unpack-version → `-ma4`/`-ma5`), then build the per-set matrix off-thread (reuse Task 11's cancellable/bounded builder). Derive the version selection by **intersecting the user-selected version folders/ranges with the set's format**, never by constructing a range from the raw unpack-version. Fall back to `shared.CommandLineArguments`/`RARVersions` for the metadata-less flat set. Add `// TODO(-rr): thread set.HasRecoveryRecord once a -rr switch exists (deferred).`
- [ ] **Step 4 — run, expect PASS.**
- [ ] **Step 5 — commit** (`fix: per-set command/version matrices from normalized set metadata (#6)`).

### Task 8: App — per-set hash gate, basename, dir-qualified CRC, per-set dirs (#8, #10-app, #9-app, #7-app)

**Files:** Modify `ArchiveSetPlanner.cs` (`BuildOptionsForSet` hash loop 153-156 + dirs 122-125; `BuildExpectedVolumeCrcs` 67-95); Consumes `VerificationSnapshot` (Task 4), `SRRArchiveSet.ArchivedDirectories` + 3 time maps (Task 1), the qualified/basename key convention (Task 2). Test `ArchiveSetPlannerTests`.

- [ ] **Step 1 — failing tests:** (#8) set B's `options.Hashes` excludes set A's first-volume CRC when a combined verification snapshot covers both (uses `VerificationSnapshot.HashesForVolumes(set.VolumeNames)`, qualified-first + basename fallback). (#10) `DVD1\release.rar` matches a flat `release.rar` SFV entry on any separator. (#9) two sets, identical basenames in `CD1\`/`CD2\`, get distinct CRCs; **and** the common flat-SFV case still populates the map (not empty). (#7) `options.RAROptions.ArchiveDirectoryPaths` + all three directory-time maps equal the set's own (from Task 1), not the release union — except the synthetic flat set, which keeps the union.
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** replace the `VerificationHashes` pooling with `shared.Verification.HashesForVolumes(set.VolumeNames)`; replace `Path.GetFileName` at 70/77 with a separator-neutral `LastSegment`; key `BuildExpectedVolumeCrcs` qualified-first with basename fallback to agree with Task 2 (never empty in the common case); source `ArchiveDirectoryPaths` + the three time maps from `set` (fallback to `shared` only for the flat no-SRR set, detected by `set.Key == ""`).
- [ ] **Step 4 — run, expect PASS.**
- [ ] **Step 5 — commit** (`fix: per-set hash gate, cross-platform basename, dir-qualified CRC, per-set dirs (#8,#10,#9,#7)`).

---

## Phase E — Import / config

### Task 9: App — retire stale auto-SFV on every import (#15)

**Files:** Modify `ReconstructorViewModel.cs` (`TryExtractStoredSFV` ~2576-2612); Test `ReScene.App.Core.Tests/`.

- [ ] **Step 1 — failing test:** import A (embedded SFV → `VerificationPath` under `_sfvTempDir`), then import B with `StoredFiles.Count == 0` → `VerificationPath` cleared, `_sfvTempDir` null.
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** move the retire block (clear `VerificationPath` under `_sfvTempDir`, `Cleanup(_sfvTempDir)`, null it) to run **before** the `StoredFiles.Count == 0` early return (or unconditionally at method entry).
- [ ] **Step 4 — run, expect PASS; commit** (`fix: retire previous auto-extracted SFV on no-stored-files import (#15)`).

### Task 10: App — complete per-set config round-trip (#22)

**Files:** Modify `Models/ImportedSRRState.cs` (add a complete per-set DTO list), `ImportedSRRStateMapper.cs` (`Capture` 18-60 incl. `hasState`, `Apply` 66-105); Consumes Task 1's construction seam. Test `ReScene.App.Core.Tests/`.

- [ ] **Step 1 — failing test:** capture→apply a two-set imported state → `ArchiveSets.Count == 2` with each set's full data (volumes, CRCs, all timestamps, compression/dict/version/solid, host/attrs, large flags, dirs); and `Capture`'s `hasState` returns true when only `ArchiveSets` is populated. Add a legacy DTO (no set list) but present `SRRFilePath` → falls back to `ResolveSets` re-parse (unchanged).
- [ ] **Step 2 — run, expect FAIL** (sets dropped; merged single set synthesized).
- [ ] **Step 3 — implement:** add a `List<ArchiveSetDto>` (complete fields) to `ImportedSRRState`; map it in `Capture` (and add `state.ArchiveSets.Count > 0` to `hasState`) and `Apply` (rebuild `SRRArchiveSet`s via Task 1's seam). Guard: only prefer a restored non-empty set list when it is complete; otherwise leave re-parse to `ResolveSets`.
- [ ] **Step 4 — run, expect PASS; commit** (`fix: persist and restore complete per-set archive sets in config (#22)`).

---

## Phase F — Robustness

### Task 11: App — bounded, cancellable -mt matrix preserving -mt0 (#13)

**Files:** Modify `RARCommandLineBuilder.cs` (267-268, 284, and the build entry), `ReconstructorViewModel.cs` (`SwitchMTStart/End` setters 714-715, matrix build in `BuildSharedSettings`); Consumed also by Task 7's per-set builds. Test `RARCommandLineBuilderTests`.

- [ ] **Step 1 — failing tests:** `SwitchMTEnd = int.MaxValue` → returns without OOM/overflow, values bounded to the version-valid max; a matrix whose cardinality would exceed a defined cap → the builder throws/returns a signalled "too large" result rejected before allocation; `-mt0` is **preserved** as a valid value (0 not clamped away); cancellation passed into the expansion loop stops it promptly.
- [ ] **Step 2 — run, expect FAIL** (inclusive loop wraps; no cap; 0 excluded by `Math.Max(1,…)`).
- [ ] **Step 3 — implement:** allow the low end to include 0 (`-mt0` is byte-significant per the RAR4 manual), clamp the high end to a version-aware valid max (RAR4 ≤ 16, RAR5+ per its manual) — not a single universal 64; compute the total matrix cardinality with `checked` arithmetic and reject (validation error) before allocating when it exceeds a defined cap; pass the run `CancellationToken` into the expansion loop (check periodically) and build off the UI thread via `Task.Run`.
- [ ] **Step 4 — run, expect PASS; commit** (`fix: bound and cancel the -mt/option matrix, preserve -mt0 (#13)`).

### Task 12: App — checked volume-size conversion (#21)

**Files:** Modify `RARCommandLineBuilder.cs` (`BuildVolumeArgument` 370-388); Test `RARCommandLineBuilderTests`.

- [ ] **Step 1 — failing tests:** blank + GB unit → `-v15000k` (not `-v15000000000`); `VolumeSize = long.MaxValue` + GB → a defined validation fallback, **no overflow** (today `sizeValue * 1000 * 1000` overflows).
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** `if (!long.TryParse(s.VolumeSize, out long v) || v <= 0) return $"-v{DefaultVolumeSizeKb}k";` before the switch; convert each unit with `checked(...)`, catching `OverflowException` → the same fixed KB fallback.
- [ ] **Step 4 — run, expect PASS; commit** (`fix: checked volume-size conversion with safe fallback (#21)`).

### Task 13: App — guard preflight directory enumeration (#18)

**Files:** Modify `ReconstructorViewModel.cs` (~1302, ~1417); Test `ReScene.App.Core.Tests/`.

- [ ] **Step 1 — failing test:** preflight enumeration throwing `UnauthorizedAccessException` → Start surfaces a validation error, `IsRunning` false, no escape.
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** wrap the two `Directory.Enumerate*` preflight calls in `try { … } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _fileDialog.ShowError("Validation Error", …); return; }` (mirror 1405/1427).
- [ ] **Step 4 — run, expect PASS; commit** (`fix: guard preflight directory enumeration (#18)`).

---

## Phase G — Path guard

### Task 14: App — link-resolved, filesystem-correct overlap guard (#2, #26)

**Files:** Modify `ReconstructorFieldGuidance.cs` (`PathsOverlap` 138-159); Consumes `ReconstructionPathGuard` (Task 5). Test `ReconstructorFieldGuidanceTests`.

- [ ] **Step 1 — failing tests:** (#2) two distinct lexical paths whose deepest existing ancestor resolves to the same real dir (linked ancestor with a nonexistent child) → overlap detected; resolution failure on an existing path → fail closed (treated as attention-needed), not silently non-overlapping. (#26) case-sensitivity honored from the actual filesystem (equal on case-insensitive volumes, distinct on case-sensitive).
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** replace the lexical `Path.GetFullPath` + `OrdinalIgnoreCase` with `ReconstructionPathGuard.ResolveReal` + `IsStrictDescendant`/equality using the filesystem-appropriate comparer; when `ReconstructionPathGuard` throws (safety indeterminate for an existing path) treat the pair as overlapping/attention-needed (fail closed).
- [ ] **Step 4 — run, expect PASS; commit** (`fix: link-resolved, filesystem-correct path-overlap guard (#2,#26)`).

---

## Phase H — Progress / logging

### Task 15: App — per-set outcome rows + TimeProvider ETA (#23, #25)

**Files:** Modify `ReconstructionProgressTracker.cs` (constructor 19-27 add `TimeProvider`, phase-clear 148-156, `Tick` 187-199), `ReconstructorViewModel.cs` (`ReportSetSummary` 1935-1972, tracker construction); Test `ReconstructionProgressTrackerTests`.

- [ ] **Step 1 — failing tests:** (#23) set 1 succeeds with combo C, set 2 seeds C then fails → set 2's active row is **not** "Match" (each row finalized from its own `SetOutcome`, not the global `anySuccess` at 1970-1972); prior-set rows survive the set boundary. (#25) with an injected `FakeTimeProvider`, a progress event reporting 100 s remaining then advancing the clock 5 s with no new event → remaining ≈ 95 s (today it stays 100 s and pushes ETA forward via `DateTime.Now`).
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** inject `TimeProvider` into the tracker (default `TimeProvider.System`; tests pass `FakeTimeProvider`); replace `DateTime.Now` in `Tick` with `_timeProvider.GetLocalNow()`, and cache the fixed estimated-completion instant, subtracting elapsed each tick. Stamp rows with the active set and preserve prior-set rows across phase changes (distinguish set boundary from phase boundary). In `ReportSetSummary`, finalize **each** set's row from its own outcome; remove the global `anySuccess` overwrite.
- [ ] **Step 4 — run, expect PASS; commit** (`fix: per-set outcome rows + TimeProvider-based ETA (#23,#25)`).

### Task 16: App — timestamp summary in finally, thread-safe batched log, set/attempt progress (#19, #20, #24)

**Files:** Modify `ReconstructorViewModel.cs` (timestamp display ~2293, log append 2338-2354, progress mapping ~2244, run `finally`); Test `ReScene.App.Core.Tests/`.

- [ ] **Step 1 — failing tests:** (#19) two sets each with a timestamp failure → warning shown exactly once, from the **run's `finally`** (also fires on cancel/exception paths). (#20) N log events → at most K UI dispatches via a thread-safe queue (counting `IUiDispatcher`), content preserved and the **final batch is flushed** (synchronous drain at end); ordering per target preserved. (#24) progress labeled by **set and attempt/stage** (seed vs full search) so it does not appear to rewind within a set.
- [ ] **Step 2 — run, expect FAIL.**
- [ ] **Step 3 — implement:** accumulate `_timestampFailures` and show/clear one summary from the outer run `finally`; replace the per-event whole-string rebuild with a thread-safe bounded queue + an atomic "flush scheduled" flag + a single coalesced dispatcher post + a synchronous final drain, preserving per-target order; label progress `Set X/N · <stage>` (or aggregate attempts) rather than raw per-invocation percent.
- [ ] **Step 4 — run, expect PASS; commit** (`fix: single timestamp summary, thread-safe batched log, set/attempt progress (#19,#20,#24)`).

---

## Coverage check (all 25 findings → task)

#1 T5+T6 · #2 T5+T14 · #3 T6 · #4 T6 · #5 T6 · #6 T7 · #7 T1+T8 · #8 T4+T8 · #9 T2+T8 · #10 T2+T8 · #11 T3 · #12 T3 · #13 T11 · #14 T4+T6 · #15 T9 · #17 T6 · #18 T13 · #19 T16 · #20 T16 · #21 T12 · #22 T10 · #23 T15 · #24 T16 · #25 T15 · #26 T5+T14. (#16 = verified false positive, no task.)

## Sequencing rationale (no broken intermediate state)

Lib-first with backward-compatible keys (T1–T2) → app infra that later tasks depend on: normalization (T3), named verification snapshot (T4), path guards (T5) → the atomic work-root/relocation change (T6, which needs T5 and does not strand single-set output at any commit) → per-set matrices (T7, needs T3 + the off-thread cancellable builder from T11 — implement T11's builder cancellation as part of T7 if reached first, else T7 consumes it) → per-set CRC/hash/dirs (T8, needs T1/T2/T4) → import/config (T9–T10, T10 needs T1's seam) → robustness (T11–T13) → path guard consumer (T14, needs T5) → progress (T15–T16). Note: if executing strictly in order, move T11 (matrix cancellation/bounding) before T7 so T7's off-thread per-set builds reuse it; the coverage is unchanged.

## Final verification (after all tasks)

- [ ] Clean non-incremental build `-p:BaseOutputPath=bin2/` → 0 warnings / 0 errors.
- [ ] Full suites green: `ReScene.App.Core.Tests`, `ReScene.Manager.Tests`, `ReScene.Lib/ReScene.Tests`; `PublicApi.ReScene.approved.txt` current.
- [ ] Delete `bin2/` (worktree + `E:/Projects/avalonia-agent-mcp`).
- [ ] Manual smoke (needs WinRAR + a real single-set SRR): reconstruct a single-set release; verified volumes land in `OutputPath\output\` — the definitive proof of #3.
