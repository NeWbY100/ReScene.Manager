using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Models;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Behaviors;
using ReScene.Manager.Services;
using ReScene.Manager.Views;
using ReScene.SRS;

namespace ReScene.Manager.Tests;

/// <summary>
/// Small-window layout degradation tests for <see cref="SRSCreatorView"/> (spec rev 12; task
/// brief numbers: threshold 520, config row AutoToStar 110 compact / 80 help-open, log 80, Help
/// body MaxHeight 40, compact CI bound <see cref="CompactInvariantRig.CiBound"/> == 307, pinned
/// band ceiling 75). Adapts <c>ReconstructorCompactTests</c>' five-part shape (Task 2's template)
/// to this view's simpler, sub-tab-free three-band structure: no splitter section (this view has
/// none), and a NEW pinned-band section (#5) asserting the actual defect this task fixes directly
/// — the Create SRS button's bounds while band 1 is scrolled to both extremes.
/// </summary>
public class SRSCreatorCompactTests
{
    // ── Inert VM construction (mirrors SRSCreatorViewTests.CreateViewModel) ──

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

    private static SRSCreatorViewModel CreateVm() =>
        new(
            new InertSrsCreationService(),
            new AvaloniaFileDialogService(static () => null), // headless: dialogs no-op, never block
            new InertTempDirectoryService(),
            new DefaultAppSettingsService(),
            new InlineUiDispatcher());

    private const double Threshold = 520;
    private const double CompactInner = 319;   // the canonical 700x450 minimum window
    private const double ExpandedInner = 521;  // comfortably above Threshold

    /// <summary>
    /// The brief's "corrected feedback inventory" worst case, forced together: ISO selection
    /// visible, all three FieldStatusLines non-None (with realistic, wrapping-length messages —
    /// FieldStatusLine's message TextBlock wraps, so a short message would understate the floor),
    /// and Cancel + ProgressMessage + ProgressBar all visible. Used by every invariant/no-clip
    /// check so "worst case" means the same thing everywhere it is asserted.
    /// </summary>
    private static void ForceWorstCase(SRSCreatorViewModel vm)
    {
        vm.IsISOSource = true;
        vm.SampleStatus = FieldStatus.Warning("This looks like a very small sample — check it is not truncated before continuing.");
        vm.MainFileStatus = FieldStatus.Warning("This file doesn't exist — match offsets will stay 0.");
        vm.OutputStatus = FieldStatus.Info("Auto-filled from the sample name. Change it if needed.");
        vm.IsCreating = true;
        vm.ShowProgress = true;
        vm.ProgressMessage = "Profiling sample...";
    }

    // ── 1. Invariant (spec §1's four checks; CompactInvariantRig) ────

    [AvaloniaFact]
    public void Invariant_ExpandedModeFloor_UnderThreshold()
    {
        SRSCreatorViewModel vm = CreateVm();
        ForceWorstCase(vm);
        var view = new SRSCreatorView { DataContext = vm };

        (Window window, Grid root) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            Assert.DoesNotContain("compactHeight", root.Classes);
            double floor = CompactInvariantRig.MeasureFloor(root);
            Assert.True(floor < Threshold, $"expanded-mode floor {floor:F1} must be under Threshold {Threshold}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Invariant_CompactFloor_HelpClosed_WithinCiBound()
    {
        SRSCreatorViewModel vm = CreateVm();
        ForceWorstCase(vm);
        var view = new SRSCreatorView { DataContext = vm };

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
        SRSCreatorViewModel vm = CreateVm();
        ForceWorstCase(vm);
        var view = new SRSCreatorView { DataContext = vm };

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

            // 4. Pinned band (row 2) is never the budget donor — its natural height stays small
            // and positive regardless of mode, and within the spec's <=75 ceiling even with
            // Cancel + ProgressMessage + ProgressBar all forced visible (ForceWorstCase).
            Control pinnedBand = root.Children.OfType<Control>().Single(c => Grid.GetRow(c) == 2);
            Assert.True(pinnedBand.DesiredSize.Height is > 0 and <= 75,
                $"pinned band height {pinnedBand.DesiredSize.Height:F1} out of the expected pinned-row range");
        }
        finally { window.Close(); }
    }

    // ── 2. Rendered matrix: compact @700x450, fresh @Threshold, fresh @Threshold+1 ──

    [AvaloniaFact]
    public void RenderedMatrix_CompactAt700x450_ReachabilityNoClipAndTabWalk() =>
        AssertReachabilityNoClipAndTabWalk(CompactInner, expectCompact: true);

    [AvaloniaFact]
    public void RenderedMatrix_FreshAtThresholdExactly_IsExpanded_ReachabilityNoClipAndTabWalk() =>
        AssertReachabilityNoClipAndTabWalk(Threshold, expectCompact: false);

    [AvaloniaFact]
    public void RenderedMatrix_FreshAtThresholdPlusOne_IsExpanded_ReachabilityNoClipAndTabWalk() =>
        AssertReachabilityNoClipAndTabWalk(Threshold + 1, expectCompact: false);

    private static void AssertReachabilityNoClipAndTabWalk(double innerHeight, bool expectCompact)
    {
        AssertNoClip(innerHeight, expectCompact);
        AssertConfigAndActionReachable(innerHeight);
        AssertTabWalk(innerHeight);
    }

    private static void AssertNoClip(double innerHeight, bool expectCompact)
    {
        SRSCreatorViewModel vm = CreateVm();
        ForceWorstCase(vm); // criterion B worst case: every conditional forced
        var view = new SRSCreatorView { DataContext = vm };

        (Window window, Grid root) = CompactViewRig.HostAt(view, innerHeight);
        try
        {
            Assert.Equal(expectCompact, root.Classes.Contains("compactHeight"));
            CompactInvariantRig.AssertArrangesWithin(root, root.Bounds.Height);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Criterion A for the LAST config control (App name TextBox — no x:Name of its own, so
    /// distinguished by its Width="400", the only TextBox in the view with that width) and the
    /// primary action (Create SRS button, content-matched like the existing
    /// <c>SRSCreatorViewTests</c> suite already does for Cancel). Both routed through the config
    /// band's own ScrollViewer, identified by Grid.Row rather than by uniqueness-among-
    /// ScrollViewers — the Help body is ALSO a bare, non-templated ScrollViewer, so Grid.Row is
    /// the only unambiguous handle.
    /// <para>
    /// Input/Output paths are set so <c>CanCreateSRS()</c> is true and the button is genuinely
    /// enabled — for the DEFAULT inert VM (both paths empty) Create SRS is disabled and Avalonia
    /// correctly excludes it from Tab order entirely (same precedent as the Reconstructor's own
    /// "Start" button, which its own fixture comment documents as absent for the same reason);
    /// "reachable by keyboard" is only a meaningful check once the button can actually take
    /// focus.
    /// </para>
    /// </summary>
    private static void AssertConfigAndActionReachable(double innerHeight)
    {
        SRSCreatorViewModel vm = CreateVm();
        vm.InputPath = @"C:\release\sample.mkv";
        vm.OutputPath = @"C:\release\sample.srs";
        var view = new SRSCreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, innerHeight);
        try
        {
            ScrollViewer configScroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(sv => Grid.GetRow(sv) == 1);

            TextBox appName = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Width == 400);
            AssertReachableByAllThreeRoutes(window, configScroller, appName);

            Button createButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Create SRS");
            Assert.True(createButton.IsEffectivelyEnabled, "test precondition: Create SRS must be enabled to be a meaningful keyboard-reachability target");
            AssertReachableByAllThreeRoutes(window, configScroller, createButton);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Each route is exercised from a genuine "not yet visible" start (offset reset between
    /// routes) — otherwise the first route scrolling the target into view would make the other
    /// two trivially no-op without ever exercising their own mechanism. Harmless no-op for the
    /// pinned Create SRS button (never inside <paramref name="scroller"/>'s clipped-out region,
    /// so every route's own early "already visible" check returns immediately) — still a real
    /// assertion that it stays fully visible regardless.
    /// </summary>
    private static void AssertReachableByAllThreeRoutes(Window window, ScrollViewer scroller, Control target)
    {
        scroller.Offset = default;
        Dispatcher.UIThread.RunJobs();
        CompactViewRig.AssertReachableByWheel(window, target);

        scroller.Offset = default;
        Dispatcher.UIThread.RunJobs();
        CompactViewRig.AssertReachableByKeyboard(window, target);

        scroller.Offset = default;
        Dispatcher.UIThread.RunJobs();
        CompactViewRig.AssertReachableByThumb(window, target);
    }

    private static void AssertTabWalk(double innerHeight)
    {
        SRSCreatorViewModel vm = CreateVm();
        var view = new SRSCreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, innerHeight);
        try
        {
            // In compact mode Help starts collapsed (condition 5): the body's own prose is not a
            // tab stop while collapsed, so the header toggle is the walk's genuine entry point.
            // In expanded/flat mode the disclosure contributes NOTHING to tab order at all (its
            // header is hidden by style and its body is plain, non-focusable prose) — the
            // Sample File TextBox is the first genuinely focusable control either way, so it is
            // the flat-mode sentinel.
            bool compact = root.Classes.Contains("compactHeight");
            Control sentinel = compact
                ? root.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure")
                    .GetVisualDescendants().OfType<ToggleButton>().Single()
                : window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "InputTextBox");
            CompactViewRig.AssertTabWalkStaysVisible(window, sentinel);
        }
        finally { window.Close(); }
    }

    // ── 3. Tab-order snapshots ────────────────────────────────────────
    //
    // Bare type:name identities (CompactViewRig.SnapshotTabOrder's own Describe()) are not
    // enough here: none of this view's picker/action buttons carry an explicit
    // AutomationProperties.Name, so a fixture built from them alone cannot tell "Main file
    // Browse" apart from "Output Browse", meaning two same-typed controls trading places (a
    // real reordering regression) would leave the fixture unchanged. SnapshotSemanticTabOrder
    // below re-walks Tab the same way (leaves root's scope, or repeats an already-seen
    // control) but resolves each stop's IDENTITY by which command/property it is bound to —
    // stable across renames, unambiguous between same-typed siblings. Enabling Create SRS
    // (InputPath/OutputPath set) before capturing means its own position is actually PINNED by
    // the fixture, not left unverified because it happened to be absent.

    /// <summary>
    /// Resolved fresh against THIS CALL's <paramref name="vm"/> — RelayCommand instances are
    /// per-VM-instance, not shared/static, so comparing against a cached dictionary built from a
    /// DIFFERENT test's VM would silently never match. Direct reference comparison per command,
    /// no caching.
    /// </summary>
    private static string DescribeSemantic(Control control, SRSCreatorViewModel vm) => control switch
    {
        Button { Command: { } c } when ReferenceEquals(c, vm.BrowseInputCommand) => "Button:BrowseInput",
        Button { Command: { } c } when ReferenceEquals(c, vm.BrowseMainFileCommand) => "Button:BrowseMainFile",
        Button { Command: { } c } when ReferenceEquals(c, vm.ClearMainFileCommand) => "Button:ClearMainFile",
        Button { Command: { } c } when ReferenceEquals(c, vm.BrowseOutputCommand) => "Button:BrowseOutput",
        Button { Command: { } c } when ReferenceEquals(c, vm.CreateSRSCommand) => "Button:CreateSRS",
        Button { Command: { } c } when ReferenceEquals(c, vm.CancelCreationCommand) => "Button:Cancel",
        Button { Command: { } c } when ReferenceEquals(c, vm.SaveLogCommand) => "Button:SaveLog",
        TextBox { Name: "InputTextBox" } => "TextBox:Input",
        TextBox { Name: "OutputTextBox" } => "TextBox:Output",
        TextBox { Width: 400 } => "TextBox:AppName",
        TextBox => "TextBox:MainFilePath", // the only remaining unnamed, non-400-width TextBox
        ToggleButton toggle => $"ToggleButton:{AutomationProperties.GetName(toggle)}",
        _ => throw new Xunit.Sdk.XunitException($"DescribeSemantic has no case for {control.GetType().Name} — extend the switch rather than let an unidentified stop silently pass."),
    };

    /// <summary>
    /// Same walk-and-terminate contract as <see cref="CompactViewRig.SnapshotTabOrder"/> (leaves
    /// <paramref name="root"/>'s scope, or repeats an already-seen control), reimplemented here
    /// rather than extending the shared rig, so Tasks 4-6's own use of the unmodified rig is
    /// never put at risk by a change scoped to this view's own semantic-identity need.
    /// </summary>
    private static IReadOnlyList<string> SnapshotSemanticTabOrder(Window window, Control root, SRSCreatorViewModel vm)
    {
        Control focused = window.FocusManager?.GetFocusedElement() as Control
            ?? throw new Xunit.Sdk.XunitException("SnapshotSemanticTabOrder requires focus already inside root — focus a starting control first.");

        List<string> order = [];
        HashSet<Control> seen = [];
        const int MaxSteps = 500;
        for (int step = 0; step < MaxSteps; step++)
        {
            bool within = ReferenceEquals(root, focused) || root.IsVisualAncestorOf(focused);
            if (!within || !seen.Add(focused))
            {
                return order;
            }

            order.Add(DescribeSemantic(focused, vm));

            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            focused = window.FocusManager?.GetFocusedElement() as Control
                ?? throw new Xunit.Sdk.XunitException($"SnapshotSemanticTabOrder lost focus entirely at step {step}.");
        }

        throw new Xunit.Sdk.XunitException($"SnapshotSemanticTabOrder did not leave root or loop back within {MaxSteps} steps.");
    }

    [AvaloniaFact]
    public void TabOrderSnapshot_Normal_MatchesPreChangeFixture()
    {
        SRSCreatorViewModel vm = CreateVm();
        vm.InputPath = @"C:\release\sample.mkv";
        vm.OutputPath = @"C:\release\sample.srs"; // Create SRS enabled: its own position is pinned, not left unverified
        var view = new SRSCreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            Assert.DoesNotContain("compactHeight", root.Classes);

            // The TRUE first control (Sample File's own Browse button precedes InputTextBox in
            // DockPanel declaration order) — not a mid-sequence sentinel, so the complete order
            // is captured, not a tail that happens to omit the first stop.
            Button sentinel = window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.BrowseInputCommand));
            sentinel.Focus();
            Dispatcher.UIThread.RunJobs();

            IReadOnlyList<string> order = SnapshotSemanticTabOrder(window, root, vm);
            Assert.Equal(NormalModeTabOrderFixture, order);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TabOrderSnapshot_Compact_MatchesSpecSection2Order()
    {
        SRSCreatorViewModel vm = CreateVm();
        vm.InputPath = @"C:\release\sample.mkv";
        vm.OutputPath = @"C:\release\sample.srs";
        var view = new SRSCreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Assert.Contains("compactHeight", root.Classes);
            ToggleButton headerToggle = root.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure")
                .GetVisualDescendants().OfType<ToggleButton>().Single();
            headerToggle.Focus();
            Dispatcher.UIThread.RunJobs();

            IReadOnlyList<string> order = SnapshotSemanticTabOrder(window, root, vm);
            Assert.Equal(CompactModeTabOrderFixture, order);
        }
        finally { window.Close(); }
    }

    // ── 4. Chrome ─────────────────────────────────────────────────────

    [AvaloniaFact]
    public void SingleIntroInstance_ExistsInBothModes()
    {
        SRSCreatorViewModel vm = CreateVm();
        var normalView = new SRSCreatorView { DataContext = vm };
        (Window normalWindow, Grid normalRoot) = CompactViewRig.HostAt(normalView, ExpandedInner);
        try
        {
            Assert.DoesNotContain("compactHeight", normalRoot.Classes);
            Assert.Equal(1, CountIntroInstances(normalWindow));
        }
        finally { normalWindow.Close(); }

        SRSCreatorViewModel vm2 = CreateVm();
        var compactView = new SRSCreatorView { DataContext = vm2 };
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
            .Count(t => t.Text is not null && t.Text.StartsWith("Create an SRS (Sample Rescue Storage)", StringComparison.Ordinal));

    [AvaloniaFact]
    public void CompactEntry_HelpStartsCollapsed_BodyReachable_ExpanderResetsOnReentry()
    {
        SRSCreatorViewModel vm = CreateVm();
        var view = new SRSCreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Expander helpDisclosure = root.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure");
            Assert.False(helpDisclosure.IsExpanded); // condition 5: compact entry starts collapsed

            helpDisclosure.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            // "Invocable" is proven by REACHABILITY/usability (focusable, enabled, unobscured, a
            // real Tab lands on it) — this body has no interactive children (plain prose), so
            // its own compact-only-focusable ScrollViewer IS the route.
            ScrollViewer body = helpDisclosure.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(body.Focusable);
            Assert.True(body.IsEffectivelyEnabled);
            CompactViewRig.AssertReachableByKeyboard(window, body);

            // Restore to normal, then re-enter compact: durability is compact-SESSION scoped only.
            window.Height += 250; // comfortably above Threshold + hysteresis slack
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("compactHeight", root.Classes);
            Assert.True(helpDisclosure.IsExpanded); // flat mode: force-expanded

            // The staged-focus guard's actual point: restoring from a focus captured on the
            // body (which just went non-focusable — flat mode's base style, not the
            // compact-only override) must relocate focus, not strand it. RestoreFocusTarget was
            // wired to InputTextBox in the view's ctor, so that is where it must land.
            TextBox inputTextBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "InputTextBox");
            Assert.True(inputTextBox.IsFocused,
                "restoring from a focused compact body must relocate focus to the wired RestoreFocusTarget (InputTextBox), not strand it");

            window.Height -= 250;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("compactHeight", root.Classes);
            Assert.False(helpDisclosure.IsExpanded, "re-entering compact must reset Help to collapsed, not resume the prior session's open state");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HelpOpenDonation_ConfigRowMin80_BodyMaxHeight40_AppNameKeyboardReachable()
    {
        SRSCreatorViewModel vm = CreateVm();
        var view = new SRSCreatorView { DataContext = vm };
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

            TextBox appName = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Width == 400);
            CompactViewRig.AssertReachableByKeyboard(window, appName);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CompactBodyScroller_NotFocusableNormally_FocusableAndNamedInCompact()
    {
        SRSCreatorViewModel vm = CreateVm();
        var normalView = new SRSCreatorView { DataContext = vm };
        (Window normalWindow, Grid normalRoot) = CompactViewRig.HostAt(normalView, ExpandedInner);
        try
        {
            // Flat mode force-expands the body (so this scroller IS realized/attached even
            // though the header stays hidden) — criterion F requires it NOT be a new Tab stop.
            ScrollViewer body = normalRoot.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure")
                .GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.False(body.Focusable);
        }
        finally { normalWindow.Close(); }

        SRSCreatorViewModel vm2 = CreateVm();
        var compactView = new SRSCreatorView { DataContext = vm2 };
        (Window compactWindow, Grid compactRoot) = CompactViewRig.HostAt(compactView, CompactInner);
        try
        {
            Expander helpDisclosure = compactRoot.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure");
            helpDisclosure.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            ScrollViewer body = helpDisclosure.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(body.Focusable);
            Assert.Equal("Help content", AutomationProperties.GetName(body));
        }
        finally { compactWindow.Close(); }
    }

    /// <summary>
    /// Codex round-5's wording (design doc line 5/201): Avalonia's ScrollViewer handles PAGE
    /// keys, not arrows. All four built-ins exercised with genuine key input against a REAL,
    /// attached ScrollViewer — never a synthetic Offset-setter poke.
    /// <para>
    /// MEASURED: this view's actual intro prose (172 characters) renders at ~35 DIPs at the
    /// app's own enforced minimum width (<c>MainWindow.MinWidth="700"</c>, confirmed in
    /// MainWindow.axaml) — under the 40-DIP HelpBodyMaxHeight donation cap, so it never
    /// genuinely overflows and there is nothing for the real production text to page through at
    /// any window size the app allows. The body's own Text is therefore temporarily lengthened
    /// (synthetic content, this test only) so the four keys can be proven against REAL overflow;
    /// the scroller/keys are a generic mechanism (this class of ScrollViewer, not this specific
    /// prose) and <see cref="CompactBodyScroller_NotFocusableNormally_FocusableAndNamedInCompact"/>
    /// already covers the production text's own focusability/naming.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void CompactBodyScroller_AllFourPageKeys_MoveOffsetBothWaysAndToExtents()
    {
        SRSCreatorViewModel vm = CreateVm();
        var view = new SRSCreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Expander helpDisclosure = root.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure");
            helpDisclosure.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            ScrollViewer body = helpDisclosure.GetVisualDescendants().OfType<ScrollViewer>().Single();
            TextBlock introText = body.GetVisualDescendants().OfType<TextBlock>().Single();
            introText.Text = string.Concat(Enumerable.Repeat("Create an SRS from a sample video file. ", 20));
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

    // ── 5. Pinned band (the defect this task exists to fix) ───────────

    /// <summary>
    /// Directly asserts the defect the whole task exists to fix: with band 1 (config)
    /// independently scrolled to its top AND its bottom extreme, the pinned Create SRS button's
    /// bounds — translated into window coordinates — stay fully inside the window the entire
    /// time, with Cancel/ProgressMessage/ProgressBar all forced visible (the worst case for the
    /// pinned band's own height). Pre-change (today's DockPanel), the equivalent button
    /// collapsed to a zero-height sliver at the very bottom edge under these exact conditions
    /// (measured red-phase evidence — see task report).
    /// </summary>
    [AvaloniaFact]
    public void PinnedActionBand_CreateSRSButtonStaysWithinWindow_BandOneScrolledToTopAndBottom()
    {
        SRSCreatorViewModel vm = CreateVm();
        vm.IsCreating = true;
        vm.ShowProgress = true; // forces Cancel + ProgressMessage + ProgressBar visible
        var view = new SRSCreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Assert.Contains("compactHeight", root.Classes);
            Button createButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Create SRS");
            Button cancelButton = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Cancel");
            ProgressBar bar = window.GetVisualDescendants().OfType<ProgressBar>().Single();
            Assert.True(cancelButton.IsVisible);
            Assert.True(bar.IsVisible);

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

    /// <summary>
    /// A degenerate (zero-width or zero-height) control translates to a single point, which
    /// trivially satisfies any containment check — exactly the pre-change defect (the Create SRS
    /// button collapsing to <c>Height=0</c>, see the task report's red evidence) would have
    /// slipped past a containment-only check. Effective visibility and a positive size are
    /// asserted FIRST, unconditionally, so a collapsed/invisible control fails outright instead
    /// of being reported as "contained".
    /// </summary>
    private static void AssertFullyWithinWindow(Control control, Window window)
    {
        Assert.True(control.IsEffectivelyVisible, $"{control.GetType().Name} is not effectively visible.");
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0,
            $"{control.GetType().Name} has a non-positive size ({control.Bounds.Width:F1}x{control.Bounds.Height:F1}) — collapsed, not merely positioned badly.");

        Point? topLeft = control.TranslatePoint(new Point(0, 0), window);
        Point? bottomRight = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window);
        Assert.True(topLeft is not null && bottomRight is not null,
            $"{control.GetType().Name} could not be translated into window coordinates.");

        const double Slack = 0.5;
        Rect windowBounds = new(window.Bounds.Size);
        Assert.True(
            topLeft!.Value.X >= windowBounds.X - Slack && topLeft.Value.Y >= windowBounds.Y - Slack &&
            bottomRight!.Value.X <= windowBounds.Right + Slack && bottomRight.Value.Y <= windowBounds.Bottom + Slack,
            $"{control.GetType().Name} bounds ({topLeft.Value}..{bottomRight.Value}) exceed window bounds {windowBounds}");
    }

    // ── 6. Frame-rig parity (criterion F: normal-mode pixels unchanged) ──

    /// <summary>
    /// Compares the flat-mode header region (row 0) against a standalone reconstruction of the
    /// PRE-TASK markup (verbatim tab-description TextBlock, the row-0 shape before this task
    /// wrapped it in the helpDisclosure Expander), both forced through a real render tick (the
    /// Reconstructor's own frame-rig pattern) before measuring. The same two DELIBERATE,
    /// spec-mandated differences the Reconstructor's own equivalent test documents apply here
    /// for the identical reasons: (1) the content's own inset (house rule, narrows available
    /// width by ~4 DIPs) and (2) the flat-mode wrapper is now Expander+ScrollViewer+TextBlock
    /// rather than a bare TextBlock — invisible when the content needs no scrolling (confirmed
    /// by <see cref="SingleIntroInstance_ExistsInBothModes"/>), but a structurally different
    /// visual tree nonetheless.
    /// </summary>
    [AvaloniaFact]
    public void FrameRig_NormalMode_HeaderRegionMatchesPreDisclosureShape()
    {
        SRSCreatorViewModel vm = CreateVm();
        var view = new SRSCreatorView { DataContext = vm };
        (Window newWindow, Grid newRoot) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            Assert.DoesNotContain("compactHeight", newRoot.Classes);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            Control newRow0 = newRoot.Children.OfType<Control>().Single(c => Grid.GetRow(c) == 0);
            Size newSize = newRow0.Bounds.Size;

            Window oldWindow = BuildPreDisclosureRow0Window();
            try
            {
                oldWindow.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Control oldRow0 = (Control)oldWindow.Content!;
                Size oldSize = oldRow0.Bounds.Size;

                // Height must match exactly — the visually significant dimension (a
                // taller/shorter header block would shift every row below it).
                Assert.Equal(oldSize.Height, newSize.Height, precision: 0);

                // Width is narrower by a bounded, EXPLAINED amount (content inset + reserved
                // vertical-scrollbar track — same reasoning as the Reconstructor's own test).
                double widthNarrowing = oldSize.Width - newSize.Width;
                Assert.True(widthNarrowing is > 0 and <= 30,
                    $"header region width narrowed by {widthNarrowing:F1} DIPs (old {oldSize.Width:F1}, new {newSize.Width:F1}) — expected a small, explained narrowing (inset + reserved scrollbar track), not an unbounded drift");
            }
            finally { oldWindow.Close(); }
        }
        finally { newWindow.Close(); }
    }

    /// <summary>Verbatim reconstruction of SRSCreatorView.axaml's row-0 TextBlock before this task (git history).</summary>
    private static Window BuildPreDisclosureRow0Window()
    {
        var textBlock = new TextBlock
        {
            Text = "Create an SRS (Sample Rescue Storage) file from a sample video file. The SRS stores enough data to reconstruct the exact sample from any copy of the same video.",
            Foreground = (IBrush?)Application.Current!.FindResource("ForegroundSecondary"),
            FontSize = (double)Application.Current!.FindResource("FontSizeCaption")!,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6),
        };

        return new Window { Width = CompactInvariantRig.InnerWidth, SizeToContent = SizeToContent.Height, Content = textBlock };
    }

    // ── Fixtures (captured from real, green SnapshotSemanticTabOrder runs against this task's
    // finished implementation, WITH Create SRS enabled — see task report for the capture
    // method). Each entry is a stable semantic identity (bound command, name, or a
    // distinguishing attribute — never a bare type), so a reordering that swaps two same-typed
    // siblings (e.g. the two "Browse" buttons trading places) changes the fixture and is
    // caught — a bare type:name fixture could not tell them apart. ──

    /// <summary>
    /// Normal mode, captured from the TRUE first control (Sample File's own Browse button,
    /// which precedes InputTextBox in DockPanel declaration order — not a mid-sequence
    /// sentinel that would silently omit it): Sample File's Browse + its TextBox, Main file's
    /// Browse/Clear + its TextBox, Output's Browse + its TextBox, the App name TextBox, Create
    /// SRS (InputPath/OutputPath set so it is genuinely enabled and its own position is
    /// pinned — CanExecute false for the default inert VM would otherwise leave it absent and
    /// unverified, the same situation the Reconstructor's own "Start" button fixture
    /// documents), then Save log. Cancel is absent (hidden, IsCreating false).
    /// </summary>
    private static readonly IReadOnlyList<string> NormalModeTabOrderFixture =
    [
        "Button:BrowseInput", "TextBox:Input", "Button:BrowseMainFile", "Button:ClearMainFile",
        "TextBox:MainFilePath", "Button:BrowseOutput", "TextBox:Output", "TextBox:AppName",
        "Button:CreateSRS", "Button:SaveLog",
    ];

    /// <summary>
    /// Compact order (spec §2): disclosure header toggle → (body skipped: Help starts collapsed
    /// per condition 5, so the plain-prose body is IsVisible=false and correctly excluded from
    /// Tab order) → identical tail to normal mode (this walk starts one stop earlier, at the
    /// header toggle, rather than at Sample File's Browse button).
    /// </summary>
    private static readonly IReadOnlyList<string> CompactModeTabOrderFixture =
    [
        "ToggleButton:Help",
        "Button:BrowseInput", "TextBox:Input", "Button:BrowseMainFile", "Button:ClearMainFile",
        "TextBox:MainFilePath", "Button:BrowseOutput", "TextBox:Output", "TextBox:AppName",
        "Button:CreateSRS", "Button:SaveLog",
    ];
}
