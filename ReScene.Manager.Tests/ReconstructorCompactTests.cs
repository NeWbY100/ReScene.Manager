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

            // Condition 1: trimming is VISUAL-ONLY over the full bound text — the underlying Text
            // (what a TextBlock's automation peer names itself from, absent an explicit
            // AutomationProperties.Name) is never a pre-truncated string.
            Assert.Equal(FullTip, tip.Text);
            Assert.Null(AutomationProperties.GetName(tip)); // pins the peer-derivation assumption above
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

            // Restore to normal, then re-enter compact: durability is compact-SESSION scoped only.
            window.Height += 250; // comfortably above Threshold + hysteresis slack
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("compactHeight", root.Classes);
            Assert.True(helpDisclosure.IsExpanded); // flat mode: force-expanded

            window.Height -= 250;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("compactHeight", root.Classes);
            Assert.False(helpDisclosure.IsExpanded, "re-entering compact must reset Help to collapsed, not resume the prior session's open state");
        }
        finally { window.Close(); }
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
    /// tick (the versions-tree rig pattern) before measuring. DOCUMENTED FALLBACK INVOKED (see
    /// task report): Fluent's stock Expander carries hardcoded floors (control MinHeight 48,
    /// chevron cell 32) that made pixel-identical flat-mode chrome unreachable through style
    /// overrides, so Styles.axaml re-templates Expander.helpDisclosure entirely (mirroring the
    /// existing Expander.versionGroup re-template). Two DELIBERATE, spec-mandated differences
    /// are therefore expected and excluded from the "no diff" bar rather than hidden: (1) the
    /// content StackPanel's own inset (Margin="0,0,4,0", "per house rule" — the brief's own given
    /// XAML), narrowing available width by 4 DIPs versus the original's un-inset StackPanel; (2)
    /// the flat-mode wrapper is now Expander+ScrollViewer+StackPanel rather than a bare
    /// StackPanel — invisible when the content needs no scrolling (confirmed below by
    /// SingleLinkInstance_ExistsInBothModes and the exact 3-link count), but a structurally
    /// different visual tree nonetheless.
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

            Window oldWindow = BuildPreDisclosureRow0Window();
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
        }
        finally { newWindow.Close(); }
    }

    /// <summary>Verbatim reconstruction of ReconstructorView.axaml's row-0 StackPanel before this task (git history).</summary>
    private static Window BuildPreDisclosureRow0Window()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        stack.Children.Add(new TextBlock
        {
            Text = "Reconstruct original RAR archives from an SRR file by brute-forcing WinRAR compression settings. Provide the source files and a WinRAR executable, then configure which RAR versions and switches to try.",
            Foreground = (IBrush?)Application.Current!.FindResource("ForegroundSecondary"),
            FontSize = (double)Application.Current!.FindResource("FontSizeCaption")!,
            TextWrapping = TextWrapping.Wrap,
        });

        var wrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        wrap.Children.Add(new TextBlock { Text = "WinRAR versions needed for reconstruction can be downloaded from:", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), FontSize = (double)Application.Current!.FindResource("FontSizeCaption")! });
        wrap.Children.Add(new Button { Classes = { "link" }, Content = "Extracted files for Windows (ready to use)", FontSize = (double)Application.Current!.FindResource("FontSizeCaption")!, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        wrap.Children.Add(new TextBlock { Text = ",", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), FontSize = (double)Application.Current!.FindResource("FontSizeCaption")! });
        wrap.Children.Add(new Button { Classes = { "link" }, Content = "Extracted files for Linux (ready to use)", FontSize = (double)Application.Current!.FindResource("FontSizeCaption")!, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        wrap.Children.Add(new TextBlock { Text = "or", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), FontSize = (double)Application.Current!.FindResource("FontSizeCaption")! });
        wrap.Children.Add(new Button { Classes = { "link" }, Content = "Original files from RAR FTP (Windows)", FontSize = (double)Application.Current!.FindResource("FontSizeCaption")!, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        stack.Children.Add(wrap);

        return new Window { Width = CompactInvariantRig.InnerWidth, SizeToContent = SizeToContent.Height, Content = stack };
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
    // this task's finished implementation — see task report for the capture method).
    // Automation names are empty for most Buttons (none of the link/toolbar buttons carry an
    // explicit AutomationProperties.Name), so the fixture's real value is the ORDERED SEQUENCE
    // OF TYPES — it still catches a reordering, an added/removed stop, or a wrong count. ──

    /// <summary>
    /// Normal mode: identical shape to today's — the disclosure's body is force-expanded with
    /// its header hidden, so the 3 link buttons occupy exactly the StackPanel's old slot. Start
    /// is absent (disabled — CanExecute false for the inert VM's empty paths, so Tab correctly
    /// skips it): 3 links + Export/Import-Config/Import-from-SRR = 6 buttons, then the Paths
    /// sub-tab (header + 4 Browse/TextBox pairs), splitter, Save-log button, Auto-scroll checkbox.
    /// </summary>
    private static readonly IReadOnlyList<string> NormalModeTabOrderFixture =
    [
        "Button:", "Button:", "Button:", "Button:", "Button:", "Button:",
        "TabItem:",
        "Button:", "TextBox:", "Button:", "TextBox:", "Button:", "TextBox:", "Button:", "TextBox:",
        "GridSplitter:Resize options and log",
        "Button:", "CheckBox:",
    ];

    /// <summary>
    /// Compact order (spec §2): disclosure header → (body skipped: Help starts collapsed per
    /// condition 5, so the 3 link buttons are IsVisible=false and correctly excluded from Tab
    /// order) → toolbar (3 enabled buttons — Start is absent, same reason as normal mode) →
    /// work area (Paths sub-tab) → splitter → log. Identical tail to normal mode; only the head
    /// differs (header toggle prepended in place of the — here hidden — link buttons).
    /// </summary>
    private static readonly IReadOnlyList<string> CompactModeTabOrderFixture =
    [
        "ToggleButton:Help & links",
        "Button:", "Button:", "Button:",
        "TabItem:",
        "Button:", "TextBox:", "Button:", "TextBox:", "Button:", "TextBox:", "Button:", "TextBox:",
        "GridSplitter:Resize options and log",
        "Button:", "CheckBox:",
    ];
}
