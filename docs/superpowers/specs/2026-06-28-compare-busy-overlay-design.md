# Compare Busy Overlay (Async Load + Compare) (Design)

**Date:** 2026-06-28
**Status:** Draft — revised after review (pending final approval)
**Branch:** `feature/show-all-rar-flags` (accumulating Compare/Inspector improvements for v1.7.1)
**Scope:** `ReScene.NET` app only — `FileCompareViewModel`, `FileCompareView.xaml(.cs)`. No lib change.

## Background

In the Compare tab, loading the second file makes the window briefly freeze. `LoadFile`
(`FileCompareViewModel.cs:343`) runs everything synchronously on the UI thread:
`_compareService.LoadFileData`, `_compareService.ParseDetailedBlocks`, `new MemoryMappedDataSource`,
and `RefreshComparison` → `_compareService.Compare(...)` + `CompareHighlighter.Apply(...)`. Because it
blocks the UI thread there is no feedback, and a spinner could not even animate during it. The same
freeze happens on **Swap** and **Close** (both call `RefreshComparison`, which compares when both
panes are loaded).

The VM already has the relevant infrastructure: an injected `IUiDispatcher`, and an async byte-diff
path (`RunDiffAsync` / `IHexDiffComputer.ComputeAsync`, already `Task.Run` on the thread pool). The
codebase already uses `await Task.Run(...)` from UI-thread VM methods (e.g. `InspectorViewModel`), and
no `ConfigureAwait(false)` exists anywhere in the app, so awaited continuations resume on the UI
thread.

## Goal

Run the load+compare off the UI thread and show an **indeterminate inline busy overlay** ("Comparing
files…") while it runs, so the UI stays responsive and the user gets clear feedback. Apply the same
to Swap.

## Decision (agreed)

Inline overlay (non-modal, over the Compare view), indeterminate. App-only change on the current
branch; ships in v1.7.1.

## Architecture

### Async surface

These become `async Task`:
- `LoadFile(bool, string)` → `LoadFileAsync(bool, string)`; the public `LoadLeftFile`/`LoadRightFile`
  → `LoadLeftFileAsync`/`LoadRightFileAsync`.
- `RefreshComparison()` → `RefreshComparisonAsync()`.
- `Swap` → `[RelayCommand] async Task SwapAsync` and `ClosePane` → `[RelayCommand] async Task
  ClosePaneAsync` (both currently call `RefreshComparison`).

**Callers adapt:**
- Browse commands (`BrowseLeftAsync`/`BrowseRightAsync`, already `async Task`) `await` the load.
- Drag-drop (`FileCompareView.xaml.cs` `OnPreviewDrop`, a `void` event handler) **fire-and-forgets**:
  `_ = vm.LoadLeftFileAsync(file);` (it cannot `await`). The re-entrancy guard + disabled controls
  cover overlap.
- MainWindow does **not** route to this VM's load methods (only `InspectorViewModel.LoadFile`), so
  there is no other entry point — recorded as an invariant: **every entry point is the UI thread.**

### Threading split (in `LoadFileAsync`)

The order matters. The synchronous teardown stays on the UI thread *before* backgrounding:

1. **UI thread, before `Task.Run`:** `CancelDiff()`; clear `LeftHexDataSource`/`RightHexDataSource`;
   `pane.Source?.Dispose()`; `pane.Source = null`; set `LeftFilePath`/`RightFilePath`, `pane.Path`,
   `pane.FileSize`. (This preserves the existing disposal-race protection — the byte-diff is cancelled
   and the hex bindings cleared before the old source is disposed.)
2. **Background (`Task.Run`):** `var data = _compareService.LoadFileData(filePath); var blocks =
   _compareService.ParseDetailedBlocks(filePath); var source = new MemoryMappedDataSource(filePath);`
   — pure compute/IO, no UI access.
3. **UI thread, after `await`:** assign `pane.Data = data; pane.Blocks = blocks; pane.Source = source;`
   then `await RefreshComparisonAsync()`.

`RefreshComparisonAsync()`:
- Clear + populate `LeftTreeRoots`/`RightTreeRoots` (`PopulateTree`) on the UI thread.
- When both panes loaded: `var result = await Task.Run(() => _compareService.Compare(_left.Data,
  _right.Data, _left.Blocks, _right.Blocks, _left.Source, _right.Source));` (background), then on the
  UI thread set `_compareResult = result`, run `CompareHighlighter.Apply(...)` (it mutates the bound
  tree nodes), and `UpdateStatus()`.
- When fewer than two panes: the cheap `else` branch (no `Task.Run`), as today.

### Busy state, overlay, and re-entrancy

- Add `[ObservableProperty] public partial bool IsComparing { get; set; }` with
  `[NotifyPropertyChangedFor(nameof(IsNotComparing))]`, and `public bool IsNotComparing =>
  !IsComparing`. (`IsNotComparing` drives `IsEnabled` bindings — the app has no plain bool→bool
  inverse converter, only `BooleanToVisibility` and `InverseBoolToVisibilityConverter`, so a VM
  property is cleaner than a new converter.)
- A private wrapper enforces the **guard-before-try** ordering so the `finally` never clears another
  load's flag:
  ```csharp
  private async Task RunBusyAsync(Func<Task> work)
  {
      if (IsComparing)
      {
          return;            // another load owns the overlay; ignore this request
      }

      IsComparing = true;    // set on the UI thread (caller is always UI-thread)
      try
      {
          await work();
      }
      finally
      {
          IsComparing = false;
      }
  }
  ```
  `LoadFileAsync` and `SwapAsync` run their body through `RunBusyAsync` (heavy — both panes can be
  compared). `ClosePaneAsync` reduces the pane count, so its `RefreshComparisonAsync` hits the cheap
  no-compare branch; it `await`s the refresh directly **without** `RunBusyAsync` (closing should not
  show "Comparing…"). Both `Swap`/`Close` are still disabled while another op is busy (see below).
- `IsComparing` is set/cleared only on the UI thread (caller is UI-thread; the `finally` runs after an
  await that resumes on the UI thread) — so `PropertyChanged` fires on the UI thread and the overlay
  binding updates safely.

### Overlay placement (XAML)

`FileCompareView.xaml`'s root is a `DockPanel`; the panes live in a content `Grid` (≈ line 91, three
columns: left / splitter / right) that already hosts the drop-zone overlays as its last children. Add
the busy overlay as the **last child of that content `Grid`** with `Grid.ColumnSpan="3"` (spanning
both panes and the splitter) — a semi-transparent `Border`/`Grid`, `Visibility` bound to `IsComparing`
via the existing `BooleanToVisibility` converter, containing an indeterminate `ProgressBar`
(`IsIndeterminate="True"`) and a `TextBlock` "Comparing files…". (Adding it to the `DockPanel` would
dock rather than overlay — must be the content `Grid`.)

- Browse / Swap / Close controls bind `IsEnabled="{Binding IsNotComparing}"` (disabled while busy).

### Concurrency safety of background `Compare`

`Compare(...)` reads the panes' memory-mapped sources on the background thread:
`FileComparer.Compare` → `BlockDataMatches` → `MemoryMappedDataSource.Read` → `_accessor.ReadArray`.
This is safe and adds a *second* concurrent reader alongside the already-backgrounded byte-diff
(`HexDiffComputer.ComputeAsync`): `MemoryMappedDataSource.Read` is stateless (position is a parameter,
no shared cursor), and concurrent reads of a read-only `MemoryMappedViewAccessor` are thread-safe.
`CompareHighlighter.Apply` (which both re-reads the source via `HasBlockDifferences` and mutates bound
tree nodes) runs on the UI thread, sequenced strictly **after** the awaited `Compare` — never
concurrent with it. The re-entrancy guard prevents a second load from disposing a source mid-compare.

### Error handling

The existing `try/catch` in `LoadFileAsync` (reset the pane, clear the hex source, set an error
`StatusMessage`) is preserved, now wrapping the awaited work. `RunBusyAsync`'s `finally` always clears
`IsComparing`, so the overlay never sticks after an error.

## Data Flow

Browse (UI) → `await LoadLeftFileAsync(path)` → `RunBusyAsync` sets `IsComparing=true` → UI-thread
teardown → background `Task.Run` parse → UI-thread assign + `await RefreshComparisonAsync` (trees on
UI, `Compare` on background, highlight/status on UI) → `finally` clears `IsComparing` → overlay hides.

## Testing & Verification

- **Migrate the existing tests (they break otherwise):** `ReScene.NET.Tests/FileCompareViewModelMkvTests.cs`
  has three tests — `Compare_MetadataDiffers_MarksTreeNodesDifferent`,
  `Compare_OnlyClusterContentDiffers_MarksClusterNodesDifferent`,
  `Compare_IdenticalFiles_ReportsIdentical` — that call `LoadLeftFile`/`LoadRightFile` synchronously
  then assert. Rewrite them to `await vm.LoadLeftFileAsync(...)` / `await vm.LoadRightFileAsync(...)`
  before asserting (the `Task.Run` hop means results aren't ready synchronously). Inject a
  `SynchronousUiDispatcher` (pattern at `ReconstructorViewModelDialogTests.cs:49`).
- **`IsComparing` lifecycle test (deterministic):** a gated fake `IFileCompareService` whose first
  background call (`LoadFileData`) signals entry via a `TaskCompletionSource`/`ManualResetEventSlim`
  **and then blocks** on a second gate. The test starts the load without awaiting, `await`s the entry
  signal, asserts `IsComparing == true` (and `IsNotComparing == false`), releases the gate, awaits the
  load task, asserts `IsComparing == false`. (Blocking the *first* background call avoids racing the
  thread-pool hop.)
- **Re-entrancy:** calling `LoadLeftFileAsync` while `IsComparing` is true is a no-op (returns without
  touching the in-flight load's flag/state).
- **Disposal race (regression guard):** mirror `InspectorViewModelImageTests.LoadFile_SecondFileWhileTextActive_DoesNotFailFromDisposedSource`
  — loading a second file while a diff/selection is active must not throw from a disposed source.
- **Build:** clean non-incremental, **0 warnings / 0 errors**; full `ReScene.NET` suite green.
- **Manual:** load two files; the overlay ("Comparing files…") shows with an animated indeterminate
  bar while the window stays responsive, and disappears when results render; Browse/Swap/Close are
  disabled during it; Swap shows the overlay; an error still clears the overlay and shows the error
  status.

## Non-Goals

- No determinate percentage (would require instrumenting `IFileCompareService.Compare` with progress
  callbacks — out of scope for a short operation).
- No modal progress window; no user-facing cancel.
- No change to `IFileCompareService` / the comparison logic, `MemoryMappedDataSource`, or the lib.

## File Structure

- `ReScene.NET/ViewModels/FileCompareViewModel.cs` — async `LoadFileAsync`/`LoadLeftFileAsync`/
  `LoadRightFileAsync`/`RefreshComparisonAsync`/`SwapAsync`/`ClosePaneAsync`; UI-thread teardown then
  `Task.Run` parse + `Task.Run` `Compare`; `IsComparing` + `IsNotComparing`; `RunBusyAsync` wrapper
  (guard-before-try).
- `ReScene.NET/Views/FileCompareView.xaml` — busy overlay in the content `Grid` (`Grid.ColumnSpan=3`,
  `Visibility` ← `IsComparing`); `IsEnabled="{Binding IsNotComparing}"` on Browse/Swap/Close.
- `ReScene.NET/Views/FileCompareView.xaml.cs` — drag-drop `OnPreviewDrop` fire-and-forgets the async
  load (`_ = vm.LoadLeftFileAsync(file)`).
- `ReScene.NET.Tests/FileCompareViewModelMkvTests.cs` — migrate the three tests to `await` + inject
  `SynchronousUiDispatcher`; add the `IsComparing` lifecycle + re-entrancy + disposal-race tests
  (new test file is fine if cleaner).
