# Multi-Archive-Set RAR Reconstruction (Design)

**Date:** 2026-06-28
**Status:** Draft — revised after two independent reviews (pending final approval)
**Branch:** `feature/multi-archive-set-reconstruction`
**Scope:** `ReScene.Lib` (SRR parser + `Manager` engine + SFV parser) and `ReScene.NET`
(Reconstructor view-model, import model, brute-force service seam, Brute Force Progress window).

## Background

Reconstructing the release `Resident_Evil_4_PAL_MULTI5_NGC-ALiEN` from its SRR fails: the CRC fails
for **all of disc B** and for the **last volume (`r28`) of disc A**. A code investigation confirmed
the user's hypothesis and found three compounding root causes, all in the engine.

The SRR contains **two independent RAR archive sets**, confirmed by inspecting the file:

| Set | Dir | Volumes (ordered) | Archives | Own SFV |
|-----|-----|-------------------|----------|---------|
| `DVD1/aln-re4a` | `DVD1` | `aln-re4a.rar`, `.r00`…`.r28` (30) | `aln-re4a.iso` | `DVD1/aln-re4a.sfv` |
| `DVD2/aln-re4b` | `DVD2` | `aln-re4b.rar`, `.r00`…`.r28` (30) | `aln-re4b.iso` | `DVD2/aln-re4b.sfv` |

Both sets are RAR4, from the same group, with the same 50 MB volume size — so the two discs almost
certainly used **identical** RAR settings. Each set archives a **distinctly named** content file
(`aln-re4a.iso` vs `aln-re4b.iso`).

A second, **directory-less** multi-set release exists in the test corpus and is used as a fixture:
`ReScene.Lib/ReScene.Tests/TestData/cleanup_script/007.A.View.To.A.Kill.1985.UE.iNTERNAL.DVDRip.XviD-iNCiTE.fine_2cd.srr`
— two sets at the **root** (`incite-avtak.ue.xvid.cd1.*` / `…cd2.*`), distinguished only by base name,
each with its own SFV (`…cd1.sfv` / `…cd2.sfv`) and content (`…cd1.avi` / `…cd2.avi`). The design
must group this correctly **without** relying on a directory prefix.

### Root causes (file:line evidence)

1. **No per-archive-set modeling.** The SRR format has no grouping construct; every volume of both
   sets is a flat list of type-`0x71` RARFile blocks distinguished only by the directory and/or base
   name baked into the filename. The parser flattens everything into one
   `ArchivedFiles`/`ArchivedFileCrcs`, and captures compression/version/Host-OS/LARGE metadata
   **once** from the first header seen in the whole SRR (`SRRFileParser.cs:389-404`
   `if (srr.CompressionMethod == null)`, `:407-413` `??=`). `BuildRAROptions` therefore builds
   **one** `RAROptions` for everything (`ReconstructorViewModel.cs:1352-1396`).

2. **One brute-force run over one merged input.** The whole release (both ISOs) is copied into a
   single `input` dir and RAR is invoked once over `.\*`, producing **one** archive of *both* ISOs
   (`InputDirectoryPreparer.cs:87-106`; `Manager.cs:225-233`). The brute-force loop iterates
   *versions × switch combos*, never *archive sets* (`Manager.cs:241-301`). There is no second pass.

3. **Match = one first-volume hash, then positional cross-disc rename.** Success is declared after
   hashing **only the first produced volume** against a flat union of *every* CRC
   (`Manager.cs:635` returns the first volume only; `:660` hashes it; `:668`
   `if (!options.Hashes.Contains(hash)) continue`; `:693-708` returns success immediately). On
   success, `RenameMatchedOutput` maps the single produced set **positionally** onto the full
   cross-disc name list (`Manager.cs:835-849`, `originalNames[i]`). There is **no post-output
   verification**: the only CRC check in the pipeline validates *input* files before brute-forcing —
   `ValidateArchiveFileCrcs` **throws `InvalidDataException` on any missing/mismatched input**
   (`InputDirectoryPreparer.cs:269`), it does not validate produced output.

**Why disc B fails entirely:** it is never reconstructed as its own archive; one disc-A-winning combo
is stamped across both sets' names.
**Why `r28` of disc A fails:** only volume 1 is ever verified; every later volume — produced from an
archive of *both* ISOs, not disc A's single ISO — diverges, and `r28` (the partial final volume) is
the most settings-sensitive. It is the same root cause, not a separate defect.

## Goals

- Reconstruct **each archive set independently and correctly**, including multi-disc releases whose
  discs were packed with different RAR settings.
- **Verify every produced volume** (not just the first) before declaring a set reconstructed; surface
  honest pass/fail instead of silently mis-renaming.
- Keep **single-set** reconstruction byte-identical to today (output bytes and output location).

## Decisions (agreed + resolved in review)

- **Search strategy: seeded with fallback.** Fully brute-force the first set; for each later set, try
  the first set's winning combo **first**, then fall back to a full search if it does not reproduce
  that set.
- **Output layout: preserve subfolders.** Rebuilt volumes are written under the release's structure
  (`<OutputPath>\output\DVD1\aln-re4a.rar`, `…\output\DVD2\…`). A directory-less set writes to
  `<OutputPath>\output\` (i.e. today's location).
- **Scope: fix A + B + C** (per-set reconstruction; full-volume CRC verification with positional
  assignment + per-volume CRC check; per-set header-driven metadata/version).
- **Per-volume CRC source: SFV only.** The type-`0x71` RARFile blocks carry **only the volume name**
  (`SRRFileParser.cs:171-201`); `ArchivedFileCrcs` are the *content* (e.g. ISO) CRCs, **not** the
  volume-file CRCs. The only source of per-volume `.rar`/`.rNN` CRCs is an SFV (embedded in the SRR
  or user-supplied). **If a set has no per-volume CRCs available, it fails honestly** (see Error
  Handling) — there is no silent volume-1-only degrade.

## Architecture

The fix splits along the existing lib/app seam: **the lib learns to *group* an SRR into sets and to
*honestly verify* one set; the app learns to *loop* over the sets.**

### Lib change 1 — Per-set model in the SRR parser (fix A & C)

Add a `public sealed class SrrArchiveSet` and an `IReadOnlyList<SrrArchiveSet> ArchiveSets` property
on `SRRFile`. The parser populates it during `Load`: each time it reads a RARFile block
(`SRRFile.cs:505-511`) it computes that volume's **set key** and, for the embedded headers that
immediately follow (`ParseEmbeddedRarHeaders` → `ProcessFileHeader` → `AddArchiveEntry`,
`SRRFile.cs:513-514`, which runs **synchronously right after** the block is added), routes the
results into the **current set** in addition to the existing flat union.

**Set key** = the volume's directory plus its base name (volume extension stripped):
`Path.GetDirectoryName(volumeName)` + `RARVolumeNaming.GetBaseName(Path.GetFileName(volumeName))`,
e.g. `DVD1/aln-re4a` (RE4) or `incite-avtak.ue.xvid.cd1` (the directory-less 2cd fixture). Because
`RARVolumeNaming` is `internal` and `GetBaseName` takes a **bare filename**, add a small **public**
helper in the lib — `RARVolumeIdentifier.GetArchiveSetKey(string volumePath)` — that performs the
split + base-name strip + recombination, stable across a set's mixed extensions (`.rar`/`.rNN` and
`.partNN.rar`). The parser and any app-side grouping both use this helper. Grouping by
directory **plus** base name handles both the directory-distinguished case (RE4) and the
base-name-only case (the 2cd fixture).

**Content-file → set membership (definition):** a content file belongs to the set under whose RARFile
blocks its header is parsed, in SRR block order. This is exactly the "route the embedded-header
entries to the current set" mechanism above, and is the authoritative rule (the SRR's block ordering
groups each volume series' headers under that series).

Each `SrrArchiveSet` carries:

- `Key` (string) and `Directory` (string, empty for root-level volumes).
- `VolumeNames` (ordered `IReadOnlyList<string>`, **with** directory prefix, in SRR order).
- `ArchivedFiles` / `ArchivedFileCrcs` (the content this set archives, e.g. `aln-re4a.iso`).
- `ArchivedFileTimestamps`/`CreationTimes`/`AccessTimes` and directory timestamps for this set.
- Per-set header-derived metadata, captured from **this set's** first file/archive header (not a
  global snapshot): `CompressionMethod`, `RARVersion`, `DictionarySize`, `HostOS`, `FileAttributes`,
  LARGE flag + `HighPackSize`/`HighUnpSize`, `IsSolidArchive`, `HasRecoveryRecord`, and the comment
  fields.

**Backward compatibility:** the existing flat `SRRFile` properties
(`ArchivedFiles`/`ArchivedFileCrcs`/`CompressionMethod`/`DetectedFileHostOS`/…) are unchanged and
remain the union across all sets. A single-set SRR yields exactly one `SrrArchiveSet` whose data
equals today's flat data; no existing consumer breaks.

### Lib change 2 — Honest per-set verification in `Manager` (fix B)

Keep the cheap volume-1 pre-check that decides whether to complete all volumes (it avoids compressing
a multi-GB ISO for obviously-wrong combos). Change what happens **after** `CompleteAllVolumes`
finishes and the producing process has been `await`ed (`Manager.cs:696-700`) — verification runs
strictly against a **completed** volume set, never an in-progress one:

- **Assign produced volumes to output names positionally** (volume order: `.rar`, `.r00`, …) and
  **verify each position's CRC** against the set's expected `name → CRC` map. Success requires
  **every** position to verify. CRC is the *verification*, not the assignment key (positional
  assignment is correct because RAR emits volumes in deterministic order; using CRC as the sole
  assignment key risks binding a produced volume to the wrong name when CRCs coincide). If positional
  verification fails but a unique CRC-based reassignment exists, that is treated as a failed match
  (no reorder), not a silent fix.
- **Continue-on-near-miss:** if volume 1 matched but a later volume fails verification, this combo is
  a near-miss — clean up its output (honoring `DeleteRARFiles`) and **continue the brute-force loop**
  to the next switch combo, then the next version, until a combo reproduces every volume or the
  search space is exhausted (then the set is reported failed). This is the control flow missing today.
- **Diagnostics:** log each near-miss explicitly, e.g.
  `version 3.51 / -m1…: volume 1 matched but aln-re4a.r17 CRC mismatch (expected 56bffb4c, got …) — continuing`.

**Decoupling from the rename gate.** Today rename-to-original-names is gated on
`RenameToOriginalNames && StopOnFirstMatch` (`Manager.cs:816-818`). Full-volume verification now runs
**unconditionally** when committing a set's output. Renaming the verified volumes to their original
names still honors the user's `RenameToOriginalNames` toggle, but **no longer requires
`StopOnFirstMatch`**.

**Output directory creation.** `MatchedRarWriter.MoveMatchedFile` does a bare `File.Move` with no
destination-directory creation (`MatchedRarWriter.cs:16-30`). The rename step must
`Directory.CreateDirectory(Path.GetDirectoryName(outputPath))` before moving (needed for the new
`output\<dir>\` placement, and harmless otherwise).

**Pure core for testability:** extract the decision into a pure function
`VolumeMatchEvaluator.Evaluate(orderedProducedFiles, orderedExpected) → (AllMatch, PerVolumeResults, FirstMismatch)`
with no `rar.exe` dependency. `Manager` calls it; the shell-out paths keep their existing integration
coverage.

**Seed support and result shape.** `Manager.BruteForceRARVersionAsync` returns a
`BruteForceRunResult { bool Success; WinningCombo? Combo }` instead of `bool`, where
`WinningCombo = (int Version, string[] FilteredArgs, int ArchiveAttrIteration, int NotContentIteration)`
— the **filtered** args (pre-`BuildFinalArguments`), plus the attribute-iteration indices so a seed
run reproduces the exact combination (`-ma4`/`-vn`/`-z` are re-derived by re-running the filtered
args through `BuildFinalArguments`, `Manager.cs:587,724`). It also **accepts** an optional
`SeedCombo` to try **before** the full loop, falling back to the full search on a miss.

### Options & seam changes

- `BruteForceOptions` gains `ExpectedVolumeCrcs` (`Dictionary<string,string>` name→CRC) and
  `SeedCombo` (nullable). `Hashes` stays on `BruteForceOptions` and, per set, equals
  `ExpectedVolumeCrcs.Values` (the cheap volume-1 gate). Per-set rename names and version ranges
  remain on the nested `RAROptions` (consistent with today's placement of `OriginalRarFileNames`).
- The existing seam changes shape: `IBruteForceService.RunAsync` /
  `BruteForceService.RunAsync` (`ReScene.NET/Services/IBruteForceService.cs`,
  `BruteForceService.cs:17`) and `Manager.BruteForceRARVersionAsync` return
  `BruteForceRunResult` instead of `Task<bool>`. This fans out to the VM and its tests in one task.

### App change — the per-set loop, seeding, and layout

`ReconstructorViewModel.StartAsync` iterates `ArchiveSets`. `BuildRAROptions`/`BuildBruteForceOptions`/
`ResolveOutputRenameNames` (today parameterless, reading the flat `_import`) are parameterized by a
per-set state, extracted into a **pure per-set option-builder helper**.

For each set:

1. Build one `BruteForceOptions`:
   - **Input** = only that set's content files (`CopySelectedEntries` with the set's `ArchivedFiles`),
     so RAR archives just `aln-re4a.iso`.
   - **`ExpectedVolumeCrcs`** = that set's `name → CRC` map (see Data Flow), `Hashes` = its values.
   - **`OriginalRarFileNames`** = that set's volume **base filenames** (rename inside Manager uses
     bare names as today; the `DVD1\` subfolder is applied by the VM during relocation, step 3).
   - **Patching/format metadata** = **that set's** header values: Host OS, attributes, LARGE,
     timestamps, comment, **solid**, **recovery record**.
   - **Version range** seeded/derived from that set's header metadata (fix C) within the user's
     selected ranges.
2. **Isolated working directory.** `OutputDirectoryPath` for the run is:
   - single set → `<OutputPath>` (today's behavior exactly: `…\input`, `…\output`, finals at
     `<OutputPath>\output\<name>`); **no relocation, byte-identical**;
   - multi-set → `<OutputPath>\.rescene-work\<sanitizedSetKey>` (cleaned at the start of that set's
     run), so `input`/`output`/`comment.txt`/candidate scratch (`version-args.rar`, `Manager.cs:572`)
     never collide across sequential sets.
3. **Relocate (multi-set only).** On success, move the verified volumes from
   `<workRoot>\output\` into `<OutputPath>\output\<set.Directory>\` (creating the directory; a fast
   same-drive rename since both live under `<OutputPath>`). Clean/fail-fast on a pre-existing
   non-empty `output\<set.Directory>\` so re-runs are deterministic.
4. **Seed across sets:** capture set *n*'s `WinningCombo` and pass it as set *n+1*'s `SeedCombo`.
5. **Partial failure:** each set's run is wrapped so a per-set failure (including a thrown input-CRC
   `InvalidDataException`) marks that set failed and **continues** to the next set; every successful
   set's output is kept; the run is fully successful only when all sets pass.

### Interaction with `StopOnFirstMatch`

`StopOnFirstMatch` and continue-on-near-miss are different axes:

- **Full per-volume verification and the commit-rename always apply** when a set's output is
  committed, regardless of `StopOnFirstMatch`.
- **Continue-on-near-miss is internal and unconditional** — a near-miss is never a committable match.
- **Exploratory mode (`StopOnFirstMatch` unchecked, "test all versions").** The **first
  fully-verified** combo for a set is the one whose output is kept and the one used to seed the next
  set; later fully-verified combos are logged but do **not** re-stamp the kept output.

## Data Flow

**Import (once, on SRR load):** `SRRFile.Load` → `ArchiveSets`. `SrrImportParser` maps these into the
imported state (alongside today's flat fields), exposing a per-set list to the VM.

**Expected per-volume CRC source (per set), in priority order:**
1. The SRR's **embedded SFV** stored block whose set key matches the set's key
   (`GetArchiveSetKey("DVD1/aln-re4a.sfv") == "DVD1/aln-re4a"`), parsed `name → CRC` and matched to
   volumes by bare filename (the SFV lists `aln-re4a.r00`, the set volume is `DVD1\aln-re4a.r00`).
2. Otherwise the user's verification file (`VerificationPath`) filtered to that set's volume names.

If **neither** yields the set's volume CRCs, the set **fails honestly**: it is reported as
"cannot verify — supply its .sfv" and not reconstructed (no volume-1-only degrade). This is reachable
only for SRRs that embed no SFV *and* whose supplied verification file does not cover the set.

**Per set:** VM → `BruteForceOptions` → `Manager.BruteForceRARVersionAsync` (seed first) → per-set
input prep → brute-force loop → `VolumeMatchEvaluator` → positional rename (dir created) into
`<workRoot>\output\` → VM relocates to `<OutputPath>\output\<dir>\` → `WinningCombo` returned → VM
seeds next set.

## UI / Progress

This **extends** the existing Brute Force Progress window rather than reusing it unchanged:

- `BruteForceProgressEventArgs` gains a set dimension (index/total/key). The progress grid (a single
  `ObservableCollection<VersionEntry>`) gains a **`Set` column** and groups by set — lower friction
  than multiple DataGrids and it preserves the recently-added live-duration timer wiring (only the
  in-flight set's current row counts up). `ReconstructionProgressTracker`, which today clears
  `VersionEntries` on phase change, is extended to handle set boundaries.
- The window header shows **"Set X of N: DVD1\aln-re4a"**; a **final summary** lists each set's
  outcome (✓ / ✗ / Cancelled / Not attempted).
- The System log gets `=== Set 1/2: DVD1\aln-re4a ===` banners plus the fix-B near-miss diagnostics.
- On import, when an SRR has more than one set, a new `FieldStatus` info property
  (`ArchiveSetStatus`) is bound in **both** surfaces — the advanced Paths tab **and** the Beginner
  wizard import step — reading e.g. *"This release has 2 archive sets (DVD1, DVD2); each is
  reconstructed independently."*

## Error Handling

- A set whose search is exhausted is logged as failed with its closest near-miss; other sets run.
- A set with no per-volume CRC source fails honestly (above).
- A per-set input-CRC `InvalidDataException` (`InputDirectoryPreparer.cs:269`) is caught by the loop
  and marks that set failed; it does not abort the whole run.
- **Cancellation:** completed-and-verified sets are retained; the in-flight set's working directory
  and any partial `output\<dir>\` are removed; remaining sets are not attempted. The summary shows
  Cancelled (in-flight) and Not-attempted (remaining) distinctly from ✓/✗.
- Per-set output is kept on success; the summary makes partial success explicit.

## Testing & Verification

- **Lib parser grouping** (`SRRTestDataBuilder`, RAR4): a synthetic two-set SRR → `ArchiveSets.Count
  == 2` with correct volumes/content/CRCs/per-set metadata; the directory-less `…fine_2cd.srr`
  fixture → two sets grouped by base name with per-set SFV/content; a single-set SRR → one set whose
  content equals the flat union (the back-compat guarantee). (No RAR5 header builder exists, so
  synthetic multi-set coverage is RAR4-only; the repro and the in-repo fixture are both RAR4.)
- **`VolumeMatchEvaluator`** (pure, no `rar.exe`): all-match → success + correct positional
  assignment; near-miss (volume 1 matches, `r17` does not) → not a match + reports the mismatch; a
  coincident-CRC case does not silently reassign.
- **App loop** (engine seam, no RAR): two sets → correct per-set input/CRCs/names/metadata and
  per-set working dirs/relocation targets; seeding threads set 1's `WinningCombo` into set 2; a failed
  set still lets the next run; **single set → the option-builder produces the same
  `BruteForceOptions`, working dir, and output path it builds today** (snapshot assertion — byte
  identity of `.rar` output is covered only by the manual check, not unit tests).
- **Build:** clean non-incremental, **0 warnings / 0 errors**, both suites green.
- **Manual:** reconstruct `Resident_Evil_4_PAL_MULTI5_NGC-ALiEN`; both discs reconstruct, all 60
  volume CRCs match, output under `output\DVD1\` and `output\DVD2\`.

## Non-Goals

- The **custom-packer direct reconstruction** path (`SRRReconstructor`, `Manager.cs:160-181`) is out
  of scope; it does not brute-force and is rare. It keeps the flat model; multi-set custom-packer
  support is a noted follow-up.
- **Same inner content name across different sets** (e.g. two sets both archiving `data\movie.bin`):
  each set builds its `input` independently in its isolated working dir from its own content list
  resolved against the release directory; if the same relative path belongs to two sets it is assumed
  to be the same source bytes copied into each set's input. Same-name-but-different-content across
  sets is explicitly a non-goal.
- No SRS/SRR-creation, Inspector, or other-tab changes; no cross-set parallelism (sequential + seed).

## Decomposition / ordering

Submodule-first and additive-first:

1. **Lib:** `SrrArchiveSet` + `ArchiveSets` (parser grouping) — additive, fully testable alone.
2. **Lib:** `SFVFile` byte/stream/lines parser (`SFVFile.Parse`/`ReadLines`, tolerant of junk lines;
   today `ReadFile` reads a path via `File.ReadAllLines` and throws on malformed) + `ReadFile`
   delegates to it.
3. **Lib:** `RARVolumeIdentifier.GetArchiveSetKey` public helper.
4. **Lib:** `VolumeMatchEvaluator` (pure).
5. **Lib:** `Manager` verify-all + positional-assign + continue-on-near-miss + dir-create + seed
   support, returning `BruteForceRunResult`; `BruteForceOptions` new fields. (Single-set callers keep
   working — `Success` maps to today's `bool`.)
6. **App:** `IBruteForceService`/`BruteForceService` return-shape change (fans out to VM + tests).
7. **App:** per-set state in `SrrImportParser`/`ImportedSrrInfo`/`ReconstructionImportState` + mapper;
   per-set option-builder helper.
8. **App:** the VM per-set loop, seeding, relocation, partial-failure + cancellation reporting.
9. **App/UI:** progress set-dimension, `Set` column/grouping, summary, `ArchiveSetStatus` info line in
   both surfaces.

The submodule must land and the pointer be bumped before the app compiles against the new lib API. No
flat `SRRFile` field is removed; `BuildRAROptions`'s flat `_import` plumbing is refactored into the
per-set builder rather than deleted.

## File Structure

**Lib (`ReScene.Lib/ReScene/`):**
- `SRR/SrrArchiveSet.cs` — new per-set model.
- `SRR/SRRFile.cs` — `ArchiveSets` property + internal accumulation.
- `SRR/SRRFileParser.cs` — route embedded-header entries/metadata to the current set.
- `SRR/SFVFile.cs` — byte/stream/lines parser overload (tolerant), `ReadFile` delegates.
- `RAR/RARVolumeIdentifier.cs` — `GetArchiveSetKey(string volumePath)`.
- `Core/VolumeMatchEvaluator.cs` — new pure verify + positional-assignment helper.
- `Core/Manager.cs` — fix B (verify-all, positional assign + CRC verify, continue-on-near-miss,
  dir-create), seed support, `BruteForceRunResult`.
- `Core/MatchedRarWriter.cs` — `MoveMatchedFile` (or its caller) creates the destination directory.
- `Core/BruteForceOptions.cs` — `ExpectedVolumeCrcs`, `SeedCombo`; new `BruteForceRunResult`/
  `WinningCombo` types.

**App (`ReScene.NET/`):**
- `Services/IBruteForceService.cs`, `Services/BruteForceService.cs` — return `BruteForceRunResult`.
- `ViewModels/Reconstruction/SrrImportParser.cs`, `ImportedSrrInfo.cs`,
  `ReconstructionImportState.cs` (+ mapper) — carry the per-set list.
- `ViewModels/ReconstructorViewModel.cs` — per-set loop, seeding, relocation, partial-failure +
  cancellation reporting; per-set option-builder helper under `ViewModels/Reconstruction/`.
- `Views/BruteForceProgressWindow.xaml(.cs)` — `Set` column/grouping + final summary.
- `Views/ReconstructorView.xaml` + `Views/Wizards/ReconstructWizardBody.xaml` — `ArchiveSetStatus`
  info line.
- Tests under `ReScene.Lib/ReScene.Tests/` and `ReScene.NET.Tests/`.
