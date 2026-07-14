# RAR Reconstruction Correctness Fixes — Implementation Plan (rev. 5, post-4×-codex-review)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Fix all 25 verified codex findings in the RAR reconstruction subsystem — restoring the common single-set workflow and closing the multi-set correctness/safety gaps — with the shared infrastructure and engine contracts the fixes depend on built first, and no task leaving the build broken.

**Architecture:** Lib-first: per-set directories, a backward-compatible dir-qualified CRC, and an engine result that returns the committed matches **grouped by combo, first verified match kept as the seed** (plus the custom-packer's written files). Then app-side infrastructure (metadata normalization, a named verification snapshot that becomes the sole verification source, two reserved-root path guards, a format/version compatibility map, a `TimeProvider` seam, an immutable switch snapshot). Then the atomic work-root/relocation redesign that relocates exactly the engine-reported committed files of the kept match. Then per-set matrices (via version-bounded switch args, incl. the corrected `-mt0..64` table), robustness, path-guard, and progress fixes. TDD throughout.

**Tech Stack:** .NET 10 · CommunityToolkit.Mvvm 8.4 · xUnit · `Microsoft.Extensions.TimeProvider.Testing` (FakeTimeProvider) · `ReScene.App.Core` + `ReScene.Lib` submodule. Spec: `docs/superpowers/specs/2026-07-11-reconstruction-fixes-design.md` (updated to rev. 5). This revision adopts all concerns from four codex reviews of rev. 1–4, grounded in a read of the engine (`Manager`/`SRRReconstructor`), the `-mt`/switch matrix builder, and the version-selection code (`RARVersionSelector`). Rev. 5 corrects the engine-truth details codex round-4 surfaced: the `-ma4/-ma5` map (RAR5 needs `-ma5` on 500-699, 7.x is RAR7-native), `-mt` band-pruned at build time (not version-bounded, because `FilterArgumentsForVersion` drops args not rows), all-or-none committed matches, an explicit single-set custom-packer relocation branch, distinct/non-overlapping reserved roots with live-input overlap rejection, field-by-field null metadata, and a scan-state-guarded installed-versions capture.

## Ground-truth notes (verified against source; do not re-assume)

- Engine output lives at `<OutputDirectoryPath>\output`; input at `…\input`; trial candidates are written **into `output`** under generated names `…{versionDir}-{joinedArgs}[-patched].rar`; `comment.txt` at the root; logs at `…\logs` (`Manager.cs:583-630`, `InputDirectoryPreparer.cs:99-107`).
- After success, `output` holds the committed winner **plus** retained non-match leftovers when `RAROptions.DeleteRARFiles==false` (`Manager.cs:742-753`); with `RenameToOriginalNames==false` the winner keeps a generated name **indistinguishable by pattern** from leftovers. `CompleteAllVolumes==false` produces only the **first** volume (`Manager.cs:433-451,690`).
- `BruteForceRunResult(bool Success, WinningCombo? Combo)`; `WinningCombo(int Version, IReadOnlyList<RARCommandLineArgument> Args)` — **no produced file paths** are returned today (`BruteForceRunResult.cs`, `WinningCombo.cs`, `Manager.cs:388,821`). Custom-packer returns `Combo==null` (`Manager.cs:231`). `RenameMatchedOutput` is `void` today (`Manager.cs:937-994`).
- Exploratory mode (`StopOnFirstMatch==false`): the outer loop (`Manager.cs:335-350`) **continues after a match**, each full match runs `RenameMatchedOutput` (commits to `output`), and `winningCombo = combo` at `Manager.cs:340` **overwrites with each later match → the LAST wins**. This contradicts the original design (`2026-06-28…-design.md`: the FIRST fully-verified combo is the kept seed). All verified matches produce **byte-identical** volumes (they all satisfy the same expected CRCs), so keeping the first match's *combo* is a reporting/seeding choice — the first match's committed *file paths* always resolve to correct bytes regardless of later matches.
- `-mt` matrix (`RARCommandLineBuilder.cs:267-268,354`): today `mtLo = SwitchMT ? Math.Max(1, …) : 0` **drops `-mt0`**, and each arg is `new($"-mt{z}", 360)` (Min 360, **no Max**). RAR4 permits `-mt 0..16` (`users_manual4.00.txt:1227`); RAR5+ up to 64. **`FilterArgumentsForVersion` (`RARVersionSelector.cs:117-122`) DROPS an inapplicable arg, it does not skip the row** — so a version-bounded `-mt20` on a RAR4 exe silently becomes a no-`-mt` command (degraded duplicate rows). Therefore `-mt` must be **band-pruned at build time** (RAR4→16, RAR5/7→64) via a `maxThreads` parameter, not version-bounded per arg.
- `-ma4`/`-ma5` are already emitted bounded `(500,699)` (`RARCommandLineBuilder.cs:100,105`). Engine format policy (verified): `ParseRARArchiveVersion` — with `-ma4`→RAR4, `-ma5`→RAR5 regardless of exe; else `<500`→RAR4, `<700`→RAR5, `>=700`→RAR7. `ShouldSkipRAR6TimestampCombination` treats `550-699` **without** `-ma5` as RAR4-format. Net: **RAR4** = `<500` native or `500-699`+`-ma4`; **RAR5** = `500-699`+**required** `-ma5` (700+ can't — `-ma5` is filtered at 700 and 700 is RAR7-native); **RAR7** = `>=700` native.
- `HasScannedVersions` is set `false` on a `WinRARPath` change (`ReconstructorViewModel.cs:202`) but `_lastScan` keeps the **prior** scan until the async rescan completes (`:530-531`); the VM already guards `SelectedVersionFolders = HasScannedVersions ? … : []` (`:1688`). So an installed-versions capture must be `HasScannedVersions ? [.. _lastScan] : []`, never a raw `_lastScan`.
- `RARVersionThresholds` (500/700) is **internal** to `ReScene.Lib` — App.Core cannot reference it; App-side code mirrors the two constants locally (documented as tracking the lib's internal thresholds).
- Custom-packer (`SRRReconstructor`) writes **directly to `OutputDirectoryPath` root**, possibly nested (`DVD1\x.rar`), no brute-force/rename (`Manager.cs:211-226`, `SRRReconstructor.cs:40,140-141`).
- Rename maps produced→original **positionally** via `RAROptions.OriginalRARFileNames`, using `Path.GetFileName(originalNames[i])` (`Manager.cs:963-965,984-986`) — the Unix-backslash bug (#10) is here too.
- `VersionRange(int Start incl, int End excl)` over **executable** version×100 (200-800). Format per (exe, args): `<500`→RAR4, `500-699`→RAR5 default / RAR4 with `-ma4` / RAR5 with `-ma5`, `>=700`→RAR7; `-ma4/-ma5` carry `Min=500,Max=699` and are filtered out elsewhere (`RARVersionSelector.ParseRARArchiveVersion`/`FilterArgumentsForVersion`, `RARVersionThresholds` 500/700). `SRRArchiveSet.RARVersion` is an **unpack** version, never read per-set today (`ArchiveSetPlanner.cs:99-169`).
- `SharedReconstructionSettings.VerificationHashes` is values-only; the run still re-reads the SFV after cleanup via `ResolveSfvVolumeNames()`/`TryLoadUserSfv(VerificationPath)` (`ReconstructorViewModel.cs:1553-1560`). `SFVFileEntry.FileName`/`SHA1FileEntry.FileName` exist. `RARSwitchSettings` is a copyable `sealed record`. `RARVolumeIdentifier.IsRARVolume` recognizes `.rar/.rNN/.sNN/.NNN`. `ReScene.Lib` exposes internals only to `ReScene.Tests`, so App.Core-facing seams must be **public**.

## Global Constraints

- **Single-set output contract:** a single-set run produces byte-identical `.rar` output at `OutputPath\output\<name>` — same bytes AND same location (no `<dir>` subfolder even for a non-empty `Key`/`Directory`).
- **Committed-file identity:** relocation moves **exactly the files the engine reports as committed for the kept match** (`BruteForceRunResult.Matches[0].Files` — the first **fully-placed** verified combo), never files it discovers by scanning the work-root. Brute-force sources are guarded strictly under `<workRoot>\output`; custom-packer sources strictly under the custom work root — never `input\` sources or `DeleteRARFiles==false` leftovers. A `CommittedMatch` is recorded **all-or-none**: an incomplete placement is not a match, so a later complete combo can still be the seed. The custom-packer path is reported separately (`CustomPackerFiles`); single-set custom relocates it, multi-set custom is rejected before the engine runs.
- **First verified match is the seed:** exploratory runs (`StopOnFirstMatch==false`) keep the **first** fully-placed verified combo as `Combo`/`Matches[0]` (not the last-overwritten one); every fully-placed match is returned in discovery order. Because all matches are byte-identical, `Matches[0].Files` are the canonical committed output.
- **Two reserved guarded roots, distinct & non-overlapping:** every destructive delete/move targets a strict descendant of exactly one **validated** reserved root — the output tree (`OutputPath\output`) or the scratch tree (`OutputPath\.rescene-work`) — resolved together via `ResolveReservedRoots`, which verifies each resolves (real links, every component) under the real `OutputPath` **and** that the two do not resolve equal or nested (junction collapse). Destructive cleanup of a reserved root enumerates and guards its children; it never deletes via an unresolved path. When safety cannot be established for an existing path, **fail closed** (validation error), never delete. Untrusted `set.Directory`/`set.Key` can never widen or redirect a delete. **No live input** (imported SRR, verification file, concrete release input files, selected WinRAR executable/dir) may `Overlaps` either reserved subtree — Start is rejected if one does.
- **Full-volume verification never silently disabled:** expected-CRC keying stores exactly **one canonical key per volume** (never both qualified+basename aliases — that double-counts coverage); `Manager` looks up canonical-then-legacy-basename. An empty `ExpectedVolumeCrcs` in a case the old basename logic would have covered is a defect.
- **Honest reporting:** a set reports success only when its own complete committed volume set is placed at its final location. Multi-set custom-packer (`Combo==null`, root/nested layout) is reported **unsupported/failed**, never a false success (the original design's noted non-goal).
- **TDD, small commits, green each task; build gate:** `dotnet build ReScene.Manager.slnx -c Debug -p:BaseOutputPath=bin2/` → 0 warnings/0 errors; relevant `dotnet test` green; delete `bin2/` after.
- **Lib-first & backward-compatible:** lib tasks land + pointer bumped before app builds against them; dir-qualified CRC keeps a legacy basename fallback so the app works across the gap; update `PublicApi.ReScene.approved.txt`.
- **Deferred:** recovery-record (`-rr`) — no switch exists; a `// TODO(-rr)` marks where it would attach.
- **Commit trailer:** `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` + `Claude-Session: https://claude.ai/code/session_018sZM14bBaWLT2ammzasLmL` (earlier plan-doc commits used the Opus 4.8 trailer; the session model switched — new commits use the Fable 5 trailer).

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

### Task 3: Lib — engine returns grouped committed matches (first-kept) + custom-packer files (D1 foundation for #3/#4/#5)

**Files:** Create `ReScene.Lib/ReScene/Core/CommittedMatch.cs`; Modify `BruteForceRunResult.cs`, `Manager.cs` (`RenameMatchedOutput` 937-994 → returns committed dest paths; outer loop 335-350 → keep-first + accumulate matches; full-match commit 813-821 → carry each match's paths out of `TryProcessCommandLinesAsync`; custom-packer branch 211-232 → written paths), `MatchedRarWriter.cs` (`MoveMatchedFile` 16-30), `SRRReconstructor.cs` (returns written paths), `PublicApi.ReScene.approved.txt`; Test lib Manager tests.

**Interfaces — Produces:**
- `sealed record CommittedMatch(WinningCombo Combo, IReadOnlyList<string> Files)` — one per verified combo whose **complete** required volume set was placed in `output`; `Files` = absolute dest paths actually placed (full set when `CompleteAllVolumes`, else the single first volume; release or generated names per `RenameToOriginalNames`).
- `RenameMatchedOutput` returns a structured `(IReadOnlyList<string> Placed, bool Complete)` — `Complete` is false if any required `MoveMatchedFile` reported failure (an occupied destination, or the post-move existence check failing).
- **All-or-none:** a `CommittedMatch` is recorded **only when `Complete`**. Incomplete placement is **not** treated as a match — `TryProcessCommandLinesAsync` does not return `Found=true` for it, so the outer loop keeps searching and a later fully-placed combo can still become `Matches[0]`. (Today `MoveMatchedFile` failures are only logged while `Found=true` is still returned — `Manager.cs:816-821`.)
- `BruteForceRunResult(bool Success, WinningCombo? Combo)` — **positional shape unchanged** so existing 2-arg callers keep compiling; `Combo` is the KEPT/seed combo = the first fully-placed verified match's combo.
- `IReadOnlyList<CommittedMatch> Matches { get; init; } = []` — non-positional; `Matches[0]` is the first fully-placed verified match; `Combo` mirrors `Matches[0].Combo`. Exploratory runs return every fully-placed match in discovery order.
- `IReadOnlyList<string> CustomPackerFiles { get; init; } = []` — populated only on the `Combo==null` custom-packer path; `Matches` empty there.
- Empty `Matches` and `CustomPackerFiles` on failure.

- [ ] **Step 1 — failing tests (pure/seam where possible):**
  - `RenameMatchedOutput` returns the exact placed dest paths + `Complete==true` — CAV multi-volume and non-CAV single-volume — **including a source==dest no-op** (still reported placed at its final path).
  - **occupied destination / mid-CAV placement failure** → `RenameMatchedOutput` returns `Complete==false`; that combo is **not** recorded as a match; a subsequent fully-placed combo becomes `Matches[0]`.
  - `MoveMatchedFile(src,dst)` uses a filesystem-correct path equality (case-sensitive where the FS is) for its source==dest short-circuit, and **verifies the destination exists** after the move before returning success.
  - exploratory outer loop with **two** fully-placed verified combos and `StopOnFirstMatch==false` → `Matches.Count==2`, `Matches[0]` is the FIRST discovered combo, and `result.Combo==Matches[0].Combo` (first-kept, not last-overwritten).
  - `StopOnFirstMatch==true` with a match → `Matches.Count==1`.
  - a failed run → empty `Matches` and `CustomPackerFiles`.
  - the custom-packer branch → `CustomPackerFiles` = its written volume paths, `Matches` empty, `Combo==null`.
- [ ] **Step 2 — run FAIL** (result has no `Matches`).
- [ ] **Step 3 — implement:** make `RenameMatchedOutput` return `(Placed, Complete)` (both branches, incl. the no-op dest); fix `MoveMatchedFile` equality + post-move existence check; in `TryProcessCommandLinesAsync` treat `Complete==false` as **not a match** (do not return `Found=true`; continue); return each fully-placed match's `(combo, paths)`; in the outer loop accumulate a `CommittedMatch` per fully-placed match and set `winningCombo` **only when not already found** (keep-first); thread `Matches` + the first `Combo` into `BruteForceRunResult`; populate `CustomPackerFiles` on the custom-packer return; approve `PublicApi`.
- [ ] **Step 4 — run PASS; commit** (`fix(lib): return grouped committed matches (first-kept) + custom-packer files (D1)`), then **bump the superproject pointer** for Tasks 1–3 (`chore: bump ReScene.Lib (per-set dirs, dir-qualified CRC, committed matches)` + trailer). Delete `bin2/`.

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

**Interfaces — Produces:**
- `sealed record VerificationSnapshot(HashType HashType, IReadOnlyList<(string Name, string Hash)> Entries)` with `AllHashes`; `IReadOnlyList<string> VolumeNames` (**preserving the `RARVolumeIdentifier.IsRARVolume` filtering that `ResolveSfvVolumeNames` applied** — SFV entries that are not RAR volumes are excluded); `IReadOnlyDictionary<string,string> Crc32ByName` (empty for SHA1); `HashesForVolumes(IEnumerable<string>)` (canonical qualified-first, then **unambiguous** basename fallback). Carried on `SharedReconstructionSettings.Verification`. **Only CRC32 snapshots populate `ExpectedVolumeCrcs`; SHA1 entries feed `options.Hashes` only.**
- `static string VerificationSnapshot.LastSegment(string name)` — app-side basename helper (splits on `/` and `\`; mirrors the lib's private `LastSegment` from Task 2) used by `HashesForVolumes`, the planner's canonical keying, and Task 12.
- Embedded per-set SFV priority is preserved: when a set has an embedded SFV (`LoadEmbeddedSfvBytes`/`EmbeddedSfvMatchesSet`), its entries win; the user's `VerificationPath` snapshot only **fills gaps** the embedded SFV does not cover.

- [ ] **Step 1 — failing tests (as seams, not self-contradictory):** (i) `BuildExpectedVolumeCrcs` derives per-set CRCs from a `VerificationSnapshot` (no file I/O), qualified-first + basename fallback, map non-empty for the flat-SFV case, **and `ExpectedVolumeCrcs.Count == set.VolumeNames.Count` — exactly one canonical key per volume, no qualified+basename double-count** (this is the #9 canonical-keying assertion; Task 12 does not repeat it); (ii) `snapshot.VolumeNames` **excludes** a non-RAR SFV entry (e.g. a stray `.nfo`) while keeping `.rar/.rNN`, matching the old `IsRARVolume` filter; (iii) an embedded per-set SFV wins over the user snapshot, and the user snapshot fills a volume the embedded SFV omits; (iv) basename fallback resolves only when unambiguous — two snapshot entries sharing a basename under different dirs do **not** collapse to one; (v) a SHA1 snapshot yields empty `Crc32ByName` but populated `AllHashes`. Rejection of `VerificationPath` under `OutputPath` is tested **separately** in Task 10's Start-rejection (not by deleting-and-using the same path).
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement:** parse the file once into a `VerificationSnapshot` at Start **before** cleanup, applying `IsRARVolume` filtering to `VolumeNames`; carry it on `SharedReconstructionSettings`; add `LastSegment`; rewrite `BuildExpectedVolumeCrcs` (ONE canonical dir-qualified key per volume), the volume-name fallback, and `LoadVerificationHashes` to read the snapshot with embedded-SFV-wins/user-fills-gaps precedence; **delete** the post-cleanup `ResolveSfvVolumeNames()`/`TryLoadUserSfv(VerificationPath)` reads (1553-1560).
- [ ] **Step 4 — run PASS; commit** (`fix: verification snapshot is the sole post-cleanup verification source, one canonical key (#14,#9-keying)`).

### Task 6: App — reserved-root path guards with full-component link resolution (#1, foundation for #2/#26)

**Files:** Create `ReconstructionPathGuard.cs`; Test `ReconstructionPathGuardTests`.

**Interfaces — Produces:**
- `static string ResolveReal(string path)` — resolves **every** component (walk root→leaf via `Directory.ResolveLinkTarget`/OS final-path), re-appending non-existent suffixes; not `Directory.Exists`-gated (access-denied ≠ absent). Throws `IOException`-family when an existing path can't be resolved.
- `static bool IsStrictDescendant(string root, string candidate)` — real-resolves both; filesystem-appropriate comparer (case-insensitive on Windows/macOS-default, case-sensitive where the FS is; throw when indeterminate for an existing path → callers fail closed).
- `static bool IsSameOrDescendant(string root, string candidate)` / `static bool Overlaps(string a, string b)` — `IsSameOrDescendant` is equality **or** strict-descendant; `Overlaps` is `a==b || IsStrictDescendant(a,b) || IsStrictDescendant(b,a)` (real-resolved, FS-correct). Used for live-input rejection (a live input must not overlap a reserved subtree) and root-distinctness.
- `static (string OutputRoot, string ScratchRoot) ResolveReservedRoots(string outputPath)` — resolves BOTH reserved roots, verifies each resolves under real `OutputPath`, **and asserts they are distinct and mutually non-overlapping** (throws if a junction makes them equal or nested). Every destructive operation resolves the pair through this first.
- `static string ResolveOutputRoot(string outputPath)` / `ResolveScratchRoot(string outputPath)` — the reserved roots individually (used where only one is needed), same per-root verification.
- `static string ResolveOutputChild(string outputPath, string relative)` / `ResolveScratchChild(string outputPath, string setKey)` — strict descendants of the respective root (throw on traversal); `ResolveScratchChild` sanitizes the key and appends a short stable hash for collision resistance.

- [ ] **Step 1 — failing tests:** child under the correct root; `..`/rooted `relative` throws; two raw keys sanitizing alike → distinct scratch dirs; a **junction ancestor above a normal child** whose real target escapes → `IsStrictDescendant` false / root-validate throws; access-denied ancestor → throws (fail closed), not lexical fallback; case-sensitivity per platform; `ResolveScratchChild(out,"x") != ResolveScratchRoot(out)`; `IsSameOrDescendant(root,root)` true and `Overlaps` symmetric; `ResolveReservedRoots` **throws** when a junction makes `output` and `.rescene-work` resolve to the same or a nested location.
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement** per interfaces (component-walking resolver).
- [ ] **Step 4 — run PASS; commit** (`feat: reserved-root path guards with full-component link resolution (#1)`).

### Task 7: App — format/version compatibility map (D2 foundation for #6)

**Files:** Create `RarFormatCompatibility.cs` — with **app-local documented constants** `const int Rar5Floor = 500; const int Rar7Floor = 700;` (a comment notes they mirror the lib-internal `RARVersionThresholds`, which App.Core cannot reference). Test `RarFormatCompatibilityTests`.

**Interfaces — Produces:**
- `enum RarFormat { Rar4, Rar5, Rar7 }`; `static RarFormat FormatForUnpackVersion(int unpackVersion)` (`<50`→Rar4, `<70`→Rar5, else Rar7 — matching `MapFormat`).
- `static bool ExecutableSupports(int exeVersion, RarFormat fmt, out bool needsMa4, out bool needsMa5)` — **matching the engine's actual policy** (verified: `-ma4`/`-ma5` args are bounded `(500,699)` in `RARCommandLineBuilder.cs:100,105`; `ShouldSkipRAR6TimestampCombination` treats `550-699` **without** `-ma5` as RAR4; `ParseRARArchiveVersion` returns RAR7 for `>=700`):
  - **Rar4:** `exe<Rar5Floor` (native, no `-ma`) or `Rar5Floor..Rar7Floor-1` (`needsMa4`). **Not** `>=Rar7Floor` (700+ cannot make RAR4 — `-ma4` is filtered out at 700 and 700 is RAR7-native).
  - **Rar5:** `Rar5Floor..Rar7Floor-1` with **`needsMa5` required** (500-699; `-ma5` must be emitted — unflagged 550-699 is RAR4). **Not** `<Rar5Floor` and **not** `>=Rar7Floor` (the `Max=699` bound filters `-ma5` at 700 and 700 is RAR7-native — this engine cannot coerce 7.x to RAR5).
  - **Rar7:** `>=Rar7Floor` (native, no `-ma`).
- `readonly record struct FormatSelection(IReadOnlyList<VersionRange> Ranges, IReadOnlyList<string> Folders, bool NeedsMa4, bool NeedsMa5, bool Empty)`.
- `static FormatSelection SelectFor(RarFormat fmt, IReadOnlyList<VersionRange> userRanges, IReadOnlyList<string> userFolders, IReadOnlyList<InstalledRARVersion> installed)` — intersects the **format-capable** exe versions (Rar4: `<700`; Rar5: `500-699`; Rar7: `>=700`) with the user's selected ranges/folders; `NeedsMa4`/`NeedsMa5` are the **aggregate** over the surviving selection (Rar4→`NeedsMa4` true iff any surviving exe is `500-699`; Rar5→`NeedsMa5` always true; Rar7→both false). Task 11 emits `-ma4`/`-ma5` as a `RARCommandLineArgument` bounded `Min=500,Max=699` (matching the existing builder) so it applies per-exe via `FilterArgumentsForVersion` — a mixed RAR4 selection applies `-ma4` only to its `500-699` exes, leaving `<500` native. `Empty==true` when nothing is capable.
- **No-scan path:** when `installed` is empty, `SelectFor` ignores the (absent) installed list, clips the user's ranges to the format-capable version bounds, and returns empty `Folders`. **Whether to treat the run as no-scan is decided by the caller passing `installed=[]` (Task 11 captures `HasScannedVersions ? [..] : []`) — `SelectFor` never reads scan state.**

- [ ] **Step 1 — failing tests — one per case:** RAR4+390→native, `NeedsMa4` false; RAR4+560→`NeedsMa4`; **RAR4 with both 390 and 560 selected → both ranges survive, `NeedsMa4` true (the version-bounded `-ma4` applies only to 560; 390 stays native)**; RAR4+700→**excluded** (not capable); RAR5+560→`NeedsMa5` **true** (required); RAR5+700→**excluded** (7.x is RAR7-native, cannot make RAR5); RAR7+700→native, both flags false; same-version folder variants preserved via `Folders`; empty intersection (RAR5 set, only 390 selected)→`Empty`; no-scan (empty `installed`, RAR5, user range `500-699`)→clipped ranges, empty `Folders`, not `Empty`.
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement** the mapping and `SelectFor` (capable-band intersection + aggregate `-ma` flags + no-scan clipping).
- [ ] **Step 4 — run PASS; commit** (`feat: RAR format↔executable-version compatibility map, engine-correct -ma policy (#6 foundation)`).

---

## Phase C — Robustness primitive needed by later matrix builds

### Task 8: App — bounded, cancellable option-matrix builder with band-pruned -mt (#13)

Placed before the per-set matrices (Task 11) so they reuse it.

**Why not version-bound `-mt`:** `FilterArgumentsForVersion` **drops** an inapplicable arg rather than skipping the row (verified `RARVersionSelector.cs:117-122`). So a version-bounded `-mt20 (Min=500)` on a 390 exe would silently become a **no-`-mt`** command — many rows collapsing to identical degraded duplicates, a match reported outside the requested thread range, and inflated cardinality. Instead we **prune the `-mt` range by executable band at build time** via a `maxThreads` parameter the caller sets from the target format (RAR4→16, RAR5/7→64). `-mt` itself stays a uniform `new($"-mt{z}", 360)` arg (no per-arg upper bound, no `ArchiveVersion` flag).

**Files:** `RARCommandLineBuilder.cs` (267-268 range normalization + `maxThreads` param, 354, build entry → token overload + cardinality cap), `ReconstructorViewModel.cs` (`SwitchMTStart/End` setters 714-715 clamp to 0..64; global-matrix call site off-thread **and lazy**); Test `RARCommandLineBuilderTests`.

**Interfaces — Produces:** `static IReadOnlyList<RARCommandLineArgument[]> BuildCommandLineArguments(RARSwitchSettings s, CancellationToken ct, int maxThreads = 64)` that:
- (a) **includes `-mt0`** — drop the `Math.Max(1, …)` floor.
- (b) **clamps BOTH endpoints to `0..maxThreads` BEFORE ordering** — `lo = min(clamp(Start), clamp(End))`, `hi = max(…)` — so a range like `100..200` clamps to `64..64` (a single `-mt64`), never an empty loop; and a RAR4 per-set build (`maxThreads=16`) never generates `-mt17..64` rows at all.
- (c) computes cardinality with `checked` arithmetic and **throws a typed "matrix too large" exception before allocating** when it exceeds a defined cap.
- (d) checks `ct` periodically. Callers invoke it via `Task.Run(…, ct)`; the builder itself is synchronous+cancellable (no `Task.Run` inside it).

- [ ] **Step 1 — failing tests:** `SwitchMTEnd=int.MaxValue`→clamped to `maxThreads`, no overflow; `-mt0` preserved when `Start==0`; **`Start=100,End=200,maxThreads=64` → single `-mt64` row, not empty**; **`maxThreads=16` (RAR4) → no `-mt17..64` rows emitted at all** (pruned at build time, not left to arg-filtering); a cap-exceeding matrix→typed exception before allocation; a cancelled token→`OperationCanceledException` promptly.
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement:** allow 0; clamp both endpoints to `0..maxThreads` before ordering; keep `-mt{z}` as `new($"-mt{z}", 360)`; checked cardinality + cap; periodic `ct.ThrowIfCancellationRequested()`; clamp the VM setters to 0..64. **Make the VM's global (flat, metadata-less) matrix lazy** so an imported multi-set run that uses per-set matrices (Task 11) never eagerly builds/cap-checks the global one — a huge global range must not abort a valid per-set run.
- [ ] **Step 4 — run PASS; commit** (`fix: bounded, cancellable matrix builder with band-pruned -mt (#13)`).

---

## Phase D — Work-root / relocation core (atomic)

### Task 9: App — link-resolved, filesystem-correct overlap guard (#2, #26)

Placed before the destructive Task 10 so the real-path Release/Output/Verify overlap check exists first.

**Files:** `ReconstructorFieldGuidance.cs` (`PathsOverlap` 138-159, `PathsNeedAttention` 100-108 to include verification vs the reserved subtrees); Consumes `ReconstructionPathGuard` (Task 6). Test `ReconstructorFieldGuidanceTests`.

The verification/release/imported overlap is flagged only when a path resolves under the two subtrees reconstruction destructively clears — `OutputPath\output` or `OutputPath\.rescene-work` — **not** merely under the `OutputPath` root (Task 10's cleanup preserves unrelated root files, and multi-set root sets legitimately share the output root).

- [ ] **Step 1 — failing tests:** (#2) junction ancestor whose real target lands under `output` → overlap detected; resolution failure on an existing path → fail closed (attention-needed); (#26) case-correct per filesystem; a verification path under `output` is flagged, but a verification path in the `OutputPath` **root** (outside `output`/`.rescene-work`) is **not** flagged.
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement:** replace lexical compare with `ReconstructionPathGuard.ResolveReal` + filesystem-correct comparer; flag verification/release/imported paths that are strict descendants of the resolved `output` or `.rescene-work` roots (not the bare `OutputPath`); fail closed on indeterminate.
- [ ] **Step 4 — run PASS; commit** (`fix: link-resolved overlap guard scoped to the reserved subtrees (#2,#26)`).

### Task 10: App — uniform work-root + relocate exactly the committed files + clear-once + cancel cleanup (#3, #1, #4, #5, #17)

Atomic. Consumes: `ReconstructionPathGuard` (T6), `BruteForceRunResult.Matches`/`CustomPackerFiles` (T3), the overlap guard (T9). Injects a file-operation seam for deterministic rollback tests. **Test fakes MUST create the files they report as committed** (the source guard requires them to exist).

**Files:** `ArchiveSetPlanner.cs` (`WorkRootFor` 182-185), `ReconstructorViewModel.cs` (relocation 1855-1898, cleanup 1904-1928, run loop 1601-1637, pre-run cleanup 1417-1447), `ReScene.Manager/Views/Wizards/BeginnerWizardFactory.cs` (confirm surface 204-219); Test `ReScene.App.Core.Tests/`, `ReScene.Manager.Tests/`.

**Interfaces — Consumes:** an `IFileMover` seam (`Move(src,dst)`, default `File.Move(...,overwrite:false)`) injected for the rollback test.

- [ ] **Step 1 — failing tests:** (a) single set, non-empty key + `Directory="DVD1"` → the kept match's files (`RunResult.Matches[0].Files`) land at `OutputPath\output\<name>` (NOT `output\DVD1\`); scratch removed. (b) two sets sharing `Directory="DVD1"` → both survive under `output\DVD1\`. (c) `Directory="../../x"` → set fails, no delete/move outside `output`. (d) **brute source guard band:** a reported committed path that resolves **outside `<workRoot>\output`** (symlink/`..` escape, a `<workRoot>\input\foo.rar` falsely reported, or a duplicate reported twice) → set fails, nothing moved; only existing, unique, regular files strictly under **`<workRoot>\output`** are relocated. (e) `CompleteAllVolumes=false` (one committed file) and `RenameToReleaseNames=false` (generated names) both relocate correctly because identity comes from the result, not scanning. (f) preflight destination-exists → fail; injected mover failing on move N → rollback moves this set's already-moved files **back** (guarded both directions). (g) cancel mid-set → `<workRoot>` removed via scratch guard; committed set untouched. (h) **single-set custom packer** (`Combo==null`, `CustomPackerFiles` populated, incl. nested `DVD1\x.rar`) → its files (guarded under the **custom work root**) relocate to `output\...` and scratch is cleaned. (i) **multi-set custom packer** → **rejected before the engine is invoked** (planner preflight), reported unsupported. (j) confirm text (VM + wizard) names the `output`+`.rescene-work` subtrees; unrelated `OutputPath` root files survive cleanup. (k) **live-input overlap:** an imported-SRR/verification/**concrete release file**/**selected WinRAR executable** that resolves same-as / under / above `output` or `.rescene-work` → Start rejected; the same paths in the `OutputPath` **root but outside** both subtrees are allowed. (l) empty `Directory` on a multi-set member → final path is `output\<name>` (root sets share the output root, no throw). (m) **distinct roots:** a junction making `output` and `.rescene-work` resolve nested → Start rejected before any mutation (`ResolveReservedRoots` throws).
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement:**
  - `WorkRootFor` → `ReconstructionPathGuard.ResolveScratchChild(OutputPath, set.Key or "release")`.
  - Resolve the reserved-root pair via `ResolveReservedRoots(OutputPath)` (distinct + non-overlapping) **once, before any mutation**; abort the whole run if it throws.
  - **Branch-specific source guard:** brute-force → each path in `Matches[0].Files` must `ResolveReal` to an existing, unique regular file strictly under **`<workRoot>\output`**; custom → each `CustomPackerFiles` path strictly under the **custom work root** (`<workRoot>`). Reject otherwise (set fails, no partial move).
  - **Final path per file** (not a directory resolve): `dest = ResolveOutputChild(OutputPath, rel)` where `rel = LastSegment(name)` for a single set and `rel = Combine(set.Directory, LastSegment(name))` for multi-set (empty `Directory` → `LastSegment(name)`, output root); require the committed set to be **complete** for the mode (count/name check against the set's expected volumes when `RenameToReleaseNames`); `Directory.CreateDirectory(dirOf(dest))`; preflight no-overwrite; `IFileMover.Move` tracking dests; on any failure move the already-moved dests back (both directions guarded), never recursively delete a shared target.
  - **Custom-packer branches:** single-set custom (`Combo==null`, `CustomPackerFiles`) relocates those files with the same guard/completeness/rollback machinery; multi-set custom is **rejected in the planner before invoking the engine** (not merely reported failed after the fact).
  - Cleanup deletes only the guarded `<workRoot>` (scratch descendant); pre-run cleanup clears the guarded `output` + `.rescene-work` subtrees by enumerating children, preserves unrelated `OutputPath` root files, updates both confirm messages, and **rejects Start when any live input** (imported SRR, verification file, concrete release input files, selected WinRAR executable/dir) `Overlaps` `output` or `.rescene-work`.
  - Run loop: per-set `finally` cleans the work-root for any uncommitted set; committed sets untouched.
- [ ] **Step 4 — run PASS; commit** (`fix: relocate committed/custom files with branch-specific source + overlap guards (#3,#1,#4,#5,#17)`).

---

## Phase E — Per-set correctness

### Task 11: App — per-set command/version matrices via compatibility map (#6)

**Policy — metadata replaces switch groups, field by field:** each `SRRArchiveSet` metadata field is independently nullable. A set **replaces** the snapshot's value for a switch group **only when that group's field is present** — compression only when compression is known, dictionary only when the dictionary size is known, solid only when the solid flag is known, and format/`-ma` + version constraint only when `RARVersion` is present. Groups whose field is absent, and switches the metadata never controls (`-r`, `-ds`, timestamps, `-mt`, volume), are left exactly as the snapshot carries them. A set with **no** relevant metadata falls back to the **global matrix** regardless of whether `set.Key` is empty. (This is why Task 7 has no "unselected switch not force-added" test.)

**Files:** `SharedReconstructionSettings.cs` (add `RARSwitchSettings SwitchSnapshot` + `IReadOnlyList<InstalledRARVersion> InstalledVersions`), `ArchiveSetPlanner.cs` (`BuildOptionsForSet` 99-168), `ReconstructorViewModel.cs` (`BuildSharedSettings` capture, per-set build off-thread); Consumes `RarMetadataNormalizer` (T4), `RarFormatCompatibility` (T7), the cancellable builder (T8). Test `ArchiveSetPlannerTests`.

- [ ] **Step 1 — failing tests:** A `{unpack 29, m0, s-}`, B `{unpack 50, m5, s}` within user selection → A's args `-m0/-s-` (RAR4, native on a ≤499 exe, no `-ma`); B's `-m5/-s` **plus a version-bounded `-ma5` (`Min=500,Max=699`, required for RAR5)** when the surviving selection includes any 500-699 exe; **B with only a 700 exe selected → set reported failed** ("no selected WinRAR version can produce RAR5" — 7.x is RAR7-native); B's ranges/folders = `RarFormatCompatibility.SelectFor(Rar5, …)` (`FormatSelection`); an empty-intersection set (RAR5, only 3.90 selected) → **failed**, raised **inside the per-set try** (never aborting sibling sets); the `maxThreads` handed to Task 8's builder is `16` for a RAR4 set and `64` for RAR5/7; **a set with `RARVersion` present but compression/dictionary/solid null → only the format/version groups are replaced, the snapshot's compression/dictionary/solid survive**; a set with **no** relevant metadata (all null) → global matrix, even with a non-empty `set.Key`; **stale-scan capture: `HasScannedVersions==false` with a non-empty `_lastScan` → `SharedReconstructionSettings.InstalledVersions` is empty** (no-scan path, not the stale list).
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement:** carry the switch snapshot + installed versions on `SharedReconstructionSettings`; capture `InstalledVersions = HasScannedVersions ? [.. _lastScan] : []` in `BuildSharedSettings` (mirroring the existing `SelectedVersionFolders = HasScannedVersions ? … : []` at VM ~1688 — a `WinRARPath` change sets `HasScannedVersions=false` but leaves the stale `_lastScan` until the async rescan completes, so a raw `_lastScan` capture would intersect against stale installs). Per set, inside a per-set `try`: **replace each switch group only when its field is present** — compression (normalized via T4), dictionary, solid; and when `RARVersion` is present, derive `RarFormat`, call `SelectFor(fmt, userRanges, userFolders, InstalledVersions)`, fail honestly if `Empty`, else set ranges/folders from the `FormatSelection` and add the version-bounded `-ma4`/`-ma5` when `NeedsMa4`/`NeedsMa5`; pass `maxThreads` (16 for Rar4, 64 else) to Task 8's builder off-thread. No relevant metadata → `shared.CommandLineArguments`/`RARVersions`. `// TODO(-rr)`.
- [ ] **Step 4 — run PASS; commit** (`fix: per-set matrices via format/version compatibility map, field-by-field metadata (#6)`).

### Task 12: App — per-set hash gate, basename, per-set dirs (#8, #10-app, #7-app)

The #9 canonical CRC keying (`BuildExpectedVolumeCrcs` → one key, `Count == VolumeNames.Count`) is implemented **and asserted in Task 5**; Task 12 consumes it and does not repeat that assertion.

**Files:** `ArchiveSetPlanner.cs` (`BuildOptionsForSet` 119-156); Consumes `VerificationSnapshot`/`LastSegment` (T5), `SRRArchiveSet` dirs (T1). Test `ArchiveSetPlannerTests`.

- [ ] **Step 1 — failing tests:** (#8) set B `Hashes` excludes A's first-volume CRC (`snapshot.HashesForVolumes(set.VolumeNames)`); (#10) `DVD1\x.rar` matches flat `x.rar` on any separator via `LastSegment`; identical basenames under `CD1\`/`CD2\` remain **distinct** in `HashesForVolumes` (no basename aliasing across sets); (#7) `ArchiveDirectoryPaths` + all three time maps come from the set (flat set keeps the union).
- [ ] **Step 2 — run FAIL.**
- [ ] **Step 3 — implement:** `HashesForVolumes` (qualified-first, unambiguous-basename fallback); `LastSegment` basename matching; per-set dirs + three time maps from `set` (fallback to `shared` only when `set.Key==""`).
- [ ] **Step 4 — run PASS; commit** (`fix: per-set hash gate, basename, per-set dirs (#8,#10,#7)`).

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

#1 T6+T10 · #2 T6+T9 · #3 T3+T10 · #4 T10 · #5 T3+T10 · #6 T7+T11 · #7 T1+T12+T14 · #8 T5+T12 · #9 T2+T5+T12 · #10 T2+T12 · #11 T4 · #12 T4 · #13 T8 · #14 T5+T10 · #15 T13 · #17 T10 · #18 T16 · #19 T18 · #20 T18 · #21 T15 · #22 T14 · #23 T17 · #24 T18 · #25 T17 · #26 T6+T9. (#16 = false positive, no task.) New enabling work: engine grouped committed matches, first-kept (T3, for #3/#4/#5); format/version compatibility map (T7, for #6). The #9 canonical CRC is keyed+asserted in T5 (app) over the lib fallback from T2.

## Sequencing rationale (no broken intermediate state)

Lib-first with backward-compatible keys and the additive **grouped committed-matches** result (T1–T3, pointer bumped). App infra whose consumers come later: normalization (T4), verification snapshot **which also rewrites the planner's canonical CRC keying (owning the #9 assertion) in the same task** so nothing reads the old values-only member afterward (T5), path guards (T6), format/version map (T7), the cancellable matrix builder (T8) **before** the per-set matrices, the overlap guard (T9) **before** the destructive relocation (T10). The atomic T10 switches the work-root and relocates the **kept match's** committed files in one commit (single-set never strands). Per-set matrices (T11) reuse T4/T7/T8; per-set hash/dirs (T12) reuse T1/T5 and consume T2/T5's keying. Import/config/robustness (T13–T16), progress (T17–T18). Every task ends green.

## Final verification (after all tasks)

- [ ] Clean non-incremental build `-p:BaseOutputPath=bin2/` → 0 warnings / 0 errors; `PublicApi.ReScene.approved.txt` current.
- [ ] Full suites green: `ReScene.App.Core.Tests`, `ReScene.Manager.Tests`, `ReScene.Lib/ReScene.Tests`.
- [ ] Delete `bin2/` (worktree + `E:/Projects/avalonia-agent-mcp`).
- [ ] Manual smoke (WinRAR + a real single-set SRR): verified volumes land in `OutputPath\output\` — the definitive proof of #3.
