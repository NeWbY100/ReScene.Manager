# Compare Busy Overlay Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Run the Compare tab's load+compare off the UI thread and show an indeterminate inline "Comparing files…" overlay while it runs, so the window stays responsive.

**Architecture:** `FileCompareViewModel`'s load/compare/swap/close become `async Task`; the parse and `Compare(...)` run in `Task.Run` while tree population, highlighting, and status updates stay on the UI thread after the `await`. An `IsComparing` flag (via a `RunBusyAsync` guard-before-try wrapper) drives an inline overlay in the view; `IsNotComparing` disables Browse/Swap/Close while busy.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), xUnit.

**Spec:** `docs/superpowers/specs/2026-06-28-compare-busy-overlay-design.md`

## Global Constraints

- **App only** (`ReScene.NET`), branch `feature/show-all-rar-flags`. No `ReScene.Lib` change.
- **Build/test only with `-p:BaseOutputPath=bin2/`** (the running app locks `bin/`). NEVER kill the app.
- **Verify non-incrementally:** `dotnet build … --no-incremental` → **0 warnings, 0 errors** (strict analyzers).
- After verifying, delete scratch: `find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null`.
- **Indeterminate only**; no modal window; no user-facing cancel.
- `[RelayCommand]` strips the `Async` suffix, so `SwapAsync`→`SwapCommand`, `CloseLeftAsync`→`CloseLeftCommand`, etc. — existing XAML command bindings stay valid.
- **End the commit message** with `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## Task 1: Async load/compare + busy state (view-model)

**Files:**
- Modify: `ReScene.NET/ViewModels/FileCompareViewModel.cs`
- Modify: `ReScene.NET/Views/FileCompareView.xaml.cs` (drag-drop caller adaptation — required to keep the build green)
- Test: `ReScene.NET.Tests/FileCompareViewModelMkvTests.cs` (migrate 3 tests; add lifecycle + re-entrancy tests)

**Interfaces:**
- Produces: `Task LoadLeftFileAsync(string)`, `Task LoadRightFileAsync(string)`, `bool IsComparing`, `bool IsNotComparing`. Removes the synchronous `LoadLeftFile`/`LoadRightFile`/`LoadFile`/`Swap`/`ClosePane`/`CloseLeft`/`CloseRight`.

- [ ] **Step 1: Migrate the existing tests to async (RED)**

In `ReScene.NET.Tests/FileCompareViewModelMkvTests.cs`, make the three test methods `async Task` and `await` the loads. Replace each `vm.LoadLeftFile(left); vm.LoadRightFile(right);` pair with:

```csharp
        await vm.LoadLeftFileAsync(left);
        await vm.LoadRightFileAsync(right);
```

So the three signatures become:
```csharp
    [Fact]
    public async Task Compare_MetadataDiffers_MarksTreeNodesDifferent()
    [Fact]
    public async Task Compare_OnlyClusterContentDiffers_MarksClusterNodesDifferent()
    [Fact]
    public async Task Compare_IdenticalFiles_ReportsIdentical()
```
(Body unchanged except the two load calls.) Then add the lifecycle + re-entrancy tests and a gated fake service to the same class:

```csharp
    private sealed class GatedCompareService : IFileCompareService
    {
        private readonly ManualResetEventSlim _release = new(false);
        public ManualResetEventSlim Entered { get; } = new(false);

        public void Release() => _release.Set();

        public object? LoadFileData(string filePath)
        {
            Entered.Set();
            _release.Wait();
            return null; // data unused by the IsComparing lifecycle; null avoids PopulateTree on an unknown type
        }

        public IReadOnlyList<RARDetailedBlock> ParseDetailedBlocks(string filePath) => [];

        public CompareResult Compare(object? left, object? right,
            IReadOnlyList<RARDetailedBlock> leftBlocks, IReadOnlyList<RARDetailedBlock> rightBlocks,
            IHexDataSource? leftSource, IHexDataSource? rightSource) => new();
    }

    [Fact]
    public async Task IsComparing_TrueDuringLoad_FalseAfter()
    {
        string path = WriteMkv("one.mkv", BuildMkv("libebml", 0xAA));
        var gated = new GatedCompareService();
        using var vm = new FileCompareViewModel(gated, new NoOpFileDialogService(), new StubHexDiffComputer());

        Task load = vm.LoadLeftFileAsync(path); // runs synchronously up to the Task.Run await

        Assert.True(gated.Entered.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(vm.IsComparing);            // set synchronously before the first await
        Assert.False(vm.IsNotComparing);

        gated.Release();
        await load;

        Assert.False(vm.IsComparing);
        Assert.True(vm.IsNotComparing);
    }

    [Fact]
    public async Task LoadWhileComparing_IsIgnored()
    {
        string path = WriteMkv("one.mkv", BuildMkv("libebml", 0xAA));
        var gated = new GatedCompareService();
        using var vm = new FileCompareViewModel(gated, new NoOpFileDialogService(), new StubHexDiffComputer());

        Task first = vm.LoadLeftFileAsync(path);
        Assert.True(gated.Entered.Wait(TimeSpan.FromSeconds(5)));

        await vm.LoadRightFileAsync(path); // re-entrancy guard: returns immediately, no-op
        Assert.True(vm.IsComparing);       // the first load still owns the flag
        Assert.Equal(string.Empty, vm.RightFilePath); // the ignored load did not set state

        gated.Release();
        await first;
        Assert.False(vm.IsComparing);
    }
```

Add the needed usings to the test file: `using System.Collections.ObjectModel;` is already present; add `using ReScene.Core.Comparison;` (for `CompareResult`/`IFileCompareService`), `using ReScene.RAR;` (for `RARDetailedBlock`). Confirm `IFileCompareService`'s exact member signatures by reading `ReScene.NET/Services/IFileCompareService.cs` (or `ReScene.Core` equivalent) and match the `GatedCompareService` overrides to them verbatim (parameter types/order).

- [ ] **Step 2: Run the tests to confirm RED**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj \
  --filter "FullyQualifiedName~FileCompareViewModelMkvTests" -p:BaseOutputPath=bin2/
```
Expected: **build error** — `LoadLeftFileAsync`/`LoadRightFileAsync`/`IsComparing`/`IsNotComparing` don't exist yet (CS1061/CS0117).

- [ ] **Step 3: Add `IsComparing`/`IsNotComparing` + `RunBusyAsync`**

In `FileCompareViewModel.cs`, add near the other `[ObservableProperty]` declarations (e.g. after `FilesIdentical`, ~line 244):

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotComparing))]
    public partial bool IsComparing { get; set; }

    /// <summary>Inverse of <see cref="IsComparing"/>, for IsEnabled bindings (no inverse-bool converter exists).</summary>
    public bool IsNotComparing => !IsComparing;
```

Add the wrapper (guard BEFORE the try so the `finally` never clears another load's flag):

```csharp
    /// <summary>
    /// Runs <paramref name="work"/> with the busy overlay shown. Re-entrant calls are ignored (the
    /// in-flight operation keeps ownership of <see cref="IsComparing"/>); the guard precedes the
    /// try/finally so a later call can never clear the flag the earlier one owns.
    /// </summary>
    private async Task RunBusyAsync(Func<Task> work)
    {
        if (IsComparing)
        {
            return;
        }

        IsComparing = true;
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

- [ ] **Step 4: Convert load to async with the threading split**

Replace the public `LoadLeftFile`/`LoadRightFile` (lines ~333/341) and the private `LoadFile` (lines ~343-405) with:

```csharp
    /// <summary>Loads and parses a file into the left comparison pane (off the UI thread).</summary>
    public Task LoadLeftFileAsync(string filePath) => LoadFileAsync(true, filePath);

    /// <summary>Loads and parses a file into the right comparison pane (off the UI thread).</summary>
    public Task LoadRightFileAsync(string filePath) => LoadFileAsync(false, filePath);

    private Task LoadFileAsync(bool isLeft, string filePath) => RunBusyAsync(async () =>
    {
        ComparePane pane = Pane(isLeft);
        try
        {
            // UI-thread teardown BEFORE backgrounding: cancel the byte-diff and clear/dispose the
            // old source so a pending render can't touch a disposed mapping.
            CancelDiff();
            LeftDiffRanges = null;
            RightDiffRanges = null;
            if (isLeft)
            {
                LeftHexDataSource = null;
            }
            else
            {
                RightHexDataSource = null;
            }

            pane.Source?.Dispose();
            pane.Source = null;

            if (isLeft)
            {
                LeftFilePath = filePath;
            }
            else
            {
                RightFilePath = filePath;
            }

            pane.Path = filePath;
            pane.FileSize = new FileInfo(filePath).Length;

            // Background: parse + memory-map (no UI access).
            var parsed = await Task.Run(() => (
                data: _compareService.LoadFileData(filePath),
                blocks: _compareService.ParseDetailedBlocks(filePath),
                source: new MemoryMappedDataSource(filePath)));

            // UI thread (post-await): assign + refresh.
            pane.Data = parsed.data;
            pane.Blocks = parsed.blocks;
            pane.Source = parsed.source;
            await RefreshComparisonAsync();
        }
        catch (Exception ex)
        {
            if (isLeft)
            {
                LeftFilePath = string.Empty;
            }
            else
            {
                RightFilePath = string.Empty;
            }

            pane.DisposeAndReset();

            if (isLeft)
            {
                LeftHexDataSource = null;
            }
            else
            {
                RightHexDataSource = null;
            }

            StatusMessage = $"Error loading {(isLeft ? "left" : "right")} file: {ex.Message}";
        }
    });
```

(Match the tuple member types to `ComparePane`'s `Data`/`Blocks`/`Source` property types — read `ComparePane` if the inferred tuple types don't assign cleanly; the names above mirror the current synchronous assignments.)

- [ ] **Step 5: Convert `RefreshComparison` to async with the `Task.Run` compare**

Replace `RefreshComparison()` (lines ~411-445) with:

```csharp
    private async Task RefreshComparisonAsync()
    {
        LeftTreeRoots.Clear();
        RightTreeRoots.Clear();
        LeftProperties.Clear();
        RightProperties.Clear();

        if (_left.Data is not null)
        {
            PopulateTree(LeftTreeRoots, _left.Data, true);
        }

        if (_right.Data is not null)
        {
            PopulateTree(RightTreeRoots, _right.Data, false);
        }

        if (_left.Data is not null && _right.Data is not null)
        {
            _compareResult = await Task.Run(() => _compareService.Compare(_left.Data, _right.Data,
                _left.Blocks, _right.Blocks,
                _left.Source, _right.Source));
            CompareHighlighter.Apply(_compareResult, LeftTreeRoots, RightTreeRoots,
                _left.Blocks, _right.Blocks,
                _left.Source, _right.Source);
            UpdateStatus();
        }
        else
        {
            _compareResult = null;
            HasDiffSummary = false;
            FilesIdentical = false;
            StatusMessage = "Load files on both sides to compare.";
        }
    }
```

- [ ] **Step 6: Convert Swap and Close to async**

Replace the `Swap`/`CloseLeft`/`CloseRight`/`ClosePane` commands (lines ~272-321) with:

```csharp
    [RelayCommand]
    private Task CloseLeftAsync() => ClosePaneAsync(true);

    [RelayCommand]
    private Task CloseRightAsync() => ClosePaneAsync(false);

    private async Task ClosePaneAsync(bool isLeft)
    {
        CancelDiff();
        if (isLeft)
        {
            LeftHexDataSource = null;
        }
        else
        {
            RightHexDataSource = null;
        }

        Pane(isLeft).DisposeAndReset();

        if (isLeft)
        {
            LeftFilePath = string.Empty;
        }
        else
        {
            RightFilePath = string.Empty;
        }

        LeftDiffRanges = null;
        RightDiffRanges = null;
        await RefreshComparisonAsync(); // after close < 2 panes loaded → cheap no-compare branch
    }

    [RelayCommand]
    private Task SwapAsync() => RunBusyAsync(async () =>
    {
        CancelDiff();
        (_left, _right) = (_right, _left);
        (LeftFilePath, RightFilePath) = (RightFilePath, LeftFilePath);
        LeftHexDataSource = null;
        RightHexDataSource = null;
        LeftDiffRanges = null;
        RightDiffRanges = null;
        await RefreshComparisonAsync();
    });
```

- [ ] **Step 7: Update the Browse callers + drag-drop caller**

In `FileCompareViewModel.cs` `BrowseLeftAsync`/`BrowseRightAsync` (lines ~256/268), `await` the load:

```csharp
        if (path is not null)
        {
            await LoadLeftFileAsync(path);   // BrowseLeftAsync
        }
```
```csharp
        if (path is not null)
        {
            await LoadRightFileAsync(path);  // BrowseRightAsync
        }
```

In `ReScene.NET/Views/FileCompareView.xaml.cs` `OnPreviewDrop` (lines ~77-84), fire-and-forget the async load (the handler is `void`):

```csharp
        if (IsOnLeftSide(e))
        {
            _ = vm.LoadLeftFileAsync(file);
        }
        else
        {
            _ = vm.LoadRightFileAsync(file);
        }
```

- [ ] **Step 8: Run the tests (GREEN) + clean build + full suite**

```bash
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj \
  --filter "FullyQualifiedName~FileCompareViewModelMkvTests" -p:BaseOutputPath=bin2/
dotnet build E:/Projects/ReScene.NET/ReScene.NET/ReScene.NET.csproj -p:BaseOutputPath=bin2/ --no-incremental
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/
```
Expected: focused tests pass (5: 3 migrated + 2 new); **0 Warning(s) 0 Error(s)**; full app suite green.

- [ ] **Step 9: Commit**

```bash
cd E:/Projects/ReScene.NET
git add ReScene.NET/ViewModels/FileCompareViewModel.cs ReScene.NET/Views/FileCompareView.xaml.cs ReScene.NET.Tests/FileCompareViewModelMkvTests.cs
git commit -m "$(cat <<'EOF'
feat(compare): run load+compare off the UI thread with a busy flag

LoadLeftFileAsync/LoadRightFileAsync/SwapAsync/CloseLeftAsync/CloseRightAsync
parse and Compare on a background thread; tree population, highlighting, and
status updates stay on the UI thread after the await. IsComparing (via a
guard-before-try RunBusyAsync wrapper) tracks the busy state; IsNotComparing
drives IsEnabled. Re-entrant loads are ignored. Drag-drop fire-and-forgets.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Inline busy overlay + disabled controls (view)

**Files:**
- Modify: `ReScene.NET/Views/FileCompareView.xaml`

**Interfaces:**
- Consumes: `IsComparing` (overlay `Visibility`), `IsNotComparing` (`IsEnabled`) from Task 1.

- [ ] **Step 1: Confirm the bool→Visibility converter key**

Read `ReScene.NET/App.xaml` and note the exact `x:Key` of the `BooleanToVisibilityConverter` (the spec/review call it `BoolToVisibility`). Use that key verbatim in the overlay binding below. If the key differs, use the real one.

- [ ] **Step 2: Add the busy overlay to the content `Grid`**

In `ReScene.NET/Views/FileCompareView.xaml`, the content area is the `<Grid>` at line ~91 (three columns: left / splitter / right), whose last children are the existing drop overlays. Add the busy overlay as the **last child of that `Grid`** (so it renders on top), spanning all three columns:

```xml
      <!-- Busy overlay: shown while a comparison runs (work happens off the UI thread). -->
      <Border Grid.Column="0" Grid.ColumnSpan="3"
              Background="#80000000"
              Visibility="{Binding IsComparing, Converter={StaticResource BoolToVisibility}}">
        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Width="240">
          <TextBlock Text="Comparing files…"
                     HorizontalAlignment="Center"
                     Margin="0,0,0,8"
                     Foreground="White" />
          <ProgressBar IsIndeterminate="True" Height="6" />
        </StackPanel>
      </Border>
```

(Match `Grid.Column`/`Grid.ColumnSpan` and any `Panel.ZIndex` convention the existing drop overlays use so the busy overlay sits above them; if the drop overlays set a `ZIndex`, give this a higher one.)

- [ ] **Step 3: Disable Browse/Swap/Close while busy**

On the toolbar buttons (the `Grid` at line ~16: `CloseLeftCommand` ~29, `BrowseLeftCommand` ~36, `SwapCommand` ~49, `CloseRightCommand` ~60, `BrowseRightCommand` ~67), add `IsEnabled="{Binding IsNotComparing}"` to each of the five buttons. Example:

```xml
                Command="{Binding BrowseLeftCommand}"
                IsEnabled="{Binding IsNotComparing}"
                Style="{StaticResource GhostButton}"
```

- [ ] **Step 4: Clean build (XAML compiles, analyzers clean)**

```bash
dotnet build E:/Projects/ReScene.NET/ReScene.NET/ReScene.NET.csproj -p:BaseOutputPath=bin2/ --no-incremental
dotnet test E:/Projects/ReScene.NET/ReScene.NET.Tests/ReScene.NET.Tests.csproj -p:BaseOutputPath=bin2/
```
Expected: **0 Warning(s) 0 Error(s)**; full suite green.

- [ ] **Step 5: Commit**

```bash
cd E:/Projects/ReScene.NET
git add ReScene.NET/Views/FileCompareView.xaml
git commit -m "$(cat <<'EOF'
feat(compare): inline busy overlay while comparing

A semi-transparent overlay with an indeterminate progress bar ("Comparing
files…") spans the Compare panes while IsComparing is set; Browse/Swap/Close are
disabled (IsNotComparing) during the operation.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
)"
```

---

## Final verification (after both tasks)

- [ ] Clean non-incremental build of `ReScene.NET` with `-p:BaseOutputPath=bin2/`: **0 warnings, 0 errors**.
- [ ] Full `ReScene.NET.Tests` run with `-p:BaseOutputPath=bin2/`: **0 failures**.
- [ ] Delete scratch: `find E:/Projects/ReScene.NET -type d -name bin2 -prune -exec rm -rf {} + 2>/dev/null`.
- [ ] **Manual:** load two files in Compare — the "Comparing files…" overlay shows with an animated bar while the window stays responsive, then disappears when results render; Browse/Swap/Close are disabled during it; Swap shows the overlay; loading a bad/locked file clears the overlay and shows the error status; drag-drop still works.

## Notes on cross-cutting concerns

- **UI-thread invariant:** all entry points (Browse commands, drag-drop, no MainWindow routing to this VM) are on the UI thread, so awaited continuations resume on the UI thread (no `ConfigureAwait(false)` anywhere) — the post-await tree/highlight/status updates are UI-thread-safe.
- **Concurrency:** background `Compare` reads the memory-mapped sources (stateless reads of a read-only accessor, already done by the byte-diff thread); `CompareHighlighter.Apply` runs on the UI thread, sequenced after the awaited `Compare`. The re-entrancy guard prevents a second load from disposing a source mid-compare.
- **YAGNI:** indeterminate only; `ClosePaneAsync` doesn't show the overlay (its refresh hits the cheap no-compare branch).
