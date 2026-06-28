# Multi-Archive-Set RAR Reconstruction (Design)

**Date:** 2026-06-28
**Status:** Draft (pending review)
**Branch:** `feature/multi-archive-set-reconstruction`
**Scope:** `ReScene.Lib` (SRR parser + `Manager` engine) and `ReScene.NET` (Reconstructor
view-model, import model, Brute Force Progress window).

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

### Root causes (file:line evidence)

1. **No per-archive-set modeling.** The SRR format has no grouping construct; every volume of both
   discs is a flat list of type-`0x71` RARFile blocks distinguished only by the directory baked into
   the filename. The parser flattens everything into one `ArchivedFiles`/`ArchivedFileCrcs`, and
   captures compression/version/Host-OS/LARGE metadata **once** from the first header seen in the
   whole SRR (`SRRFileParser.cs:389-404` `if (srr.CompressionMethod == null)`, `:407-408` `??=`).
   `BuildRAROptions` therefore builds **one** `RAROptions` for everything
   (`ReconstructorViewModel.cs:1352-1396`).

2. **One brute-force run over one merged input.** The whole release (both ISOs) is copied into a
   single `input` dir and RAR is invoked once over `.\*`, producing **one** archive of *both* ISOs
   (`InputDirectoryPreparer.cs:87-106`; `Manager.cs:225-233`). The brute-force loop iterates
   *versions × switch combos*, never *archive sets* (`Manager.cs:276-299`). There is no second pass.

3. **Match = one first-volume hash, then positional cross-disc rename.** Success is declared after
   hashing **only the first produced volume** against a flat union of *every* CRC
   (`Manager.cs:635` returns the first volume only; `:660` hashes it; `:668`
   `if (!options.Hashes.Contains(hash)) continue`; `:693-708` returns success immediately). On
   success, `RenameMatchedOutput` maps the single produced set **positionally** onto the full
   cross-disc name list (`Manager.cs:835-849`, `originalNames[i]`). The only CRC validation in the
   pipeline checks *input* files before brute-forcing and merely logs
   (`InputDirectoryPreparer.cs:201-259`); there is **no post-output verification**.

**Why disc B fails entirely:** it is never reconstructed as its own archive; one disc-A-winning combo
is stamped across both discs' names.
**Why `r28` of disc A fails:** only volume 1 is ever verified; every later volume — produced from an
archive of *both* ISOs, not disc A's single ISO — diverges, and `r28` (the partial final volume) is
the most settings-sensitive. It is the same root cause, not a separate defect.

## Goals

- Reconstruct **each archive set independently and correctly**, including multi-disc releases whose
  discs were packed with different RAR settings.
- **Verify every produced volume** (not just the first) before declaring a set reconstructed; surface
  honest pass/fail instead of silently mis-renaming.
- Keep **single-set** reconstruction behavior byte-identical to today.

## Decisions (agreed)

- **Search strategy: seeded with fallback.** Fully brute-force the first set; for each later set, try
  the first set's winning `(version, switches)` **first**, then fall back to a full search if it does
  not reproduce that set.
- **Output layout: preserve subfolders.** Rebuilt volumes are written under the release's structure
  (`output\DVD1\aln-re4a.rar`, `output\DVD2\aln-re4b.rar`).
- **Scope: fix A + B + C** (per-set reconstruction; full-volume CRC verification + CRC-based rename;
  per-set header-driven metadata/version).

## Architecture

The fix splits along the existing lib/app seam: **the lib learns to *group* an SRR into sets and to
*honestly verify* one set; the app learns to *loop* over the sets.**

### Lib change 1 — Per-set model in the SRR parser (fix A & C)

Add a `public sealed class SrrArchiveSet` and an `IReadOnlyList<SrrArchiveSet> ArchiveSets` property
on `SRRFile`. The parser populates it during `Load`: each time it reads a RARFile block
(`SRRFile.cs:505-511`) it computes that volume's **set key** and, for the embedded headers that
immediately follow (`ParseEmbeddedRarHeaders` → `ProcessFileHeader` → `AddArchiveEntry`,
`SRRFile.cs:513-514`), routes the results into the **current set** in addition to the existing flat
union.

**Set key:** the volume's **base name including its directory**, e.g. `DVD1/aln-re4a` — derived by
stripping the RAR volume extension (`.rar`/`.rNN`/`.partNN.rar`) from the RARFile block's filename
using the existing volume-naming helpers (`RARVolumeNaming`/`RARVolumeIdentifier`). This groups
correctly when sets differ by directory (`DVD1\` vs `DVD2\`) **and** when they differ only by base
name in the same directory (`cd1.rar` vs `cd2.rar`).

Each `SrrArchiveSet` carries:

- `Key` (string) and `Directory` (string, may be empty for root-level volumes).
- `VolumeNames` (ordered `IReadOnlyList<string>`, **with** directory prefix, in SRR order).
- `ArchivedFiles` / `ArchivedFileCrcs` (the content this set archives, e.g. `aln-re4a.iso`).
- `ArchivedFileTimestamps`/`CreationTimes`/`AccessTimes` and directory timestamps for this set.
- Per-set header-derived metadata: `CompressionMethod`, `RARVersion`, `DictionarySize`, `HostOS`,
  `FileAttributes`, LARGE flag + `HighPackSize`/`HighUnpSize`, comment fields — captured from
  **this set's** first file header, not a global snapshot.

**Backward compatibility:** the existing flat `SRRFile` properties
(`ArchivedFiles`/`ArchivedFileCrcs`/`CompressionMethod`/`DetectedFileHostOS`/…) are unchanged and
remain the union across all sets. A single-set SRR yields exactly one `SrrArchiveSet` whose data
equals today's flat data; no existing consumer breaks.

### Lib change 2 — Honest per-set verification in `Manager` (fix B)

Keep the cheap volume-1 pre-check that decides whether to complete all volumes (it avoids compressing
a multi-GB ISO for obviously-wrong combos). Change what happens **after** `CompleteAllVolumes`
finishes:

- **Verify every produced volume's CRC** against the set's expected `name → CRC` map; declare success
  only when **all** volumes match.
- **Assign each produced volume to its output name by CRC match**, not by positional index
  (replacing `originalNames[i]` at `Manager.cs:835-849`). Tiebreak by position if two volumes share a
  CRC.
- **Continue-on-near-miss:** if volume 1 matched but a later volume fails verification, this combo is
  a near-miss — clean up its output (honoring `DeleteRARFiles`) and **continue the brute-force loop**
  to the next switch combo, then the next version, until a combo reproduces every volume or the
  search space is exhausted (then the set is reported failed). This is the control flow that is
  missing today.
- **Diagnostics:** log each near-miss explicitly, e.g.
  `version 3.51 / -m1…: volume 1 matched but aln-re4a.r17 CRC mismatch (expected 56bffb4c, got …) — continuing`.

**Pure core for testability:** extract the decision into a pure function
`VolumeMatchEvaluator.Evaluate(producedFiles, expectedNameToCrc) → (AllMatch, NameAssignments, FirstMismatch)`
with no `rar.exe` dependency. `Manager` calls it; the shell-out paths keep their existing integration
coverage.

**Seed support:** `Manager` accepts an optional seed combo `(version, args)` to try **before** the
full loop, and returns the **winning combo** on success (so the VM can seed the next set). Add to
`BruteForceOptions`: `ExpectedVolumeCrcs` (`Dictionary<string,string>` name→CRC), an optional
`SeedCombo`, and a winning-combo value on the result. `Hashes` continues to feed the cheap volume-1
gate and, per set, equals `ExpectedVolumeCrcs.Values`.

### App change — the per-set loop, seeding, and layout

`ReconstructorViewModel.StartAsync` iterates `ArchiveSets`:

1. Build one `BruteForceOptions` per set:
   - **Input** = only that set's content files (`CopySelectedEntries` with the set's
     `ArchivedFiles`), so RAR archives just `aln-re4a.iso`.
   - **`ExpectedVolumeCrcs`** = that set's `name → CRC` map (see Data Flow for the source), with
     `Hashes` = its values.
   - **`OriginalRarFileNames`** = that set's `VolumeNames` **with** the `DVD1\` prefix preserved.
   - **Patching metadata** (Host OS / attributes / LARGE / timestamps / comment) = **that set's**
     header values.
   - **Version range** seeded/derived from that set's header metadata (fix C) within the user's
     selected ranges.
2. Run each set in an **isolated per-set working directory** so candidate scratch
   (`version-args.rar`) and the `input` dir never collide across sequential sets; relocate each set's
   verified volumes into `output\<set-directory>\` preserving structure. (Single-set runs keep
   today's working directory = `OutputPath`, output at `OutputPath\output\`, unchanged.)
3. **Seed across sets:** capture set *n*'s winning combo from the `Manager` result and pass it as set
   *n+1*'s `SeedCombo`.
4. **Partial failure:** attempt all sets regardless of individual failures; keep every successful
   set's output; report per-set pass/fail; the run is fully successful only when all sets pass.

A thin engine seam is introduced if the VM currently constructs `Manager` directly, so the loop is
unit-testable without RAR. Per-set option building is extracted into a pure helper.

## Data Flow

**Import (once, on SRR load):** `SRRFile.Load` → `ArchiveSets`. `SrrImportParser` maps these into the
imported state (alongside today's flat fields), exposing a per-set list to the VM.

**Expected per-volume CRC source (fix B verification):** for each set, in priority order —
1. the SRR's **embedded SFV** stored block whose path matches the set's directory (e.g.
   `DVD1/aln-re4a.sfv`), parsed `name → CRC` — authoritative and already per-set;
2. otherwise the user's verification file (`VerificationPath`) filtered to that set's volume names.

If neither yields the set's volumes, fall back to the cheap volume-1-only behavior for that set and
log a warning that full verification was unavailable.

**Per set:** VM → `BruteForceOptions` (above) → `Manager.BruteForceRARVersionAsync` → per-set input
prep → brute-force loop (seed first) → `VolumeMatchEvaluator` → CRC-based rename into
`output\<dir>\` → winning combo returned → VM seeds next set.

## UI / Progress

- The Brute Force Progress window gains a **"Set X of N: DVD1\aln-re4a"** header; the per-version
  rows reset at each set boundary (each set has its own table); a **final summary** lists each set's
  outcome (✓ / ✗).
- The System log gets `=== Set 1/2: DVD1\aln-re4a ===` banners plus the fix-B near-miss diagnostics.
- On import, when an SRR has more than one set, the form shows an info line via the existing
  `FieldStatus` pattern: *"This release has 2 archive sets (DVD1, DVD2); each is reconstructed
  independently."*
- No new windows; reuses the existing progress/log/status plumbing (including the recently added
  Start/End/Duration columns and live duration).

## Error Handling

- A set whose search is exhausted is logged as failed with its closest near-miss; other sets still
  run.
- Per-set output is kept on success; the run summary makes partial success explicit.
- Existing failure modes (missing input files, CRC validation of inputs, cancellation) are unchanged
  and now scoped per set.

## Testing & Verification

- **Lib parser grouping** (`SRRTestDataBuilder`): a synthetic two-set SRR →
  `ArchiveSets.Count == 2` with correct volumes/content/CRCs/per-set metadata; a single-set SRR → one
  set equal to the flat union (the back-compat guarantee).
- **`VolumeMatchEvaluator`** (pure, no `rar.exe`): all-match → success + correct CRC-based name
  assignment; near-miss (volume 1 matches, `r17` does not) → not a match + reports the mismatch;
  duplicate-CRC tiebreak by position.
- **App loop** (engine seam, no RAR): two sets → correct per-set input/CRCs/prefixed names/metadata;
  seeding threads set 1's winner into set 2; partial failure reports correctly; single set → one run
  with unchanged working directory.
- **Build:** clean non-incremental, **0 warnings / 0 errors**, both test suites green.
- **Manual:** reconstruct `Resident_Evil_4_PAL_MULTI5_NGC-ALiEN`; both discs reconstruct, all 60
  volume CRCs match, output under `output\DVD1\` and `output\DVD2\`.

## Non-Goals

- The **custom-packer direct reconstruction** path (`SRRReconstructor`, used when a custom packer is
  detected) is out of scope; it does not brute-force and is rare. It keeps using the flat model;
  multi-set custom-packer support is a noted follow-up.
- No change to SRS/SRR creation, the Inspector, or other tabs.
- No cross-set parallelism (sets run sequentially; seeding already removes most redundant work).

## File Structure

**Lib (`ReScene.Lib/ReScene/`):**
- `SRR/SrrArchiveSet.cs` — new per-set model.
- `SRR/SRRFile.cs` — `ArchiveSets` property + internal accumulation.
- `SRR/SRRFileParser.cs` — route embedded-header entries/metadata to the current set.
- `Core/VolumeMatchEvaluator.cs` — new pure verify + CRC-based name-assignment helper.
- `Core/Manager.cs` — fix B (verify-all, CRC rename, continue-on-near-miss), seed support, return
  winning combo.
- `Core/BruteForceOptions.cs` — `ExpectedVolumeCrcs`, `SeedCombo`, winning-combo result.

**App (`ReScene.NET/`):**
- `ViewModels/Reconstruction/SrrImportParser.cs`, `ImportedSrrInfo.cs`,
  `ReconstructionImportState.cs` (+ mapper) — carry the per-set list.
- `ViewModels/ReconstructorViewModel.cs` — per-set loop, seeding, partial-failure reporting; engine
  seam.
- A pure per-set option-builder helper under `ViewModels/Reconstruction/`.
- `Views/BruteForceProgressWindow.xaml(.cs)` — set header + final summary.
- Tests under `ReScene.Lib/ReScene.Tests/` and `ReScene.NET.Tests/`.
