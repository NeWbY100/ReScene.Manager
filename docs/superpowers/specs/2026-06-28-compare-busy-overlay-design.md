# Compare Busy Overlay (Async Load + Compare) (Design)

**Date:** 2026-06-28
**Status:** Draft (pending review)
**Branch:** `feature/show-all-rar-flags` (accumulating Compare/Inspector improvements for v1.7.1)
**Scope:** `ReScene.NET` app only — `FileCompareViewModel` + `FileCompareView.xaml`. No lib change.

## Background

In the Compare tab, loading the second file makes the window briefly freeze. `LoadFile`
(`FileCompareViewModel.cs:343`) runs everything synchronously on the UI thread:
`_compareService.LoadFileData`, `_compareService.ParseDetailedBlocks`, `new MemoryMappedDataSource`,
and `RefreshComparison` → `_compareService.Compare(...)` + `CompareHighlighter.Apply(...)`. Because
it blocks the UI thread, there is no feedback and a spinner could not even animate during it.

The VM already has the infrastructure to do better: an injected `IUiDispatcher` (`_uiDispatcher`) and
an async byte-diff path (`RunDiffAsync` / `IHexDiffComputer.ComputeAsync`) that reports progress via
`StatusMessage`. The structural load+compare is the one piece still synchronous.

## Goal

Run the load+compare off the UI thread and show an **indeterminate inline busy overlay** ("Comparing
files…") while it runs, so the UI stays responsive and the user gets clear feedback.

## Decision (agreed)

Inline overlay (non-modal, over the Compare view), indeterminate. App-only change on the current
branch; ships in v1.7.1.

## Architecture

### Threading split

`LoadFile(bool isLeft, string filePath)` becomes `async Task`; the public `LoadLeftFile` /
`LoadRightFile` follow (`async Task`). The work divides as:

- **Background (`Task.Run`):** `_compareService.LoadFileData(filePath)`,
  `_compareService.ParseDetailedBlocks(filePath)`, `new MemoryMappedDataSource(filePath)`, and (in
  `RefreshComparison`) `_compareService.Compare(...)`. These are pure compute/IO with no UI access.
- **UI thread (after `await`):** assign `pane.Data` / `pane.Blocks` / `pane.Source` / `pane.Path` /
  `pane.FileSize`; populate `LeftTreeRoots` / `RightTreeRoots` (`PopulateTree`); run
  `CompareHighlighter.Apply(...)` (it mutates the bound tree nodes); set `StatusMessage` and the diff
  summary flags.

Continuations naturally resume on the UI thread (the await is from a UI-thread context), so explicit
`_uiDispatcher` marshalling is only needed if a continuation runs off-context; the implementation
uses plain `await` and keeps UI mutations after the awaited `Task.Run`.

`RefreshComparison` becomes `async Task RefreshComparisonAsync`: clear + populate the trees on the UI
thread, then `var result = await Task.Run(() => _compareService.Compare(...))`, then apply the
highlighter + status on the UI thread.

**Unaffected:** the hex data sources (`LeftHexDataSource`/`RightHexDataSource`) are assigned on node
**selection** (`FileCompareViewModel.cs:599-608`), not during load — `LoadFile` only clears them — so
the threading change does not touch hex-view wiring. The byte-diff path (`RunDiffAsync`) is already
async and is unchanged beyond the existing `CancelDiff()` call at load start.

### Busy state + overlay

- Add `[ObservableProperty] public partial bool IsComparing { get; set; }` to `FileCompareViewModel`.
  Set `true` at the start of `LoadFile` and `false` in a `finally`.
- In `FileCompareView.xaml`, add an overlay as the last child of the root layout container (so it
  renders on top): a semi-transparent `Border`/`Grid` with `Visibility` bound to `IsComparing` via
  the app's existing bool→Visibility converter, containing an indeterminate `ProgressBar`
  (`IsIndeterminate="True"`) and a `TextBlock` "Comparing files…". Because it overlays the content, it
  also visually blocks the panes while busy.

### Concurrency

- The Browse / Swap / Close controls bind `IsEnabled` to the negation of `IsComparing` (disabled while
  busy), using the app's existing inverse-bool converter (or an `IsComparing`-false binding).
- `LoadFile` has a re-entrancy guard: if `IsComparing` is already true, it returns early (a stray
  drag-drop or programmatic `LoadLeftFile`/`LoadRightFile` during the brief busy window is ignored
  rather than interleaving). No `CancellationToken`/supersession machinery — the operation is short
  and the disabled controls + guard prevent overlap.
- No user-facing cancel.

### Error handling

The existing `try/catch` in `LoadFile` (reset the pane, clear the hex source, set an error
`StatusMessage`) is preserved, now wrapping the awaited work. A `finally` always clears
`IsComparing`, so the overlay never sticks on after an error.

## Data Flow

Browse command (already async) → `await LoadLeftFile/RightFile(path)` → `LoadFile` sets
`IsComparing=true` → background `Task.Run` parses the pane + (when both panes present) computes the
`CompareResult` → UI thread populates trees, applies highlighting, updates status →
`finally` sets `IsComparing=false` → overlay hides.

## Testing & Verification

- **VM tests** (`ReScene.NET.Tests`, existing synchronous test `IUiDispatcher`): a gated fake
  `IFileCompareService` whose `Compare` (or `LoadFileData`) blocks on a `TaskCompletionSource` —
  start a load (don't await to completion), assert `IsComparing == true`; release the gate, await,
  assert `IsComparing == false`.
- **Behavior preserved:** after an awaited load of both sides, `LeftTreeRoots`/`RightTreeRoots` are
  populated and the compare result/status match the pre-change output (move the relevant existing
  Compare VM test, if any, to `await` the now-async load).
- **Re-entrancy:** calling `LoadLeftFile` while `IsComparing` is true is a no-op (the in-flight load
  wins).
- **Build:** clean non-incremental, **0 warnings / 0 errors**; full `ReScene.NET` suite green.
- **Manual:** load two files in Compare; the overlay ("Comparing files…") shows with an animated
  indeterminate bar while the window stays responsive, and disappears when results render; Browse/Swap/
  Close are disabled during it; an error still clears the overlay and shows the error status.

## Non-Goals

- No determinate percentage (would require instrumenting `IFileCompareService.Compare` with progress
  callbacks — out of scope for a short operation).
- No modal progress window; no user-facing cancel.
- No change to `IFileCompareService` / the comparison logic itself, nor to the lib.

## File Structure

- `ReScene.NET/ViewModels/FileCompareViewModel.cs` — `LoadFile`/`LoadLeftFile`/`LoadRightFile` →
  `async Task`; `RefreshComparison` → `RefreshComparisonAsync` with the `Task.Run` split;
  `IsComparing` observable + re-entrancy guard + `finally`.
- `ReScene.NET/Views/FileCompareView.xaml` — inline busy overlay; `IsEnabled` bindings on
  Browse/Swap/Close.
- `ReScene.NET/Views/FileCompareView.xaml.cs` and any caller of `LoadLeftFile`/`LoadRightFile`
  (drag-drop / MainWindow routing) — adapt to the async signature (await or fire-and-forget).
- `ReScene.NET.Tests/…` — `IsComparing` lifecycle test (gated fake) + preserved-behavior test.
