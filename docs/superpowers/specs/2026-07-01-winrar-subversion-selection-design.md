# Per-Sub-Version WinRAR Selection in the RAR Reconstructor (Design)

**Date:** 2026-07-01
**Status:** Draft (pending review)
**Branch:** `feature/winrar-subversion-selection`
**Scope:** `ReScene.NET` app (RAR Reconstructor version-selection UI, SRR-import
reconciliation, command-line range building, config round-trip) **plus** a small
`ReScene.Lib` addition (`Manager.TryParseRARVersion`) and one latent-crash hardening
(`GetValidRarDirectories`).

## Background

Users report they "wish to be able to skip [WinRAR] versions and just select the ones
they think will work." They mean the many **WinRAR application sub-versions** (e.g.
`5.50`, `5.60`, `6.24`), *not* the RAR archive-format version.

Today the RAR Reconstructor exposes only six coarse major-version checkboxes —
`Version2`..`Version7` (`ReconstructorViewModel.cs:390-395`), rendered as a flat
`2.x 3.x 4.x 5.x 6.x 7.x` row in `ReconstructorView.xaml`. These expand into broad
version ranges via `RarCommandLineBuilder.BuildVersionRanges`
(`RarCommandLineBuilder.cs:16-50`): `Version5` → `VersionRange(500, 600)`, etc. The
engine then enumerates the WinRAR versions folder and tests **every** installed
sub-version that falls inside an enabled range. Ticking "5.x" runs all your 5.x
sub-versions — exactly the pain the feedback describes.

### What already works in our favour

The set of installed sub-versions is **fully discoverable** — the engine already does it:

- `Manager.BruteForceRARVersionAsync` calls `Directory.GetDirectories(RARInstallationsDirectoryPath)`
  (`Manager.cs:203`) — immediate subfolders only.
- `Manager.GetValidRarDirectories` (`Manager.cs:614-637`) keeps a folder only if it
  contains `rar.exe` (`Manager.cs:620-625`), parses the folder name to a version via
  `Manager.ParseRARVersion` (`Manager.cs:99`, **public static**; e.g. `winrar-560` → `560`,
  values `< 100` normalised `×10`), and keeps it when
  `RAROptions.RARVersions.Any(r => r.InRange(version))` (`Manager.cs:630`).
- `VersionRange.InRange` is `version >= Start && version < End` (`VersionRange.cs:56`), so a
  single version is representable as a tight range `[v, v+1)`. **No engine change is needed
  to restrict the brute-force to a hand-picked set.**

### Latent engine bug (in scope to fix)

In `GetValidRarDirectories`, `ParseRARVersion(dirName)` is called *after* the `rar.exe`
check with no guard. A folder containing `rar.exe` but whose name has no parseable version
(e.g. `winrar-beta/`) makes `ParseRARVersion` throw `FormatException`, which propagates out
of `BruteForceRARVersionAsync` and aborts the whole run. We touch version parsing here, so
we harden it.

## Goal

Let the user choose **individual installed WinRAR sub-versions** in the RAR Reconstructor,
replacing the six flat major checkboxes with a grouped, tri-state tree of the sub-versions
actually present in the WinRAR versions folder, while preserving today's convenience
(import auto-selection) and config profiles.

## Decisions (agreed during brainstorming)

1. **Unified grouped tree.** Replace the six flat checkboxes with a tree: tri-state major
   headers (`5.x`, `6.x`, …) over leaf rows for each installed sub-version. Collapsed it
   reads like today; expanded the user ticks individual versions.
2. **Import auto-select = all installed sub-versions in matched majors.** Importing an SRR
   that maps to (say) 5.x + 6.x ticks every installed 5.x and 6.x folder — faithful to
   today's behaviour and maximum match probability. The user then unticks what they want to
   skip.
3. **Config stores exact sub-versions, with old-config fallback.** A new
   `SelectedRarVersions` list persists the ticked leaf versions. On load, tick those that
   exist in the current folder and drop the rest. Configs lacking the field (old configs)
   fall back to "tick all installed sub-versions in the enabled majors" using the retained
   `Version2..7` booleans.

## Architecture

### `ReScene.Lib`

**`Manager.TryParseRARVersion(string rarVersionDirectoryName, out int version)` → `bool`**
Non-throwing sibling of `ParseRARVersion`. Returns `false` (and `version = 0`) when the name
has no parseable version or the parsed number is invalid; otherwise `true` with the
normalised version. `ParseRARVersion` is refactored to call it and throw on `false`, so the
two never diverge.

```csharp
public static bool TryParseRARVersion(string rarVersionDirectoryName, out int version)
{
    version = 0;
    Match m = _rarVersionRegex.Match(rarVersionDirectoryName);
    if (!m.Success || !int.TryParse(m.Groups[1].Value, out int n))
    {
        return false;
    }

    version = n < 100 ? n * 10 : n;
    return true;
}
```

**`GetValidRarDirectories` hardening.** Replace the unguarded `ParseRARVersion(dirName)` with
`TryParseRARVersion`; when it returns `false`, log (`"Unrecognised WinRAR version folder name:
{dir}"`) and `continue` instead of throwing.

### `ReScene.NET`

**`WinRarVersionScanner` (new, pure static helper).**
`IReadOnlyList<InstalledRarVersion> Scan(string? folder)` returns the valid installed
versions, applying the **same** rules as the engine so the UI never shows a version the
engine would ignore:

- `null`/empty/non-existent folder → empty list.
- For each immediate subdirectory (`Directory.GetDirectories`, non-recursive): include it
  only if `rar.exe` exists in it **and** `Manager.TryParseRARVersion(name, out version)` is
  `true`.
- Returns records `InstalledRarVersion(int Version, string FolderName, string Path)` sorted
  ascending by `Version`.

`InstalledRarVersion` is a small immutable record. `Scan` performs disk I/O; the VM calls it
off the UI thread (`Task.Run`) and applies results on the UI thread, matching the Compare
busy-work pattern.

**Tree node VM types (new).**
- `RarVersionLeaf` : `[ObservableProperty] bool IsChecked`; read-only `int Version`,
  `string Label` (friendly, derived from the parsed version as `Version / 100` `.`
  `Version % 100` two-digit — e.g. `560` → `5.60`, `700` → `7.00`), `string FolderName`
  (tooltip). A leaf toggle notifies its parent to recompute tri-state and syncs the coarse
  major booleans.
- `RarVersionGroup` : read-only `int Major`, `string Header` (e.g. `5.x`),
  `ObservableCollection<RarVersionLeaf> Leaves`, computed `bool? IsChecked` (true=all,
  false=none, null=some), computed `string CountText` (`"(2 of 4)"`), and a command/handler
  to set all leaves. Group and leaf changes route through the VM so intent, tri-state, and
  the `Version2..7` booleans stay consistent.

**`ReconstructorViewModel` changes.**
- New `ObservableCollection<RarVersionGroup> VersionGroups`.
- New `RescanVersionsCommand`, `SelectAllVersionsCommand`, `SelectNoVersionsCommand`.
- `Version2..7` **retained** as *coarse intent* / fallback / config compatibility (§Reconciliation).
- Scan trigger points: `OnWinRarPathChanged`, `RefreshFromSettings` (settings-driven folder
  change), and manual `RescanVersionsCommand`.
- `BuildSwitchSettings` (`ReconstructorViewModel.cs:~1780`) is extended so the selected leaf
  set flows into range building (§Command line).

**`RarSwitchSettings`** gains `IReadOnlyList<int> SelectedRarVersions` — a snapshot of the
**currently-ticked leaf versions** taken at Start; empty when no folder has been scanned.
This is distinct from the *pending config intent* (below): the intent is a not-yet-applied
wish list consumed by the next reconcile, whereas this is the materialised result of a scan.

**`RarCommandLineBuilder.BuildVersionRanges`** becomes:
- If `SelectedRarVersions` is non-empty → return one tight `VersionRange(v, v + 1)` per
  selected version (deduplicated, ascending).
- Else → today's broad ranges from `Version2..7` (fallback when nothing has been scanned,
  e.g. beginner wizard or pre-folder editing).

**`ReconstructorView.xaml`** replaces the flat checkbox row with a `TreeView`/grouped
`ItemsControl` bound to `VersionGroups`, plus **Rescan**, **Select all**, **Select none**
controls. Placement: the same sub-tab/section the checkboxes occupy today.

## Reconciliation model

A scan materialises the tree from **folder contents × current intent**. Every scan runs the
same reconcile step; only the source of "which leaves to tick" differs, resolved at the
moment each event occurs:

- **Config load** sets a *pending explicit selection* — a VM field distinct from the
  ticked-leaf snapshot (e.g. `_pendingVersionSelection : List<int>?`). The next scan ticks
  exactly the installed leaves whose version is in that list, then clears the pending field;
  missing entries are dropped.
- **SRR import** (and old configs with only major booleans) sets coarse intent via
  `Version2..7`. Next scan ticks **all installed leaves whose major is enabled**.
- **Manual** tree edits are the live selection immediately; after each edit the VM syncs
  `Version2..7` to "any leaf in this major ticked", keeping coarse intent and config coherent.

Import/config may set intent *before* a folder is known; the scan is where intent becomes
concrete ticks. If a scan occurs with no pending explicit selection and no enabled major
(fresh state), nothing is ticked.

## Data flow

```
WinRAR folder set/changed (or Rescan)
  -> WinRarVersionScanner.Scan(folder)            [off UI thread]
  -> reconcile installed versions with current intent
  -> populate VersionGroups (ticks + tri-state)   [UI thread]

Start
  -> BuildSwitchSettings snapshots SelectedRarVersions (+ Version2..7)
  -> BuildVersionRanges -> tight ranges per ticked leaf
  -> BruteForceOptions.RAROptions.RARVersions
  -> engine enumerates folder, keeps in-range versions (unchanged path)
```

The engine re-derives progress size from the same ranges
(`CalculateBruteForceProgressSize`), so progress scales correctly with no change there.

## Config round-trip

- `ReconstructorConfig` gains `public List<int>? SelectedRarVersions { get; set; }`
  (nullable / absent-tolerant).
- `ReconstructorConfigMapper.Capture` writes the currently-ticked leaf versions.
- `ReconstructorConfigMapper.Apply` sets the config's `SelectedRarVersions` as the VM's
  pending explicit selection (`_pendingVersionSelection`) and triggers a scan/reconcile.
  `Version2..7` are still captured/applied for the
  no-folder fallback and old-config compatibility (absent `SelectedRarVersions` → fall back
  to "tick all installed in enabled majors").

## Error handling / edge cases

- **No folder / invalid folder** → empty tree + hint "Select a WinRAR versions folder to
  choose versions." Start already validates `Directory.Exists`, so this never blocks a real
  run.
- **Folder present but no valid versions** (no `rar.exe`, or all names unparseable) → empty
  tree + hint mirroring the engine's existing "no RAR executables found" warning.
- **Unparseable folder names** → skipped by the scanner and (now) skipped, not crashed, by
  the engine.
- **Stale config picks** (version no longer installed) → silently dropped on load.
- **Empty selection at Start** (folder present, everything unticked) → block with a clear
  message ("Select at least one WinRAR version.") instead of silently brute-forcing nothing.
  This is a deliberate behaviour change from today's silent-range path.

## Testing & Verification

- `WinRarVersionScanner` (temp-dir fixtures): rar.exe filter; unparseable-name skip;
  grouping/sort; empty/missing folder → empty; a folder without rar.exe excluded.
- `Manager.TryParseRARVersion`: valid (`winrar-560` → `560`); normalised (`< 100` → `×10`);
  invalid name → `false`; and `ParseRARVersion` still throws on invalid (unchanged contract).
- `GetValidRarDirectories` hardening: a folder containing `rar.exe` with an unparseable name
  is skipped (no throw), valid siblings still returned.
- Reconciliation: import intent ticks all-in-major; config explicit ticks the subset and
  drops missing; manual leaf toggle syncs the major booleans; group tri-state math
  (all/none/some) and `CountText`.
- `BuildVersionRanges`: tight ranges from a selected leaf set (dedup/ascending);
  broad-range fallback when `SelectedRarVersions` is empty.
- `ReconstructorConfigMapper`: `SelectedRarVersions` round-trip; old config (field absent)
  falls back to enabled-major ticking.
- Build: clean non-incremental, **0 warnings / 0 errors** (`-p:BaseOutputPath=bin2/`); full
  `ReScene.Lib` and `ReScene.NET` suites green.
- Manual: import a solid multi-version release SRR → matched majors' leaves ticked; untick
  some → brute-force log/args show only the chosen versions; drop a new WinRAR version into
  the folder → Rescan surfaces it; empty the folder → hint shown.

## Non-Goals

- No change to compression / dictionary / timestamp search axes.
- No auto-guessing the *exact* WinRAR build from the SRR (the SRR does not carry it).
- No recursive folder scanning — immediate subdirectories only, matching the engine.
- No Beginner-wizard UI change; it relies on import auto-select, which now flows through the
  same reconcile step.

## File Structure

- `ReScene.Lib/ReScene/Core/Manager.cs` — `TryParseRARVersion`; refactor `ParseRARVersion`;
  harden `GetValidRarDirectories`.
- `ReScene.NET/ViewModels/Reconstruction/WinRarVersionScanner.cs` — new scanner +
  `InstalledRarVersion` record.
- `ReScene.NET/ViewModels/Reconstruction/RarVersionLeaf.cs`,
  `.../RarVersionGroup.cs` — new tree node VMs.
- `ReScene.NET/ViewModels/ReconstructorViewModel.cs` — `VersionGroups`, scan/reconcile,
  commands, `BuildSwitchSettings`, empty-selection guard.
- `ReScene.NET/ViewModels/Reconstruction/RarSwitchSettings.cs` — `SelectedRarVersions`.
- `ReScene.NET/ViewModels/Reconstruction/RarCommandLineBuilder.cs` — `BuildVersionRanges`.
- `ReScene.NET/Models/ReconstructorConfig.cs` +
  `.../Reconstruction/ReconstructorConfigMapper.cs` — `SelectedRarVersions` round-trip.
- `ReScene.NET/Views/ReconstructorView.xaml` — grouped tri-state tree + rescan/select-all.
- `ReScene.Lib/ReScene.Tests/…`, `ReScene.NET.Tests/…` — tests above.
