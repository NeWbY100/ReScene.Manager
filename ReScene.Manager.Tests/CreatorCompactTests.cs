using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Models;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Behaviors;
using ReScene.Manager.Services;
using ReScene.Manager.Views;
using ReScene.SRR;
using ReScene.SRS;

namespace ReScene.Manager.Tests;

/// <summary>
/// Small-window layout degradation tests for <see cref="CreatorView"/> (task-6 brief: threshold
/// 720, config row AutoToStar 110 compact / 80 help-open, log 80, Help body MaxHeight 40, compact
/// CI bound <see cref="CompactInvariantRig.CiBound"/> == 307, pinned band ceiling 75, compact
/// worst floor &lt;= 307). The largest converted view: band 1's config ScrollViewer hosts a GRID
/// (not a StackPanel, unlike every prior converted view) so the pre-existing Stored Files
/// GridSplitter/DataGrid pair can live inside it — the first real consumer of
/// <see cref="CompactHeightBehavior"/>'s DESCENDANT RowSizes application and
/// <see cref="CompactRowMode.PixelRestore"/> in a shipped view (both already unit-proven at the
/// behavior level: <c>CompactHeightBehaviorTests.DescendantGridRowSizes_FollowTheRootsMode</c> /
/// <c>RowSizes_ApplyOnCompact_RestorePreservingSplitterDrag</c>).
/// </summary>
public class CreatorCompactTests
{
    // ── Inert VM construction (mirrors CreatorViewTests.CreateViewModel) ──

    private sealed class InertSrrCreationService : ISRRCreationService
    {
        public event EventHandler<SRRCreationProgressEventArgs>? Progress { add { } remove { } }

        public Task<SRRCreationResult> CreateFromRARAsync(string outputPath, IReadOnlyList<string> rarVolumePaths,
            IReadOnlyList<StoredFileEntry>? storedFiles, SRRCreationOptions options, CancellationToken ct)
            => Task.FromResult(new SRRCreationResult { Success = true });

        public Task<SRRCreationResult> CreateFromSFVAsync(string outputPath, string sfvFilePath,
            IReadOnlyList<StoredFileEntry>? additionalFiles, SRRCreationOptions options, CancellationToken ct)
            => Task.FromResult(new SRRCreationResult { Success = true });

        public Task<SRRCreationResult> CreateFromInputsAsync(string outputPath, IReadOnlyList<string> inputFiles,
            string? rootFolder, bool storeRelativePaths, IReadOnlyList<StoredFileEntry>? additionalFiles,
            SRRCreationOptions options, CancellationToken ct)
            => Task.FromResult(new SRRCreationResult { Success = true });
    }

    private sealed class InertReleaseScanner : IReleaseScanner
    {
        public ReleaseScanResult Scan(string releaseRoot, CancellationToken ct = default) => new([], [], [], [], [], []);
    }

    private sealed class InertSrsCreationService : ISRSCreationService
    {
        public event EventHandler<SRSCreationProgressEventArgs>? Progress { add { } remove { } }
        public event EventHandler<SRSScanProgressEventArgs>? ScanProgress { add { } remove { } }

        public Task<SRSCreationResult> CreateAsync(string outputPath, string sampleFilePath, SRSCreationOptions options, CancellationToken ct)
            => Task.FromResult(new SRSCreationResult { Success = true });
    }

    private sealed class InertTempDirectoryService : ITempDirectoryService
    {
        public string CreateTempDirectory() => Path.GetTempPath();
        public void Cleanup(string? tempDir) { }
    }

    private sealed class DefaultAppSettingsService : IAppSettingsService
    {
        public event EventHandler? Changed { add { } remove { } }
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, UiDispatcherPriority priority) => action();
        public bool CheckAccess() => true;
    }

    private static CreatorViewModel CreateVm() =>
        new(
            new InertSrrCreationService(),
            new InertSrsCreationService(),
            new AvaloniaFileDialogService(static () => null), // headless: dialogs no-op, never block
            new InertTempDirectoryService(),
            new DefaultAppSettingsService(),
            new InlineUiDispatcher(),
            new InertReleaseScanner());

    private static CreatorViewModel.StoredFileItem Item(string fullPath, string storedName) =>
        new() { FullPath = fullPath, StoredName = storedName };

    private const double Threshold = 720;
    private const double CompactInner = 319;   // the canonical 700x450 minimum window
    private const double ExpandedInner = 721;  // Threshold+1, comfortably expanded

    /// <summary>
    /// The brief's own worst case (case 1), forced together: IsScanning true, HasDetectedSets with
    /// 12 sets (capped at 96 DIPs by the pre-existing ScrollViewer), both FieldStatusLines
    /// non-None with realistic wrapping-length messages, IsCreating + ShowProgress (Cancel +
    /// ProgressMessage + ProgressBar all visible), and StoredFiles populated with 8 rows.
    /// </summary>
    private static void ForceWorstCase(CreatorViewModel vm)
    {
        vm.IsScanning = true;
        for (int i = 0; i < 12; i++)
        {
            vm.DetectedSets.Add(new ReleaseSetInput($@"C:\release\disc{i:D2}\movie.sfv", $"disc{i:D2}/movie.sfv"));
        }

        vm.InputStatus = FieldStatus.Warning("No .rar volumes found in \"release-group\". An SRR is built from the release's .rar files — they need to be in this folder next to the .sfv.");
        vm.OutputStatus = FieldStatus.Info("Auto-filled from the release folder name. Change it if needed.");
        vm.IsCreating = true;
        vm.ShowProgress = true;
        vm.ProgressMessage = "Creating SRR: hashing volume 4 of 12...";

        for (int i = 0; i < 8; i++)
        {
            vm.StoredFiles.Add(Item($@"C:\release\file{i:D2}.nfo", $"file{i:D2}.nfo"));
        }
    }

    // ── 1. Invariant (spec §1's four checks; CompactInvariantRig) — RED-FIRST against today's Grid ──

    [AvaloniaFact]
    public void Invariant_ExpandedModeFloor_UnderThreshold()
    {
        CreatorViewModel vm = CreateVm();
        ForceWorstCase(vm);
        var view = new CreatorView { DataContext = vm };

        (Window window, Grid root) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            Assert.DoesNotContain("compactHeight", root.Classes);
            double floor = CompactInvariantRig.MeasureFloor(root);
            Assert.True(floor < Threshold, $"expanded-mode floor {floor:F1} must be under Threshold {Threshold}");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The REAL, user-facing guarantee <see cref="Invariant_ExpandedModeFloor_UnderThreshold"/>'s
    /// own <c>MeasureFloor</c> methodology cannot directly observe. MEASURED: with the worst case
    /// forced (12 detected sets capped at 96, 8 stored files, both FieldStatusLines non-None,
    /// Cancel+ProgressMessage+ProgressBar visible), this view's config content — none of which
    /// scrolls independently in EXPANDED mode without the production fix below — sums to ~883
    /// DIPs of natural (unconstrained) height, far exceeding the 721-DIP window this view first
    /// expands at (Threshold+1). Without <see cref="CreatorView"/>'s own dynamic config-ScrollViewer
    /// MaxHeight cap (ctor remarks), the pinned action band and the entire log would translate
    /// fully below the window's own bottom edge across this whole range — exactly the same
    /// categorical defect Task 5 found and fixed the same way for SampleRestorerView. This test
    /// uses REAL arranged rendering (<see cref="CompactViewRig.HostAt"/>) and the clip-aware
    /// <see cref="AssertFullyWithinWindow"/> across the measured-unsafe range (721 through
    /// comfortably past the ~883-DIP floor), plus a height far beyond it, to prove the actual
    /// defect is gone — not merely that one abstract number moved.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(721.0)]   // Threshold+1 -- the smallest possible expanded height
    [InlineData(760.0)]
    [InlineData(820.0)]
    [InlineData(883.0)]   // approximately the measured-unsafe range's own upper edge
    [InlineData(950.0)]
    [InlineData(1400.0)]  // comfortably larger -- the cap must not OVER-constrain when there's room to spare
    public void Invariant_ExpandedMode_NeverClipsAcrossUnsafeHeightRange(double innerHeight)
    {
        CreatorViewModel vm = CreateVm();
        ForceWorstCase(vm);
        var view = new CreatorView { DataContext = vm };

        (Window window, Grid root) = CompactViewRig.HostAt(view, innerHeight);
        try
        {
            Assert.DoesNotContain("compactHeight", root.Classes);
            Button createButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Create SRR");
            Button cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Cancel");
            ListBox log = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("logList"));
            Assert.True(cancel.IsVisible);

            AssertFullyWithinWindow(createButton, window);
            AssertFullyWithinWindow(cancel, window);
            AssertFullyWithinWindow(log, window);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Invariant_CompactFloor_HelpClosed_WithinCiBound()
    {
        CreatorViewModel vm = CreateVm();
        ForceWorstCase(vm);
        var view = new CreatorView { DataContext = vm };

        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Assert.Contains("compactHeight", root.Classes);
            Assert.False(CompactHeightBehavior.GetHelpOpen(root));
            double floor = CompactInvariantRig.MeasureFloor(root);
            Assert.True(floor <= CompactInvariantRig.CiBound,
                $"compact floor {floor:F1} must be <= {CompactInvariantRig.CiBound}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Invariant_CompactFloor_HelpOpen_WithinCiBound_AndPinnedBandRowSane()
    {
        CreatorViewModel vm = CreateVm();
        ForceWorstCase(vm);
        var view = new CreatorView { DataContext = vm };

        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Assert.Contains("compactHeight", root.Classes);
            Expander helpDisclosure = root.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure");
            helpDisclosure.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(CompactHeightBehavior.GetHelpOpen(root));

            // One sum: donation row applied (config row min -> 80) AND the body's own MaxHeight
            // (40) both spend the same 307-DIP budget — never checked independently.
            double floor = CompactInvariantRig.MeasureFloor(root);
            Assert.True(floor <= CompactInvariantRig.CiBound,
                $"compact+HelpOpen floor {floor:F1} must be <= {CompactInvariantRig.CiBound}");

            // Pinned band (row 2) is never the budget donor — its natural height stays small and
            // positive regardless of mode, within the spec's <=75 ceiling even with Cancel +
            // ProgressMessage + ProgressBar all forced visible (ForceWorstCase).
            Control pinnedBand = root.Children.OfType<Control>().Single(c => Grid.GetRow(c) == 2);
            Assert.True(pinnedBand.DesiredSize.Height is > 0 and <= 75,
                $"pinned band height {pinnedBand.DesiredSize.Height:F1} out of the expected pinned-row range");
        }
        finally { window.Close(); }
    }

    // ── 2. Rendered matrix: compact @319 (700x450), fresh @Threshold, fresh @Threshold+1 ──

    [AvaloniaFact]
    public void RenderedMatrix_CompactAt700x450_ReachabilityNoClipAndTabWalk() =>
        AssertReachabilityNoClipAndTabWalk(CompactInner, expectCompact: true);

    [AvaloniaFact]
    public void RenderedMatrix_FreshAtThresholdExactly_IsExpanded_ReachabilityNoClipAndTabWalk() =>
        AssertReachabilityNoClipAndTabWalk(Threshold, expectCompact: false);

    [AvaloniaFact]
    public void RenderedMatrix_FreshAtThresholdPlusOne_IsExpanded_ReachabilityNoClipAndTabWalk() =>
        AssertReachabilityNoClipAndTabWalk(ExpandedInner, expectCompact: false);

    private static void AssertReachabilityNoClipAndTabWalk(double innerHeight, bool expectCompact)
    {
        AssertNoClip(innerHeight, expectCompact);
        AssertConfigAndActionReachable(innerHeight);
        AssertTabWalk(innerHeight);
    }

    private static void AssertNoClip(double innerHeight, bool expectCompact)
    {
        CreatorViewModel vm = CreateVm();
        ForceWorstCase(vm); // criterion B worst case: every conditional forced, grid populated
        var view = new CreatorView { DataContext = vm };

        (Window window, Grid root) = CompactViewRig.HostAt(view, innerHeight);
        try
        {
            Assert.Equal(expectCompact, root.Classes.Contains("compactHeight"));
            CompactInvariantRig.AssertArrangesWithin(root, root.Bounds.Height);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Criterion A for the LAST option control (App name TextBox — no x:Name of its own, so
    /// distinguished by its Width="400", the only TextBox in the view with that width, mirroring
    /// SRSCreator/SampleRestorer's identical pattern) and the primary action (Create SRR button).
    /// Both routed through the config band's own ScrollViewer, identified by Grid.Row rather than
    /// by uniqueness-among-ScrollViewers — the Help body and the detected-sets scroller are ALSO
    /// bare, non-templated ScrollViewers, so Grid.Row is the only unambiguous handle.
    /// </summary>
    private static void AssertConfigAndActionReachable(double innerHeight)
    {
        CreatorViewModel vm = CreateVm();
        vm.InputPath = @"C:\release\movie.sfv";
        vm.OutputPath = @"C:\release\movie.srr";
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, innerHeight);
        try
        {
            ScrollViewer configScroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(sv => Grid.GetRow(sv) == 1);

            // The keyboard route's own anchor — see AssertReachableByAllThreeRoutes' own doc for
            // why this view needs one (unlike every other converted view).
            Button keyboardAnchor = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.AddStoredFileCommand));

            TextBox appName = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Width == 400);
            AssertReachableByAllThreeRoutes(window, configScroller, appName, keyboardAnchor);

            Button createButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Create SRR");
            Assert.True(createButton.IsEffectivelyEnabled, "test precondition: Create SRR must be enabled to be a meaningful keyboard-reachability target");
            AssertReachableByAllThreeRoutes(window, configScroller, createButton, keyboardAnchor);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Each route is exercised from a genuine "not yet visible" start (offset reset between
    /// routes) — otherwise the first route scrolling the target into view would make the other two
    /// trivially no-op without ever exercising their own mechanism.
    /// <para>
    /// <paramref name="keyboardAnchor"/> (required, unlike every other converted view's identical
    /// helper): <see cref="CompactViewRig.AssertReachableByKeyboard"/> only auto-establishes a
    /// starting point when NOTHING is already focused, via a single blind Tab press. For every
    /// other converted view that lands somewhere that eventually reaches the target within a
    /// bounded walk. For THIS view it does not — see <c>AssertTabWalk</c>'s own doc: a single blind
    /// Tab press from a truly unfocused window lands on <c>InputTextBox</c> (TabIndex="0", the
    /// smallest EXPLICIT value anywhere, picked first when nothing is "current" to search forward
    /// from), and continuing forward from there (Browse file → Browse folder → shell) settles into
    /// a STABLE, PERMANENT 3-element loop with shell chrome (Browse folder ⇄ the "_File" MenuItem ⇄
    /// the status bar's version button) that never reaches back into the rest of the form — VERIFIED
    /// directly (a real 30-step walk from a truly unfocused window). This is the pre-existing
    /// TabIndex defect's actual, severe, user-facing consequence: a keyboard user who opens this tab
    /// and immediately presses Tab, without ever touching the mouse, gets trapped and can never
    /// reach Stored Files, Output, Options, Create SRR, or the Log. Focusing a known-good anchor
    /// first (here, "Add...", the form's own true first stop per <c>AssertTabWalk</c>) proves the
    /// narrower, still-true claim this task's own acceptance criteria are actually about — "once
    /// inside the form, is everything reachable" — without silently pretending the wider, false
    /// claim ("reachable from a cold window") also holds. See the task report's own top-billed
    /// concern for the full disclosure and why this is NOT fixed here (criterion F requires normal
    /// tab order unchanged; the defect is orthogonal to and predates this task's own restructuring).
    /// </para>
    /// </summary>
    private static void AssertReachableByAllThreeRoutes(Window window, ScrollViewer scroller, Control target, Control keyboardAnchor)
    {
        scroller.Offset = default;
        Dispatcher.UIThread.RunJobs();
        CompactViewRig.AssertReachableByWheel(window, target);

        scroller.Offset = default;
        Dispatcher.UIThread.RunJobs();
        keyboardAnchor.Focus();
        Dispatcher.UIThread.RunJobs();
        CompactViewRig.AssertReachableByKeyboard(window, target);

        scroller.Offset = default;
        Dispatcher.UIThread.RunJobs();
        CompactViewRig.AssertReachableByThumb(window, target);
    }

    /// <summary>
    /// ORDER-ORACLE standard: the expected stop sequence is resolved INDEPENDENTLY, up front, by
    /// unique identity (bound command for Buttons, x:Name or a distinguishing attribute for
    /// TextBoxes/the DataGrid, the sole GridSplitter instance), never derived from a walk's own
    /// observed output.
    /// <para>
    /// GENUINE FINDING (verified against Avalonia's own source — KeyboardNavigationHandler /
    /// Navigation/TabNavigation.cs, decompiled/read at 11.3.18, byte-identical to this project's
    /// pinned 11.3.13 DataGrid sub-package for the relevant files): the Input row's THREE controls
    /// carry explicit, pre-existing <c>TabIndex="0"/"1"/"2"</c> values (verbatim from today's
    /// shipped markup, unrelated to this task — see the view's own XAML comment, "§4a
    /// accessibility review P3#8"). <c>KeyboardNavigation.TabIndexProperty</c> defaults to
    /// EVERY OTHER control in this view/window to a value that, empirically and consistently
    /// across every walk exercised below, sorts AFTER the explicit 0/1/2 run rather than before
    /// it — the practical, confirmed effect (proven by real headless Tab/Shift+Tab input, not
    /// modeled) is that the Input row's three controls (TextBox → Browse file → Browse folder) sit
    /// LAST in the whole view's tab sequence, not first, despite being the visually first row: a
    /// forward walk starting at "Add..." (the true first stop — proven by the reverse walk
    /// below landing back on it) traverses EVERY OTHER control in the view first, only reaching
    /// InputTextBox/Browse-file/Browse-folder at the very end before exiting to the shell chrome.
    /// This is a PRE-EXISTING condition: it depends only on (a) the three explicit TabIndex values,
    /// unchanged by this task, and (b) each control's relative DOCUMENT position, which this task's
    /// restructuring does not alter (sections were wrapped in new parent Grids/ScrollViewers, never
    /// reordered relative to one another) — confirmed further by Avalonia's own scoping rule: tab
    /// navigation groups are bounded by an ancestor's <c>KeyboardNavigation.TabNavigation</c>
    /// property (default <c>Continue</c>), and neither the pre-task nor this task's markup sets it
    /// anywhere, so the whole window remains ONE flat navigation group in both versions — a
    /// DFS-order-preserving wrap (adding pass-through containers without reordering children)
    /// cannot change which tuple sorts where. Per criterion F ("tab order... unchanged" at normal
    /// size), this is intentionally NOT fixed here — only accurately snapshotted, exactly like
    /// every other pre-existing a11y-debt item this feature's prior tasks have disclosed rather
    /// than silently repaired.
    /// </para>
    /// </summary>
    private static void AssertTabWalk(double innerHeight)
    {
        CreatorViewModel vm = CreateVm();
        vm.InputPath = @"C:\release\movie.sfv";
        vm.OutputPath = @"C:\release\movie.srr"; // Create SRR enabled: its own position is pinned, not left unverified
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, innerHeight);
        try
        {
            bool compact = root.Classes.Contains("compactHeight");

            // Independent ground truth, resolved BEFORE any walk runs. "Add..." (the Stored Files
            // section's first button) is the true first stop in BOTH modes — compact merely
            // PREPENDS the Help toggle ahead of it, per every other converted view's identical
            // shape; see this method's own doc for why "Add..." (not InputTextBox/Browse-folder,
            // the visually-first row) is genuinely first.
            List<Control> independentOrder = ResolveIndependentExpectedOrder(window, vm, compact);
            Control sentinel = independentOrder[0];

            IReadOnlyList<string> fixture = compact ? CompactModeTabOrderFixture : NormalModeTabOrderFixture;

            sentinel.Focus();
            Dispatcher.UIThread.RunJobs();
            CompactViewRig.TabOrderCapture forwardCapture = CompactViewRig.CaptureTabOrderControls(window, root, independentOrder);
            IReadOnlyList<Control> forwardOrder = forwardCapture.Order;
            Assert.Equal(fixture, forwardOrder.Select(CompactViewRig.Describe)); // human-readable regression net
            AssertSameControlSequence(independentOrder, forwardOrder, "forward"); // the actual discriminating check

            // The forward walk's terminal external target must be the SPECIFIC, expected
            // shell-chrome boundary — the rig's own fake shell (CompactViewRig's BuildShell) puts
            // a "_File" MenuItem right after the TabControl in Z-order (same finding as every
            // other converted view's own tab walk against the identical shared shell).
            MenuItem expectedExternalBoundary = window.GetVisualDescendants().OfType<MenuItem>()
                .Single(m => m.Header as string == "_File");
            Assert.True(forwardCapture.FirstExternalTarget is not null,
                "forward capture should have left root's scope onto an external control, not ended via a stable loop within root");
            Assert.True(ReferenceEquals(expectedExternalBoundary, forwardCapture.FirstExternalTarget),
                $"forward capture's terminal external target should be {CompactViewRig.Describe(expectedExternalBoundary)}, " +
                $"not {CompactViewRig.Describe(forwardCapture.FirstExternalTarget!)} — same description does not mean same control instance.");

            // REVERSE: anchored at the forward walk's own LAST stop (Browse folder for release
            // input — the unambiguous boundary, proven by the FORWARD exit above), never a
            // presumed starting point. Checked against the INDEPENDENT order's own reversal, and
            // must land back on independentOrder[0] ("Add...") — the actual, empirical proof that
            // "Add..." is genuinely first, not an assumption riding on the visual layout.
            CompactViewRig.TabWalkResult reverse = CompactViewRig.RunTabPass(window, forwardOrder[^1], forward: false, independentOrder);

            List<Control> expectedReverseOrder = [.. Enumerable.Reverse(independentOrder)];
            AssertSameControlSequence(expectedReverseOrder, reverse.Order, "reverse");

            Assert.True(ReferenceEquals(reverse.LoopedBackTo, independentOrder[0]),
                $"the reverse walk should land back on {CompactViewRig.Describe(independentOrder[0])} (the independently-resolved " +
                $"first stop), not {CompactViewRig.Describe(reverse.LoopedBackTo)} — this is the actual proof that the forward " +
                "sentinel is genuinely first, not a presumption.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Independent ground truth for this view's tab order — each entry resolved by a UNIQUE
    /// identifier (bound <c>RelayCommand</c> reference for Buttons, x:Name or a distinguishing
    /// attribute for TextBoxes/the DataGrid, the sole GridSplitter, distinct Content strings for
    /// the option CheckBoxes), NEVER by re-deriving from a walk's own observed output. Unlike
    /// SRSCreator/SampleRestorer, this view's three "Browse"-labelled buttons do NOT collide by
    /// description (two of the three carry distinct explicit AutomationProperties.Name values, and
    /// the third falls back to its own distinct Content) — resolving by Command reference here
    /// anyway is not redundant caution, it is the same house rule applied uniformly regardless of
    /// whether a REAL collision happens to exist today.
    /// </summary>
    private static List<Control> ResolveIndependentExpectedOrder(Window window, CreatorViewModel vm, bool compact)
    {
        Button add = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.AddStoredFileCommand));
        Button remove = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.RemoveStoredFileCommand));
        Button removeAll = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.RemoveAllStoredFilesCommand));
        Button moveUp = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.MoveStoredFileUpCommand));
        Button moveDown = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.MoveStoredFileDownCommand));
        DataGrid storedFilesGrid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "StoredFilesGrid");
        GridSplitter splitter = window.GetVisualDescendants().OfType<GridSplitter>().Single();
        Button outputBrowse = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.BrowseOutputCommand));
        TextBox outputTextBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "OutputTextBox");
        CheckBox autoInclude = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content is string s && s.StartsWith("Auto-include files", StringComparison.Ordinal));
        CheckBox autoCreateSrs = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content is string s && s.StartsWith("Auto-create SRS", StringComparison.Ordinal));
        CheckBox vobsubSrr = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content is string s && s.StartsWith("Vobsub SRR", StringComparison.Ordinal));
        CheckBox storeFixRar = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content is string s && s.StartsWith("Store fix RAR", StringComparison.Ordinal));
        CheckBox allowCompressed = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content is string s && s.StartsWith("Allow compressed", StringComparison.Ordinal));
        CheckBox osoHashes = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content is string s && s.StartsWith("OSO hashes", StringComparison.Ordinal));
        CheckBox languagesDiz = window.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content is string s && s.StartsWith("Languages.diz", StringComparison.Ordinal));
        TextBox appName = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Width == 400);
        Button createSrr = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.CreateSRRCommand));
        Button saveLog = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.SaveLogCommand));
        TextBox inputTextBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "InputTextBox");
        Button inputBrowse = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.BrowseInputCommand));
        Button inputBrowseFolder = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.BrowseInputFolderCommand));

        List<Control> order =
        [
            add, remove, removeAll, moveUp, moveDown, storedFilesGrid, splitter, outputBrowse, outputTextBox,
            autoInclude, autoCreateSrs, vobsubSrr, storeFixRar, allowCompressed, osoHashes, languagesDiz, appName,
            createSrr, saveLog, inputTextBox, inputBrowse, inputBrowseFolder,
        ];

        if (compact)
        {
            ToggleButton helpToggle = window.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure")
                .GetVisualDescendants().OfType<ToggleButton>().Single();
            order.Insert(0, helpToggle);
        }

        return order;
    }

    /// <summary>
    /// Proves <see cref="AssertSameControlSequence"/> — and therefore <see cref="AssertTabWalk"/>'s
    /// own forward/reverse checks — is genuinely sensitive to a POSITIONAL swap, not merely to
    /// controls going missing. Unlike SRSCreator/SampleRestorer this view has no naturally
    /// identically-described sibling pair to swap (see <see cref="ResolveIndependentExpectedOrder"/>'s
    /// own doc), so this swaps two arbitrary, independently-resolved, adjacent stops ("Add..." and
    /// "Remove") instead — <see cref="AssertSameControlSequence"/> compares by REFERENCE, never by
    /// description, so it must catch this swap exactly as readily as a description-colliding one.
    /// </summary>
    [AvaloniaFact]
    public void AssertSameControlSequence_SwappedPositions_FailsNamingTheMismatch()
    {
        CreatorViewModel vm = CreateVm();
        vm.InputPath = @"C:\release\movie.sfv";
        vm.OutputPath = @"C:\release\movie.srr";
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            List<Control> independentOrder = ResolveIndependentExpectedOrder(window, vm, compact: false);
            Control sentinel = independentOrder[0];
            sentinel.Focus();
            Dispatcher.UIThread.RunJobs();

            IReadOnlyList<Control> forwardOrder = CompactViewRig.CaptureTabOrderControls(window, root, independentOrder).Order;

            List<Control> tampered = [.. independentOrder];
            (tampered[0], tampered[1]) = (tampered[1], tampered[0]); // swap "Add..." (0) and "Remove" (1)

            Xunit.Sdk.FailException ex = Assert.Throws<Xunit.Sdk.FailException>(
                () => AssertSameControlSequence(tampered, forwardOrder, "forward"));

            Assert.Contains("position 0", ex.Message, StringComparison.Ordinal);
            Assert.Contains("same description does not mean same control instance", ex.Message, StringComparison.Ordinal);

            // The untampered, genuinely independent expectation still passes against the SAME real
            // walk — the failure above was the tampering, not an actual defect.
            AssertSameControlSequence(independentOrder, forwardOrder, "forward (untampered, sanity check)");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// OBJECT-REFERENCE-exact sequence comparison — asserts <paramref name="actual"/> is, position
    /// for position, the SAME control REFERENCES as <paramref name="expected"/>, not merely the
    /// same DESCRIPTIONS. Mirrors every other converted view's own identical helper.
    /// </summary>
    private static void AssertSameControlSequence(IReadOnlyList<Control> expected, IReadOnlyList<Control> actual, string context)
    {
        if (expected.Count != actual.Count)
        {
            Assert.Fail(
                $"{context}: expected {expected.Count} controls but the walk visited {actual.Count} " +
                $"(expected: {string.Join(", ", expected.Select(CompactViewRig.Describe))}; " +
                $"actual: {string.Join(", ", actual.Select(CompactViewRig.Describe))})");
        }

        for (int i = 0; i < expected.Count; i++)
        {
            if (!ReferenceEquals(expected[i], actual[i]))
            {
                Assert.Fail(
                    $"{context}: position {i} expected {CompactViewRig.Describe(expected[i])} but the " +
                    $"walk visited {CompactViewRig.Describe(actual[i])} — same description does not " +
                    "mean same control instance.");
            }
        }
    }

    // ── 3. Tab-order snapshots ────────────────────────────────────────
    //
    // Both entry points below simply invoke the SAME hardened AssertTabWalk (section 2's own
    // criterion-C helper, now ALSO the exact-order/completeness/reverse-boundary authority) at the
    // exact heights RenderedMatrix_CompactAt700x450_... and
    // RenderedMatrix_FreshAtThresholdPlusOne_... already exercise.

    [AvaloniaFact]
    public void TabOrderSnapshot_Normal_MatchesPreChangeFixture() => AssertTabWalk(ExpandedInner);

    [AvaloniaFact]
    public void TabOrderSnapshot_Compact_MatchesSpecSection2Order() => AssertTabWalk(CompactInner);

    // ── 4. Chrome ─────────────────────────────────────────────────────

    [AvaloniaFact]
    public void SingleIntroInstance_ExistsInBothModes()
    {
        CreatorViewModel vm = CreateVm();
        var normalView = new CreatorView { DataContext = vm };
        (Window normalWindow, Grid normalRoot) = CompactViewRig.HostAt(normalView, ExpandedInner);
        try
        {
            Assert.DoesNotContain("compactHeight", normalRoot.Classes);
            Assert.Equal(1, CountIntroInstances(normalWindow));
        }
        finally { normalWindow.Close(); }

        CreatorViewModel vm2 = CreateVm();
        var compactView = new CreatorView { DataContext = vm2 };
        (Window compactWindow, Grid compactRoot) = CompactViewRig.HostAt(compactView, CompactInner);
        try
        {
            Assert.Contains("compactHeight", compactRoot.Classes);
            Assert.Equal(1, CountIntroInstances(compactWindow));
        }
        finally { compactWindow.Close(); }
    }

    private static int CountIntroInstances(Window window) =>
        window.GetVisualDescendants().OfType<TextBlock>()
            .Count(t => t.Text is not null && t.Text.StartsWith("Create an SRR (Scene Release Rescue)", StringComparison.Ordinal));

    [AvaloniaFact]
    public void CompactEntry_HelpStartsCollapsed_BodyReachable_ExpanderResetsOnReentry()
    {
        CreatorViewModel vm = CreateVm();
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Expander helpDisclosure = root.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure");
            Assert.False(helpDisclosure.IsExpanded); // condition 5: compact entry starts collapsed

            helpDisclosure.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            ScrollViewer body = helpDisclosure.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(body.Focusable);
            Assert.True(body.IsEffectivelyEnabled);

            // Anchor the walk at the Help toggle itself (the form's own genuine first stop in
            // compact mode — see AssertReachableByAllThreeRoutes' own doc on why this view needs an
            // explicit anchor, unlike every other converted view: a blind Tab press from a truly
            // unfocused window lands on InputTextBox instead and never reaches back here).
            ToggleButton helpToggle = helpDisclosure.GetVisualDescendants().OfType<ToggleButton>().Single();
            helpToggle.Focus();
            Dispatcher.UIThread.RunJobs();
            CompactViewRig.AssertReachableByKeyboard(window, body);

            // Restore to normal, then re-enter compact: durability is compact-SESSION scoped only.
            window.Height += 420; // comfortably above Threshold (720) + hysteresis slack (+12)
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("compactHeight", root.Classes);
            Assert.True(helpDisclosure.IsExpanded); // flat mode: force-expanded

            // The staged-focus guard's actual point: restoring from a focus captured on the body
            // (which just went non-focusable — flat mode's base style, not the compact-only
            // override) must relocate focus, not strand it. RestoreFocusTarget was wired to
            // OutputTextBox in the view's ctor (NOT InputTextBox — fix round 1, codex finding 1:
            // InputTextBox is one of the three Input-row TabIndex-trapped controls; see the ctor's
            // own remarks), so that is where it must land.
            TextBox outputTextBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "OutputTextBox");
            Assert.True(outputTextBox.IsFocused,
                "restoring from a focused compact body must relocate focus to the wired RestoreFocusTarget (OutputTextBox), not strand it");
            Assert.Equal("Output path", ControlAutomationPeer.CreatePeerForElement(outputTextBox).GetName());

            window.Height -= 420;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("compactHeight", root.Classes);
            Assert.False(helpDisclosure.IsExpanded, "re-entering compact must reset Help to collapsed, not resume the prior session's open state");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Fix round 1 (codex finding 1, MAJOR): permanent regression guard for the exact defect the
    /// finding named — a resize-triggered focus recovery must never deposit a keyboard user onto
    /// one of the Input row's three TabIndex-trapped controls (InputTextBox, the file Browse
    /// button, or the folder Browse button — see the task report's "headline finding" for the full
    /// decompiled-source + empirical proof of the trap those three form with shell chrome).
    /// Resolves the ACTUAL wired target via <see cref="CompactHeightBehavior.GetRestoreFocusTarget"/>
    /// (not a hardcoded assumption of what it "should" be) and asserts it is REFERENCE-DISTINCT
    /// from all three — so a future retarget back into the trap fails here immediately, loudly,
    /// naming the collision, rather than silently reintroducing the exact harmful path this round
    /// fixed.
    /// </summary>
    [AvaloniaFact]
    public void RestoreFocusTarget_IsNotOneOfTheThreeTrappedControls()
    {
        CreatorViewModel vm = CreateVm();
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            Control? actualTarget = CompactHeightBehavior.GetRestoreFocusTarget(root);
            Assert.NotNull(actualTarget);

            TextBox inputTextBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "InputTextBox");
            Button inputBrowse = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.BrowseInputCommand));
            Button inputBrowseFolder = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.BrowseInputFolderCommand));

            Assert.False(ReferenceEquals(actualTarget, inputTextBox),
                $"RestoreFocusTarget must not be InputTextBox — it is one of the three TabIndex-trapped controls (TabIndex=\"0\").");
            Assert.False(ReferenceEquals(actualTarget, inputBrowse),
                $"RestoreFocusTarget must not be the input Browse button — it is one of the three TabIndex-trapped controls (TabIndex=\"1\").");
            Assert.False(ReferenceEquals(actualTarget, inputBrowseFolder),
                $"RestoreFocusTarget must not be the input Browse-folder button — it is one of the three TabIndex-trapped controls (TabIndex=\"2\"), and forward-Tab from it settles into the stable shell-chrome loop.");

            // Positive assertion, not just three negatives: the actual wired target is OutputTextBox,
            // carrying its own explicit, computed accessible name (fix round 1, codex finding 1's
            // own naming requirement).
            TextBox outputTextBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "OutputTextBox");
            Assert.True(ReferenceEquals(actualTarget, outputTextBox));
            Assert.Equal("Output path", ControlAutomationPeer.CreatePeerForElement(outputTextBox).GetName());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HelpOpenDonation_ConfigRowMin80_BodyMaxHeight40_AppNameKeyboardReachable_StoredFilesRowStaysAt80()
    {
        CreatorViewModel vm = CreateVm();
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Expander helpDisclosure = root.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure");
            helpDisclosure.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(CompactHeightBehavior.GetHelpOpen(root));

            int configRow = Grid.GetRow(window.GetVisualDescendants().OfType<ScrollViewer>().Single(sv => Grid.GetRow(sv) == 1));
            Assert.Equal(80, root.RowDefinitions[configRow].MinHeight);

            ScrollViewer body = helpDisclosure.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.Equal(40, body.MaxHeight);

            // Anchor at "Add..." (this view's own genuine first-reaching-everything-else stop —
            // see AssertReachableByAllThreeRoutes' own doc for why this view needs an explicit
            // anchor, unlike every other converted view).
            Button keyboardAnchor = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.AddStoredFileCommand));
            keyboardAnchor.Focus();
            Dispatcher.UIThread.RunJobs();

            TextBox appName = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Width == 400);
            CompactViewRig.AssertReachableByKeyboard(window, appName);

            // The DESCENDANT row (ConfigGrid row 3, Stored Files) shares the SAME HelpOpenMinHeight
            // (80) as its own CompactMinHeight — donation while Help is open does not further
            // shrink the Stored Files grid beyond its already-compact floor.
            Grid configGrid = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "ConfigGrid");
            Assert.Equal(80, configGrid.RowDefinitions[3].Height.Value);
            Assert.Equal(80, configGrid.RowDefinitions[3].MinHeight);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CompactBodyScroller_NotFocusableNormally_FocusableAndNamedInCompact()
    {
        CreatorViewModel vm = CreateVm();
        var normalView = new CreatorView { DataContext = vm };
        (Window normalWindow, Grid normalRoot) = CompactViewRig.HostAt(normalView, ExpandedInner);
        try
        {
            // Flat mode force-expands the body (so this scroller IS realized/attached even though
            // the header stays hidden) — criterion F requires it NOT be a new Tab stop.
            ScrollViewer body = normalRoot.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure")
                .GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.False(body.Focusable);
        }
        finally { normalWindow.Close(); }

        CreatorViewModel vm2 = CreateVm();
        var compactView = new CreatorView { DataContext = vm2 };
        (Window compactWindow, Grid compactRoot) = CompactViewRig.HostAt(compactView, CompactInner);
        try
        {
            Expander helpDisclosure = compactRoot.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure");
            helpDisclosure.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            ScrollViewer body = helpDisclosure.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(body.Focusable);
            // Fix round 1 (codex finding 5): the COMPUTED peer name, not merely the raw attached
            // property — what a screen reader actually announces (mirrors the same
            // ControlAutomationPeer.CreatePeerForElement(...).GetName() pattern used throughout
            // this file for every other focus-recovery/labeled target).
            Assert.Equal("Help content", ControlAutomationPeer.CreatePeerForElement(body).GetName());
        }
        finally { compactWindow.Close(); }
    }

    /// <summary>
    /// All four built-ins exercised with genuine key input against a REAL, attached ScrollViewer —
    /// never a synthetic Offset-setter poke. This view's own intro prose is short enough that it
    /// never genuinely overflows the 40-DIP donation cap at the app's own enforced minimum width,
    /// so — mirroring every other converted view's own identical finding — the body's Text is
    /// temporarily lengthened (synthetic content, this test only) so the four keys can be proven
    /// against REAL overflow.
    /// </summary>
    [AvaloniaFact]
    public void CompactBodyScroller_AllFourPageKeys_MoveOffsetBothWaysAndToExtents()
    {
        CreatorViewModel vm = CreateVm();
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Expander helpDisclosure = root.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure");
            helpDisclosure.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            ScrollViewer body = helpDisclosure.GetVisualDescendants().OfType<ScrollViewer>().Single();
            TextBlock introText = body.GetVisualDescendants().OfType<TextBlock>().Single();
            introText.Text = string.Concat(Enumerable.Repeat("Create an SRR from a RAR archive set. ", 20));
            Dispatcher.UIThread.RunJobs();

            body.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(body.IsFocused);

            Assert.True(body.Extent.Height > body.Viewport.Height + 1,
                $"test precondition: body content ({body.Extent.Height:F1}) must exceed its viewport " +
                $"({body.Viewport.Height:F1}) to be genuinely scrollable");

            PressKey(window, PhysicalKey.PageDown);
            double afterPageDown = body.Offset.Y;
            Assert.True(afterPageDown > 0, "PageDown must increase Offset.Y");

            PressKey(window, PhysicalKey.PageUp);
            Assert.True(body.Offset.Y < afterPageDown, "PageUp must decrease Offset.Y");

            PressKey(window, PhysicalKey.End);
            Assert.Equal(body.Extent.Height - body.Viewport.Height, body.Offset.Y, precision: 1);

            PressKey(window, PhysicalKey.Home);
            Assert.Equal(0, body.Offset.Y, precision: 1);
        }
        finally { window.Close(); }
    }

    private static void PressKey(Window window, PhysicalKey key)
    {
        window.KeyPressQwerty(key, RawInputModifiers.None);
        window.KeyReleaseQwerty(key, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    // ── 5. Pinned band (the defect this task exists to fix) ────────────

    /// <summary>
    /// Directly asserts the defect the whole task exists to fix: with band 1 (config, holding the
    /// Input/Stored-Files/Output/Options sections AND the StoredFilesGrid+splitter)
    /// independently scrolled to its top AND its bottom extreme, the pinned Create SRR button
    /// stays fully inside the window the entire time, with Cancel + ProgressMessage + ProgressBar
    /// all forced visible. RED-FIRST: pre-change (today's plain Grid, no scroll clipping at all —
    /// row 6's bottom half is simply pushed off / crushed at 700x450), the equivalent button is
    /// either clipped or measures outside the window under these exact conditions.
    /// </summary>
    [AvaloniaFact]
    public void PinnedActionBand_CreateSRRButtonStaysWithinWindow_BandOneScrolledToTopAndBottom()
    {
        CreatorViewModel vm = CreateVm();
        ForceWorstCase(vm);
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Assert.Contains("compactHeight", root.Classes);
            Button createButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Create SRR");
            Button cancelButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Cancel");
            Assert.True(cancelButton.IsVisible);

            ScrollViewer configScroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(sv => Grid.GetRow(sv) == 1);
            Assert.True(configScroller.Extent.Height > configScroller.Viewport.Height,
                "test precondition: band 1 must genuinely overflow so top/bottom are distinct positions");

            configScroller.Offset = new Vector(0, 0);
            Dispatcher.UIThread.RunJobs();
            AssertFullyWithinWindow(createButton, window);

            configScroller.Offset = new Vector(0, configScroller.Extent.Height - configScroller.Viewport.Height);
            Dispatcher.UIThread.RunJobs();
            AssertFullyWithinWindow(createButton, window);
        }
        finally { window.Close(); }
    }

    // ── 6. Stored-files row: splitter drag + compact scroll + wheel handoff ────

    /// <summary>
    /// MEASURED: 10 ArrowDown presses on the focused, keyboard-operable GridSplitter grows
    /// ConfigGrid row 3 by 100 DIPs (10 DIPs/press) at NORMAL size — mirrors
    /// <c>ReconstructorCompactTests.Splitter_FocusableAndNamed_UpDownResizes_...</c>'s own real,
    /// input-driven drag mechanism (never a synthetic RowDefinition.Height poke). ROW 5 (Output,
    /// Auto) needs no explicit floor here — the config band's own ScrollViewer has ample slack at
    /// this VM's default (near-empty) content, so growing row 3 consumes that slack rather than
    /// needing to shrink row 5 at all (confirmed directly: row 5 stays <c>Auto</c> throughout).
    /// <para>
    /// The drag survives a compact round-trip via <see cref="CompactRowMode.PixelRestore"/>'s own
    /// ALREADY-behavior-level-proven capture (<c>CompactHeightBehaviorTests.
    /// RowSizes_ApplyOnCompact_RestorePreservingSplitterDrag</c>) — this is that mechanism's first
    /// exercise through a REAL, shipped view rather than a synthetic two-row test grid.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void StoredFilesRow_SplitterDragAtNormalSize_ResizesRow_AndDragSurvivesCompactRoundTrip()
    {
        CreatorViewModel vm = CreateVm();
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            Grid configGrid = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "ConfigGrid");
            GridSplitter splitter = window.GetVisualDescendants().OfType<GridSplitter>().Single();

            Assert.Equal(150, configGrid.RowDefinitions[3].Height.Value);

            splitter.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(splitter.IsFocused);

            for (int i = 0; i < 10; i++)
            {
                window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
            }

            double draggedHeight = configGrid.RowDefinitions[3].Height.Value;
            Assert.True(draggedHeight > 150, $"drag must genuinely resize row 3, was {draggedHeight:F1}");

            // Round-trip: compact overwrites to the descendant PixelRestore compact minimum (80)...
            window.Height = CompactInner + ChromeOverheadFor(window, root);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("compactHeight", root.Classes);
            Assert.Equal(80, configGrid.RowDefinitions[3].Height.Value);
            Assert.Equal(80, configGrid.RowDefinitions[3].MinHeight);

            // ...and restoring must recover the DRAGGED height, not merely the original 150.
            // Threshold+12 (RestoreSlack), NOT ExpandedInner (Threshold+1): CompactHeightBehavior's
            // restore-only hysteresis needs height >= Threshold+12 to re-expand an ALREADY-compact
            // instance — ExpandedInner is only sufficient for a FRESH instance's first evaluation.
            window.Height = (Threshold + 12) + ChromeOverheadFor(window, root);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("compactHeight", root.Classes);
            Assert.True(Math.Abs(draggedHeight - configGrid.RowDefinitions[3].Height.Value) < 0.5,
                $"restoring from compact must recover the user's DRAGGED height ({draggedHeight:F1}), not just the authored NormalHeight (150) — got {configGrid.RowDefinitions[3].Height.Value:F1}");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// <see cref="CompactViewRig.HostAt"/> only ever sets <c>window.Height</c> ONCE per call
    /// (never re-targets an already-shown window — see its own remarks on why a transient
    /// under/over-shoot could wrongly latch <c>CompactHeightBehavior</c>'s hysteresis). This test
    /// genuinely needs to resize the SAME live window twice (drag, then compact, then restore) to
    /// prove the round-trip, so it reproduces the rig's own chrome-overhead arithmetic directly on
    /// the ALREADY-open window instead.
    /// </summary>
    private static double ChromeOverheadFor(Window window, Grid innerRoot) => window.Height - innerRoot.Bounds.Height;

    /// <summary>
    /// In compact mode the Stored Files row is fixed at its 80-DIP PixelRestore floor regardless of
    /// content — with enough rows to exceed that height, the DataGrid's OWN internal virtualized
    /// scrollbar (not the outer config-band ScrollViewer) is what reaches the remaining rows.
    /// </summary>
    [AvaloniaFact]
    public void CompactMode_StoredFilesRowIsEighty_GridScrollsInternally()
    {
        CreatorViewModel vm = CreateVm();
        for (int i = 0; i < 8; i++)
        {
            vm.StoredFiles.Add(Item($@"C:\release\file{i:D2}.nfo", $"file{i:D2}.nfo"));
        }
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Assert.Contains("compactHeight", root.Classes);
            Grid configGrid = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "ConfigGrid");
            Assert.Equal(80, configGrid.RowDefinitions[3].Height.Value);
            Assert.Equal(80, configGrid.RowDefinitions[3].MinHeight);

            DataGrid grid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "StoredFilesGrid");
            Assert.True(grid.Bounds.Height <= 80 + 0.5, $"grid's own rendered height ({grid.Bounds.Height:F1}) should be pinned to the 80-DIP compact row");

            ScrollBar gridBar = grid.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Vertical);
            Assert.True(gridBar.Maximum > 0, "8 rows inside an 80-DIP grid must need the grid's OWN internal virtualization scroll");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Fix round 1 (codex finding 4): real arrow-key/current-row visibility coverage for
    /// StoredFilesGrid's own <c>ScrollHandoffBehavior.Handoff="True"</c> wiring — disclosed as a
    /// gap in the original task report (concern 4) because, UNLIKE SampleRestorer's SRSEntriesGrid,
    /// this grid has no per-row focusable control (a plain two-column text grid, no checkbox
    /// column) to individually target. Mirrors SampleRestorer's own
    /// <c>Handoff_KeyboardNavigation_ChainsBringIntoViewToOuterViewer_BottomRowEndsFullyVisible</c>
    /// adapted to that difference: focuses the GRID CONTROL ITSELF directly (confirmed, via a
    /// throwaway diagnostic, to be the genuine keyboard entry point here — ordinary arrow-key
    /// browsing on this grid shape never moves focus off the grid onto a cell/row, only its own
    /// <c>SelectedIndex</c>/current-cell state, exactly as <c>ScrollHandoffBehavior</c>'s own
    /// remarks document for DataGrid generally), then drives real ArrowDown presses deep into a
    /// 12-row, virtualized, compact (80-DIP) grid and asserts the LAST row ends fully
    /// clip-aware-visible in the window — proving <c>ScrollHandoffBehavior</c>'s
    /// <c>CurrentCellChanged</c>-&gt;<c>BringIntoView</c> chain reaches the config band's own outer
    /// ScrollViewer even without a per-row control to focus.
    /// </summary>
    [AvaloniaFact]
    public void Handoff_KeyboardArrowNavigation_ChainsBringIntoViewToOuterViewer_LastRowEndsFullyVisible()
    {
        CreatorViewModel vm = CreateVm();
        for (int i = 0; i < 12; i++)
        {
            vm.StoredFiles.Add(Item($@"C:\release\file{i:D2}.nfo", $"file{i:D2}.nfo"));
        }
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            ScrollViewer configScroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(sv => Grid.GetRow(sv) == 1);
            configScroller.Offset = default;
            Dispatcher.UIThread.RunJobs();

            DataGrid grid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "StoredFilesGrid");
            grid.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(grid.IsFocused, "test precondition: the grid control itself (not a per-row control, which this grid shape has none of) must be the genuine keyboard entry point");

            for (int i = 0; i < vm.StoredFiles.Count - 1; i++)
            {
                window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(vm.StoredFiles.Count - 1, grid.SelectedIndex);
            Assert.True(grid.IsFocused, "focus should stay on the grid control throughout ordinary (non-edit) arrow-key browsing");

            DataGridRow lastRow = grid.GetVisualDescendants().OfType<DataGridRow>()
                .Single(r => ReferenceEquals(r.DataContext, vm.StoredFiles[^1]));
            AssertFullyWithinWindow(lastRow, window);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Wheel at the grid's own extent moves the config band's OWN (band 1) scroller — the platform
    /// default (<c>ScrollViewer.IsScrollChainingEnabled</c>, never overridden in this app), NOT a
    /// custom mechanism: <see cref="ScrollHandoffBehavior"/>'s own wheel path was removed entirely
    /// (Task 5's final ruling — see its own remarks) after being proven redundant with the platform
    /// default. Mirrors <c>SampleRestorerCompactTests.WheelHandoffAtGridExtent_...</c>'s identical
    /// regression guard, adapted: this grid sits at a fixed ConfigGrid ROW (not inside a StackPanel
    /// section), and the grid needs no separate "reveal" stage the way SampleRestorer's did — the
    /// Stored Files row is close enough to compact band 1's own top that a couple of ticks already
    /// bring some sliver of the grid into view from the default (offset-zero) start.
    /// </summary>
    [AvaloniaFact]
    public void WheelHandoffAtGridExtent_PlatformDefaultMovesConfigBandScroller()
    {
        CreatorViewModel vm = CreateVm();
        for (int i = 0; i < 12; i++)
        {
            vm.StoredFiles.Add(Item($@"C:\release\file{i:D2}.nfo", $"file{i:D2}.nfo"));
        }
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            DataGrid grid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "StoredFilesGrid");
            ScrollBar gridBar = grid.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Vertical);
            ScrollViewer configScroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(sv => Grid.GetRow(sv) == 1);

            const int MaxRevealTicks = 200;
            for (int tick = 0; tick < MaxRevealTicks && TryVisibleCenterInWindow(grid, window) is null; tick++)
            {
                window.MouseWheel(VisibleCenterInWindow(configScroller, window), new Vector(0, -1));
                Dispatcher.UIThread.RunJobs();
            }
            Assert.NotNull(TryVisibleCenterInWindow(grid, window)); // test precondition: the grid must have SOME visible sliver to wheel "at" it

            const int MaxDriveTicks = 200;
            for (int tick = 0; tick < MaxDriveTicks && gridBar.Value < gridBar.Maximum; tick++)
            {
                window.MouseWheel(VisibleCenterInWindow(grid, window), new Vector(0, -1));
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(gridBar.Value >= gridBar.Maximum, "test precondition: the grid must genuinely reach its own bottom extent");

            Assert.True(configScroller.Extent.Height > configScroller.Viewport.Height,
                "test precondition: the config band must have genuine room to scroll for the hand-off to be observable");

            double gridBefore = gridBar.Value;
            double outerBefore = configScroller.Offset.Y;

            window.MouseWheel(VisibleCenterInWindow(grid, window), new Vector(0, -1));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(gridBefore, gridBar.Value); // the grid itself did not move further
            Assert.True(configScroller.Offset.Y > outerBefore,
                $"the config band's own scroller should have moved from {outerBefore}, was {configScroller.Offset.Y}");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The center of <paramref name="element"/>'s TRUE visible region — the intersection of its own
    /// translated bounds against EVERY <c>ClipToBounds</c> ancestor's own translated bounds.
    /// Mirrors <c>SampleRestorerCompactTests</c>'s own identical helper. Returns null (never
    /// throws) when the element has no visible region at all.
    /// </summary>
    private static Point? TryVisibleCenterInWindow(Control element, Window window)
    {
        if (!element.IsAttachedToVisualTree() || !element.IsEffectivelyVisible)
        {
            return null;
        }

        if (TransformRect(element, new Rect(element.Bounds.Size), window) is not { } elementInWindow)
        {
            return null;
        }

        Rect visible = new(window.Bounds.Size);
        foreach (Visual ancestor in element.GetVisualAncestors())
        {
            if (ancestor is not Control clipper || !clipper.ClipToBounds)
            {
                continue;
            }

            if (TransformRect(clipper, new Rect(clipper.Bounds.Size), window) is not { } clipperInWindow)
            {
                return null;
            }

            visible = visible.Intersect(clipperInWindow);
        }

        visible = visible.Intersect(elementInWindow);
        return visible.Width > 0 && visible.Height > 0
            ? new Point(visible.X + (visible.Width / 2), visible.Y + (visible.Height / 2))
            : null;
    }

    private static Point VisibleCenterInWindow(Control element, Window window) =>
        TryVisibleCenterInWindow(element, window)
            ?? throw new InvalidOperationException($"{element.GetType().Name} has no visible (clip-aware) region at all.");

    // ── 7. Detected-sets bounding (verifying the EXISTING cap holds inside the new structure) ──

    [AvaloniaFact]
    public void DetectedSetsRegion_With12Sets_StaysWithinExisting96Cap_Compact() =>
        AssertDetectedSetsRegionStaysWithinCap(CompactInner, expectCompact: true);

    [AvaloniaFact]
    public void DetectedSetsRegion_With12Sets_StaysWithinExisting96Cap_Normal() =>
        AssertDetectedSetsRegionStaysWithinCap(ExpandedInner, expectCompact: false);

    private static void AssertDetectedSetsRegionStaysWithinCap(double innerHeight, bool expectCompact)
    {
        CreatorViewModel vm = CreateVm();
        for (int i = 0; i < 12; i++)
        {
            vm.DetectedSets.Add(new ReleaseSetInput($@"C:\release\disc{i:D2}\movie.sfv", $"disc{i:D2}/movie.sfv"));
        }
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, innerHeight);
        try
        {
            Assert.Equal(expectCompact, root.Classes.Contains("compactHeight"));
            Assert.True(vm.HasDetectedSets);

            ScrollViewer detectedSetsScroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(sv => sv.MaxHeight == 96);
            Assert.True(detectedSetsScroller.IsVisible);
            Assert.True(detectedSetsScroller.Bounds.Height <= 96.5,
                $"detected-sets region height {detectedSetsScroller.Bounds.Height:F1} exceeds the existing 96-DIP cap");

            // The cap must be doing real work here, not merely never binding — 12 realistic-length
            // relative names comfortably exceed 96 DIPs of natural content height.
            Assert.True(detectedSetsScroller.Extent.Height > detectedSetsScroller.Viewport.Height,
                "test precondition: 12 detected sets must genuinely overflow the 96-DIP cap for it to prove anything");
        }
        finally { window.Close(); }
    }

    // ── LabeledBy audit: computed UIA names resolve to their own header (fix round 1, codex finding 5) ──

    /// <summary>
    /// Both grids/lists in this view use <c>AutomationProperties.LabeledBy</c> to pair themselves
    /// with a sibling header TextBlock (mirrors SampleRestorer's own
    /// <c>SRSEntriesGrid_UIAName_ResolvesToEmbeddedSRSFilesHeader</c>) — resolved via the REAL
    /// automation peer, not the raw attached property, so this proves what a screen reader
    /// actually announces on landing here, not merely that the XAML attribute exists.
    /// </summary>
    [AvaloniaFact]
    public void LabeledByAudit_StoredFilesGridAndLogList_ResolveToTheirOwnHeaders()
    {
        CreatorViewModel vm = CreateVm();
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            DataGrid storedFilesGrid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "StoredFilesGrid");
            Assert.Equal("Stored Files", ControlAutomationPeer.CreatePeerForElement(storedFilesGrid).GetName());

            ListBox logList = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("logList"));
            Assert.Equal("Log", ControlAutomationPeer.CreatePeerForElement(logList).GetName());
        }
        finally { window.Close(); }
    }

    // ── 8. Frame-rig parity (criterion F: normal-mode pixels unchanged) + splitter (criterion E) ──

    /// <summary>
    /// Same technique as every other converted view's own hardened version (RenderTargetBitmap +
    /// CopyPixels, exact integer pixel size gate BEFORE any byte read, full-buffer compare — no
    /// mask/crop/intersection). LOCAL copy of <c>AssertFullRasterPixelIdentity</c> /
    /// <c>RenderToPixelBuffer</c> (not promoted into the shared rig — promotion is an open
    /// controller decision, per every other converted view's own identical note).
    /// </summary>
    [AvaloniaFact]
    public void FrameRig_NormalMode_HeaderRegionMatchesPreDisclosureShape()
    {
        CreatorViewModel vm = CreateVm();
        var view = new CreatorView { DataContext = vm };
        (Window newWindow, Grid newRoot) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            Assert.DoesNotContain("compactHeight", newRoot.Classes);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            Control newRow0 = newRoot.Children.OfType<Control>().Single(c => Grid.GetRow(c) == 0);
            Size newRowSize = newRow0.Bounds.Size;

            TextBlock newCaption = newRow0.GetVisualDescendants().OfType<TextBlock>().Single();
            Size newCaptionSize = newCaption.Bounds.Size;

            Window oldWindow = BuildPreDisclosureRow0Window();
            try
            {
                oldWindow.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Control oldRow0 = (Control)oldWindow.Content!;
                Size oldSize = oldRow0.Bounds.Size;

                Assert.Equal(oldSize.Height, newRowSize.Height, precision: 0);

                // The intro TextBlock's own documented, intentional inset (Margin="0,0,4,0", "per
                // house rule" — matches SRSCreator/Reconstructor's own identical value; this view's
                // intro sentence, like SRSCreator's own, does not push any word across a line-break
                // boundary at the narrower measure, confirmed by the byte-for-byte raster check
                // below).
                double widthNarrowing = oldSize.Width - newCaptionSize.Width;
                Assert.Equal(4.0, widthNarrowing, precision: 0);

                Assert.Equal(oldSize.Width, newRowSize.Width, precision: 0);

                AssertFullRasterPixelIdentity(oldRow0, oldSize, newRow0, newRowSize);
            }
            finally { oldWindow.Close(); }
        }
        finally { newWindow.Close(); }
    }

    /// <summary>
    /// Extends the raster comparison beyond row 0 (every other converted view's own established
    /// practice once a view's config band content is genuinely re-hosted): the Input section's own
    /// caption is band 1's FIRST rendered content, now nested THREE levels deeper than before
    /// (ScrollViewer &gt; ConfigGrid &gt; StackPanel, vs. directly under the old flat Grid) — the
    /// most direct, cheapest proof that none of those new pass-through containers silently narrows
    /// or insets it (no compactScrollInset-style class was added here — deliberately: unlike
    /// SampleRestorer's own StackPanel, which never existed before this kind of task touched it,
    /// this Input StackPanel is genuinely pre-existing markup with a pre-existing zero margin, and
    /// nothing about this task changes that; this test is what proves it, rather than assuming it).
    /// </summary>
    [AvaloniaFact]
    public void FrameRig_NormalMode_InputCaptionMatchesPreChangeShape()
    {
        CreatorViewModel vm = CreateVm();
        var view = new CreatorView { DataContext = vm };
        (Window newWindow, Grid newRoot) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            Assert.DoesNotContain("compactHeight", newRoot.Classes);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            ScrollViewer configScroller = newRoot.Children.OfType<ScrollViewer>().Single(sv => Grid.GetRow(sv) == 1);
            TextBlock newCaption = configScroller.GetVisualDescendants().OfType<TextBlock>()
                .Single(tb => tb.Inlines is [Run { Text: "Input " }, ..]);
            Size newCaptionSize = newCaption.Bounds.Size;

            Window oldWindow = BuildPreConversionInputCaptionWindow();
            try
            {
                oldWindow.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Control oldCaption = (Control)oldWindow.Content!;
                Size oldSize = oldCaption.Bounds.Size;

                Assert.Equal(oldSize.Height, newCaptionSize.Height, precision: 0);

                // Zero narrowing at normal size: no inset class was applied to the Input section's
                // own StackPanel, and the wrapping ScrollViewer (VerticalScrollBarVisibility="Auto")
                // reserves no track while nothing is actually scrolling.
                Assert.Equal(oldSize.Width, newCaptionSize.Width, precision: 0);

                AssertFullRasterPixelIdentity(oldCaption, oldSize, newCaption, newCaptionSize);
            }
            finally { oldWindow.Close(); }
        }
        finally { newWindow.Close(); }
    }

    /// <summary>
    /// Fix round 1 (codex finding 2, MAJOR): the two band-scoped tests above prove SPECIFIC
    /// regions; this proves EVERY conversion-touched band at once, at REST, by comparing the
    /// ENTIRE root — the strongest, most direct answer to "every conversion-touched band must be
    /// inside compared pixels." <see cref="OldFullMarkup"/> is the pre-task
    /// <c>CreatorView.axaml</c> VERBATIM (git blob <c>67aa5e8:ReScene.Manager/Views/CreatorView.axaml</c>,
    /// <c>x:Class</c> stripped so <see cref="AvaloniaRuntimeXamlLoader"/> can parse it as a plain
    /// <see cref="UserControl"/>), loaded through the REAL XAML pipeline — deliberately NOT a
    /// hand-built C# object graph like <see cref="BuildPreDisclosureRow0Window"/> /
    /// <see cref="BuildPreConversionInputCaptionWindow"/> above, which each needed a follow-up fix
    /// for missed implicit whitespace <c>Run</c>s (see their own remarks): parsing the frozen,
    /// verbatim XAML STRING through the actual compiler eliminates that entire class of
    /// reconstruction bug structurally, for the whole page at once, rather than one hand-copied
    /// band at a time. <c>typeof(CreatorView).Assembly</c> (not <c>typeof(UserControl).Assembly</c>,
    /// which was tried first and failed to resolve <c>clr-namespace:ReScene.Manager.Controls</c> /
    /// <c>.Behaviors</c>) is what lets the parser resolve <c>controls:FieldStatusLine</c> and
    /// <c>behaviors:TextBoxDropBehavior</c>.
    /// <para>
    /// MEASURED (a throwaway diagnostic, same technique as this test): both roots render at the
    /// EXACT SAME 676x721 integer pixel size, and the ENTIRE 1,949,584-byte buffer (676x721x4) is
    /// byte-for-byte identical — zero differing bytes, not merely zero found through spot-checks.
    /// This is the comprehensive, whole-page confirmation the two narrower band tests above could
    /// only ever sample.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void FrameRig_NormalMode_FullRootMatchesPreChangeMarkup()
    {
        var oldView = (UserControl)AvaloniaRuntimeXamlLoader.Parse(OldFullMarkup, typeof(CreatorView).Assembly);
        oldView.DataContext = CreateVm();
        (Window oldWindow, Grid oldRoot) = CompactViewRig.HostAt(oldView, ExpandedInner);
        try
        {
            var newView = new CreatorView { DataContext = CreateVm() };
            (Window newWindow, Grid newRoot) = CompactViewRig.HostAt(newView, ExpandedInner);
            try
            {
                Assert.DoesNotContain("compactHeight", newRoot.Classes);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();

                AssertFullRasterPixelIdentity(oldRoot, oldRoot.Bounds.Size, newRoot, newRoot.Bounds.Size);
            }
            finally { newWindow.Close(); }
        }
        finally { oldWindow.Close(); }
    }

    /// <summary>
    /// Fix round 1 (codex finding 2's own "splitter rest/drag states" clause). A genuine
    /// OLD-vs-NEW comparison of a DRAGGED state is not meaningful and was deliberately NOT
    /// attempted: dragging the OLD splitter's "Next" pane (outer row 6, a Star-sized row
    /// containing the ENTIRE Output+Options+Action+Log composite) is not the same operation as
    /// dragging the NEW splitter's "Next" pane (ConfigGrid row 5, the Output section alone, Auto-
    /// sized) — this task's own restructuring genuinely changes what the splitter's "Next" pane
    /// IS, per the brief's own explicit row layout, so the two are not comparable at any dragged
    /// offset; MEASURED directly (confirmed via <c>Splitter_FocusableAndNamed_...</c> above): the
    /// NEW structure's row 5 stays plain <c>Auto</c> throughout a drag (there is ScrollViewer slack
    /// to absorb the growth), which the OLD structure's Star-sized composite row could never do.
    /// <para>
    /// The meaningful, ACHIEVABLE equivalent instead: capture the NEW view's own full-root raster
    /// at REST, engage a real, input-driven drag (ArrowDown — genuine keyboard input, matching
    /// this file's own established "never a synthetic property poke" discipline), capture again
    /// (basic sanity: the Stored Files row grew, nothing negative/degenerate), release the drag
    /// back to EXACTLY the original 150 (ArrowUp the same count — already independently proven
    /// exact by <see cref="StoredFilesRow_SplitterDragAtNormalSize_ResizesRow_AndDragSurvivesCompactRoundTrip"/>),
    /// and assert the FULL-ROOT RASTER after release matches the ORIGINAL rest capture BYTE FOR
    /// BYTE — proving a drag-then-undo cycle leaves no residual visual/layout drift anywhere on
    /// the page, not just that the one row's numeric Height value round-trips.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void FrameRig_NormalMode_SplitterDragThenRelease_FullRootRasterReturnsToRestState()
    {
        CreatorViewModel vm = CreateVm();
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            Grid configGrid = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "ConfigGrid");
            GridSplitter splitter = window.GetVisualDescendants().OfType<GridSplitter>().Single();
            Assert.Equal(150, configGrid.RowDefinitions[3].Height.Value);

            // Focus BEFORE capturing the rest baseline (a real bug this test's own first draft
            // hit): the splitter must stay focused throughout the whole drag/release cycle so its
            // OWN :focus-visual color (a real, EXPECTED, and separately-tested difference — see
            // Splitter_FocusVisual_MeetsContrastAgainstBothPanes) is held CONSTANT across both
            // captures. Capturing "rest" before focusing would compare an unfocused splitter
            // against a still-focused one after release and misreport that expected, unrelated
            // color change as a residual layout defect.
            splitter.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(splitter.IsFocused);

            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            PixelSize restSize = new((int)Math.Ceiling(root.Bounds.Width), (int)Math.Ceiling(root.Bounds.Height));
            byte[] restPixels = RenderToPixelBuffer(root, restSize);

            PressManyTimes(window, PhysicalKey.ArrowDown, 10);
            double draggedHeight = configGrid.RowDefinitions[3].Height.Value;
            Assert.True(draggedHeight > 150, $"drag must genuinely resize row 3, was {draggedHeight:F1}");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            // Basic sanity on the DRAGGED capture: the page must still render at a sane, positive,
            // unchanged OUTER size (only row 3's internal allocation changed) — not a claim of
            // parity against anything, since (per this test's own doc) no valid OLD-dragged
            // comparison exists.
            PixelSize draggedSize = new((int)Math.Ceiling(root.Bounds.Width), (int)Math.Ceiling(root.Bounds.Height));
            Assert.Equal(restSize, draggedSize);

            PressManyTimes(window, PhysicalKey.ArrowUp, 10);
            Assert.Equal(150, configGrid.RowDefinitions[3].Height.Value);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            PixelSize releasedSize = new((int)Math.Ceiling(root.Bounds.Width), (int)Math.Ceiling(root.Bounds.Height));
            Assert.Equal(restSize, releasedSize);

            byte[] releasedPixels = RenderToPixelBuffer(root, releasedSize);
            Assert.Equal(restPixels.Length, releasedPixels.Length);
            for (int i = 0; i < restPixels.Length; i++)
            {
                if (restPixels[i] != releasedPixels[i])
                {
                    int stride = restSize.Width * 4;
                    Assert.Fail(
                        $"drag-then-release left a residual pixel difference at ({i % stride / 4}, {i / stride}) — " +
                        $"rest byte 0x{restPixels[i]:X2} vs post-release byte 0x{releasedPixels[i]:X2}.");
                }
            }
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Verbatim reconstruction of the pre-task <c>CreatorView.axaml</c> (git blob
    /// <c>67aa5e8:ReScene.Manager/Views/CreatorView.axaml</c>) as a raw XAML string, <c>x:Class</c>
    /// stripped so it can be loaded via <see cref="AvaloniaRuntimeXamlLoader"/> as a plain
    /// <see cref="UserControl"/> — see <see cref="FrameRig_NormalMode_FullRootMatchesPreChangeMarkup"/>'s
    /// own doc for why parsing the frozen, verbatim markup through the real XAML pipeline (rather
    /// than a hand-built C# object graph) is used for the full-page comparison specifically.
    /// </summary>
    private const string OldFullMarkup = """
        <UserControl xmlns="https://github.com/avaloniaui"
                     xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                     xmlns:controls="clr-namespace:ReScene.Manager.Controls"
                     xmlns:behaviors="clr-namespace:ReScene.Manager.Behaviors"
                     x:CompileBindings="False">

          <Grid Margin="{DynamicResource PageMargin}">
            <Grid.RowDefinitions>
              <RowDefinition Height="Auto" />
              <RowDefinition Height="Auto" />
              <RowDefinition Height="Auto" />
              <RowDefinition Height="Auto" />
              <RowDefinition Height="150" MinHeight="150" />
              <RowDefinition Height="Auto" />
              <RowDefinition Height="*" MinHeight="100" />
            </Grid.RowDefinitions>

            <TextBlock Grid.Row="0"
                       Text="Create an SRR (Scene Release Rescue) file from a RAR archive set. The SRR captures RAR headers and metadata needed to reconstruct the original archives later."
                       Foreground="{DynamicResource ForegroundSecondary}" FontSize="{DynamicResource FontSizeCaption}"
                       TextWrapping="Wrap" Margin="0,0,0,6" />

            <StackPanel Grid.Row="1">
              <TextBlock TextWrapping="Wrap" Margin="0,0,0,2">
                <Run Text="Input " FontWeight="SemiBold" />
                <Run Text="— use " Foreground="{DynamicResource ForegroundSecondary}" FontSize="{DynamicResource FontSizeCaption}" />
                <Run Text="Browse" FontWeight="SemiBold" Foreground="{DynamicResource ForegroundSecondary}" FontSize="{DynamicResource FontSizeCaption}" />
                <Run Text=" for a single set's .sfv or first .rar, or " Foreground="{DynamicResource ForegroundSecondary}" FontSize="{DynamicResource FontSizeCaption}" />
                <Run Text="Browse folder…" FontWeight="SemiBold" Foreground="{DynamicResource ForegroundSecondary}" FontSize="{DynamicResource FontSizeCaption}" />
                <Run Text=" to search a release folder and its subfolders for RAR sets (e.g. multi-disc releases)." Foreground="{DynamicResource ForegroundSecondary}" FontSize="{DynamicResource FontSizeCaption}" />
              </TextBlock>
              <DockPanel Margin="0,0,0,2">
                <Button DockPanel.Dock="Right" Content="Browse folder…"
                        Command="{Binding BrowseInputFolderCommand}"
                        Classes="ghost"
                        TabIndex="2"
                        AutomationProperties.Name="Browse folder for release input"
                        AutomationProperties.HelpText="Pick a release folder to search it and its subfolders for RAR sets."
                        Margin="4,0,0,0" MinWidth="75" />
                <Button DockPanel.Dock="Right" Content="Browse"
                        Command="{Binding BrowseInputCommand}"
                        Classes="ghost"
                        TabIndex="1"
                        AutomationProperties.Name="Browse input file"
                        AutomationProperties.HelpText="Pick a single set's .sfv or first .rar file."
                        Margin="4,0,0,0" MinWidth="75" />
                <TextBox x:Name="InputTextBox" Text="{Binding InputPath}"
                         TabIndex="0"
                         AutomationProperties.Name="Input path"
                         AutomationProperties.HelpText="Accepts a release .sfv/.rar file path or a release folder path"
                         behaviors:TextBoxDropBehavior.DropMode="File" />
              </DockPanel>
              <ProgressBar IsIndeterminate="True" IsVisible="{Binding IsScanning}" Height="4"
                           Margin="0,0,0,2"
                           AutomationProperties.Name="Scanning release folder" />
              <ScrollViewer VerticalScrollBarVisibility="Auto" ScrollViewer.AllowAutoHide="False" MaxHeight="96" IsVisible="{Binding HasDetectedSets}">
                <ItemsControl ItemsSource="{Binding DetectedSets}"
                              AutomationProperties.Name="{Binding DetectedSetsSummary}">
                  <ItemsControl.ItemTemplate>
                    <DataTemplate>
                      <TextBlock Text="{Binding RelativeName}" />
                    </DataTemplate>
                  </ItemsControl.ItemTemplate>
                </ItemsControl>
              </ScrollViewer>
              <controls:FieldStatusLine Status="{Binding InputStatus}" />
            </StackPanel>

            <Border Grid.Row="2" Height="1" Background="{DynamicResource BorderSeparator}" Margin="0,4" />

            <StackPanel Grid.Row="3">
              <TextBlock Text="Stored Files" FontWeight="SemiBold" Margin="0,0,0,2" />
              <DockPanel Margin="0,0,0,2">
                <StackPanel Orientation="Horizontal">
                  <Button Content="Add..." Command="{Binding AddStoredFileCommand}" Classes="ghost" Margin="0,0,4,0" />
                  <Button Content="Remove" Command="{Binding RemoveStoredFileCommand}" Classes="ghost" Margin="0,0,4,0" />
                  <Button Content="Remove All" Command="{Binding RemoveAllStoredFilesCommand}" Classes="ghost" Margin="0,0,4,0" />
                  <Button Content="Move Up" Command="{Binding MoveStoredFileUpCommand}" Classes="ghost" Margin="0,0,4,0" />
                  <Button Content="Move Down" Command="{Binding MoveStoredFileDownCommand}" Classes="ghost" />
                </StackPanel>
                <TextBlock Text="Double-click the Stored As column to edit the name used in the SRR."
                           VerticalAlignment="Center" Margin="8,0,0,0"
                           Foreground="{DynamicResource ForegroundSecondary}" FontSize="{DynamicResource FontSizeCaption}" />
              </DockPanel>
            </StackPanel>

            <DataGrid x:Name="StoredFilesGrid"
                      Grid.Row="4"
                      ItemsSource="{Binding StoredFiles}"
                      SelectedItem="{Binding SelectedStoredFile}"
                      AutoGenerateColumns="False"
                      CanUserReorderColumns="False"
                      CanUserSortColumns="False"
                      GridLinesVisibility="Horizontal"
                      HeadersVisibility="Column"
                      SelectionMode="Single"
                      BorderThickness="0">
              <DataGrid.Columns>
                <DataGridTextColumn Header="File Path" Binding="{Binding FullPath}" IsReadOnly="True" Width="*" />
                <DataGridTextColumn Header="Stored As" Binding="{Binding StoredName}" Width="450" />
              </DataGrid.Columns>
            </DataGrid>

            <GridSplitter Grid.Row="5" Height="5" HorizontalAlignment="Stretch"
                          VerticalAlignment="Center"
                          ResizeDirection="Rows"
                          ResizeBehavior="PreviousAndNext"
                          Background="Transparent" />

            <Grid Grid.Row="6">
              <Grid.RowDefinitions>
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="Auto" />
                <RowDefinition Height="*" MinHeight="40" />
              </Grid.RowDefinitions>

              <StackPanel Grid.Row="0">
                <TextBlock TextWrapping="Wrap" Margin="0,0,0,2">
                  <Run Text="Output " FontWeight="SemiBold" />
                  <Run Text="— Where the .srr file will be written."
                       Foreground="{DynamicResource ForegroundSecondary}" FontSize="{DynamicResource FontSizeCaption}" />
                </TextBlock>
                <DockPanel>
                  <Button DockPanel.Dock="Right" Content="Browse"
                          Command="{Binding BrowseOutputCommand}"
                          Classes="ghost"
                          Margin="4,0,0,0" MinWidth="75" />
                  <TextBox x:Name="OutputTextBox" Text="{Binding OutputPath}"
                           behaviors:TextBoxDropBehavior.DropMode="File" />
                </DockPanel>
                <controls:FieldStatusLine Status="{Binding OutputStatus}" />
              </StackPanel>

              <Border Grid.Row="1" Height="1" Background="{DynamicResource BorderSeparator}" Margin="0,4" />

              <StackPanel Grid.Row="2">
                <TextBlock Text="Options" FontWeight="SemiBold" Margin="0,0,0,2" />
                <CheckBox Content="Auto-include files — Scan release directory for .nfo, .sfv, proof images, .m3u, .cue, .log files."
                          IsChecked="{Binding AutoIncludeFiles}" Margin="0,1" />
                <CheckBox Content="Auto-create SRS — Create .srs files for samples found in Sample/ subdirectory."
                          IsChecked="{Binding AutoCreateSRS}" Margin="0,1" />
                <CheckBox Content="Vobsub SRR — Create nested SRR files for subtitle archives found in Subs/ directories."
                          IsChecked="{Binding CreateVobsubSRR}" Margin="0,1" />
                <CheckBox Content="Store fix RAR — For fix/patch releases, store the main RAR file as proof."
                          IsChecked="{Binding StoreFixRAR}"
                          IsEnabled="{Binding IsFolderMode, Converter={StaticResource InverseBoolConverter}}"
                          AutomationProperties.HelpText="Automatic in folder mode — the release scan decides this"
                          Margin="0,1" />
                <CheckBox Content="Allow compressed — Accept RAR volumes that use compression (method != Store)."
                          IsChecked="{Binding AllowCompressed}" Margin="0,1" />
                <CheckBox Content="OSO hashes — Compute and store OpenSubtitles OSO hashes for archived files."
                          IsChecked="{Binding ComputeOSOHashes}" Margin="0,1" />
                <CheckBox Content="Languages.diz — Extract language metadata from VobSub .idx files and store in the SRR."
                          IsChecked="{Binding GenerateLanguagesDiz}" Margin="0,1,0,4" />
                <DockPanel Margin="0,0,0,2">
                  <TextBlock Text="App name:" VerticalAlignment="Center"
                             Margin="0,0,8,0" />
                  <TextBox Text="{Binding AppName}" Width="400"
                           HorizontalAlignment="Left" />
                  <TextBlock Text="Embedded in the SRR header to identify the creating application."
                             VerticalAlignment="Center" Margin="8,0,0,0"
                             Foreground="{DynamicResource ForegroundSecondary}" FontSize="{DynamicResource FontSizeCaption}" />
                </DockPanel>
              </StackPanel>

              <Border Grid.Row="3" Height="1" Background="{DynamicResource BorderSeparator}" Margin="0,4" />

              <StackPanel Grid.Row="4">
                <DockPanel Margin="0,0,0,2">
                  <StackPanel DockPanel.Dock="Right" Orientation="Horizontal">
                    <Button Content="Create SRR"
                            Command="{Binding CreateSRRCommand}"
                            Classes="primary"
                            Padding="16,4" Margin="0,0,4,0" />
                    <Button Content="Cancel"
                            Command="{Binding CancelCreationCommand}"
                            Classes="cancel"
                            IsVisible="{Binding IsCreating}"
                            Padding="16,4" />
                  </StackPanel>
                  <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                    <TextBlock Text="{Binding ActionHint}"
                               Foreground="{DynamicResource ForegroundSecondary}"
                               FontSize="{DynamicResource FontSizeCaption}"
                               VerticalAlignment="Center" />
                    <TextBlock Text="{Binding ProgressMessage}"
                               IsVisible="{Binding ShowProgress}"
                               VerticalAlignment="Center" />
                  </StackPanel>
                </DockPanel>
                <ProgressBar Value="{Binding ProgressPercent}"
                             Maximum="100" Height="18"
                             Margin="0,0,0,4"
                             IsVisible="{Binding ShowProgress}" />
              </StackPanel>

              <Border Grid.Row="5" Height="1" Background="{DynamicResource BorderSeparator}" Margin="0,4" />

              <DockPanel Grid.Row="6" Margin="0,0,0,2">
                <Button DockPanel.Dock="Right" Content="Save log..."
                        Command="{Binding SaveLogCommand}"
                        Classes="ghost"
                        Padding="8,2" />
                <TextBlock DockPanel.Dock="Left" Text="Log" FontWeight="SemiBold" VerticalAlignment="Center" />
                <TextBlock x:Name="SaveLogStatus" Text="{Binding SaveLogAnnouncement}"
                           AutomationProperties.LiveSetting="Polite"
                           Foreground="{DynamicResource ForegroundSecondary}" FontSize="{DynamicResource FontSizeCaption}"
                           TextTrimming="CharacterEllipsis" VerticalAlignment="Center" Margin="8,0" />
              </DockPanel>

              <ListBox Grid.Row="7"
                       ItemsSource="{Binding LogEntries}"
                       Classes="logList"
                       FontFamily="{DynamicResource MonoFontFamily}"
                       FontSize="{DynamicResource MonoFontSize}" />
            </Grid>

          </Grid>

        </UserControl>
        """;

    /// <summary>Verbatim reconstruction of CreatorView.axaml's row-0 TextBlock before this task (git history).</summary>
    private static Window BuildPreDisclosureRow0Window()
    {
        var textBlock = new TextBlock
        {
            Text = "Create an SRR (Scene Release Rescue) file from a RAR archive set. The SRR captures RAR headers and metadata needed to reconstruct the original archives later.",
            Foreground = (IBrush?)Application.Current!.FindResource("ForegroundSecondary"),
            FontSize = (double)Application.Current!.FindResource("FontSizeCaption")!,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };

        return new Window { Width = CompactInvariantRig.InnerWidth, SizeToContent = SizeToContent.Height, Content = textBlock };
    }

    /// <summary>
    /// Verbatim reconstruction of CreatorView.axaml's Input caption TextBlock before this task
    /// (git history). DIAGNOSED (same finding as SampleRestorerCompactTests' own identical
    /// reconstruction bug): the six &lt;Run&gt; elements sit on separate source lines in the XAML,
    /// and Avalonia's XAML parser collapses each inter-tag newline + indentation into an implicit,
    /// PLAIN (default-styled — does not inherit either neighbor's FontWeight/Foreground/FontSize)
    /// whitespace-only <see cref="Run"/> — CONFIRMED directly (a throwaway diagnostic dump of the
    /// real, live-hosted TextBlock's own <c>Inlines</c> showed exactly 11 entries: the 6 authored
    /// Runs plus 5 implicit " " Runs, one between each adjacent pair). Omitting them would silently
    /// drop 5 spaces and shift every pixel from the second Run onward.
    /// </summary>
    private static Window BuildPreConversionInputCaptionWindow()
    {
        var textBlock = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) };
        IBrush? secondary = (IBrush?)Application.Current!.FindResource("ForegroundSecondary");
        double captionSize = (double)Application.Current!.FindResource("FontSizeCaption")!;
        textBlock.Inlines!.Add(new Run { Text = "Input ", FontWeight = FontWeight.SemiBold });
        textBlock.Inlines!.Add(new Run { Text = " " });
        textBlock.Inlines!.Add(new Run { Text = "— use ", Foreground = secondary, FontSize = captionSize });
        textBlock.Inlines!.Add(new Run { Text = " " });
        textBlock.Inlines!.Add(new Run { Text = "Browse", FontWeight = FontWeight.SemiBold, Foreground = secondary, FontSize = captionSize });
        textBlock.Inlines!.Add(new Run { Text = " " });
        textBlock.Inlines!.Add(new Run { Text = " for a single set's .sfv or first .rar, or ", Foreground = secondary, FontSize = captionSize });
        textBlock.Inlines!.Add(new Run { Text = " " });
        textBlock.Inlines!.Add(new Run { Text = "Browse folder…", FontWeight = FontWeight.SemiBold, Foreground = secondary, FontSize = captionSize });
        textBlock.Inlines!.Add(new Run { Text = " " });
        textBlock.Inlines!.Add(new Run { Text = " to search a release folder and its subfolders for RAR sets (e.g. multi-disc releases).", Foreground = secondary, FontSize = captionSize });

        return new Window { Width = CompactInvariantRig.InnerWidth, SizeToContent = SizeToContent.Height, Content = textBlock };
    }

    /// <summary>
    /// Proves <see cref="AssertFullRasterPixelIdentity"/>'s size gate genuinely DISCRIMINATES — a
    /// capture-size disagreement fails loudly instead of silently shrinking to the intersection.
    /// Mirrors every other converted view's own identical covering test.
    /// </summary>
    [AvaloniaFact]
    public void AssertFullRasterPixelIdentity_SubDipDriftAcrossARasterLine_FailsInsteadOfShrinkingToTheIntersection()
    {
        CreatorViewModel vm = CreateVm();
        var view = new CreatorView { DataContext = vm };
        (Window newWindow, Grid newRoot) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            Control newRow0 = newRoot.Children.OfType<Control>().Single(c => Grid.GetRow(c) == 0);
            Size newRowSize = newRow0.Bounds.Size;

            Window oldWindow = BuildPreDisclosureRow0Window();
            try
            {
                oldWindow.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Control oldRow0 = (Control)oldWindow.Content!;
                Size oldSize = oldRow0.Bounds.Size;

                AssertDriftedSizeFails(new Size(DriftAcrossOneRasterLine(newRowSize.Width), newRowSize.Height));
                AssertDriftedSizeFails(new Size(newRowSize.Width, DriftAcrossOneRasterLine(newRowSize.Height)));

                AssertFullRasterPixelIdentity(oldRow0, oldSize, newRow0, newRowSize);

                void AssertDriftedSizeFails(Size drifted)
                {
                    Assert.Equal(oldSize.Width, drifted.Width, precision: 0);
                    Assert.Equal(oldSize.Height, drifted.Height, precision: 0);

                    Assert.NotEqual(
                        new PixelSize((int)Math.Ceiling(oldSize.Width), (int)Math.Ceiling(oldSize.Height)),
                        new PixelSize((int)Math.Ceiling(drifted.Width), (int)Math.Ceiling(drifted.Height)));

                    Xunit.Sdk.FailException ex = Assert.Throws<Xunit.Sdk.FailException>(
                        () => AssertFullRasterPixelIdentity(oldRow0, oldSize, newRow0, drifted));
                    Assert.Contains("EXACTLY the same integer pixel size", ex.Message, StringComparison.Ordinal);
                    Assert.Contains($"{drifted.Width:F4}x{drifted.Height:F4}", ex.Message, StringComparison.Ordinal);
                }
            }
            finally { oldWindow.Close(); }
        }
        finally { newWindow.Close(); }
    }

    private static double DriftAcrossOneRasterLine(double value) =>
        Math.Ceiling(value) == Math.Round(value) ? Math.Ceiling(value) + 0.4 : Math.Round(value);

    /// <summary>
    /// Renders both controls to a <see cref="RenderTargetBitmap"/> at their OWN true geometry and
    /// requires true byte-for-byte identity of the ENTIRE buffer on BOTH sides — no mask, no crop,
    /// no intersection, no offset. Local copy of every other converted view's own hardened helper.
    /// </summary>
    private static void AssertFullRasterPixelIdentity(Control oldControl, Size oldSize, Control newControl, Size newSize)
    {
        const int BytesPerPixel = 4;

        var oldPixelSize = new PixelSize((int)Math.Ceiling(oldSize.Width), (int)Math.Ceiling(oldSize.Height));
        var newPixelSize = new PixelSize((int)Math.Ceiling(newSize.Width), (int)Math.Ceiling(newSize.Height));

        if (oldPixelSize != newPixelSize)
        {
            Assert.Fail(
                $"the two captures must rasterise to EXACTLY the same integer pixel size before any " +
                $"comparison is meaningful — old {oldPixelSize} (bounds {oldSize.Width:F4}x{oldSize.Height:F4}) " +
                $"vs new {newPixelSize} (bounds {newSize.Width:F4}x{newSize.Height:F4}). A disagreement means " +
                "one capture has a raster column or row with no counterpart in the other; comparing their " +
                "intersection instead would leave that line unproven while still reporting full parity.");
        }

        PixelSize rasterSize = oldPixelSize;
        Assert.True(rasterSize.Width > 0 && rasterSize.Height > 0,
            $"nothing to compare — both captures rasterise to {rasterSize}.");

        byte[] oldPixels = RenderToPixelBuffer(oldControl, rasterSize);
        byte[] newPixels = RenderToPixelBuffer(newControl, rasterSize);

        int stride = rasterSize.Width * BytesPerPixel;

        for (int i = 0; i < oldPixels.Length; i++)
        {
            if (oldPixels[i] == newPixels[i])
            {
                continue;
            }

            Assert.Fail(
                $"header pixel mismatch at ({i % stride / BytesPerPixel}, {i / stride}) — old byte " +
                $"0x{oldPixels[i]:X2} vs new byte 0x{newPixels[i]:X2}. Both captures are {rasterSize} " +
                $"({oldPixels.Length} bytes each) and every byte of both is compared: no mask, no crop, " +
                "no intersection.");
        }
    }

    private static byte[] RenderToPixelBuffer(Control control, PixelSize size)
    {
        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(control);

        byte[] buffer = new byte[size.Width * size.Height * 4];
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), buffer.Length, size.Width * 4);
        }
        finally
        {
            handle.Free();
        }

        return buffer;
    }

    // ── Splitter (criterion E: tab-reachable, Up/Down-resizable, bounded by pane minimums, ──
    // ── visible >=3:1 focus indication) ─────────────────────────────────────────────────────

    /// <summary>
    /// Criterion E scoped to NORMAL size for this IN-SCROLLER splitter (task brief): unlike
    /// Reconstructor's top-level splitter (bounded by two compact-shrinkable panes), this
    /// splitter's own "previous" pane (ConfigGrid row 3) has a HARD compact floor of 80 delivered
    /// by the descendant PixelRestore entry, not by dragging — the splitter's pane-minimum bound is
    /// therefore only a meaningful, exercisable claim at NORMAL size (compact's 80 is fixed
    /// regardless of any drag). It stays focusable/operable in BOTH modes regardless (no
    /// compact-only Focusable override exists anywhere on GridSplitter).
    /// </summary>
    [AvaloniaFact]
    public void Splitter_FocusableAndNamed_UpDownResizesAtNormalSize_ClampsAtMinimum()
    {
        CreatorViewModel vm = CreateVm();
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            Assert.DoesNotContain("compactHeight", root.Classes);
            GridSplitter splitter = window.GetVisualDescendants().OfType<GridSplitter>().Single();
            Assert.Equal("Resize stored files and output", AutomationProperties.GetName(splitter));

            Grid configGrid = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Name == "ConfigGrid");
            Assert.Equal(150, configGrid.RowDefinitions[3].MinHeight);

            splitter.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(splitter.IsFocused);

            // Up shrinks row 3 toward its own 150-DIP MinHeight (unchanged, normal-mode authored
            // value — the compact 80 floor never applies here).
            PressManyTimes(window, PhysicalKey.ArrowUp, 40);
            Assert.True(configGrid.RowDefinitions[3].Height.Value >= 150 - 0.5,
                $"row 3 clamped below its 150-DIP normal-mode minimum: {configGrid.RowDefinitions[3].Height.Value:F1}");

            // Down grows it back — genuinely resizable, not just clamped at one edge.
            PressManyTimes(window, PhysicalKey.ArrowDown, 20);
            Assert.True(configGrid.RowDefinitions[3].Height.Value > 150,
                $"row 3 should have grown past 150 after 20 ArrowDown presses, was {configGrid.RowDefinitions[3].Height.Value:F1}");
        }
        finally { window.Close(); }
    }

    private static void PressManyTimes(Window window, PhysicalKey key, int count)
    {
        for (int i = 0; i < count; i++)
        {
            window.KeyPressQwerty(key, RawInputModifiers.None);
            window.KeyReleaseQwerty(key, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// Rendered <c>:focus</c> brush vs BOTH adjacent panes' own rendered pixel colors, sampled
    /// directly from a real render (never a guessed/hardcoded resource key — this splitter's two
    /// neighbors are a DataGrid, whose own Fluent-templated background is not necessarily the same
    /// named resource as a plain panel's, and a StackPanel with no explicit Background of its own).
    /// Mirrors <c>ReconstructorCompactTests.Splitter_FocusVisual_MeetsContrastAgainstBothPanes</c>'s
    /// own contrast MATH (WCAG relative luminance) exactly, sourced from ACTUAL rendered pixels
    /// instead of resource lookups.
    /// </summary>
    [AvaloniaFact]
    public void Splitter_FocusVisual_MeetsContrastAgainstBothPanes()
    {
        CreatorViewModel vm = CreateVm();
        for (int i = 0; i < 3; i++)
        {
            vm.StoredFiles.Add(Item($@"C:\release\file{i:D2}.nfo", $"file{i:D2}.nfo"));
        }
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            GridSplitter splitter = window.GetVisualDescendants().OfType<GridSplitter>().Single();
            splitter.Focus();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            var focusBrush = Assert.IsAssignableFrom<ISolidColorBrush>(splitter.Background);
            Color focusColor = focusBrush.Color;

            Point? aboveInWindow = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, -3), window);
            Point? belowInWindow = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height + 3), window);
            Assert.True(aboveInWindow is not null && belowInWindow is not null, "test precondition: both neighboring points must translate into window coordinates");

            Color abovePane = SamplePixelColor(window, aboveInWindow!.Value);
            Color belowPane = SamplePixelColor(window, belowInWindow!.Value);

            double contrastVsAbove = ContrastRatio(focusColor, abovePane);
            double contrastVsBelow = ContrastRatio(focusColor, belowPane);

            Assert.True(contrastVsAbove >= 3.0, $"focus brush vs the pane above (Stored Files grid): {contrastVsAbove:F2}:1 (need >= 3:1)");
            Assert.True(contrastVsBelow >= 3.0, $"focus brush vs the pane below (Output section): {contrastVsBelow:F2}:1 (need >= 3:1)");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Fix round 1 (codex finding 3, spec Step 4): the high-contrast smoke the default-theme
    /// contrast test above does not cover. This app has no actual shipped high-contrast SKIN —
    /// grepped the whole Resources tree and found no "HighContrast" resource dictionary, no
    /// <c>ThemeVariant</c> switching anywhere beyond the single hardcoded
    /// <c>RequestedThemeVariant="Dark"</c> in App.axaml, and no prior converted view's own test
    /// file has ever exercised one either — there is nothing resembling a real Windows
    /// high-contrast integration to toggle programmatically in a headless test, and this
    /// environment must not flip the HOST MACHINE's own real OS-level accessibility settings just
    /// to synthesize one (a disruptive, system-wide, outward-facing action far outside a unit
    /// test's blast radius). The achievable, honest equivalent: prove the splitter's focus
    /// indicator is genuinely LIVE-RESOURCE-DRIVEN (a <c>DynamicResource</c> binding to
    /// <c>AccentPrimary</c>, not a frozen/cached brush) by swapping the resource to an extreme,
    /// maximally-distinct color — the same technique a real high-contrast theme dictionary would
    /// use — and confirming the RENDERED pixel actually follows it, with contrast re-verified
    /// against both panes under the new color, then restoring the original value. This is the
    /// architectural property that makes a genuine high-contrast override safe if one is ever
    /// shipped: a hardcoded/cached brush would silently fail this exact check.
    /// <para>
    /// The override is scoped to THIS test only and restored in a <c>finally</c> block — MEASURED
    /// (a throwaway diagnostic) that <c>Application.Current.Resources["AccentPrimary"]</c> reads
    /// back <c>null</c> before any override (the real value lives in a MERGED dictionary,
    /// Resources/Tokens.axaml, not the top-level one), so restoration removes the directly-set key
    /// rather than reassigning a captured "original" value — confirmed to correctly fall back to
    /// the merged dictionary's own value afterward, not leave the key missing or wrong.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void Splitter_FocusVisual_HighContrastSmoke_FollowsLiveResourceOverride()
    {
        CreatorViewModel vm = CreateVm();
        for (int i = 0; i < 3; i++)
        {
            vm.StoredFiles.Add(Item($@"C:\release\file{i:D2}.nfo", $"file{i:D2}.nfo"));
        }
        var view = new CreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            GridSplitter splitter = window.GetVisualDescendants().OfType<GridSplitter>().Single();
            splitter.Focus();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            var beforeBrush = Assert.IsAssignableFrom<ISolidColorBrush>(splitter.Background);
            Color beforeColor = beforeBrush.Color;

            // Only the TOP-LEVEL dictionary's own key matters here (not the whole merged-dictionary
            // resolution chain TryGetResource would search) — MEASURED (a throwaway diagnostic):
            // this key is null/absent at the top level before any override (the real value lives
            // in the MERGED Resources/Tokens.axaml), so restoration must REMOVE the key rather than
            // reassign a captured value, or the override would leak past this test.
            bool hadDirectOverride = Application.Current!.Resources.ContainsKey("AccentPrimary");
            object? capturedOriginal = hadDirectOverride ? Application.Current!.Resources["AccentPrimary"] : null;

            // Maximally distinct from AccentPrimary's own default (#FF0078D4, a blue) and from
            // both neighboring panes — mirrors the kind of extreme, saturated color a real
            // Windows high-contrast theme substitutes for focus/accent brushes.
            var highContrastColor = Color.FromRgb(0xFF, 0xFF, 0x00);
            try
            {
                Application.Current!.Resources["AccentPrimary"] = new SolidColorBrush(highContrastColor);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();

                var overriddenBrush = Assert.IsAssignableFrom<ISolidColorBrush>(splitter.Background);
                Assert.Equal(highContrastColor, overriddenBrush.Color);
                Assert.NotEqual(beforeColor, overriddenBrush.Color);

                Point? aboveInWindow = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, -3), window);
                Point? belowInWindow = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height + 3), window);
                Assert.True(aboveInWindow is not null && belowInWindow is not null);

                Color abovePane = SamplePixelColor(window, aboveInWindow!.Value);
                Color belowPane = SamplePixelColor(window, belowInWindow!.Value);

                Assert.True(ContrastRatio(highContrastColor, abovePane) >= 3.0,
                    "the high-contrast override color must ALSO clear the 3:1 bar — a real high-contrast theme's own color choice would, and this proves the mechanism doesn't accidentally defeat itself");
                Assert.True(ContrastRatio(highContrastColor, belowPane) >= 3.0);
            }
            finally
            {
                if (hadDirectOverride)
                {
                    Application.Current!.Resources["AccentPrimary"] = capturedOriginal;
                }
                else
                {
                    Application.Current!.Resources.Remove("AccentPrimary");
                }
                Dispatcher.UIThread.RunJobs();
            }

            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var restoredBrush = Assert.IsAssignableFrom<ISolidColorBrush>(splitter.Background);
            Assert.Equal(beforeColor, restoredBrush.Color);
        }
        finally { window.Close(); }
    }

    /// <summary>Renders the whole window and reads back one pixel's RGBA — used to sample a
    /// neighboring pane's TRUE rendered color rather than guessing which named resource applies.</summary>
    private static Color SamplePixelColor(Window window, Point pointInWindow)
    {
        var size = new PixelSize((int)Math.Ceiling(window.Bounds.Width), (int)Math.Ceiling(window.Bounds.Height));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(window);

        byte[] buffer = new byte[size.Width * size.Height * 4];
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), buffer.Length, size.Width * 4);
        }
        finally
        {
            handle.Free();
        }

        int x = Math.Clamp((int)pointInWindow.X, 0, size.Width - 1);
        int y = Math.Clamp((int)pointInWindow.Y, 0, size.Height - 1);
        int offset = (y * size.Width * 4) + (x * 4);
        // Avalonia's RenderTargetBitmap default pixel format is BGRA8888.
        byte b = buffer[offset];
        byte g = buffer[offset + 1];
        byte r = buffer[offset + 2];
        byte a = buffer[offset + 3];
        return Color.FromArgb(a, r, g, b);
    }

    /// <summary>WCAG 2.x relative luminance + contrast ratio, computed from rendered brush colors — never a hardcoded number. Mirrors ReconstructorCompactTests' own identical helper.</summary>
    private static double ContrastRatio(Color a, Color b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        double lighter = Math.Max(la, lb);
        double darker = Math.Min(la, lb);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        double r = LinearizeChannel(c.R / 255.0);
        double g = LinearizeChannel(c.G / 255.0);
        double b = LinearizeChannel(c.B / 255.0);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static double LinearizeChannel(double c) =>
        c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    /// <summary>
    /// CLIP-AWARE (mirrors every other converted view's own identical helper): a naive
    /// "translated point within the window's own outer rectangle" check can false-PASS a control
    /// genuinely obscured by an intermediate <c>ClipToBounds</c> ancestor. A degenerate
    /// (zero-width/zero-height) control translates to a single point, which trivially satisfies
    /// any containment check — effective visibility and a positive size are asserted FIRST.
    /// </summary>
    private static void AssertFullyWithinWindow(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible, $"{control.GetType().Name} is not effectively visible.");
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0,
            $"{control.GetType().Name} has a non-positive size ({control.Bounds.Width:F1}x{control.Bounds.Height:F1}) — collapsed, not merely positioned badly.");

        if (TransformRect(control, new Rect(control.Bounds.Size), window) is not { } controlInWindow)
        {
            Assert.Fail($"{control.GetType().Name} could not be translated into window coordinates.");
            return;
        }

        Rect visible = new(window.Bounds.Size);
        foreach (Visual ancestor in control.GetVisualAncestors())
        {
            if (ancestor is not Control clipper || !clipper.ClipToBounds)
            {
                continue;
            }

            if (TransformRect(clipper, new Rect(clipper.Bounds.Size), window) is not { } clipperInWindow)
            {
                Assert.Fail($"{clipper.GetType().Name} (a clipping ancestor of {control.GetType().Name}) could not be translated into window coordinates.");
                return;
            }

            visible = visible.Intersect(clipperInWindow);
        }

        const double Slack = 0.5;
        Assert.True(
            controlInWindow.X >= visible.X - Slack && controlInWindow.Y >= visible.Y - Slack &&
            controlInWindow.Right <= visible.Right + Slack && controlInWindow.Bottom <= visible.Bottom + Slack,
            $"{control.GetType().Name} bounds ({controlInWindow}) exceed the visible (clip-aware) region {visible} — obscured by " +
            "an intermediate ClipToBounds ancestor (e.g. a ScrollViewer's own clipped viewport), not just positioned outside the window.");
    }

    private static Rect? TransformRect(Visual from, Rect localRect, Visual to)
    {
        Point? topLeft = from.TranslatePoint(new Point(localRect.X, localRect.Y), to);
        Point? bottomRight = from.TranslatePoint(new Point(localRect.Right, localRect.Bottom), to);
        return topLeft is { } tl && bottomRight is { } br ? new Rect(tl, br) : null;
    }

    // ── Fixtures (captured from real, green CompactViewRig.CaptureTabOrderControls runs against
    // this task's finished implementation — see task report for the capture method). Each entry is
    // CompactViewRig.Describe's own format (real automation peer name plus x:Name, reported
    // separately), a human-readable regression net, NOT the discriminating check itself (that is
    // AssertTabWalk's own reference-based ResolveIndependentExpectedOrder + AssertSameControlSequence,
    // proven to genuinely discriminate by AssertSameControlSequence_SwappedPositions_FailsNamingTheMismatch).
    // "Add..." (not the visually-first Input row) is genuinely first — see AssertTabWalk's own doc. ──

    private static readonly IReadOnlyList<string> NormalModeTabOrderFixture =
    [
        "Button name=\"Add...\" id=\"\"",
        "Button name=\"Remove\" id=\"\"",
        "Button name=\"Remove All\" id=\"\"",
        "Button name=\"Move Up\" id=\"\"",
        "Button name=\"Move Down\" id=\"\"",
        "DataGrid name=\"Stored Files\" id=\"StoredFilesGrid\"",
        "GridSplitter name=\"Resize stored files and output\" id=\"\"",
        "Button name=\"Browse\" id=\"\"",
        "TextBox name=\"Output path\" id=\"OutputTextBox\"",
        "CheckBox name=\"Auto-include files — Scan release directory for .nfo, .sfv, proof images, .m3u, .cue, .log files.\" id=\"\"",
        "CheckBox name=\"Auto-create SRS — Create .srs files for samples found in Sample/ subdirectory.\" id=\"\"",
        "CheckBox name=\"Vobsub SRR — Create nested SRR files for subtitle archives found in Subs/ directories.\" id=\"\"",
        "CheckBox name=\"Store fix RAR — For fix/patch releases, store the main RAR file as proof.\" id=\"\"",
        "CheckBox name=\"Allow compressed — Accept RAR volumes that use compression (method != Store).\" id=\"\"",
        "CheckBox name=\"OSO hashes — Compute and store OpenSubtitles OSO hashes for archived files.\" id=\"\"",
        "CheckBox name=\"Languages.diz — Extract language metadata from VobSub .idx files and store in the SRR.\" id=\"\"",
        "TextBox name=\"\" id=\"\"",
        "Button name=\"Create SRR\" id=\"\"",
        "Button name=\"Save log...\" id=\"\"",
        "TextBox name=\"Input path\" id=\"InputTextBox\"",
        "Button name=\"Browse input file\" id=\"\"",
        "Button name=\"Browse folder for release input\" id=\"\"",
    ];

    private static readonly IReadOnlyList<string> CompactModeTabOrderFixture =
    [
        "ToggleButton name=\"Help\" id=\"\"",
        "Button name=\"Add...\" id=\"\"",
        "Button name=\"Remove\" id=\"\"",
        "Button name=\"Remove All\" id=\"\"",
        "Button name=\"Move Up\" id=\"\"",
        "Button name=\"Move Down\" id=\"\"",
        "DataGrid name=\"Stored Files\" id=\"StoredFilesGrid\"",
        "GridSplitter name=\"Resize stored files and output\" id=\"\"",
        "Button name=\"Browse\" id=\"\"",
        "TextBox name=\"Output path\" id=\"OutputTextBox\"",
        "CheckBox name=\"Auto-include files — Scan release directory for .nfo, .sfv, proof images, .m3u, .cue, .log files.\" id=\"\"",
        "CheckBox name=\"Auto-create SRS — Create .srs files for samples found in Sample/ subdirectory.\" id=\"\"",
        "CheckBox name=\"Vobsub SRR — Create nested SRR files for subtitle archives found in Subs/ directories.\" id=\"\"",
        "CheckBox name=\"Store fix RAR — For fix/patch releases, store the main RAR file as proof.\" id=\"\"",
        "CheckBox name=\"Allow compressed — Accept RAR volumes that use compression (method != Store).\" id=\"\"",
        "CheckBox name=\"OSO hashes — Compute and store OpenSubtitles OSO hashes for archived files.\" id=\"\"",
        "CheckBox name=\"Languages.diz — Extract language metadata from VobSub .idx files and store in the SRR.\" id=\"\"",
        "TextBox name=\"\" id=\"\"",
        "Button name=\"Create SRR\" id=\"\"",
        "Button name=\"Save log...\" id=\"\"",
        "TextBox name=\"Input path\" id=\"InputTextBox\"",
        "Button name=\"Browse input file\" id=\"\"",
        "Button name=\"Browse folder for release input\" id=\"\"",
    ];
}
