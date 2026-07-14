# RAR Reconstruction Subsystem — Correctness Fixes (Design)

**Date:** 2026-07-11
**Status:** Draft (rev. 5) — refined after four codex reviews of the implementation plan; see Revision note below.
**Branch:** `avalonia-feature` (app + nested `ReScene.Lib` submodule)
**Scope:** `ReScene.App.Core` reconstruction view-models/helpers and `ReScene.Lib`
(`Manager` engine, `SRRFile`/`SRRFileParser`, `SRRArchiveSet`) — the RAR Reconstructor only.

## Background

A codex review of the reconstruction subsystem produced 26 findings; an independent
adversarial verification pass (one skeptic per finding, reading the real source) confirmed
**25 real, 1 false positive**, re-rated by actual impact to **4 High / 9 Medium / 12 Low**.
This spec plans fixes for **all 25 confirmed findings**. The lone false positive (import does
not roll back on failure) is intentionally excluded: the corrupt-SRR throw happens before any
state mutation, so the prior import stays complete and consistent.

These are correctness bugs in the reconstruction feature, mostly in `ReScene.App.Core` (shared
logic), a few in `ReScene.Lib`. All work is on the unmerged `avalonia-feature` branch; nothing
is shipped.

### Relationship to the original multi-archive-set design

The reconstruction subsystem was built to `docs/superpowers/specs/2026-06-28-multi-archive-set-reconstruction-design.md`.
Reading that spec sharpens three findings:

- **#3 is a regression against the design.** The design (Goal line 69; Architecture step 2,
  lines 206-208) requires a single-set run to write byte-identically to `OutputPath\output\`.
  The implementation instead keys "single set → `OutputPath`" on **empty `set.Key`**, but a real
  imported single-set SRR always has a **non-empty** key, so it is misrouted into the scratch dir
  and never relocated. Our fix restores the observable guarantee (identical bytes, identical final
  location) via a single uniform path (below).
- **#5 was a documented non-goal.** "Multi-set custom-packer support is a noted follow-up"
  (Non-Goals, lines 301-303). The defect is that this deferred path silently reports **success**
  while stranding output. The proportionate fix advances the follow-up just enough to be honest:
  relocate the custom-packer output layout too, and report failure if nothing was produced —
  never a false success.
- **#6 is an incomplete implementation.** Per-set header metadata (compression, dictionary,
  version, solid, recovery-record) was specified to drive each set's option matrix (lines 202-204,
  "fix C"); the fields were added to `SRRArchiveSet` but the app-side wiring in `BuildOptionsForSet`
  was never finished. Our fix completes it.

### Revision note (rev. 5, post-4×-codex-review) — clauses that supersede the original WS text

Four codex reviews of the implementation plan (grounded in a read of the engine and version-selection
code) refined several decisions. Where the WS sections below differ from these, **these govern**:

1. **Two reserved guarded roots, distinct & fail-closed** supersedes "no delete outside the output
   tree." Every destructive delete/move must target a strict descendant of a *validated* reserved
   root — the **output tree** (`OutputPath\output`) or the **scratch tree** (`OutputPath\.rescene-work`) —
   resolved together and verified (resolving every path component / real links, not lexical
   `Path.GetFullPath`, and not `Directory.Exists`-gated) to resolve under the real `OutputPath` **and**
   to be mutually distinct/non-overlapping (a junction must not collapse them). Fail closed when safety
   is indeterminate. **No live input** (imported SRR, verification file, concrete release input files,
   selected WinRAR executable/dir) may resolve same-as/under/above either reserved subtree — Start is
   rejected (`Overlaps`), because cleanup would otherwise delete a live input.
2. **Relocation moves exactly the engine-reported committed files of the kept match, all-or-none.** The
   engine result gains grouped `BruteForceRunResult.Matches` (`sealed record CommittedMatch(WinningCombo,
   Files)`, one per verified combo, in discovery order) plus `CustomPackerFiles` (a lib change).
   `RenameMatchedOutput` returns `(Placed, Complete)`; a `CommittedMatch` is recorded **only when
   Complete** (all required volumes placed) — an incomplete placement is not a match, so a later
   complete combo can still be the seed (today `MoveMatchedFile` failures are logged while `Found=true`
   is still returned, `Manager.cs:816-821`). Exploratory runs (`StopOnFirstMatch==false`) keep the
   **first** fully-placed combo as the seed (`Matches[0]`/`Combo`), not the last-overwritten one
   (`Manager.cs:340`); since all matches are byte-identical, `Matches[0].Files` are canonical. The VM
   relocates them, guarding brute-force sources strictly under `<workRoot>\output` and custom sources
   strictly under the custom work root, never files found by scanning (which mixes the winner with
   `DeleteRARFiles==false` leftovers and `input\` sources). **Single-set** custom-packer (`Combo==null`,
   `CustomPackerFiles`) is relocated with the same guards; **multi-set** custom-packer is rejected
   **before the engine runs**, never a false success.
3. **Verification snapshot is the *sole* post-cleanup source.** A named `(name→hash)` snapshot is
   parsed once *before* cleanup; all downstream volume-name and CRC lookups read it; the post-cleanup
   re-reads (`ResolveSfvVolumeNames`/`TryLoadUserSfv`) are removed. Only CRC32 snapshots feed
   `ExpectedVolumeCrcs`; SHA1 feeds `options.Hashes` only. Expected CRCs store **one canonical key per
   volume** (qualified-first, basename fallback in `Manager`) — never both aliases (double-counts).
4. **`-mt` preserves `-mt0`** (byte-significant) and is **band-pruned at build time** — clamp both
   endpoints to `0..maxThreads` (`maxThreads` = 16 for RAR4, 64 for RAR5/7) before ordering, so
   invalid rows are never generated. It is **not** version-bounded per arg: `FilterArgumentsForVersion`
   drops an inapplicable arg rather than skipping the row (`RARVersionSelector.cs:117-122`), so a
   bounded `-mt20` on a RAR4 exe would degrade to a duplicate no-`-mt` command. Plus a **checked
   cardinality cap** and cooperative cancellation; the matrix builds off the UI thread, and the global
   (flat) matrix is built **lazily** so a per-set imported run is never aborted by the global cap.
5. **#6 uses an explicit format↔executable-version compatibility map matching the engine's real
   policy** — RAR4 = `<500` native or `500-699`+`-ma4`; RAR5 = `500-699`+**required** `-ma5` (700+
   cannot make RAR5: `-ma5` is bounded `(500,699)` and 700 is RAR7-native); RAR7 = `>=700` native — the
   capable band intersected with the user's selection, empty-intersection reported honestly (verified
   against `RARCommandLineBuilder.cs:100,105`, `RARVersionSelector.ParseRARArchiveVersion` /
   `ShouldSkipRAR6TimestampCombination`). Metadata **replaces only the switch groups whose field is
   present** (compression/dictionary/solid/format each independently nullable); a set with no relevant
   metadata falls back to the global matrix regardless of `set.Key`. The installed-versions capture is
   scan-state-guarded (`HasScannedVersions ? [.. _lastScan] : []`) so a stale scan after a `WinRARPath`
   change is not intersected against.
6. **Config restore uses a *public* lib construction seam** and a versioned/complete DTO (empty dirs
   and null metadata are legitimate — completeness is a schema marker, not "non-empty").

## Goal

Every confirmed finding fixed, root-caused where the findings cluster (the work-root/relocation
architecture), mechanically where they do not; the RAR Reconstructor's common single-set workflow
restored to writing verified output to the user's chosen `OutputPath\output\`.

## Tech Stack

.NET 10 · CommunityToolkit.Mvvm 8.4 (`[ObservableProperty]`/`[RelayCommand]`) · xUnit ·
`ReScene.App.Core` (UI-framework-agnostic) + `ReScene.Lib` submodule.

## Global Constraints

- **Single-set output contract (preserve):** a single-set reconstruction must produce
  byte-identical `.rar` output at `OutputPath\output\<name>` — same bytes, same location — as the
  pre-fix behavior intended by the original design. The uniform relocation path satisfies this via a
  same-volume rename; it is verified by a snapshot/assertion test on the produced options + final
  paths (byte identity of `.rar` is covered by the manual check).
- **Two reserved guarded roots (see Revision note #1):** every destructive delete/move targets a
  strict descendant of a *validated* reserved root — the output tree (`OutputPath\output`) or the
  scratch tree (`OutputPath\.rescene-work`) — resolved via real links (every component), fail-closed
  when indeterminate. Untrusted `set.Directory`/`set.Key` (from SRR volume names) can never widen or
  redirect a delete. Relocation moves only the engine-reported committed files (Revision note #2).
- **Honest reporting:** a run reports success for a set only when that set's own output is verified
  and placed at its final location. Never report success while output is stranded, missing, or
  another set's output was destroyed.
- **TDD:** every fix begins with a failing test reproducing the defect, then the minimal fix.
- **Build gate:** clean non-incremental build, **0 warnings / 0 errors**, both test suites green,
  built with `-p:BaseOutputPath=bin2/` (cleaned after).
- **Lib-first:** `ReScene.Lib` changes (WS3 #7/#9) land and the submodule pointer is bumped before
  the app compiles against them. No flat `SRRFile` field is removed.
- **Deferred:** recovery-record (`-rr`) support is out of scope (no `-rr` switch exists in
  `RARCommandLineBuilder`; it is a new feature). #6 wires the other per-set metadata and leaves a
  documented `// TODO(-rr)` where recovery-record would attach.

---

## WS1 — Work-root / relocation / delete safety (root-cause redesign)

Fixes **#3, #1, #4, #5, #17**. The single largest change; the others are mostly mechanical.

**Current (buggy) shape.** `ArchiveSetPlanner.WorkRootFor` returns `OutputPath` when `set.Key` is
empty else `OutputPath\.rescene-work\<key>`. The engine writes verified volumes to
`<workRoot>\output`. `RelocateVerifiedOutput`/`CleanupWorkRoot` early-return when `setCount <= 1`.
For a real single set (`setCount == 1`, non-empty key) the two predicates disagree: work runs in the
scratch dir but relocation is skipped, so output is stranded in `.rescene-work\<key>\output\` while
the run reports success (#3). The relocation/cleanup deletes are unbounded (`set.Directory` can be
`..\..\x`, #1) and assume a set exclusively owns its target subfolder (two sets sharing a `Directory`
delete each other's verified output, #4). The custom-packer layout writes to the work-root root, so
`RelocateVerifiedOutput` sees no `\output` and returns `true` = success while stranding output (#5).
`OperationCanceledException` is caught-and-rethrown before any cleanup, stranding the in-flight
scratch dir (#17).

**Uniform redesign.** One path for single and multi:

1. **Always scratch.** `WorkRootFor(shared, set)` always returns
   `Path.Combine(OutputPath, ".rescene-work", Sanitize(set.Key or "release"))`. No empty-key special
   case. (Sanitize already strips `/`; extend it to also reject/replace `..` and rooted segments.)
2. **Always relocate.** After a set verifies, move its produced volumes to
   `Path.Combine(OutputPath, "output", set.Directory)` (empty `set.Directory` → `OutputPath\output`).
   Source is `<workRoot>\output` for the brute-force path **or** `<workRoot>` root for the
   custom-packer path (#5) — try both; if neither yields volumes, report the set **failed** (not
   success). Because the scratch dir is under `OutputPath`, the move is a same-volume rename.
3. **Containment guard (helper).** A new pure helper
   `OutputPathGuard.EnsureUnderOutput(outputPath, candidateFinalDir) → string`
   canonicalizes with `Path.GetFullPath` and throws/`false`s if the result is not at or under
   `Path.GetFullPath(Path.Combine(outputPath, "output"))`. Every relocation/cleanup delete calls it
   first (#1).
4. **Safe target clean (#4).** Before writing a set into `output\<dir>`, do not recursively delete a
   possibly-shared `<dir>`. Instead delete only the files this set is about to write (its own volume
   names), or clear `OutputPath\output` **once** at run start and never per-set. Chosen approach:
   clear-once at run start (simplest, deterministic re-runs; a single confirm already gates it), then
   per-set relocation only creates/moves, never recursively deletes a shared folder.
5. **Cleanup on cancel (#17).** Move the per-set cleanup into a `finally`/`try` so both the throw path
   and the normal-return cancel path remove the in-flight `<workRoot>` before propagating.

**Removed:** the `setCount <= 1` early-returns in `RelocateVerifiedOutput`/`CleanupWorkRoot`, and the
key-emptiness branch in `WorkRootFor`.

**Tests (App.Core seam, no rar.exe):** single set → work-root under `.rescene-work`, relocation
lands at `OutputPath\output\<name>`, `.rescene-work` removed; two sets sharing one `Directory` → both
survive (no sibling deletion); a `set.Directory` of `..\..\x` → guard rejects, no delete outside
output; custom-packer layout (volumes at work-root root) → relocated, success; missing output →
failure; cancel mid-set → scratch removed.

---

## WS2 — Import → switch-mapping fidelity

Fixes **#11, #12, #15, #22**. Each makes an imported SRR's real settings survive into the search.

- **#11 — RAR5 compression `0x30–0x35` dropped** (`SRRSwitchMapper.cs:65`). RAR5 archives report the
  compression method as ASCII `0x30–0x35`; the mapper accepts only `0–5`, returns null, and leaves the
  default `-m3`. **Fix:** normalize `if (method >= 0x30) method -= 0x30;` before the `0..5` range
  check. Apply the identical normalization in `SRRImportParser.DescribeCompression`
  (`SRRImportParser.cs:97-107`), which has the same bug. **Test:** map a `0x35` method → `-m5`
  enabled; `DescribeCompression(0x35)` → "Best".
- **#12 — dictionaries 8 MiB–1 GiB map to `None`** (`SRRSwitchMapper.cs:85`). **Fix:** extend
  `DictionarySwitch` + `ApplySwitchDiff` to cover `8192…1048576 KB` (MD8M…MD1G) with the matching
  builder/UI switches (`SwitchMD8M`…`SwitchMD1G` already exist). **Test:** import a 1 GiB dictionary →
  `SwitchMD1G` enabled, `-md1g` emitted.
- **#15 — stale auto-SFV not retired on a no-stored-files import** (`ReconstructorViewModel.cs:2576`).
  The block that clears the previous import's extracted SFV runs *after* the `StoredFiles.Count == 0`
  early return. **Fix:** move the retire block (clear `VerificationPath` when it lives under
  `_sfvTempDir`, `Cleanup(_sfvTempDir)`, null it) to run **before** the early return, or
  unconditionally at method entry. **Test:** import A (embedded SFV) then B (no stored files) →
  `VerificationPath` cleared, `_sfvTempDir` cleaned.
- **#22 — saved config omits `ArchiveSets`** (`ImportedSRRStateMapper.cs:44`). Restoring a multi-set
  config synthesizes one merged archive of unrelated volumes. **Fix:** serialize the per-set list into
  the persisted config DTO and restore it in `ResolveSets`; if the config predates this and lacks
  sets but names a still-present `SRRFilePath`, fall back to re-parsing (existing `ResolveSets` path).
  **Test:** round-trip a two-set config → two sets restored, not one merged set.

---

## WS3 — Per-set correctness (multi-set)

Fixes **#6, #7, #8, #9, #10**. #7 and #9 touch `ReScene.Lib`.

- **#6 — per-set metadata ignored** (`ArchiveSetPlanner.cs:111`). `BuildOptionsForSet` reuses the
  release-global `CommandLineArguments`/`RARVersions` for every set instead of each set's captured
  `CompressionMethod`/`DictionarySize`/`RARVersion`/`IsSolid`. **Fix (App.Core):** build a per-set
  command/version matrix from the set's own metadata (constrain the user-selected switches to the
  set's compression/dict/solid; derive the version range from `set.RARVersion` within the user's
  selected range), falling back to the global matrix when a set has no captured metadata (the flat
  no-SRR set). Recovery-record: leave a `// TODO(-rr)` — deferred per Global Constraints. **Test:**
  two sets with differing compression → each set's options carry its own `-mN`/`-s`/version range.
- **#7 — release-wide directories applied to every set** (`ArchiveSetPlanner.cs:122`) — **lib
  change.** `SRRArchiveSet` has no per-set directory collection, so `BuildOptionsForSet` uses
  `shared.ArchiveDirectories` for all sets, adding foreign directory headers. **Fix (lib):** add
  `ArchivedDirectories` (+ timestamps) to `SRRArchiveSet`; in `SRRFileParser` route directory records
  to `CurrentArchiveSet` (the parser already holds it at the directory-header site) in addition to the
  flat union. **Fix (app):** `BuildOptionsForSet` reads `set.ArchivedDirectories`; the flat no-SRR set
  keeps the union. **Test (lib):** a two-set SRR with differing internal directories → each set carries
  only its own; **(app):** per-set options carry per-set directories.
- **#8 — every release verification hash gates every set** (`ArchiveSetPlanner.cs:153`).
  `BuildOptionsForSet` pools all `shared.VerificationHashes` into every set's first-volume `Hashes`
  gate, so a produced first volume matching *any* set's CRC is accepted. **Fix (App.Core):** filter
  `VerificationHashes` to this set's volume filenames (as `BuildExpectedVolumeCrcs` already does for
  the user SFV) before adding to `Hashes`; do **not** drop it (SHA1 runs have no per-volume CRC map and
  rely on it). **Test:** set B's `Hashes` excludes set A's first-volume CRC.
- **#9 — CRC matching by bare basename ignores directory** (`ArchiveSetPlanner.cs:70`) — **lib change
  on the `Manager` side.** Same-basename sets in different directories collide. **Fix:** key expected
  CRCs by a directory-qualified relative path in `ArchiveSetPlanner.BuildExpectedVolumeCrcs` **and**
  `Manager.BuildExpectedInOrder` (lib) so both agree; map the set's dir-qualified names to the
  basenames the produced volumes carry. **Test (app+lib):** two sets with identical basenames in
  `CD1\`/`CD2\` → each gets its own CRCs.
- **#10 — `Path.GetFileName` on `\`-paths breaks on Linux** (`ArchiveSetPlanner.cs:70, 77`).
  **Fix (App.Core):** normalize `\`→`/` then take the last segment (reuse the lib's convention), not
  platform-native `Path.GetFileName`, on both lines. **Test:** `DVD1\release.rar` → `release.rar` on
  any platform (run the test with both separators).

---

## WS4 — Robustness / validation

Fixes **#13, #21, #14, #18**.

- **#13 — `-mt` matrix built on the UI thread, unclamped, overflow-unsafe**
  (`RARCommandLineBuilder.cs:262`). **Fix (per Revision note #4):** preserve `-mt0` (byte-significant);
  clamp the high end to a **version-aware** valid max (RAR4 ≤16, RAR5+ per its manual) — not a
  universal 1..64; compute matrix cardinality with `checked` arithmetic and reject (typed exception,
  before allocation) when it exceeds a defined cap; check the run `CancellationToken` inside the
  expansion loop; build off the UI thread via `Task.Run`. The existing `0..0` (MT off) single-iteration
  behavior is preserved. **Test:** `SwitchMTEnd = int.MaxValue` → bounded, no OOM/overflow; `-mt0`
  retained; a cap-exceeding matrix → typed exception; cancellation honored.
- **#21 — blank volume size reinterpreted in the selected unit** (`RARCommandLineBuilder.cs:372`).
  Blank size + GB unit yields `-v15000000000` (~15 TB). **Fix:** on invalid/nonpositive input return the
  fixed KB fallback directly (`-v15000k`), not the numeric default reinterpreted through the unit; use
  checked conversion. **Test:** blank size + GB → `-v15000k`.
- **#14 — output cleanup can delete the user's verification `.sfv`** (`ReconstructorViewModel.cs:1429`).
  If `VerificationPath` lives under `OutputPath`, the pre-run cleanup deletes it and the reload silently
  returns empty hashes. **Fix:** snapshot the parsed verification hashes into memory **before** the
  cleanup runs (preferred — robust regardless of path), and additionally extend the overlap guard to
  flag `VerificationPath` at/under `OutputPath`. **Test:** `VerificationPath` under `OutputPath` → hashes
  survive cleanup; run still gates on them.
- **#18 — preflight enumeration outside try/catch** (`ReconstructorViewModel.cs:1302, 1417`). An
  ACL-denied/disconnected directory throws into the global "unexpected error" handler. **Fix:** wrap the
  two `Directory.Enumerate*` preflight calls in `try/catch (IOException or UnauthorizedAccessException)`
  → a clear validation error via the dialog service, mirroring the guards already at 1405/1427.
  **Test:** enumeration throwing `UnauthorizedAccessException` → validation error, `IsRunning` stays
  false, no unhandled exception.

---

## WS5 — Path-guard correctness

Fixes **#2, #26** in `ReconstructorFieldGuidance`.

- **#2 — junction/symlink aliasing bypasses the overlap guard** (`:148`). `PathsOverlap` compares
  lexical `Path.GetFullPath`, so an output junction aliasing the release compares as distinct and the
  cleanup can delete the aliased release files. **Fix:** resolve real link targets
  (`Directory.ResolveLinkTarget(..., returnFinalTarget: true)` / final-path) before comparing; handle
  the nonexistent-path case (fall back to lexical when resolution can't run). **Test:** two lexically
  distinct paths resolving to the same target → overlap detected. (Symlink creation may require a
  platform-guarded test; if unavailable, unit-test the resolve+compare helper directly.)
- **#26 — path overlap always case-insensitive** (`:156`). On case-sensitive filesystems `/data/Release`
  and `/data/release` are wrongly conflated. **Fix:** use `OrdinalIgnoreCase` on Windows and `Ordinal`
  on case-sensitive platforms (select the comparer once from OS/filesystem). **Test:** case-differing
  paths → distinct on Linux, same on Windows.

---

## WS6 — Progress / logging / UX

Fixes **#19, #20, #23, #24, #25**.

- **#19 — timestamp-failure dialog shown per service completion** (`ReconstructorViewModel.cs:2293`).
  **Fix:** accumulate failures; show one summary after `RunArchiveSetsAsync` completes; clear the
  buffer once. **Test:** two sets each with a failure → one dialog after the run.
- **#20 — every log event marshals to the UI and rebuilds the whole log string**
  (`ReconstructorViewModel.cs:2338`). **Fix:** buffer log lines and batch/throttle the UI update
  (e.g. a coalesced dispatcher post + a bounded builder), avoiding the O(n²) whole-string rebuild per
  event. **Test:** N log events → at most K UI updates; log content preserved (bounded).
- **#23 — progress row identity ignores the active set** (`ReconstructionProgressTracker.cs:158`). A
  seeded later set reuses the first set's row and can be marked "Match" because "any set succeeded".
  **Fix:** include the active set in the row key / reset on `SetActiveSet`; finalize each row from its
  own `SetOutcome`, not a global success. **Test:** set 2 fails after set 1 succeeds → set 2's row is
  not "Match".
- **#24 — per-invocation progress shown as overall** (`ReconstructorViewModel.cs:2244`). Progress
  rewinds when the next set/fallback starts. **Fix:** aggregate progress across sets/seed stages with
  explicit weights, or label it clearly as current-set progress. Chosen: label the bar/counters as
  current set ("Set X of N") — cheaper and honest; full weighted aggregation is a noted option.
  **Test:** two sets → progress does not appear to regress within a labeled set.
- **#25 — ETA drifts between progress events** (`ReconstructionProgressTracker.cs:193`). Timer ticks
  reuse a fixed remaining-op count. **Fix:** cache the estimate's wall-clock timestamp / fixed
  completion time and subtract elapsed on each tick. **Test:** advancing the clock without a new
  progress event decreases remaining time.

---

## Lib boundary

Only two findings require `ReScene.Lib` changes:

- **#7:** `SRRArchiveSet` gains `ArchivedDirectories` (+ timestamps); `SRRFileParser` routes directory
  records to the current set. Additive; flat union unchanged; single-set back-compat preserved.
- **#9:** `Manager.BuildExpectedInOrder` and `ArchiveSetPlanner.BuildExpectedVolumeCrcs` agree on a
  directory-qualified CRC key.

Everything else (#3/#1/#4/#5/#17, #11/#12/#15/#22, #6/#8/#10, #13/#21/#14/#18, #2/#26, WS6) is in
`ReScene.App.Core` / the app. The submodule commit lands and the pointer is bumped before the app
builds against the new lib API (Global Constraints, lib-first).

## Testing & Verification

- Per-finding failing-test-first, using the existing `ArchiveSetPlannerTests` / VM headless-test
  patterns (App.Core.Tests) and `SRRTestDataBuilder` (lib tests, RAR4 synthetic).
- WS1 gets the deepest coverage (single-set relocation contract; shared-dir non-deletion; containment
  rejection; custom-packer relocation; cancel cleanup) since it is the root-cause change.
- Single-set snapshot assertion: the option-builder + work-root + final output path for a single set
  match the intended `OutputPath\output\<name>` contract (byte identity of `.rar` remains a manual
  check).
- Build: clean non-incremental, 0 warnings / 0 errors, both suites green (`-p:BaseOutputPath=bin2/`,
  cleaned after).
- Manual smoke (optional, needs WinRAR + a real single-set SRR): reconstruct a single-set release and
  confirm verified volumes land in the chosen `OutputPath\output\` — the definitive proof of #3.

## Sequencing / decomposition

Lib-first, then app; root-cause WS1 before the mechanical batches so tests build on the corrected paths.

1. **Lib:** #7 (`SRRArchiveSet.ArchivedDirectories` + parser routing) and #9 (dir-qualified CRC key in
   `Manager`/planner). Bump the submodule pointer.
2. **App / WS1:** the uniform work-root/relocation redesign + `OutputPathGuard` + safe clean-once +
   cancel cleanup (#3, #1, #4, #5, #17).
3. **App / WS3 remainder:** #6 (per-set matrices, `-rr` TODO), #8 (per-set hash gate), #10 (basename).
4. **App / WS2:** #11, #12, #15, #22.
5. **App / WS4:** #13, #21, #14, #18.
6. **App / WS5:** #2, #26.
7. **App / WS6:** #19, #20, #23, #24, #25.

## Non-Goals

- Recovery-record (`-rr`) support — deferred; #6 leaves a documented TODO where it would attach.
- Full weighted multi-set progress aggregation (#24) — the labeled current-set approach is the chosen
  fix; weighting is a noted option.
- The other five codex review passes (Creators/Editor/Restorer, Inspector/Compare, Core infra,
  Avalonia Views, Avalonia Controls/Services) are separate efforts, pending codex's usage-limit reset.

## File Structure

**Lib (`ReScene.Lib/ReScene/`):**
- `SRR/SRRArchiveSet.cs` — `ArchivedDirectories` (+ timestamps).
- `SRR/SRRFileParser.cs` — route directory records to the current set.
- `Core/Manager.cs` — dir-qualified `BuildExpectedInOrder` key (#9).

**App (`ReScene.App.Core/`):**
- `ViewModels/Reconstruction/ArchiveSetPlanner.cs` — `WorkRootFor` (uniform), `Sanitize` (reject `..`),
  `BuildOptionsForSet` (per-set matrices/dirs/hash filter/basename), `BuildExpectedVolumeCrcs` (#9 key).
- `ViewModels/Reconstruction/OutputPathGuard.cs` — **new** containment helper.
- `ViewModels/Reconstruction/SRRSwitchMapper.cs` — compression `0x30–0x35`, dictionary 8M–1G.
- `ViewModels/Reconstruction/RARCommandLineBuilder.cs` — `-mt` clamp/overflow/off-thread, volume-size
  fallback.
- `ViewModels/Reconstruction/ReconstructionProgressTracker.cs` — set-aware row identity, wall-clock ETA.
- `ViewModels/Reconstruction/ImportedSRRStateMapper.cs` — persist/restore `ArchiveSets`.
- `ViewModels/ReconstructorViewModel.cs` — uniform relocation/cleanup + guard + clear-once + cancel
  cleanup; snapshot verification hashes; preflight try/catch; per-set metadata plumb; retire stale SFV;
  timestamp-failure summary; batched log; current-set progress labeling.
- `ViewModels/Reconstruction/ReconstructorFieldGuidance.cs` — link-resolved + case-correct overlap.
- `ViewModels/Reconstruction/SRRImportParser.cs` — `DescribeCompression` normalization.

**Tests:** `ReScene.App.Core.Tests/` (planner/VM/mapper/guidance) and `ReScene.Lib/ReScene.Tests/`
(parser per-set directories, `Manager` CRC key).
