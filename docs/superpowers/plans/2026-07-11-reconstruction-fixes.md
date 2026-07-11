# RAR Reconstruction Correctness Fixes — Implementation Plan (rev. 3, post-2×-codex-review)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Fix all 25 verified codex findings in the RAR reconstruction subsystem — restoring the common single-set workflow and closing the multi-set correctness/safety gaps — with the shared infrastructure and engine contracts the fixes depend on built first, and no task leaving the build broken.

**Architecture:** Lib-first: per-set directories, a backward-compatible dir-qualified CRC, and an engine result that returns the committed output file paths. Then app-side infrastructure (metadata normalization, a named verification snapshot that becomes the sole verification source, two reserved-root path guards, a format/version compatibility map, a `TimeProvider` seam, an immutable switch snapshot). Then the atomic work-root/relocation redesign that relocates exactly the engine-reported committed files. Then per-set matrices, robustness, path-guard, and progress fixes. TDD throughout.

**Tech Stack:** .NET 10 · CommunityToolkit.Mvvm 8.4 · xUnit · `Microsoft.Extensions.TimeProvider.Testing` (FakeTimeProvider) · `ReScene.App.Core` + `ReScene.Lib` submodule. Spec: `docs/superpowers/specs/2026-07-11-reconstruction-fixes-design.md` (updated to rev. 3). This revision adopts all concerns from two codex reviews of rev. 1/2, grounded in a read of the engine (`Manager`/`SRRReconstructor`) and version-selection code.

## Ground-truth notes (verified against source; do not re-assume)

- Engine output lives at `<OutputDirectoryPath>\output`; input at `…\input`; trial candidates are written **into `output`** under generated names `…{versionDir}-{joinedArgs}[-patched].rar`; `comment.txt` at the root; logs at `…\logs` (`Manager.cs:583-630`, `InputDirectoryPreparer.cs:99-107`).
- After success, `output` holds the committed winner **plus** retained non-match leftovers when `RAROptions.DeleteRARFiles==false` (`Manager.cs:742-753`); with `RenameToOriginalNames==false` the winner keeps a generated name **indistinguishable by pattern** from leftovers. `CompleteAllVolumes==false` produces only the **first** volume (`Manager.cs:433-451,690`).
- `BruteForceRunResult(bool Success, WinningCombo? Combo)`; `WinningCombo(int Version, IReadOnlyList<RARCommandLineArgument> Args)` — **no produced file paths** are returned today (`BruteForceRunResult.cs`, `WinningCombo.cs`, `Manager.cs:388,821`). Custom-packer returns `Combo==null` (`Manager.cs:231`).
- Custom-packer (`SRRReconstructor`) writes **directly to `OutputDirectoryPath` root**, possibly nested (`DVD1\x.rar`), no brute-force/rename (`Manager.cs:211-226`, `SRRReconstructor.cs:40,140-141`).
- Rename maps produced→original **positionally** via `RAROptions.OriginalRARFileNames`, using `Path.GetFileName(originalNames[i])` (`Manager.cs:963-965,984-986`) — the Unix-backslash bug (#10) is here too.
- `VersionRange(int Start incl, int End excl)` over **executable** version×100 (200-800). Format per (exe, args): `<500`→RAR4, `500-699`→RAR5 default / RAR4 with `-ma4` / RAR5 with `-ma5`, `>=700`→RAR7; `-ma4/-ma5` carry `Min=500,Max=699` and are filtered out elsewhere (`RARVersionSelector.ParseRARArchiveVersion`/`FilterArgumentsForVersion`, `RARVersionThresholds` 500/700). `SRRArchiveSet.RARVersion` is an **unpack** version, never read per-set today (`ArchiveSetPlanner.cs:99-169`).
- `SharedReconstructionSettings.VerificationHashes` is values-only; the run still re-reads the SFV after cleanup via `ResolveSfvVolumeNames()`/`TryLoadUserSfv(VerificationPath)` (`ReconstructorViewModel.cs:1553-1560`). `SFVFileEntry.FileName`/`SHA1FileEntry.FileName` exist. `RARSwitchSettings` is a copyable `sealed record`. `RARVolumeIdentifier.IsRARVolume` recognizes `.rar/.rNN/.sNN/.NNN`. `ReScene.Lib` exposes internals only to `ReScene.Tests`, so App.Core-facing seams must be **public**.

## Global Constraints

- **Single-set output contract:** a single-set run produces byte-identical `.rar` output at `OutputPath\output\<name>` — same bytes AND same location (no `<dir>` subfolder even for a non-empty `Key`/`Directory`).
- **Committed-file identity:** relocation moves **exactly the files the engine reports as committed** (new `BruteForceRunResult.CommittedFiles`), never files it discovers by scanning the work-root. Never relocate `input\` sources or `DeleteRARFiles==false` leftovers.
- **Two reserved guarded roots:** every destructive delete/move targets a strict descendant of exactly one **validated** reserved root — the output tree (`OutputPath\output`) or the scratch tree (`OutputPath\.rescene-work`) — each verified to itself resolve (real links, every component) under the real `OutputPath`. Destructive cleanup of a reserved root enumerates and guards its children; it never deletes via an unresolved path. When safety cannot be established for an existing path, **fail closed** (validation error), never delete. Untrusted `set.Directory`/`set.Key` can never widen or redirect a delete.
- **Full-volume verification never silently disabled:** expected-CRC keying stores exactly **one canonical key per volume** (never both qualified+basename aliases — that double-counts coverage); `Manager` looks up canonical-then-legacy-basename. An empty `ExpectedVolumeCrcs` in a case the old basename logic would have covered is a defect.
- **Honest reporting:** a set reports success only when its own complete committed volume set is placed at its final location. Multi-set custom-packer (`Combo==null`, root/nested layout) is reported **unsupported/failed**, never a false success (the original design's noted non-goal).
- **TDD, small commits, green each task; build gate:** `dotnet build ReScene.Manager.slnx -c Debug -p:BaseOutputPath=bin2/` → 0 warnings/0 errors; relevant `dotnet test` green; delete `bin2/` after.
- **Lib-first & backward-compatible:** lib tasks land + pointer bumped before app builds against them; dir-qualified CRC keeps a legacy basename fallback so the app works across the gap; update `PublicApi.ReScene.approved.txt`.
- **Deferred:** recovery-record (`-rr`) — no switch exists; a `// TODO(-rr)` marks where it would attach.
- **Commit trailer:** `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.

---

## Phase A — Lib (submodule first)

### Task 1: Lib — per-set directory membership + all three time maps + public construction seam (#7)

**Files:** `ReScene.Lib/ReScene/SRR/SRRArchiveSet.cs`, `SRRFileParser.cs` (dir branch ~708-715), `ReScene.Lib/ReScene.Tests/RAR4HeaderBuilder.cs` (emits only mtime, ~92 — extend for ctime/atime), `PublicApi.ReScene.approved.txt`; Test `SRRArchiveSetTests.cs`.

**Interfaces — Produces:** `SRRArchiveSet.ArchivedDirectories` + `.ArchivedDirectoryTimestamps`/`.ArchivedDirectoryCreationTimes`/`.ArchivedDirectoryAccessTimes`; a **public** factory/`init` seam (`public static SRRArchiveSet FromRestored(...)` or public `init` collections) usable from `ReScene.App.Core` for config restore (Task 11) — NOT internal.

- [ ] **Step 1 — extend the test builder:** add creation/access extended-time emission to `RAR4HeaderBuilder` (today only mtime at ~92) so a test can set distinct m/c/a times on a directory record.
- [ ] **Step 2 — failing test:** a synthetic two-set SRR where **both** sets contain a directory of the **same name** `Subs` but with **different** m/c/a times → assert `sets[0].ArchivedDirectories==["Subs"]` with set 0's three times, `sets[1]` with set 1's — proving no flat last-wins contamination (same name is the discriminating case; `SubsA`/`SubsB` would not expose it).
- [ ] **Step 3 — run, expect FAIL.**
- [ ] **Step 4 — implement:** add the membership list + three time maps to `SRRArchiveSet` with a public construction seam; route dir records + all three times to `srr.CurrentArchiveSet?` in `SRRFileParser`'s `isDirectory` branch; approve `PublicApi`.
- [ ] **Step 5 — run PASS; commit** (`fix(lib): per-set directory membership + m/c/a times + public restore seam (#7)`).

### Task 2: Lib — canonical dir-qualified CRC + Unix-safe rename (#9, #10-lib)

**Files:** `ReScene.Lib/ReScene/Core/Manager.cs` (`BuildExpectedInOrder` 157-169, `RenameMatchedOutput` 963-986); Test lib Manager tests.

**Interfaces — Produces:** `BuildExpectedInOrder` looks up each ordered volume by its **canonical dir-qualified key first, then legacy basename fallback**, never returning empty where basename would have matched. A private `LastSegment(string)` (splits on `/` and `\`) replaces `Path.GetFileName` on SRR-internal names in both `BuildExpectedInOrder` and `RenameMatchedOutput`.

- [ ] **Step 1 — failing tests:** (a) collision `CD1/x.rar`→A, `CD2/x.rar`→B; set with `CD2\x.rar` → B. (b) common flat-SFV case (`x.r00` keys, `DVD1\x.r00` volumes) → still matched via basename fallback (map not empty). (c) `RenameMatchedOutput` with `originalNames=["DVD1\\x.rar"]` on a simulated non-Windows separator → output name `x.rar`, not a literal-backslash name.
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement:** add `LastSegment`; qualified-first + basename fallback lookup keyed on ONE canonical key; replace `Path.GetFileName(originalNames[i/0])` with `LastSegment`.
- [ ] **Step 4 — run PASS; full lib suite green; commit** (`fix(lib): canonical dir-qualified CRC + Unix-safe volume rename (#9,#10)`).

### Task 3: Lib — engine returns committed output file paths (D1 foundation for #3/#4/#5)

**Files:** `ReScene.Lib/ReScene/Core/BruteForceRunResult.cs`, `Manager.cs` (`RenameMatchedOutput` 937-994 returns the committed paths; `BruteForceRARVersionAsync` 388/821 threads them into the result), `SRRReconstructor.cs` (returns written paths), `PublicApi.ReScene.approved.txt`; Test lib Manager tests.

**Interfaces — Produces:** `BruteForceRunResult(bool Success, WinningCombo? Combo, IReadOnlyList<string> CommittedFiles)` — absolute paths of the volumes actually committed for the winning combo (full set when `CompleteAllVolumes`, else the single first volume; release names or generated names per `RenameToOriginalNames`); empty on failure. The custom-packer path returns the paths it wrote (root/nested).

- [ ] **Step 1 — failing tests (pure/seam where possible):** `RenameMatchedOutput` collects and returns the exact destination paths it moved (CAV multi-volume and non-CAV single-volume); a failed run → empty `CommittedFiles`; the custom-packer branch returns its written volume paths.
- [ ] **Step 2 — run FAIL** (result has no `CommittedFiles`).
- [ ] **Step 3 — implement:** add the property; collect committed destination paths in `RenameMatchedOutput`; populate in both success returns and the custom-packer return; approve `PublicApi`.
- [ ] **Step 4 — run PASS; commit** (`fix(lib): return committed output file paths from a reconstruction run (D1)`), then **bump the superproject pointer** for Tasks 1–3 (`chore: bump ReScene.Lib (per-set dirs, dir-qualified CRC, committed files)` + trailer). Delete `bin2/`.

---

## Phase B — App shared infrastructure

### Task 4: App — shared RAR-metadata normalization (#11, #12)

**Files:** Create `ReScene.App.Core/ViewModels/Reconstruction/RarMetadataNormalizer.cs`; Modify `SRRSwitchMapper.cs` (58-98), `SRRImportParser.cs` (`DescribeCompression` ~97-107), `ReconstructorViewModel.cs` (`ApplySwitchDiff` ~2098+ eight new dict set-cases); Test `RarMetadataNormalizerTests`, `SRRSwitchMapperTests`.

**Interfaces — Produces:** `static int RarMetadataNormalizer.NormalizeCompressionMethod(int raw)` (`0x30..0x35`→`0..5`, passes `0..5`, else `-1`); `static SRRSwitchMapper.DictionarySwitch RarMetadataNormalizer.DictionarySwitchFor(int sizeKb)` covering `64…1048576`→`MD64K…MD1G`. (VM toggles `SwitchMD8M…SwitchMD1G` already exist, `RARSwitchSettings.cs:44-51`.)

- [ ] **Step 1 — failing tests:** `NormalizeCompressionMethod(0x35)==5,(3)==3,(0x99)==-1`; `DictionarySwitchFor(1048576)==MD1G`; `Map(srr)` for `0x35`→`(5,"Best")`, `1048576`→`(MD1G,…)`; `DescribeCompression(0x35)`→"Best (-m5)".
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement** the normalizer; extend `DictionarySwitch` enum with the 8 large sizes; route `MapCompression`/`MapDictionary`/`DescribeCompression` through it; add the 8 `ApplySwitchDiff` set-cases.
- [ ] **Step 4 — run PASS; commit** (`fix: shared RAR5 compression + large-dictionary normalization (#11,#12)`).

### Task 5: App — verification snapshot as the SOLE verification source (#14, enables #8)

**Files:** Create `VerificationSnapshot.cs`; Modify `SharedReconstructionSettings.cs`, `ArchiveSetPlanner.cs` (`BuildExpectedVolumeCrcs` 67-95 consumes the snapshot), `ReconstructorViewModel.cs` (parse once before cleanup ~1449; remove post-cleanup reads at 1553-1560 `ResolveSfvVolumeNames`/`TryLoadUserSfv` and `LoadVerificationHashes` file read); Test `ReScene.App.Core.Tests/`.

**Interfaces — Produces:** `sealed record VerificationSnapshot(HashType HashType, IReadOnlyList<(string Name, string Hash)> Entries)` with `AllHashes`, `IReadOnlyList<string> VolumeNames`, `IReadOnlyDictionary<string,string> Crc32ByName` (empty for SHA1), and `HashesForVolumes(IEnumerable<string>)` (canonical qualified-first, basename fallback). Carried on `SharedReconstructionSettings.Verification`. **Only CRC32 snapshots populate `ExpectedVolumeCrcs`; SHA1 entries feed `options.Hashes` only.**

- [ ] **Step 1 — failing tests (as seams, not self-contradictory):** (i) `BuildExpectedVolumeCrcs` derives per-set CRCs from a `VerificationSnapshot` (no file I/O), qualified-first + basename fallback, map non-empty for the flat-SFV case; (ii) volume-name fallback (formerly `ResolveSfvVolumeNames`) comes from `snapshot.VolumeNames`; (iii) a SHA1 snapshot yields empty `Crc32ByName` but populated `AllHashes`. Rejection of `VerificationPath` under `OutputPath` is tested **separately** in Task 7 (not by deleting-and-using the same path).
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement:** parse the file once into a `VerificationSnapshot` at Start **before** cleanup; carry it on `SharedReconstructionSettings`; rewrite `BuildExpectedVolumeCrcs`, the volume-name fallback, and `LoadVerificationHashes` to read the snapshot; **delete** the post-cleanup `ResolveSfvVolumeNames()`/`TryLoadUserSfv(VerificationPath)` reads (1553-1560).
- [ ] **Step 4 — run PASS; commit** (`fix: verification snapshot is the sole post-cleanup verification source (#14)`).

### Task 6: App — reserved-root path guards with full-component link resolution (#1, foundation for #2/#26)

**Files:** Create `ReconstructionPathGuard.cs`; Test `ReconstructionPathGuardTests`.

**Interfaces — Produces:**
- `static string ResolveReal(string path)` — resolves **every** component (walk root→leaf via `Directory.ResolveLinkTarget`/OS final-path), re-appending non-existent suffixes; not `Directory.Exists`-gated (access-denied ≠ absent). Throws `IOException`-family when an existing path can't be resolved.
- `static bool IsStrictDescendant(string root, string candidate)` — real-resolves both; filesystem-appropriate comparer (case-insensitive on Windows/macOS-default, case-sensitive where the FS is; throw when indeterminate for an existing path → callers fail closed).
- `static string ResolveOutputRoot(string outputPath)` / `ResolveScratchRoot(string outputPath)` — the reserved roots themselves, each **verified to resolve under real `OutputPath`** (throws otherwise, catching reserved-root junction escape).
- `static string ResolveOutputChild(string outputPath, string relative)` / `ResolveScratchChild(string outputPath, string setKey)` — strict descendants of the respective root (throw on traversal); `ResolveScratchChild` sanitizes the key and appends a short stable hash for collision resistance.

- [ ] **Step 1 — failing tests:** child under the correct root; `..`/rooted `relative` throws; two raw keys sanitizing alike → distinct scratch dirs; a **junction ancestor above a normal child** whose real target escapes → `IsStrictDescendant` false / root-validate throws; access-denied ancestor → throws (fail closed), not lexical fallback; case-sensitivity per platform; `ResolveScratchChild(out,"x") != ResolveScratchRoot(out)`.
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement** per interfaces (component-walking resolver).
- [ ] **Step 4 — run PASS; commit** (`feat: reserved-root path guards with full-component link resolution (#1)`).

### Task 7: App — format/version compatibility map (D2 foundation for #6)

**Files:** Create `RarFormatCompatibility.cs`; Consumes `RARVersionThresholds` (lib, 500/700). Test `RarFormatCompatibilityTests`.

**Interfaces — Produces:**
- `enum RarFormat { Rar4, Rar5, Rar7 }`; `static RarFormat FormatForUnpackVersion(int unpackVersion)` (`<50`→Rar4, `<70`→Rar5, else Rar7 — matching `MapFormat`).
- `static bool ExecutableSupports(int exeVersion, RarFormat fmt, out bool needsMa4, out bool needsMa5)` — Rar4: `exe<500` (no `-ma`) or `500-699` (`needsMa4`); Rar5: `500-699` (`needsMa5`/default); Rar7: `>=700`.
- `static (IReadOnlyList<VersionRange> Ranges, IReadOnlyList<string> Folders, bool Empty) SelectFor(RarFormat fmt, IReadOnlyList<VersionRange> userRanges, IReadOnlyList<string> userFolders, IReadOnlyList<InstalledRARVersion> installed)` — intersects the format-capable exe versions with the user's selected ranges/folders; `Empty==true` when nothing is capable.

- [ ] **Step 1 — failing tests — one per case:** RAR4+old exe(390)→no `-ma`; RAR4+560→`needsMa4`; RAR5+560→`needsMa5`/default; RAR7+700; same-version folder variants preserved via `Folders`; empty intersection (RAR5 set, only 390 selected)→`Empty`; a metadata switch the user didn't select is not force-added.
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement** the mapping.
- [ ] **Step 4 — run PASS; commit** (`feat: RAR format↔executable-version compatibility map (#6 foundation)`).

---

## Phase C — Robustness primitive needed by later matrix builds

### Task 8: App — bounded, cancellable option-matrix builder preserving -mt0 (#13)

Placed before the per-set matrices (Task 10) so they reuse it.

**Files:** `RARCommandLineBuilder.cs` (267-268, 284, build entry → async/token overload + cardinality cap), `ReconstructorViewModel.cs` (`SwitchMTStart/End` setters 714-715; call site off-thread); Test `RARCommandLineBuilderTests`.

**Interfaces — Produces:** `static IReadOnlyList<RARCommandLineArgument[]> BuildCommandLineArguments(RARSwitchSettings s, CancellationToken ct)` that (a) allows `-mt0`, (b) clamps the high end to a version-aware valid max (RAR4 ≤16; RAR5+ per manual), (c) computes cardinality with `checked` arithmetic and **throws a typed "matrix too large" exception before allocating** when it exceeds a defined cap, (d) checks `ct` periodically. Callers invoke it via `Task.Run(…, ct)` — the planner/VM stay off the UI thread; the builder itself is synchronous+cancellable (no `Task.Run` inside it).

- [ ] **Step 1 — failing tests:** `SwitchMTEnd=int.MaxValue`→no overflow, bounded; `-mt0` preserved; a cap-exceeding matrix→typed exception before allocation; a cancelled token→`OperationCanceledException` promptly.
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement:** include 0 in the `-mt` range, clamp the high end version-aware; checked cardinality + cap; periodic `ct.ThrowIfCancellationRequested()`; clamp the VM setters to the same bounds.
- [ ] **Step 4 — run PASS; commit** (`fix: bounded, cancellable option-matrix builder, -mt0 preserved (#13)`).

---

## Phase D — Work-root / relocation core (atomic)

### Task 9: App — link-resolved, filesystem-correct overlap guard (#2, #26)

Placed before the destructive Task 10 so the real-path Release/Output/Verify overlap check exists first.

**Files:** `ReconstructorFieldGuidance.cs` (`PathsOverlap` 138-159, `PathsNeedAttention` 100-108 to include verification vs output); Consumes `ReconstructionPathGuard` (Task 6). Test `ReconstructorFieldGuidanceTests`.

- [ ] **Step 1 — failing tests:** (#2) junction ancestor → overlap detected; resolution failure on an existing path → fail closed (attention-needed); (#26) case-correct per filesystem; verification path under output flagged.
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement:** replace lexical compare with `ReconstructionPathGuard.ResolveReal` + filesystem-correct comparer; add verification-vs-output to the overlap set; fail closed on indeterminate.
- [ ] **Step 4 — run PASS; commit** (`fix: link-resolved, filesystem-correct overlap guard incl. verification path (#2,#26)`).

### Task 10: App — uniform work-root + relocate exactly the committed files + clear-once + cancel cleanup (#3, #1, #4, #5, #17)

Atomic. Consumes: `ReconstructionPathGuard` (T6), `BruteForceRunResult.CommittedFiles` (T3), the overlap guard (T9). Injects a file-operation seam for deterministic rollback tests.

**Files:** `ArchiveSetPlanner.cs` (`WorkRootFor` 182-185), `ReconstructorViewModel.cs` (relocation 1855-1898, cleanup 1904-1928, run loop 1601-1637, pre-run cleanup 1417-1447), `ReScene.Manager/Views/Wizards/BeginnerWizardFactory.cs` (confirm surface 204-219); Test `ReScene.App.Core.Tests/`, `ReScene.Manager.Tests/`.

**Interfaces — Consumes:** an `IFileMover` seam (`Move(src,dst)`, default `File.Move(...,overwrite:false)`) injected for the rollback test.

- [ ] **Step 1 — failing tests:** (a) single set, non-empty key + `Directory="DVD1"` → committed files (from `RunResult.CommittedFiles`) land at `OutputPath\output\<name>` (NOT `output\DVD1\`); scratch removed. (b) two sets sharing `Directory="DVD1"` → both survive. (c) `Directory="../../x"` → set fails, no delete outside output. (d) `RunResult.CommittedFiles` are the ONLY files relocated — `DeleteRARFiles=false` leftovers and `input\` sources are ignored. (e) `CompleteAllVolumes=false` (one committed file) and `RenameToReleaseNames=false` (generated names) both relocate correctly because identity comes from the result, not scanning. (f) preflight destination-exists → fail; injected mover failing on move N → rollback of this set's moved files only. (g) cancel mid-set → `<workRoot>` removed via scratch guard; committed set untouched. (h) multi-set custom-packer (`Combo==null`) → reported **failed/unsupported**, not success. (i) confirm text (VM + wizard) names the output+scratch subtrees; unrelated `OutputPath` root files survive; a still-needed input under a reserved root → Start rejected.
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement:**
  - `WorkRootFor` → `ReconstructionPathGuard.ResolveScratchChild(OutputPath, set.Key or "release")`.
  - Relocation takes `RunResult.CommittedFiles`; target = `ResolveOutputRoot(OutputPath)` when `setCount==1` else `ResolveOutputChild(OutputPath, set.Directory)`; require the committed set to be **complete** for the mode (count/name check against the set's expected volumes when `RenameToReleaseNames`); `Directory.CreateDirectory`; preflight no-overwrite; `IFileMover.Move` tracking dests; rollback moved dests on any failure; never recursively delete a shared target. Multi-set + `Combo==null` (custom packer) → return failure with an "unsupported" log.
  - Cleanup deletes only the guarded `<workRoot>` (scratch descendant); pre-run cleanup clears the guarded output + scratch subtrees by enumerating children, preserves unrelated `OutputPath` root files, updates both confirm messages, and rejects Start when an imported-SRR/verification/WinRAR path resolves under a reserved root.
  - Run loop: per-set `finally` cleans the work-root for any uncommitted set; committed sets untouched.
- [ ] **Step 4 — run PASS; commit** (`fix: relocate engine-reported committed files with reserved-root guards (#3,#1,#4,#5,#17)`).

---

## Phase E — Per-set correctness

### Task 11: App — per-set command/version matrices via compatibility map (#6)

**Files:** `SharedReconstructionSettings.cs` (add `RARSwitchSettings SwitchSnapshot` + `IReadOnlyList<InstalledRARVersion> InstalledVersions`), `ArchiveSetPlanner.cs` (`BuildOptionsForSet` 99-168), `ReconstructorViewModel.cs` (`BuildSharedSettings` capture, per-set build off-thread); Consumes `RarMetadataNormalizer` (T4), `RarFormatCompatibility` (T7), the cancellable builder (T8). Test `ArchiveSetPlannerTests`.

- [ ] **Step 1 — failing tests:** A `{unpack 29, m0, s-}`, B `{unpack 50, m5, s}` within user selection → A's args `-m0/-s-`, B's `-m5/-s/-ma5`; B's version ranges/folders = `RarFormatCompatibility.SelectFor(Rar5, …)` intersected with user selection; an empty-intersection set (RAR5, only 3.90 selected) → the set is reported **failed** ("no selected WinRAR version can produce RAR5") not a silent no-match; the flat metadata-less set → global matrix unchanged.
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement:** carry the switch snapshot + installed versions on `SharedReconstructionSettings`. Per set with metadata: derive `RarFormat` from `set.RARVersion`; clone the snapshot, set compression (normalized), dictionary, solid, and `-ma4/-ma5` from `ExecutableSupports`; version ranges/folders from `SelectFor` (fail the set honestly when `Empty`); build the matrix via Task 8's cancellable builder off-thread. Flat set → `shared.CommandLineArguments`/`RARVersions`. `// TODO(-rr)`.
- [ ] **Step 4 — run PASS; commit** (`fix: per-set matrices via format/version compatibility map (#6)`).

### Task 12: App — per-set hash gate, basename, canonical CRC, per-set dirs (#8, #10-app, #9-app, #7-app)

**Files:** `ArchiveSetPlanner.cs` (`BuildOptionsForSet` 119-156, `BuildExpectedVolumeCrcs`); Consumes `VerificationSnapshot` (T5), `SRRArchiveSet` dirs (T1), the canonical key (T2). Test `ArchiveSetPlannerTests`.

- [ ] **Step 1 — failing tests:** (#8) set B `Hashes` excludes A's first-volume CRC (`snapshot.HashesForVolumes(set.VolumeNames)`); (#10) `DVD1\x.rar` matches flat `x.rar` any separator; (#9) identical basenames in `CD1\`/`CD2\` distinct **and** flat-SFV case not empty **and** `ExpectedVolumeCrcs.Count == VolumeNames.Count` (single canonical key, no double-count); (#7) `ArchiveDirectoryPaths` + all three time maps from the set (flat set keeps union).
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement:** `HashesForVolumes`; `LastSegment` basename; ONE canonical dir-qualified key in `BuildExpectedVolumeCrcs` (Manager already falls back, T2); per-set dirs + three time maps from `set` (fallback to `shared` only when `set.Key==""`).
- [ ] **Step 4 — run PASS; commit** (`fix: per-set hash gate, basename, canonical CRC, per-set dirs (#8,#10,#9,#7)`).

---

## Phase F — Import / config / robustness

### Task 13: App — retire stale auto-SFV on every import (#15)
**Files:** `ReconstructorViewModel.cs` (`TryExtractStoredSFV` ~2576-2612); Test `ReScene.App.Core.Tests/`.
- [ ] **Step 1 — failing test:** import A (embedded SFV) then B (`StoredFiles.Count==0`) → `VerificationPath` cleared, `_sfvTempDir` null.
- [ ] **Step 2 — FAIL.** **Step 3 — implement:** move the retire block before the `Count==0` early return. **Step 4 — PASS; commit** (`fix: retire previous auto-extracted SFV on no-stored-files import (#15)`).

### Task 14: App — complete, versioned per-set config round-trip (#22)
**Files:** `Models/ImportedSRRState.cs` (add `SchemaVersion` + a complete `ArchiveSetDto` list), `ImportedSRRStateMapper.cs` (`Capture` incl. `hasState`, `Apply` rebuild via Task 1's public seam); Test `ReScene.App.Core.Tests/`.
- [ ] **Step 1 — failing test:** capture→apply a two-set state → 2 sets with full data (volumes ordered, CRCs, all timestamps incl. dir m/c/a, compression/dict/version/solid, host/attrs, large flags, `HasRecoveryRecord`), CI comparers preserved; `hasState` true when only `ArchiveSets` populated; a DTO with `SchemaVersion` set but empty dirs/null metadata is treated as **complete** (presence marker, not "non-empty"); a legacy DTO (no set list, older `SchemaVersion`) with `SRRFilePath` → re-parse via `ResolveSets`.
- [ ] **Step 2 — FAIL.** **Step 3 — implement** the versioned complete DTO + mapping via the public seam; `hasState` includes `ArchiveSets.Count>0`; prefer a restored set list only when `SchemaVersion` marks it complete. **Step 4 — PASS; commit** (`fix: complete versioned per-set config round-trip (#22)`).

### Task 15: App — checked volume-size conversion (#21)
**Files:** `RARCommandLineBuilder.cs` (`BuildVolumeArgument` 370-388); Test `RARCommandLineBuilderTests`.
- [ ] **Step 1 — failing tests:** blank+GB→`-v15000k`; `long.MaxValue`+GB→fallback, no overflow.
- [ ] **Step 2 — FAIL.** **Step 3 — implement:** `if (!long.TryParse|| v<=0) return $"-v{DefaultVolumeSizeKb}k";` then `checked(...)` per unit catching `OverflowException`→same fallback. **Step 4 — PASS; commit** (`fix: checked volume-size conversion (#21)`).

### Task 16: App — guard preflight directory enumeration (#18)
**Files:** `ReconstructorViewModel.cs` (~1302, ~1417); Test `ReScene.App.Core.Tests/`.
- [ ] **Step 1 — failing test:** enumeration throwing `UnauthorizedAccessException` → validation error, `IsRunning` false, no escape.
- [ ] **Step 2 — FAIL.** **Step 3 — implement:** wrap both `Directory.Enumerate*` in `try/catch (IOException or UnauthorizedAccessException)` → `ShowError`; return. **Step 4 — PASS; commit** (`fix: guard preflight directory enumeration (#18)`).

---

## Phase G — Progress / logging

### Task 17: App — per-set outcome rows + TimeProvider ETA (#23, #25)

**Files:** add `Microsoft.Extensions.TimeProvider.Testing` to `ReScene.App.Core.Tests.csproj`; `ReconstructionProgressTracker.cs` (ctor 19-27 add `TimeProvider`, phase/set-clear 148-156, `Tick` 187-199), `ReconstructorViewModel.cs` (finalize each set's row when the set completes in the run loop ~1636; remove the global `anySuccess` overwrite at `ReportSetSummary` 1970-1972; stop `OnStatusChanged` assigning whole-run `LastRunSucceeded`/`ProgressMessage` per engine attempt); Test `ReconstructionProgressTrackerTests`.

- [ ] **Step 1 — failing tests (FakeTimeProvider):** (#23) set 1 succeeds, set 2 seeds then fails → set 2's row not "Match"; each set's row finalized from its own outcome at set completion (not recovered in `ReportSetSummary`); prior-set rows survive set boundaries. (#25) 100 s remaining then +5 s with no event → ~95 s remaining.
- [ ] **Step 2 — FAIL.**
- [ ] **Step 3 — implement:** inject `TimeProvider` (default `System`, tests `FakeTimeProvider`); `Tick` uses `_timeProvider.GetLocalNow()` + cached fixed completion instant minus elapsed; stamp rows with active set, preserve prior-set rows across phase changes; finalize per-set rows at set completion; delete the global `anySuccess` overwrite; gate per-attempt whole-run status assignments.
- [ ] **Step 4 — PASS; commit** (`fix: per-set outcome rows + TimeProvider ETA (#23,#25)`).

### Task 18: App — timestamp summary in finally, generation-safe batched log, set/attempt progress (#19, #20, #24)

**Files:** `ReconstructorViewModel.cs` (timestamp display ~2293, log append 2338-2354, progress mapping ~2244, run `finally`); Test `ReScene.App.Core.Tests/`.

- [ ] **Step 1 — failing tests:** (#19) two sets each with a failure → one summary from the run's `finally` (also on cancel/exception); `_timestampFailures` accessed thread-safely. (#20) N events → ≤K dispatches via a thread-safe queue with an atomic flush flag, a **run-generation token** so a queued flush from a prior run cannot repopulate after Start clears, a synchronous final drain, per-target order preserved. (#24) progress labeled `Set X/N · <stage>` (seed vs full) so it does not rewind within a set.
- [ ] **Step 2 — FAIL.**
- [ ] **Step 3 — implement** per the tests.
- [ ] **Step 4 — PASS; commit** (`fix: timestamp summary in finally, generation-safe batched log, set/attempt progress (#19,#20,#24)`).

---

## Coverage check (all 25 → task)

#1 T6+T10 · #2 T6+T9 · #3 T3+T10 · #4 T10 · #5 T3+T10 · #6 T7+T11 · #7 T1+T12+T14 · #8 T5+T12 · #9 T2+T12 · #10 T2+T12 · #11 T4 · #12 T4 · #13 T8 · #14 T5+T10 · #15 T13 · #17 T10 · #18 T16 · #19 T18 · #20 T18 · #21 T15 · #22 T14 · #23 T17 · #24 T18 · #25 T17 · #26 T6+T9. (#16 = false positive, no task.) New enabling work: engine committed-files result (T3, for #3/#4/#5), format/version map (T7, for #6).

## Sequencing rationale (no broken intermediate state)

Lib-first with backward-compatible keys and the additive committed-files result (T1–T3, pointer bumped). App infra whose consumers come later: normalization (T4), verification snapshot **which also updates the planner in the same task** so nothing reads the old values-only member afterward (T5), path guards (T6), format/version map (T7), the cancellable matrix builder (T8) **before** the per-set matrices, the overlap guard (T9) **before** the destructive relocation (T10). The atomic T10 switches the work-root and relocates the engine-reported committed files in one commit (single-set never strands). Per-set matrices (T11) reuse T4/T7/T8; per-set CRC/dirs (T12) reuse T1/T2/T5. Import/config/robustness (T13–T16), progress (T17–T18). Every task ends green.

## Final verification (after all tasks)

- [ ] Clean non-incremental build `-p:BaseOutputPath=bin2/` → 0 warnings / 0 errors; `PublicApi.ReScene.approved.txt` current.
- [ ] Full suites green: `ReScene.App.Core.Tests`, `ReScene.Manager.Tests`, `ReScene.Lib/ReScene.Tests`.
- [ ] Delete `bin2/` (worktree + `E:/Projects/avalonia-agent-mcp`).
- [ ] Manual smoke (WinRAR + a real single-set SRR): verified volumes land in `OutputPath\output\` — the definitive proof of #3.
