# Enable-Solid (`-s`) Switch Driven by the SRR (Design)

**Date:** 2026-06-29
**Status:** Draft (pending review)
**Branch:** `feature/solid-reconstruction`
**Scope:** `ReScene.NET` app only — RAR Reconstructor switch model, SRR import mapping, command-line
builder, config round-trip, advanced-tab UI. No `ReScene.Lib` change.

## Background

When reconstructing a release whose archive header has the SOLID flag set, the rebuild comes out
**non-solid**. The brute-force never tries `-s`. Today the only solid-related switch is the
*disable*-solid one:

- `SrrSwitchMapper.Map` (`SrrSwitchMapper.cs:53`): `SwitchSDash = srr.IsSolidArchive.HasValue ?
  !srr.IsSolidArchive.Value : null` — it computes only "should we force non-solid?".
- `RarCommandLineBuilder` (`RarCommandLineBuilder.cs:286-288`): the only solid emission is
  `if (s.SwitchSDash) switches.Add(new("-s-", 201));`.
- `RarSwitchSettings` / the VM (`ReconstructorViewModel.cs:457`) have `SwitchSDash` but **no
  enable-solid toggle**; the advanced UI (`ReconstructorView.xaml:276`) has only
  `"-s-: Disable solid archiving."`.

So: non-solid SRR → `-s-` (correct); **solid SRR → `SwitchSDash=false` → no `-s` and no `-s-` → `rar
a` defaults to non-solid (wrong)**. For multi-file solid releases this changes the packed bytes, so
the release cannot be reconstructed at all (every combo near-misses under the v1.7.0 full-volume CRC
verification). For a single-file archive it flips only the header SOLID bit.

## Goal

Add an enable-solid (`-s`) switch and drive it from the SRR's solid flag, so a solid original is
reconstructed solid.

## Decision (agreed)

Add a `"-s: Solid archiving."` checkbox alongside the existing `"-s-: Disable solid archiving."`,
mutually exclusive. The import sets the correct one from `srr.IsSolidArchive`. Old configs keep
loading (the new field defaults off).

## Architecture

### Switch + mutual exclusion (view-model)

- Add `[ObservableProperty] public partial bool SwitchS { get; set; }` ("enable solid") to
  `ReconstructorViewModel`.
- `-s` and `-s-` are opposites — enforce radio-like exclusion via the generated partial hooks:
  - `partial void OnSwitchSChanged(bool value) { if (value) SwitchSDash = false; }`
  - `partial void OnSwitchSDashChanged(bool value) { if (value) SwitchS = false; }`
  Setting a flag to `false` inside a hook is a no-op for the other hook (it only acts on `true`), so
  there is no re-entrancy loop.
- Add `SwitchS = SwitchS` to `BuildSwitchSettings()` (next to the existing `SwitchSDash` at
  `ReconstructorViewModel.cs:1815`).

### Switch settings + command line

- Add `public bool SwitchS { get; init; }` to `RarSwitchSettings`.
- In `RarCommandLineBuilder.BuildCommandLineArguments`, replace the `-s-` emission so exactly one is
  emitted, `-s` taking precedence:
  ```csharp
  if (s.SwitchS)
  {
      switches.Add(new("-s", 200));
  }
  else if (s.SwitchSDash)
  {
      switches.Add(new("-s-", 201));
  }
  ```
  `-s` (solid) is supported by all RAR4/5 versions, so a low `minimumVersion` (200) keeps it for every
  tested version (`FilterArgumentsForVersion`). The VM exclusion already prevents both being set; the
  `else if` is belt-and-suspenders.

### SRR import drives the switch (the fix)

- `SrrSwitchMapper.SwitchDiff` gains `bool? SwitchS` (alongside the existing `bool? SwitchSDash`).
- `SrrSwitchMapper.Map` sets the pair from `srr.IsSolidArchive`:
  - `true` (solid) → `SwitchS = true`, `SwitchSDash = false`
  - `false` (non-solid) → `SwitchS = false`, `SwitchSDash = true`
  - `null` (unknown) → both `null` (toggles untouched)
- The view-model's apply step (around `ReconstructorViewModel.cs:2177-2180`) applies both: when
  `diff.SwitchS is { } s` → `SwitchS = s`; when `diff.SwitchSDash is { } sd` → `SwitchSDash = sd`. The
  mapper always provides a consistent pair (one true, one false), so the exclusion hooks never
  conflict regardless of apply order.

### Config round-trip

- Add `public bool SwitchS { get; set; }` to `ReconstructorConfig`.
- `ReconstructorConfigMapper`: `Capture` sets `SwitchS = vm.SwitchS` (next to the `SwitchSDash` line);
  `Apply` sets `vm.SwitchS = c.SwitchS`. Old config files lack the field → `System.Text.Json`
  defaults it to `false`, and the existing `SwitchSDash` still loads — backward compatible.

### UI

- In `ReconstructorView.xaml`, add immediately **above** the existing `"-s-"` checkbox
  (`:276`): `<CheckBox Content="-s: Solid archiving." IsChecked="{Binding SwitchS}" Margin="0,1" />`.
- No Beginner-wizard change: the wizard relies on the import-set switches (`SrrSwitchMapper`), so it
  benefits automatically.

## Data Flow

Import SRR → `SrrSwitchMapper.Map(srr)` reads `srr.IsSolidArchive` → `SwitchDiff { SwitchS,
SwitchSDash }` → VM applies both toggles (mutually exclusive) → `BuildSwitchSettings()` snapshots
`SwitchS`/`SwitchSDash` → `RarCommandLineBuilder` emits `-s` (or `-s-`) into every brute-force combo →
the brute-force reproduces the original's solid state.

## Error Handling

None new — pure switch wiring. When the SRR doesn't specify solid (`IsSolidArchive == null`) both
toggles are left as the user set them, exactly as today.

## Testing & Verification

- `SrrSwitchMapper` tests: solid SRR → `SwitchS == true && SwitchSDash == false`; non-solid →
  `SwitchS == false && SwitchSDash == true`; unknown → both null.
- `RarCommandLineBuilder` tests: `SwitchS` → args contain `-s`; `SwitchSDash` → contain `-s-`;
  `SwitchS` set → never `-s-` (precedence); neither → neither.
- VM mutual-exclusion tests: setting `SwitchS = true` clears `SwitchSDash`, and setting `SwitchSDash
  = true` clears `SwitchS`.
- `ReconstructorConfigMapper` test: `SwitchS` round-trips (capture→apply).
- Build: clean non-incremental, **0 warnings / 0 errors**; full `ReScene.NET` suite green.
- Manual: import a solid-release SRR; the advanced tab shows `-s` checked and `-s-` unchecked; the
  System log / brute-force args include `-s`; a non-solid SRR shows `-s-` checked.

## Non-Goals

- No change to the compression version/method search (the separate `File Data` mismatch axis).
- No lib change; no Beginner-wizard UI change; no brute-forcing of both solid states (the SRR is
  authoritative).

## File Structure

- `ReScene.NET/ViewModels/ReconstructorViewModel.cs` — `SwitchS` observable + exclusion hooks +
  `BuildSwitchSettings` + the import-apply line.
- `ReScene.NET/ViewModels/Reconstruction/RarSwitchSettings.cs` — `SwitchS`.
- `ReScene.NET/ViewModels/Reconstruction/RarCommandLineBuilder.cs` — emit `-s`/`-s-`.
- `ReScene.NET/ViewModels/Reconstruction/SrrSwitchMapper.cs` — `SwitchDiff.SwitchS` + `Map`.
- `ReScene.NET/Models/ReconstructorConfig.cs` + `ViewModels/Reconstruction/ReconstructorConfigMapper.cs` — `SwitchS` round-trip.
- `ReScene.NET/Views/ReconstructorView.xaml` — `-s` checkbox.
- `ReScene.NET.Tests/…` — mapper, builder, VM exclusion, config round-trip tests.
