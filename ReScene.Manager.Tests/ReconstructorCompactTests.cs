using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Core;
using ReScene.Manager.Behaviors;
using ReScene.Manager.Services;
using ReScene.Manager.Views;

namespace ReScene.Manager.Tests;

/// <summary>
/// Small-window layout degradation tests for <see cref="ReconstructorView"/> (spec rev 12; task
/// brief numbers: threshold 421, TabControl minimums 130/96/60, log 80, Help body MaxHeight 38,
/// compact CI bound <see cref="CompactInvariantRig.CiBound"/> == 307). This is the TEMPLATE per-view
/// shape every later view task (SRSCreator, SRSReconstructor, SampleRestorer, Creator) copies —
/// <see cref="CompactViewRig"/> members plus VM property setters only, no other undefined helpers.
/// </summary>
public class ReconstructorCompactTests
{
    // ── Inert VM construction (mirrors ReconstructorViewTests.CreateVm) ──

    private sealed class InertBruteForceService : IBruteForceService
    {
        public event EventHandler<BruteForceProgressEventArgs>? Progress { add { } remove { } }
        public event EventHandler<BruteForceStatusChangedEventArgs>? StatusChanged { add { } remove { } }
        public event EventHandler<LogEventArgs>? LogMessage { add { } remove { } }
        public event EventHandler<FileCopyProgressEventArgs>? FileCopyProgress { add { } remove { } }
        public event EventHandler<CRCValidationProgressEventArgs>? CRCValidationProgress { add { } remove { } }
        public event EventHandler<TimestampPreservationFailedEventArgs>? TimestampPreservationFailed { add { } remove { } }

        public Task<BruteForceRunResult> RunAsync(BruteForceOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(new BruteForceRunResult(true, null));
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, UiDispatcherPriority priority) => action();
        public bool CheckAccess() => true;
    }

    private sealed class InertUiTimerFactory : IUiTimerFactory
    {
        public IUiTimer Create(TimeSpan interval, Action onTick) => new NoOpTimer();

        private sealed class NoOpTimer : IUiTimer
        {
            public void Start() { }
            public void Stop() { }
        }
    }

    private static ReconstructorViewModel CreateVm() =>
        new(
            new InertBruteForceService(),
            new AvaloniaFileDialogService(static () => null),
            new InlineUiDispatcher(),
            new InertUiTimerFactory());

    private const double Threshold = 421;
    private const double CompactInner = 319;   // the canonical 700x450 minimum window
    private const double ExpandedInner = 521;  // comfortably above Threshold

    private const string FullTip =
        "Tip: click “Import from SRR” to auto-configure versions, compression, " +
        "dictionary, timestamps and Host OS from the release's SRR.";

    // ── 1. Invariant (spec §1's four checks; CompactInvariantRig) ────

    [AvaloniaFact]
    public void Invariant_ExpandedModeFloor_UnderThreshold()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.CustomPackerWarning = "Custom packer detected."; // worst case: warning row forced visible
        var view = new ReconstructorView { DataContext = vm };

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
        ReconstructorViewModel vm = CreateVm();
        vm.CustomPackerWarning = "Custom packer detected.";
        var view = new ReconstructorView { DataContext = vm };

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
    public void Invariant_CompactFloor_HelpOpen_WithinCiBound_AndPinnedToolbarRowSane()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.CustomPackerWarning = "Custom packer detected.";
        var view = new ReconstructorView { DataContext = vm };

        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Assert.Contains("compactHeight", root.Classes);
            Expander helpDisclosure = root.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure");
            helpDisclosure.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(CompactHeightBehavior.GetHelpOpen(root));

            // One sum: donation rows applied (TabControl min -> 60) AND the body's own MaxHeight
            // (38) both spend the same 307-DIP budget — never checked independently.
            double floor = CompactInvariantRig.MeasureFloor(root);
            Assert.True(floor <= CompactInvariantRig.CiBound,
                $"compact+HelpOpen floor {floor:F1} must be <= {CompactInvariantRig.CiBound}");

            // 4. Pinned/action row (the persistent toolbar, row 1) is never the budget donor —
            // its natural height stays small and positive regardless of mode.
            Control toolbar = root.Children.OfType<Control>().Single(c => Grid.GetRow(c) == 1);
            Assert.True(toolbar.DesiredSize.Height is > 0 and <= 40,
                $"pinned toolbar row height {toolbar.DesiredSize.Height:F1} out of the expected pinned-row range");
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

    /// <summary>
    /// Each of the four checks below gets its OWN fresh view/VM/window instance, rather than
    /// sharing one window across tab switches: switching TabControl.SelectedIndex mid-test left
    /// focus stranded on a control from the PREVIOUS tab (now detached/invisible), and Avalonia's
    /// own Tab navigation from a stale focused element that no longer participates in the tree
    /// behaved unpredictably (observed: an endless Button-only cycle that never reached the new
    /// tab's controls at all). A fresh host per check is simpler, isolates each scenario, and
    /// matches how a real user would arrive at each tab independently.
    /// </summary>
    private static void AssertReachabilityNoClipAndTabWalk(double innerHeight, bool expectCompact)
    {
        AssertNoClip(innerHeight, expectCompact);
        AssertLastControlReachable(innerHeight, tabIndex: 4,
            c => c.Content as string == "I - Set not content indexed attribute on each file before compressing.");
        AssertLastControlReachable(innerHeight, tabIndex: 5,
            c => c.Content as string == "Patch brute-forced RAR headers to match the original archive (Host OS, attributes, LARGE flag, mtime).");
        AssertTabWalk(innerHeight);
    }

    private static void AssertNoClip(double innerHeight, bool expectCompact)
    {
        ReconstructorViewModel vm = CreateVm();
        vm.CustomPackerWarning = "Custom packer detected."; // criterion B worst case: warning forced
        var view = new ReconstructorView { DataContext = vm };

        (Window window, Grid root) = CompactViewRig.HostAt(view, innerHeight);
        try
        {
            Assert.Equal(expectCompact, root.Classes.Contains("compactHeight"));
            CompactInvariantRig.AssertArrangesWithin(root, root.Bounds.Height);
        }
        finally { window.Close(); }
    }

    private static void AssertLastControlReachable(double innerHeight, int tabIndex, Func<CheckBox, bool> isTarget)
    {
        ReconstructorViewModel vm = CreateVm();
        var view = new ReconstructorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, innerHeight);
        try
        {
            var settingsTabs = window.GetVisualDescendants().OfType<TabControl>().Single(t => t.ItemCount == 6);
            settingsTabs.SelectedIndex = tabIndex;
            Dispatcher.UIThread.RunJobs();
            CheckBox target = window.GetVisualDescendants().OfType<CheckBox>().Single(isTarget);
            AssertReachableByAllThreeRoutes(window, settingsTabs, target);
        }
        finally { window.Close(); }
    }

    private static void AssertTabWalk(double innerHeight)
    {
        ReconstructorViewModel vm = CreateVm();
        var view = new ReconstructorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, innerHeight);
        try
        {
            // In compact mode Help starts collapsed (condition 5), so WindowsPackLink — the
            // RESTORE-direction target, only ever visible with the body force-expanded — is
            // hidden; the always-visible entry point there is the disclosure's own header
            // toggle. In expanded/flat mode the body IS force-expanded, so WindowsPackLink is
            // the genuinely reachable sentinel.
            bool compact = root.Classes.Contains("compactHeight");
            Control sentinel = compact
                ? window.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure")
                    .GetVisualDescendants().OfType<ToggleButton>().Single()
                : window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WindowsPackLink");
            CompactViewRig.AssertTabWalkStaysVisible(window, sentinel);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Each route is exercised from a genuine "not yet visible" start (offset reset between
    /// routes) — otherwise the first route scrolling the target into view would make the other
    /// two trivially no-op without ever exercising their own mechanism.
    /// </summary>
    private static void AssertReachableByAllThreeRoutes(Window window, TabControl settingsTabs, Control target)
    {
        ScrollViewer scroller = window.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(sv => sv.TemplatedParent is null && settingsTabs.IsVisualAncestorOf(sv));

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

    // ── 3. Tab-order snapshots ────────────────────────────────────────

    [AvaloniaFact]
    public void TabOrderSnapshot_Normal_MatchesPreChangeFixture()
    {
        ReconstructorViewModel vm = CreateVm();
        var view = new ReconstructorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            Assert.DoesNotContain("compactHeight", root.Classes);
            Button sentinel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WindowsPackLink");
            sentinel.Focus();
            Dispatcher.UIThread.RunJobs();

            IReadOnlyList<string> order = CompactViewRig.SnapshotTabOrder(window, root);
            Assert.Equal(NormalModeTabOrderFixture, order);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TabOrderSnapshot_Compact_MatchesSpecSection2Order()
    {
        ReconstructorViewModel vm = CreateVm();
        var view = new ReconstructorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Assert.Contains("compactHeight", root.Classes);
            ToggleButton headerToggle = root.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure")
                .GetVisualDescendants().OfType<ToggleButton>().Single();
            headerToggle.Focus();
            Dispatcher.UIThread.RunJobs();

            IReadOnlyList<string> order = CompactViewRig.SnapshotTabOrder(window, root);
            Assert.Equal(CompactModeTabOrderFixture, order);
        }
        finally { window.Close(); }
    }

    // ── 4. Chrome ─────────────────────────────────────────────────────

    [AvaloniaFact]
    public void SingleLinkInstance_ExistsInBothModes()
    {
        ReconstructorViewModel vm = CreateVm();
        var normalView = new ReconstructorView { DataContext = vm };
        (Window normalWindow, Grid normalRoot) = CompactViewRig.HostAt(normalView, ExpandedInner);
        try
        {
            Assert.DoesNotContain("compactHeight", normalRoot.Classes);
            Assert.Equal(3, normalWindow.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("link")));
        }
        finally { normalWindow.Close(); }

        ReconstructorViewModel vm2 = CreateVm();
        var compactView = new ReconstructorView { DataContext = vm2 };
        (Window compactWindow, Grid compactRoot) = CompactViewRig.HostAt(compactView, CompactInner);
        try
        {
            Assert.Contains("compactHeight", compactRoot.Classes);
            Assert.Equal(3, compactWindow.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("link")));
        }
        finally { compactWindow.Close(); }
    }

    [AvaloniaFact]
    public void CompactTip_NameAndHelpTextEqualFullText_TrimmingIsVisualOnly()
    {
        ReconstructorViewModel vm = CreateVm();
        var view = new ReconstructorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Assert.Contains("compactHeight", root.Classes);
            TextBlock tip = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("tipLine"));

            // Condition 1: trimming is VISUAL-ONLY over the full bound text. Retro-review finding
            // #3: asserting tip.Text alone (or that the ATTACHED AutomationProperties.Name is
            // null) is not the same claim as "AT announces the full text" — go through the REAL
            // automation peer, the same thing a screen reader actually calls.
            // TextBlockAutomationPeer.GetNameCore() returns Owner.Inlines?.Text ?? Owner.Text, so
            // with no explicit AutomationProperties.Name (asserted below) this is required to
            // equal tip.Text exactly.
            Assert.Null(AutomationProperties.GetName(tip));
            Assert.Equal(FullTip, tip.Text);
            Assert.Equal(FullTip, ControlAutomationPeer.CreatePeerForElement(tip).GetName());
            Assert.Equal(TextTrimming.CharacterEllipsis, tip.TextTrimming);
            Assert.Equal(TextWrapping.NoWrap, tip.TextWrapping);

            // Condition 2: ToolTip + AutomationProperties.HelpText both carry the full text.
            Assert.Equal(FullTip, ToolTip.GetTip(tip) as string);
            Assert.Equal(FullTip, AutomationProperties.GetHelpText(tip));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CompactTip_NeverDonates_IdenticalHeightHelpOpenAndClosed()
    {
        ReconstructorViewModel vm = CreateVm();
        var view = new ReconstructorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            TextBlock tip = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("tipLine"));
            double heightClosed = tip.Bounds.Height;

            Expander helpDisclosure = root.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure");
            helpDisclosure.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(CompactHeightBehavior.GetHelpOpen(root));

            double heightOpen = tip.Bounds.Height;
            Assert.Equal(heightClosed, heightOpen, precision: 1);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CompactEntry_HelpStartsCollapsed_LinksReachable_ExpanderResetsOnReentry()
    {
        ReconstructorViewModel vm = CreateVm();
        var view = new ReconstructorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Expander helpDisclosure = root.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure");
            Assert.False(helpDisclosure.IsExpanded); // condition 5: compact entry starts collapsed

            helpDisclosure.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();

            // "Invocable" is proven by REACHABILITY/usability (focusable, enabled, unobscured, a
            // real Tab lands on it) rather than actually raising Click — a genuine click on these
            // buttons opens a real OS browser via SystemLauncherService (ResourceLink.cs), which
            // must never fire as a side effect of an automated test run.
            Button windowsLink = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WindowsPackLink");
            Assert.True(windowsLink.Focusable);
            Assert.True(windowsLink.IsEffectivelyEnabled);
            CompactViewRig.AssertReachableByKeyboard(window, windowsLink);

            // The staged-focus guard's actual point (retro-review finding #6, mirroring Task 3's
            // identical fix): focus the header TOGGLE — visible and focusable ONLY in compact
            // mode (Styles.axaml's Grid.compactHeight ... /template/ ToggleButton IsVisible=True
            // override; flat/normal mode hides it) — then restore. The toggle going
            // IsVisible=false in flat mode must relocate focus to the wired RestoreFocusTarget
            // (WindowsPackLink), not strand it.
            ToggleButton headerToggle = helpDisclosure.GetVisualDescendants().OfType<ToggleButton>().Single();
            headerToggle.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(headerToggle.IsFocused);

            // Restore to normal, then re-enter compact: durability is compact-SESSION scoped only.
            window.Height += 250; // comfortably above Threshold + hysteresis slack
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("compactHeight", root.Classes);
            Assert.True(helpDisclosure.IsExpanded); // flat mode: force-expanded
            Assert.True(windowsLink.IsFocused,
                "restoring from a focused compact-only header toggle must relocate focus to the wired RestoreFocusTarget (WindowsPackLink), not strand it");

            window.Height -= 250;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("compactHeight", root.Classes);
            Assert.False(helpDisclosure.IsExpanded, "re-entering compact must reset Help to collapsed, not resume the prior session's open state");
        }
        finally { window.Close(); }
    }

    /// <summary>Records every launcher call so a test can assert an invocation actually fired.</summary>
    private sealed class RecordingLauncherService : ILauncherService
    {
        public List<string> OpenedUrls { get; } = [];

        public void OpenUrl(string url) => OpenedUrls.Add(url);

        public void RevealPath(string path) { }
    }

    /// <summary>
    /// Retro-review finding #4: reachability/focusability alone proves a link CAN be reached, not
    /// that activating it actually does anything. <see cref="ResourceLink.Launcher"/> is a test
    /// seam (added for this finding) precisely so a genuine invocation can be exercised safely —
    /// swapped for a <see cref="RecordingLauncherService"/> fake, restored in a finally block
    /// (it is a static, process-wide seam). Invoked via the REAL automation peer's
    /// <see cref="IInvokeProvider"/> (the same path a screen reader's "activate" gesture uses,
    /// which itself calls <c>Button.PerformClick()</c> — so this exercises Click too, not just
    /// UIA Invoke), never a raw <c>Button.ClickEvent</c> raise.
    /// </summary>
    [AvaloniaFact]
    public void CompactLinks_Invoke_RoutesThroughLauncher_WithoutARealBrowserLaunch()
    {
        var fakeLauncher = new RecordingLauncherService();
        ILauncherService originalLauncher = ResourceLink.Launcher;
        ResourceLink.Launcher = fakeLauncher;
        try
        {
            ReconstructorViewModel vm = CreateVm();
            var view = new ReconstructorView { DataContext = vm };
            (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
            try
            {
                Expander helpDisclosure = root.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure");
                helpDisclosure.IsExpanded = true;
                Dispatcher.UIThread.RunJobs();

                Button windowsLink = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "WindowsPackLink");
                string expectedUrl = Assert.IsType<string>(windowsLink.Tag);

                var invokeProvider = Assert.IsAssignableFrom<IInvokeProvider>(ControlAutomationPeer.CreatePeerForElement(windowsLink));
                invokeProvider.Invoke();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal([expectedUrl], fakeLauncher.OpenedUrls);
            }
            finally { window.Close(); }
        }
        finally { ResourceLink.Launcher = originalLauncher; }
    }

    [AvaloniaFact]
    public void HelpOpenDonation_TabRowMin60_BodyMaxHeight38_LastLinkKeyboardReachable()
    {
        ReconstructorViewModel vm = CreateVm();
        var view = new ReconstructorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            Expander helpDisclosure = root.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure");
            helpDisclosure.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(CompactHeightBehavior.GetHelpOpen(root));

            int tabControlRow = Grid.GetRow(window.GetVisualDescendants().OfType<TabControl>().Single(t => t.ItemCount == 6));
            Assert.Equal(60, root.RowDefinitions[tabControlRow].MinHeight);

            ScrollViewer body = helpDisclosure.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.Equal(38, body.MaxHeight);

            Button lastLink = window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("link")).Last();
            CompactViewRig.AssertReachableByKeyboard(window, lastLink);
        }
        finally { window.Close(); }
    }

    // ── 5. Splitter ───────────────────────────────────────────────────

    [AvaloniaFact]
    public void Splitter_FocusableAndNamed_UpDownResizes_ClampsAtCompactMinimums()
    {
        ReconstructorViewModel vm = CreateVm();
        var view = new ReconstructorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            GridSplitter splitter = window.GetVisualDescendants().OfType<GridSplitter>().Single();
            Assert.Equal("Resize options and log", AutomationProperties.GetName(splitter));

            splitter.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(splitter.IsFocused);

            int tabControlRow = Grid.GetRow(window.GetVisualDescendants().OfType<TabControl>().Single(t => t.ItemCount == 6));
            int logRow = tabControlRow + 2; // splitter sits between them at tabControlRow + 1

            // Drive the log row down to its 80-DIP compact floor (Down grows row 4 at row 6's expense).
            PressManyTimes(window, PhysicalKey.ArrowDown, 40);
            Assert.True(root.RowDefinitions[logRow].Height.Value >= 80 - 0.5,
                $"log row clamped below its 80-DIP minimum: {root.RowDefinitions[logRow].Height.Value:F1}");

            // Drive the TabControl row down to its 96-DIP compact floor (Up grows row 6 at row 4's expense).
            PressManyTimes(window, PhysicalKey.ArrowUp, 80);
            Assert.True(root.RowDefinitions[tabControlRow].Height.Value >= 96 - 0.5,
                $"TabControl row clamped below its 96-DIP minimum: {root.RowDefinitions[tabControlRow].Height.Value:F1}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Splitter_FocusVisual_MeetsContrastAgainstBothPanes()
    {
        ReconstructorViewModel vm = CreateVm();
        var view = new ReconstructorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, CompactInner);
        try
        {
            GridSplitter splitter = window.GetVisualDescendants().OfType<GridSplitter>().Single();

            splitter.Focus();
            Dispatcher.UIThread.RunJobs();

            // DynamicResource-resolved brushes come back as ImmutableSolidColorBrush at runtime,
            // not the mutable SolidColorBrush the resource dictionaries are authored with —
            // ISolidColorBrush is the common interface both implement.
            var focusBrush = Assert.IsAssignableFrom<ISolidColorBrush>(splitter.Background);

            var tabStripBrush = Assert.IsAssignableFrom<ISolidColorBrush>(Application.Current!.FindResource("SurfaceBackground"));
            var logBrush = Assert.IsAssignableFrom<ISolidColorBrush>(Application.Current!.FindResource("PanelBackground"));

            double contrastVsTabStrip = ContrastRatio(focusBrush.Color, tabStripBrush.Color);
            double contrastVsLog = ContrastRatio(focusBrush.Color, logBrush.Color);

            Assert.True(contrastVsTabStrip >= 3.0, $"focus brush vs tab-strip pane: {contrastVsTabStrip:F2}:1 (need >= 3:1)");
            Assert.True(contrastVsLog >= 3.0, $"focus brush vs log pane: {contrastVsLog:F2}:1 (need >= 3:1)");
        }
        finally { window.Close(); }
    }

    // ── 6. Frame-rig parity (criterion F: normal-mode pixels unchanged) ──

    /// <summary>
    /// Compares the flat-mode header region (row 0) against a standalone reconstruction of the
    /// PRE-TASK markup (verbatim intro TextBlock + WrapPanel of 3 links, the row-0 shape before
    /// this task wrapped it in the helpDisclosure Expander), both forced through a real render
    /// tick before measuring. DOCUMENTED FALLBACK INVOKED (see task report): Fluent's stock
    /// Expander carries hardcoded floors (control MinHeight 48, chevron cell 32) that made
    /// pixel-identical flat-mode chrome unreachable through style overrides, so Styles.axaml
    /// re-templates Expander.helpDisclosure entirely (mirroring the existing
    /// Expander.versionGroup re-template). ONE deliberate, spec-mandated difference is therefore
    /// expected and excluded from the "no diff" bar rather than hidden: the content StackPanel's
    /// own inset (Margin="0,0,4,0", "per house rule") plus the body ScrollViewer's reserved
    /// scrollbar track together narrow available width by a bounded, geometry-asserted amount
    /// versus the original's un-inset StackPanel.
    /// <para>
    /// Retro-review finding #5: geometry (height/width) alone cannot catch a shifted glyph, a
    /// recolored brush, or a reflowed line inside the surviving region — only a REAL pixel
    /// comparison can (<see cref="AssertPixelIdenticalOverCommonRegion"/>, RenderTargetBitmap +
    /// CopyPixels, same technique as HexViewControlTests). Naively cropping "old at its own
    /// natural width" and "new" to their common width is not a fair comparison, though: the
    /// pre-disclosure paragraph re-wraps its OWN words depending on how much width it is actually
    /// given, so "old at width 676" legitimately breaks lines differently than "new at width 649"
    /// even at identical total line count/height — a mechanical, expected consequence of the
    /// sanctioned width delta, not a rendering regression. So a SECOND old reconstruction is built
    /// at NEW's own measured width (no reflow possible — both sides wrap identically) and required
    /// to be byte-for-byte pixel-identical to new, no cropping needed. This gate is not vacuous —
    /// building it surfaced two real, since-fixed issues: the reflow-from-unequal-width artifact
    /// just described, and (initially) a missing Foreground="{DynamicResource ForegroundSecondary}"
    /// on three of this method's own WrapPanel TextBlocks in <see cref="BuildPreDisclosureRow0Window"/>
    /// (an inaccuracy in the "verbatim" reconstruction, not a production bug).
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void FrameRig_NormalMode_HeaderRegionMatchesPreDisclosureShape()
    {
        ReconstructorViewModel vm = CreateVm();
        var view = new ReconstructorView { DataContext = vm };
        (Window newWindow, Grid newRoot) = CompactViewRig.HostAt(view, ExpandedInner);
        try
        {
            Assert.DoesNotContain("compactHeight", newRoot.Classes);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            Control newRow0 = newRoot.Children.OfType<Control>().Single(c => Grid.GetRow(c) == 0);
            Size newSize = newRow0.Bounds.Size;

            Window oldWindow = BuildPreDisclosureRow0Window(CompactInvariantRig.InnerWidth);
            try
            {
                oldWindow.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Control oldRow0 = (Control)oldWindow.Content!;
                Size oldSize = oldRow0.Bounds.Size;

                // Height must match exactly — this is the visually significant dimension (a
                // taller/shorter header block would shift every row below it). Confirmed exact:
                // nothing about the width narrowing below causes the WrapPanel to wrap onto an
                // extra line.
                Assert.Equal(oldSize.Height, newSize.Height, precision: 0);

                // Width is narrower by a bounded, EXPLAINED amount — not just the 4-DIP content
                // inset, but ALSO the body ScrollViewer's reserved vertical-scrollbar track
                // (AllowAutoHide="False", the house style — see Styles.axaml's own rationale):
                // that track is reserved even though the content never needs to scroll at normal
                // size, since the original bare StackPanel had no such reservation at all. Pinned
                // as a measured, understood regression range rather than re-derived per run.
                double widthNarrowing = oldSize.Width - newSize.Width;
                Assert.True(widthNarrowing is > 0 and <= 30,
                    $"header region width narrowed by {widthNarrowing:F1} DIPs (old {oldSize.Width:F1}, new {newSize.Width:F1}) — expected a small, explained narrowing (inset + reserved scrollbar track), not an unbounded drift");
            }
            finally { oldWindow.Close(); }

            // Retro-review finding #5: geometry alone cannot catch a shifted glyph, a recolored
            // brush, or a misaligned baseline — only a real pixel comparison can. But naively
            // cropping the OLD-at-natural-width render and the NEW render to their common width
            // is not a fair comparison: the pre-disclosure paragraph re-wraps its OWN words
            // depending on how much width it is actually given, so "old at width 676" legitimately
            // breaks its lines differently than "new at width 649" even though both land on the
            // same total LINE COUNT (hence the identical height already asserted above) — that
            // reflow is a mechanical, expected consequence of the sanctioned width delta, not a
            // rendering regression. Isolating the real question ("does the new Expander/
            // ScrollViewer wrapper paint anything different from the old bare StackPanel, once
            // you neutralize the width delta both sides already agree is sanctioned") means giving
            // the OLD markup the SAME width NEW actually measured, so nothing reflows differently
            // — then requiring true byte-for-byte pixel identity, no cropping needed.
            Window widthMatchedOldWindow = BuildPreDisclosureRow0Window(newSize.Width);
            try
            {
                widthMatchedOldWindow.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Control widthMatchedOldRow0 = (Control)widthMatchedOldWindow.Content!;
                Size widthMatchedOldSize = widthMatchedOldRow0.Bounds.Size;

                Assert.Equal(newSize.Height, widthMatchedOldSize.Height, precision: 0);
                AssertPixelIdenticalOverCommonRegion(widthMatchedOldRow0, widthMatchedOldSize, newRow0, newSize);
            }
            finally { widthMatchedOldWindow.Close(); }
        }
        finally { newWindow.Close(); }
    }

    /// <summary>
    /// Reconstruction of ReconstructorView.axaml's row-0 StackPanel before this task (git history),
    /// verbatim except for <paramref name="width"/> — the caller picks the window width (and thus
    /// the constraint the caption/WrapPanel wrap against) so it can either reproduce the ORIGINAL
    /// natural layout (<see cref="CompactInvariantRig.InnerWidth"/>) or match today's actual
    /// narrower measured width for a fair, no-reflow pixel comparison.
    /// </summary>
    private static Window BuildPreDisclosureRow0Window(double width)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        stack.Children.Add(new TextBlock
        {
            Text = "Reconstruct original RAR archives from an SRR file by brute-forcing WinRAR compression settings. Provide the source files and a WinRAR executable, then configure which RAR versions and switches to try.",
            Foreground = (IBrush?)Application.Current!.FindResource("ForegroundSecondary"),
            FontSize = (double)Application.Current!.FindResource("FontSizeCaption")!,
            TextWrapping = TextWrapping.Wrap,
        });

        IBrush? secondary = (IBrush?)Application.Current!.FindResource("ForegroundSecondary");
        var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        wrap.Children.Add(new TextBlock { Text = "WinRAR versions needed for reconstruction can be downloaded from:", Foreground = secondary, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), FontSize = (double)Application.Current!.FindResource("FontSizeCaption")! });
        wrap.Children.Add(new Button { Classes = { "link" }, Content = "Extracted files for Windows (ready to use)", FontSize = (double)Application.Current!.FindResource("FontSizeCaption")!, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        wrap.Children.Add(new TextBlock { Text = ",", Foreground = secondary, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), FontSize = (double)Application.Current!.FindResource("FontSizeCaption")! });
        wrap.Children.Add(new Button { Classes = { "link" }, Content = "Extracted files for Linux (ready to use)", FontSize = (double)Application.Current!.FindResource("FontSizeCaption")!, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        wrap.Children.Add(new TextBlock { Text = "or", Foreground = secondary, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), FontSize = (double)Application.Current!.FindResource("FontSizeCaption")! });
        wrap.Children.Add(new Button { Classes = { "link" }, Content = "Original files from RAR FTP (Windows)", FontSize = (double)Application.Current!.FindResource("FontSizeCaption")!, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        stack.Children.Add(wrap);

        return new Window { Width = width, SizeToContent = SizeToContent.Height, Content = stack };
    }

    /// <summary>
    /// Renders both controls to their own <see cref="RenderTargetBitmap"/> (each sized to its own
    /// full bounds, independent of whatever window each is actually hosted in — <c>Render</c> is
    /// an immediate-mode draw of the visual's own subtree, not a capture of its parent's canvas —
    /// confirmed via <c>ImmediateRenderer</c>'s own source: the passed-in visual is always treated
    /// as its own root at (0,0), so neither control's real on-screen position leaks in) and asserts
    /// every pixel is byte-identical. The two callers of this method always pass equal-width
    /// controls (a width-matched reconstruction vs the real view — see the caller), so no cropping
    /// is expected; the defensive min/floor below only guards against a stray sub-DIP rounding
    /// artifact in the two independent layout passes, and is itself asserted tight.
    /// </summary>
    private static void AssertPixelIdenticalOverCommonRegion(Control oldControl, Size oldSize, Control newControl, Size newSize)
    {
        const int BytesPerPixel = 4;

        Assert.True(Math.Abs(oldSize.Width - newSize.Width) < 1.0,
            $"pixel comparison requires matched widths (old {oldSize.Width:F2}, new {newSize.Width:F2}) — " +
            "the caller must render the OLD reconstruction at NEW's own measured width first.");

        var oldPixelSize = new PixelSize((int)Math.Ceiling(oldSize.Width), (int)Math.Ceiling(oldSize.Height));
        var newPixelSize = new PixelSize((int)Math.Ceiling(newSize.Width), (int)Math.Ceiling(newSize.Height));

        byte[] oldPixels = RenderToPixelBuffer(oldControl, oldPixelSize);
        byte[] newPixels = RenderToPixelBuffer(newControl, newPixelSize);

        int commonWidth = (int)Math.Floor(Math.Min(oldSize.Width, newSize.Width));
        int commonHeight = (int)Math.Floor(Math.Min(oldSize.Height, newSize.Height));
        Assert.True(commonWidth > 0 && commonHeight > 0,
            $"common comparison region must be non-empty (old {oldSize}, new {newSize})");

        int oldStride = oldPixelSize.Width * BytesPerPixel;
        int newStride = newPixelSize.Width * BytesPerPixel;
        int rowBytes = commonWidth * BytesPerPixel;

        for (int y = 0; y < commonHeight; y++)
        {
            int oldRowStart = y * oldStride;
            int newRowStart = y * newStride;

            for (int x = 0; x < rowBytes; x++)
            {
                if (oldPixels[oldRowStart + x] == newPixels[newRowStart + x])
                {
                    continue;
                }

                int pixelX = x / BytesPerPixel;
                Assert.Fail(
                    $"header region pixel mismatch at ({pixelX}, {y}) — old byte 0x{oldPixels[oldRowStart + x]:X2} " +
                    $"vs new byte 0x{newPixels[newRowStart + x]:X2}. Compared region was " +
                    $"{commonWidth}x{commonHeight} DIPs (old render {oldPixelSize}, new render {newPixelSize}).");
            }
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

    private static void PressManyTimes(Window window, PhysicalKey key, int times)
    {
        for (int i = 0; i < times; i++)
        {
            window.KeyPressQwerty(key, RawInputModifiers.None);
            window.KeyReleaseQwerty(key, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>WCAG 2.x relative luminance + contrast ratio, computed from rendered brush colors — never a hardcoded number.</summary>
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

    // ── Fixtures (captured from real, green CompactViewRig.SnapshotTabOrder runs against
    // this task's finished implementation — see task report's retro-fix section for the capture
    // method). Retro-review finding #2: Describe() now reads the control's REAL automation peer
    // name (falling back to x:Name, then to the bare type), so same-type controls with distinct
    // content/x:Name are no longer collapsed to indistinguishable "Button:" entries — an early
    // trap, a same-type reorder, or a swapped stop is now caught by content, not just by count. ──

    /// <summary>
    /// Normal mode: identical shape to today's — the disclosure's body is force-expanded with
    /// its header hidden, so the 3 link buttons occupy exactly the StackPanel's old slot. Start
    /// is absent (disabled — CanExecute false for the inert VM's empty paths, so Tab correctly
    /// skips it): 3 links (peer name = their Content text) + Export/Import-Config/Import-from-SRR,
    /// then the Paths sub-tab (TabItem peer name falls back to its body content's ToString(), i.e.
    /// the hosted ScrollViewer — still deterministic and distinct from every other stop's type),
    /// its 4 Browse/TextBox pairs (TextBoxes disambiguated by their x:Name — Browse buttons share
    /// identical Content text but each is immediately followed by a uniquely-named TextBox, so a
    /// pair-reorder is still caught), splitter, Save-log button, Auto-scroll checkbox.
    /// </summary>
    private static readonly IReadOnlyList<string> NormalModeTabOrderFixture =
    [
        "Button:Extracted files for Windows (ready to use)",
        "Button:Extracted files for Linux (ready to use)",
        "Button:Original files from RAR FTP (Windows)",
        "Button:Export Config", "Button:Import Config", "Button:Import from SRR",
        "TabItem:Avalonia.Controls.ScrollViewer",
        "Button:Browse", "TextBox:WinRARTextBox",
        "Button:Browse", "TextBox:ReleaseTextBox",
        "Button:Browse", "TextBox:VerifyTextBox",
        "Button:Browse", "TextBox:OutputTextBox",
        "GridSplitter:Resize options and log",
        "Button:Save log...", "CheckBox:Auto-scroll",
    ];

    /// <summary>
    /// Compact order (spec §2): disclosure header → (body skipped: Help starts collapsed per
    /// condition 5, so the 3 link buttons are IsVisible=false and correctly excluded from Tab
    /// order) → toolbar (3 enabled buttons — Start is absent, same reason as normal mode) →
    /// work area (Paths sub-tab) → splitter → log. Identical tail to normal mode; only the head
    /// differs (header toggle, named by its own Content text, prepended in place of the — here
    /// hidden — link buttons).
    /// </summary>
    private static readonly IReadOnlyList<string> CompactModeTabOrderFixture =
    [
        "ToggleButton:Help & links",
        "Button:Export Config", "Button:Import Config", "Button:Import from SRR",
        "TabItem:Avalonia.Controls.ScrollViewer",
        "Button:Browse", "TextBox:WinRARTextBox",
        "Button:Browse", "TextBox:ReleaseTextBox",
        "Button:Browse", "TextBox:VerifyTextBox",
        "Button:Browse", "TextBox:OutputTextBox",
        "GridSplitter:Resize options and log",
        "Button:Save log...", "CheckBox:Auto-scroll",
    ];
}
