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

### Latent engine bug (in scope to fix — TWO call sites)

`ParseRARVersion(dirName)` is called *after* the `rar.exe` check with no guard in **two**
places, and a folder containing `rar.exe` but whose name has no parseable version (e.g.
`winrar-beta/`) makes it throw `FormatException`:

1. `GetValidRarDirectories` (`Manager.cs:628`) — throws, propagating out of
   `BruteForceRARVersionAsync` and aborting the run.
2. `CalculateBruteForceProgressSize` (`Manager.cs:381`) — the **same** unguarded call, inside
   a `Parallel.ForEach` (`Manager.cs:372`). This path runs at `Manager.cs:240` **before** the
   main loop, so it throws first, surfacing as an `AggregateException`.

We touch version parsing here, so **both** sites must be hardened, or the "unparseable folders
are skipped, not crashed" contract does not hold. Hardening only `GetValidRarDirectories`
would leave the earlier progress-sizing path still crashing.

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

**Hardening (both call sites).** Replace the unguarded `ParseRARVersion` with
`TryParseRARVersion` in:
- `GetValidRarDirectories` (`Manager.cs:628`): on `false`, log
  (`"Unrecognised WinRAR version folder name: {dir}"`) and `continue`.
- `CalculateBruteForceProgressSize` (`Manager.cs:381`): on `false`, `return;` (the
  `Parallel.ForEach` body's early-out, exactly like the existing `rar.exe`-missing `return`
  just above it).

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
  `string Label` (friendly, `$"{Version / 100}.{Version % 100:D2}"` — e.g. `560` → `5.60`,
  `700` → `7.00`), `string FolderName` (tooltip). A leaf toggle notifies its parent to
  recompute tri-state and syncs the coarse major booleans.
  - **Label caveat:** `TryParseRARVersion` normalises names `< 100` as `×10`, so two- and
    three-digit folder names render correctly (`winrar-56` → `560` → `5.60`; `winrar-624` →
    `6.24`). Only a single-digit name (`winrar-6` → `60` → `0.60`) degrades — such names do
    not occur for real WinRAR (≥ 2.x), so this is accepted. A scanner test pins
    `winrar-56` → label `5.60`.
- `RarVersionGroup` : read-only `int Major`, `string Header` (e.g. `5.x`),
  `ObservableCollection<RarVersionLeaf> Leaves`, computed `bool? IsChecked` (true=all,
  false=none, null=some), computed `string CountText` (`"(2 of 4)"`), and a command/handler
  to set all leaves. Group and leaf changes route through the VM so intent, tri-state, and
  the `Version2..7` booleans stay consistent.
  - **Header click semantics (explicit):** the header's `bool?` is *display-only* — the user
    can never set it to Indeterminate. Clicking a header in **Unchecked or Indeterminate**
    state checks **all** leaves; clicking in **Checked** state unchecks **all** leaves. (This
    overrides WPF's default 3-state click cycle, which would be user-hostile here.) Covered by
    a reconciliation test.

**`ReconstructorViewModel` changes.**
- New `ObservableCollection<RarVersionGroup> VersionGroups`.
- New `RescanVersionsCommand`, `SelectAllVersionsCommand`, `SelectNoVersionsCommand`.
- New backing state: `bool HasScannedVersions` (set true once a scan has completed for a
  folder, regardless of how many leaves resulted) and `List<int>? _pendingVersionSelection`
  (config intent, §Reconciliation).
- `Version2..7` **retained** as *coarse intent* / fallback / config compatibility (§Reconciliation).
- **A single `RescanAndReconcile()` method** owns scan + reconcile. It is invoked from every
  intent-changing event, not just folder changes:
  - `OnWinRarPathChanged` — folder set/changed. This *also* covers the settings-driven default:
    `OnSettingsChanged` → `ApplyPathDefaultsFromSettings` (`ReconstructorViewModel.cs:95`)
    assigns `WinRarPath`, which fires `OnWinRarPathChanged`. (There is **no** `RefreshFromSettings`
    method — the earlier draft named a symbol that does not exist.)
  - **After `SetRARVersionsFromSRR`** in the SRR-import path (`ReconstructorViewModel.cs:837`):
    import mutates `Version2..7` **without** touching `WinRarPath`, so it must call
    `RescanAndReconcile()` (or, if a scan already exists, at least re-run the reconcile step)
    explicitly — otherwise a folder-then-import ordering leaves the tree stale (Decision #2
    silently not applied).
  - Manual `RescanVersionsCommand`.
- **Manual leaf/group edits → major sync is imperative, not hook-driven.** There are **no**
  `OnVersion{2..7}Changed` partial hooks today and we do **not** add any (they would create a
  major→rescan→leaf→major feedback loop). Instead the leaf/group toggle handler directly
  recomputes each group's tri-state and sets `Version2..7 = "any leaf in this major ticked"`.
  A re-entrancy guard (`bool _syncingVersionState`) makes the reconcile/sync writes to leaves
  and booleans no-ops for each other.
- **Scan race:** `RescanAndReconcile` runs `Scan` on `Task.Run` and applies on the UI thread
  under a **latest-wins** guard — an incrementing `int _scanToken` captured before the await;
  results from a superseded token are discarded. Reconcile reads intent (`_pendingVersionSelection`
  / `Version2..7`) at **apply-time on the UI thread**, so it always sees current intent. Mirrors
  the Compare busy-work off-thread pattern.
- `BuildSwitchSettings` (`ReconstructorViewModel.cs:~1780`) is extended so the selected leaf
  set **and** `HasScannedVersions` flow into range building (§Command line).

**`RarSwitchSettings`** gains:
- `IReadOnlyList<int> SelectedRarVersions` — a snapshot of the **currently-ticked leaf
  versions** taken at Start.
- `bool HasScannedVersions` — whether a folder scan has produced the tree.

`SelectedRarVersions` alone is ambiguous: an empty list means *both* "no folder scanned yet"
and "scanned but user unticked everything". `HasScannedVersions` disambiguates so
`BuildVersionRanges` and the Start guard agree. This snapshot is distinct from the *pending
config intent* (below): the intent is a not-yet-applied wish list consumed by the next
reconcile, whereas this is the materialised result of a scan.

**`RarCommandLineBuilder.BuildVersionRanges`** becomes deterministic on `HasScannedVersions`:
- If **`HasScannedVersions` is false** (no scan yet — beginner wizard / pre-folder editing) →
  today's broad ranges from `Version2..7`. This preserves current behaviour when the tree was
  never materialised.
- If **`HasScannedVersions` is true** → one tight `VersionRange(v, v + 1)` per selected leaf
  version (deduplicated, ascending). If the selection is empty here, the result is an empty
  range list (⇒ zero versions) — the "scanned but nothing ticked" case, which the Start guard
  blocks before it can run (§Error handling). No other production caller reaches Start with an
  empty scanned selection; verify no caller of `BuildVersionRanges` bypasses the guard.

**`ReconstructorView.xaml`** replaces the flat checkbox row with a `TreeView`/grouped
`ItemsControl` bound to `VersionGroups`, plus **Rescan**, **Select all**, **Select none**
controls. Placement: the same sub-tab/section the checkboxes occupy today.

## Reconciliation model

A scan materialises the tree from **folder contents × current intent**. Every scan runs the
same reconcile step; only the source of "which leaves to tick" differs, resolved at the
moment each event occurs:

- **Config load** sets a *pending explicit selection* (`_pendingVersionSelection : List<int>?`,
  distinct from the ticked-leaf snapshot) **and then calls `RescanAndReconcile()`**. The
  reconcile ticks exactly the installed leaves whose version is in that list, then clears the
  pending field; missing entries are dropped.
- **SRR import** (and old configs with only major booleans) sets coarse intent via
  `Version2..7` **and then calls `RescanAndReconcile()`** (import mutates no path, so it must
  trigger the reconcile itself). The reconcile ticks **all installed leaves whose major is
  enabled**. This holds for both orderings — folder-then-import (a tree already exists; import's
  reconcile re-ticks it) and import-then-folder (no tree yet; the later `OnWinRarPathChanged`
  reconcile applies the still-current coarse intent).
- **Manual** tree edits are the live selection immediately; the toggle handler imperatively
  syncs `Version2..7` to "any leaf in this major ticked" under `_syncingVersionState`
  (no `OnVersionXChanged` hooks — see Architecture), keeping coarse intent and config coherent.

Reconcile precedence, evaluated at apply-time on the UI thread: if `_pendingVersionSelection`
is non-null, it wins (config path) and is then cleared; otherwise coarse intent
(`Version2..7`) is used. If neither is present (fresh state), nothing is ticked.

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
- **Empty selection at Start** → block with a clear message ("Select at least one WinRAR
  version.") instead of silently brute-forcing nothing. The guard keys off **VM state**, not
  the snapshot's empty list: block only when `VersionGroups` contains **at least one leaf and
  zero leaves are checked**. The "no folder scanned / no leaves" case (`HasScannedVersions ==
  false`) does **not** trip the guard — it uses the broad `Version2..7` fallback, so a wizard
  user who never expands the tree is never blocked.
  This is a deliberate behaviour change from today's silent-range path.

## Testing & Verification

- `WinRarVersionScanner` (temp-dir fixtures): rar.exe filter; unparseable-name skip;
  grouping/sort; empty/missing folder → empty; a folder without rar.exe excluded; `winrar-56`
  → version `560`, label `5.60` (pins the `< 100` normalisation label).
- `Manager.TryParseRARVersion`: valid (`winrar-560` → `560`); normalised (`< 100` → `×10`);
  invalid name → `false`; and `ParseRARVersion` still throws on invalid (unchanged contract).
- **Engine hardening — both sites:** a folder containing `rar.exe` with an unparseable name is
  skipped (no throw) in `GetValidRarDirectories` **and** in `CalculateBruteForceProgressSize`
  (the `Parallel.ForEach` path — assert no `AggregateException`); valid siblings still returned/
  counted.
- Reconciliation: import intent ticks all-in-major; **folder-then-import ordering** re-ticks
  the already-scanned tree; config explicit ticks the subset and drops missing; manual leaf
  toggle syncs the major booleans (and does not recurse, via `_syncingVersionState`); group
  tri-state math (all/none/some), `CountText`, and header-click semantics
  (Unchecked/Indeterminate → all; Checked → none).
- `BuildVersionRanges`: `HasScannedVersions == true` → tight ranges from a selected leaf set
  (dedup/ascending); `HasScannedVersions == false` → broad `Version2..7` fallback; scanned +
  empty selection → empty range list.
- Empty-selection guard: blocks when `VersionGroups` has ≥1 leaf and 0 checked; does **not**
  block when `HasScannedVersions == false`.
- `ReconstructorConfigMapper`: `SelectedRarVersions` round-trip; old config (field absent)
  falls back to enabled-major ticking.
- Build: clean non-incremental, **0 warnings / 0 errors** (`-p:BaseOutputPath=bin2/`); full
  `ReScene.Lib` and `ReScene.NET` suites green.
- Manual: import a solid multi-version release SRR → matched majors' leaves ticked; untick
  some → brute-force log/args show only the chosen versions; drop a new WinRAR version into
  the folder → Rescan surfaces it; empty the folder → hint shown.

## Beginner-wizard behaviour (no UI change; both orderings traced)

The wizard (`ReconstructWizardBody.xaml`) has `ImportSRRCommand` and a WinRAR-path field with
**no enforced order**. Both orderings are safe because import and folder-set each call
`RescanAndReconcile()`:

- **Import-then-path** (common): import sets `Version2..7`, calls reconcile — no folder yet, so
  no leaves tick and `HasScannedVersions` stays false. Later the path is set →
  `OnWinRarPathChanged` → reconcile with the *still-current* coarse intent → ticks all installed
  leaves in the enabled majors. `_pendingVersionSelection` is null throughout (no config load),
  so coarse intent correctly wins.
- **Path-then-import**: path scan ticks per whatever intent exists (default majors); then import
  updates `Version2..7` and re-runs reconcile, re-ticking to match the SRR.
- **Wizard user never expands the tree:** if a folder *was* scanned, leaves are ticked from
  intent, so the Start guard passes and tight ranges are used. If somehow no scan occurred
  (`HasScannedVersions == false`), `BuildVersionRanges` uses the broad `Version2..7` fallback and
  the guard does not fire — identical to today's behaviour. Either way the wizard runs, never
  silently empty, never wrongly blocked.

## Non-Goals

- No change to compression / dictionary / timestamp search axes.
- No auto-guessing the *exact* WinRAR build from the SRR (the SRR does not carry it).
- No recursive folder scanning — immediate subdirectories only, matching the engine.
- No Beginner-wizard UI change (behaviour traced above).

## File Structure

- `ReScene.Lib/ReScene/Core/Manager.cs` — `TryParseRARVersion`; refactor `ParseRARVersion`;
  harden **both** unguarded call sites (`GetValidRarDirectories` and
  `CalculateBruteForceProgressSize`).
- `ReScene.NET/ViewModels/Reconstruction/WinRarVersionScanner.cs` — new scanner +
  `InstalledRarVersion` record.
- `ReScene.NET/ViewModels/Reconstruction/RarVersionLeaf.cs`,
  `.../RarVersionGroup.cs` — new tree node VMs.
- `ReScene.NET/ViewModels/ReconstructorViewModel.cs` — `VersionGroups`, `RescanAndReconcile`
  (with scan-token latest-wins + `_syncingVersionState` guard), `_pendingVersionSelection`,
  `HasScannedVersions`, commands, `BuildSwitchSettings`, empty-selection guard, reconcile call
  after `SetRARVersionsFromSRR`.
- `ReScene.NET/ViewModels/Reconstruction/RarSwitchSettings.cs` — `SelectedRarVersions`,
  `HasScannedVersions`.
- `ReScene.NET/ViewModels/Reconstruction/RarCommandLineBuilder.cs` — `BuildVersionRanges`.
- `ReScene.NET/Models/ReconstructorConfig.cs` +
  `.../Reconstruction/ReconstructorConfigMapper.cs` — `SelectedRarVersions` round-trip.
- `ReScene.NET/Views/ReconstructorView.xaml` — grouped tri-state tree + rescan/select-all.
- `ReScene.Lib/ReScene.Tests/…`, `ReScene.NET.Tests/…` — tests above.
