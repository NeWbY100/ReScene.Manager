using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

    /// <summary>
    /// Codex finding (post-fix-1 review): the fix-1 version force-focused a PRESUMED first
    /// control and walked forward from it — that presumption was never actually verified. This
    /// version adopts the now-hardened <see cref="CompactViewRig"/> idioms directly: a forward
    /// walk with an INDEPENDENTLY-resolved completeness set (so an unreached control, including
    /// one that would only be reachable BEFORE the presumed sentinel, fails loudly rather than
    /// being silently absorbed), plus a REVERSE walk anchored at the forward walk's own LAST
    /// stop (the unambiguous "boundary" — the log's Save button, not a presumed starting point)
    /// that must retrace the ENTIRE forward order and land back on the forward walk's FIRST
    /// stop — the actual, empirical proof that the presumed-first control really is first,
    /// rather than an assumption. SRSCreatorView is a single keyboard-navigation scope (no
    /// nested TabControl like Reconstructor's), so this is one forward walk plus one reverse
    /// walk — no per-scope machinery.
    /// </summary>
    private static void AssertTabWalk(double innerHeight)
    {
        SRSCreatorViewModel vm = CreateVm();
        vm.InputPath = @"C:\release\sample.mkv";
        vm.OutputPath = @"C:\release\sample.srs"; // Create SRS enabled: its own position is pinned, not left unverified
        var view = new SRSCreatorView { DataContext = vm };
        (Window window, Grid root) = CompactViewRig.HostAt(view, innerHeight);
        try
        {
            bool compact = root.Classes.Contains("compactHeight");

            // In compact mode Help starts collapsed (condition 5): the body's own prose is not a
            // tab stop while collapsed, so the header toggle is the walk's genuine entry point.
            // In expanded/flat mode the disclosure contributes NOTHING to tab order at all (its
            // header is hidden by style and its body is plain, non-focusable prose) — Sample
            // File's own Browse button is the presumed first stop there, PROVEN (not merely
            // assumed) by the reverse walk's own boundary-landing assertion below.
            Control sentinel = compact
                ? root.GetVisualDescendants().OfType<Expander>().Single(e => e.Name == "HelpDisclosure")
                    .GetVisualDescendants().OfType<ToggleButton>().Single()
                : window.GetVisualDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Command, vm.BrowseInputCommand));

            IReadOnlyList<string> fixture = compact ? CompactModeTabOrderFixture : NormalModeTabOrderFixture;
            List<Control> expectedStops = ResolveExpectedStops(window, fixture);

            sentinel.Focus();
            Dispatcher.UIThread.RunJobs();
            CompactViewRig.TabOrderCapture forwardCapture = CompactViewRig.CaptureTabOrderControls(window, root, expectedStops);
            IReadOnlyList<Control> forwardOrder = forwardCapture.Order;
            Assert.Equal(fixture, forwardOrder.Select(CompactViewRig.Describe));

            // The forward walk's terminal external target must be the SPECIFIC, expected
            // shell-chrome boundary — the rig's own fake shell (CompactViewRig's BuildShell)
            // puts a "_File" MenuItem right after the TabControl in Z-order, matching
            // Reconstructor's own identical finding against the same shared shell. Confirmed by
            // a real run: FirstExternalTarget's own Describe is `MenuItem name="File" id=""`
            // (the accessible name strips the access-key underscore; matched here against the
            // raw Header property, "_File", which is what BuildShell actually declares).
            MenuItem expectedExternalBoundary = window.GetVisualDescendants().OfType<MenuItem>()
                .Single(m => m.Header as string == "_File");
            Assert.True(forwardCapture.FirstExternalTarget is not null,
                "forward capture should have left root's scope onto an external control, not ended via a stable loop within root");
            Assert.True(ReferenceEquals(expectedExternalBoundary, forwardCapture.FirstExternalTarget),
                $"forward capture's terminal external target should be {CompactViewRig.Describe(expectedExternalBoundary)}, " +
                $"not {CompactViewRig.Describe(forwardCapture.FirstExternalTarget!)} — same description does not mean same control instance.");

            // REVERSE: anchored at the forward walk's own LAST stop (the unambiguous boundary),
            // never a presumed starting point. Confirmed by a real run: a single scope means the
            // reverse walk genuinely retraces the WHOLE forward order and lands back on the
            // forward walk's FIRST stop — the actual, empirical proof that the presumed forward
            // sentinel is genuinely first, not an assumption.
            CompactViewRig.TabWalkResult reverse = CompactViewRig.RunTabPass(window, forwardOrder[^1], forward: false, expectedStops);

            List<Control> expectedReverseOrder = [.. forwardOrder.Reverse()];
            AssertSameControlSequence(expectedReverseOrder, reverse.Order, "reverse");

            Assert.True(ReferenceEquals(reverse.LoopedBackTo, forwardOrder[0]),
                $"the reverse walk should land back on {CompactViewRig.Describe(forwardOrder[0])} (the forward walk's own first " +
                $"stop), not {CompactViewRig.Describe(reverse.LoopedBackTo)} — this is the actual proof that the forward " +
                "sentinel is genuinely first, not a presumption.");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Resolves a committed, description-based fixture (see <see cref="CompactViewRig.Describe"/>)
    /// back into the REAL <see cref="Control"/> references it names, for THIS SPECIFIC window —
    /// completeness-checking is reference-based, and a fixture committed to source can only ever
    /// be strings across separate test runs. Reimplemented locally (mirroring
    /// <c>ReconstructorCompactTests</c>' own private helper of the same shape) rather than
    /// extending the shared rig, so Tasks 4-6's own use of the unmodified rig is never put at
    /// risk by a change scoped to this view's own need. Matching is a COUNTED MULTISET, not a
    /// set — this view's three identically-described "Browse" buttons need three real, distinct
    /// matches, not merely "at least one" — so a regression that removed one of them (leaving
    /// two) is still caught here rather than silently resolving successfully.
    /// </summary>
    private static List<Control> ResolveExpectedStops(Window window, IReadOnlyCollection<string> fixture)
    {
        Dictionary<string, int> expectedCounts = fixture
            .GroupBy(description => description)
            .ToDictionary(g => g.Key, g => g.Count());

        ILookup<string, Control> byDescription = window.GetVisualDescendants().OfType<Control>()
            .ToLookup(CompactViewRig.Describe);

        List<Control> resolved = [];
        List<string> shortfalls = [];
        foreach ((string description, int expectedCount) in expectedCounts)
        {
            List<Control> matches = [.. byDescription[description]];
            if (matches.Count < expectedCount)
            {
                shortfalls.Add($"\"{description}\" expects {expectedCount}, this window has {matches.Count}");
                continue;
            }

            resolved.AddRange(matches.Take(expectedCount));
        }

        if (shortfalls.Count > 0)
        {
            throw new Xunit.Sdk.XunitException(
                $"{shortfalls.Count} fixture descriptions do not have enough matching controls in " +
                $"this window (counted, not merely present): {string.Join("; ", shortfalls)}");
        }

        return resolved;
    }

    /// <summary>
    /// OBJECT-REFERENCE-exact sequence comparison — asserts <paramref name="actual"/> is,
    /// position for position, the SAME control REFERENCES as <paramref name="expected"/>, not
    /// merely the same DESCRIPTIONS. A description-based <c>Assert.Equal</c> cannot distinguish
    /// a permutation of controls that all describe identically (this view's three "Browse"
    /// buttons, none of which carry a distinguishing x:Name or accessible name); this can, since
    /// it never converts either side to a string until it already knows a mismatch exists and
    /// needs to report it. Mirrors <c>ReconstructorCompactTests</c>' own helper of the same
    /// shape.
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
    // criterion-C helper, now ALSO the exact-order/completeness/reverse-boundary authority) at
    // the exact heights RenderedMatrix_CompactAt700x450_... and
    // RenderedMatrix_FreshAtThresholdPlusOne_... already exercise. Kept as separate, named entry
    // points (rather than deleted as pure duplicates) so "the tab order is exactly this" reads
    // as its own explicit, discoverable assertion — not merely a side effect of a criterion-C
    // reachability test.

    [AvaloniaFact]
    public void TabOrderSnapshot_Normal_MatchesPreChangeFixture() => AssertTabWalk(ExpandedInner);

    [AvaloniaFact]
    public void TabOrderSnapshot_Compact_MatchesSpecSection2Order() => AssertTabWalk(CompactInner);

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
    /// RECAPTURED after the hug-bug fix (HorizontalAlignment/HorizontalContentAlignment=
    /// "Stretch" on the Expander — see the view's own XAML comment): comparing at TRUE ORIGINAL
    /// geometries — old at its own natural, unconstrained width
    /// (<see cref="CompactInvariantRig.InnerWidth"/>); new at its own real width, measured from
    /// the intro TextBlock itself (this view's row 0 has no intermediate content StackPanel
    /// unlike Reconstructor's — the Margin="0,0,4,0" inset sits directly on the TextBlock) —
    /// plus a REAL pixel comparison (RenderTargetBitmap + CopyPixels, same technique as
    /// Reconstructor's own retro-hardened version and HexViewControlTests), not merely a
    /// geometry check: geometry alone cannot catch a shifted glyph, a recolored brush, or a
    /// reflowed line inside the surviving region.
    /// <para>
    /// MEASURED, no mask needed: with the hug-bug fixed, newRow0 (the Expander) itself now
    /// matches newRoot's full 676 width exactly (confirmed: 0.0 narrowing when comparing
    /// newRow0 directly, which is what originally exposed this test as stale — see the task
    /// report). The TRUE remaining delta is the TextBlock's own 4-DIP right margin (672 vs
    /// 676) — the SAME minimal, fully-explained inset Reconstructor's own retro-review arrived
    /// at. Unlike Reconstructor's own text, this view's shorter intro (no WrapPanel/links row
    /// below it to also check) does NOT push any word across a line-break boundary at the
    /// narrower 672-DIP measure — confirmed by the byte-for-byte comparison below requiring NO
    /// excluded band beyond the mandatory trailing-width strip (present only in old's wider
    /// render, with no counterpart in new at all). If a future content change ever needs one,
    /// name and document it explicitly here — never broaden this mask blindly.
    /// </para>
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

            // NEW's true comparison partner: the intro TextBlock itself (this view's row 0 has
            // no intermediate content StackPanel, unlike Reconstructor's) — NOT newRow0 (the
            // outer Expander), which the hug-bug fix now stretches to the full 676 with no
            // narrowing of its own at all.
            TextBlock newCaption = newRow0.GetVisualDescendants().OfType<TextBlock>().Single();
            Size newSize = newCaption.Bounds.Size;

            Window oldWindow = BuildPreDisclosureRow0Window();
            try
            {
                oldWindow.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Dispatcher.UIThread.RunJobs();
                Control oldRow0 = (Control)oldWindow.Content!;
                Size oldSize = oldRow0.Bounds.Size;

                // Height must match exactly — the visually significant dimension (a
                // taller/shorter header block would shift every row below it). Confirmed exact:
                // nothing about the width narrowing below causes the TextBlock to wrap onto an
                // extra line.
                Assert.Equal(oldSize.Height, newSize.Height, precision: 0);

                // The intro TextBlock's own documented, intentional inset (Margin="0,0,4,0",
                // "per house rule") — MEASURED, not the pre-hug-bug-fix figure this test
                // originally carried (which conflated the inset with the hug bug itself).
                double widthNarrowing = oldSize.Width - newSize.Width;
                Assert.Equal(4.0, widthNarrowing, precision: 0);

                // TRUE pixel comparison at each side's own real geometry — no mask needed
                // (MEASURED, see this test's own doc comment) beyond the mandatory trailing
                // strip AssertPixelIdenticalOutsideHeaderMask itself always excludes (present
                // only in old's wider render).
                AssertPixelIdenticalOutsideHeaderMask(oldRow0, oldSize, newCaption, newSize, wordWrapExcludedHeight: 0);
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

    /// <summary>
    /// Renders both controls to their own <see cref="RenderTargetBitmap"/> at their OWN true
    /// geometry (each sized to its own full bounds, independent of whatever window each is
    /// actually hosted in), then excludes only the trailing rectangle that exists solely in
    /// <paramref name="oldControl"/>'s wider render (x from <paramref name="newSize"/>'s width
    /// to <paramref name="oldSize"/>'s width, full height) plus, if non-zero,
    /// <paramref name="wordWrapExcludedHeight"/> rows from the top — and requires true
    /// byte-for-byte pixel identity everywhere else. Mirrors
    /// <c>ReconstructorCompactTests</c>' own helper of the same shape and rationale.
    /// </summary>
    private static void AssertPixelIdenticalOutsideHeaderMask(Control oldControl, Size oldSize, Control newControl, Size newSize, double wordWrapExcludedHeight)
    {
        const int BytesPerPixel = 4;

        Assert.True(oldSize.Width > newSize.Width,
            $"the header mask assumes old is the WIDER render, since old's bare TextBlock never " +
            $"had new's content-inset narrowing (old {oldSize.Width:F2}, new {newSize.Width:F2}).");

        var oldPixelSize = new PixelSize((int)Math.Ceiling(oldSize.Width), (int)Math.Ceiling(oldSize.Height));
        var newPixelSize = new PixelSize((int)Math.Ceiling(newSize.Width), (int)Math.Ceiling(newSize.Height));

        byte[] oldPixels = RenderToPixelBuffer(oldControl, oldPixelSize);
        byte[] newPixels = RenderToPixelBuffer(newControl, newPixelSize);

        int maskedCompareWidth = (int)Math.Floor(newSize.Width);
        int compareHeight = (int)Math.Floor(Math.Min(oldSize.Height, newSize.Height));
        int wordWrapExcludedRows = (int)Math.Ceiling(wordWrapExcludedHeight);
        Assert.True(maskedCompareWidth > 0 && compareHeight > wordWrapExcludedRows,
            $"comparison region must be non-empty (old {oldSize}, new {newSize}, excluded band {wordWrapExcludedHeight:F1})");

        int oldStride = oldPixelSize.Width * BytesPerPixel;
        int newStride = newPixelSize.Width * BytesPerPixel;
        int rowBytes = maskedCompareWidth * BytesPerPixel;

        for (int y = wordWrapExcludedRows; y < compareHeight; y++)
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
                    $"{maskedCompareWidth}x{compareHeight} DIPs, rows {wordWrapExcludedRows}-{compareHeight - 1} " +
                    $"(old render {oldPixelSize}, new render {newPixelSize}); excluded: the trailing " +
                    $"strip (x from {maskedCompareWidth} to {oldPixelSize.Width - 1}, old-only).");
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

    // ── Fixtures (captured from real, green CompactViewRig.CaptureTabOrderControls runs against
    // this task's finished implementation, WITH Create SRS enabled — see task report for the
    // capture method). Each entry is CompactViewRig.Describe's own format (real automation peer
    // name plus x:Name, reported separately — see its own doc). Same-typed siblings that
    // describe identically (this view's three "Browse" buttons) are still disambiguated where
    // it matters: AssertTabWalk's completeness check is a counted multiset (ResolveExpectedStops)
    // and its reverse-order check is OBJECT-REFERENCE-exact (AssertSameControlSequence), so a
    // swap between two identically-described siblings is still caught even though the fixture
    // STRING itself could not tell them apart on its own. ──

    /// <summary>
    /// Normal mode, starting at Sample File's own Browse button — PROVEN first (not presumed):
    /// the reverse walk anchored at the tail end (Save log) retraces this exact sequence
    /// backwards and lands back on this same Browse button, empirically confirming nothing
    /// precedes it. From there: Sample File's Browse + its TextBox, Main file's Browse/Clear +
    /// its TextBox, Output's Browse + its TextBox, the App name TextBox, Create SRS
    /// (InputPath/OutputPath set so it is genuinely enabled and its own position is pinned —
    /// CanExecute false for the default inert VM would otherwise leave it absent and
    /// unverified, the same situation the Reconstructor's own "Start" button fixture
    /// documents), then Save log. Cancel is absent (hidden, IsCreating false).
    /// </summary>
    private static readonly IReadOnlyList<string> NormalModeTabOrderFixture =
    [
        "Button name=\"Browse\" id=\"\"",
        "TextBox name=\"\" id=\"InputTextBox\"",
        "Button name=\"Browse\" id=\"\"",
        "Button name=\"Clear\" id=\"\"",
        "TextBox name=\"\" id=\"\"",
        "Button name=\"Browse\" id=\"\"",
        "TextBox name=\"\" id=\"OutputTextBox\"",
        "TextBox name=\"\" id=\"\"",
        "Button name=\"Create SRS\" id=\"\"",
        "Button name=\"Save log...\" id=\"\"",
    ];

    /// <summary>
    /// Compact order (spec §2): disclosure header toggle → (body skipped: Help starts collapsed
    /// per condition 5, so the plain-prose body is IsVisible=false and correctly excluded from
    /// Tab order) → identical tail to normal mode (this walk starts one stop earlier, at the
    /// header toggle, rather than at Sample File's Browse button — likewise PROVEN first here by
    /// its own reverse walk landing back on the toggle).
    /// </summary>
    private static readonly IReadOnlyList<string> CompactModeTabOrderFixture =
    [
        "ToggleButton name=\"Help\" id=\"\"",
        "Button name=\"Browse\" id=\"\"",
        "TextBox name=\"\" id=\"InputTextBox\"",
        "Button name=\"Browse\" id=\"\"",
        "Button name=\"Clear\" id=\"\"",
        "TextBox name=\"\" id=\"\"",
        "Button name=\"Browse\" id=\"\"",
        "TextBox name=\"\" id=\"OutputTextBox\"",
        "TextBox name=\"\" id=\"\"",
        "Button name=\"Create SRS\" id=\"\"",
        "Button name=\"Save log...\" id=\"\"",
    ];
}
