using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.App.Core.ViewModels.Reconstruction;
using ReScene.Core;
using ReScene.Manager.Behaviors;
using ReScene.Manager.Services;
using ReScene.Manager.Views;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported <see cref="ReconstructorView"/> (the RAR Reconstructor tab).
/// The central gate is <b>zero binding errors</b> (via <see cref="BindingErrorSink"/>) with a
/// <see cref="ReconstructorViewModel"/> DataContext, plus: the WinRAR/Release/Output path TextBoxes are
/// two-way bound, the <c>VolumeSizeUnits</c> ComboBox is populated, the merged <c>LogEntries</c> log
/// list renders and is named, and its stick-to-bottom behavior binds to <c>AutoScrollLog</c>. The live
/// reconstruction run and the modal <see cref="BruteForceProgressWindow"/> actually opening over a real
/// owner are the Reconstructor tab's launch-smoke — here we only assert the <c>IsRunning</c> handler is a
/// safe no-op without an owning window.
/// </summary>
public class ReconstructorViewTests
{
    // ── Inert service doubles (no run is ever actually started) ──

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

    /// <summary>No-op timer factory: the elapsed-time timer never ticks in these tests.</summary>
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
            new AvaloniaFileDialogService(static () => null), // headless: dialogs no-op, never block
            new InlineUiDispatcher(),
            new InertUiTimerFactory());

    [AvaloniaFact]
    public void KeyInputs_AndVolumeUnitsCombo_AndLogs_AreBound_NoBindingErrors()
    {
        ReconstructorViewModel vm = CreateVm();

        using var sink = new BindingErrorSink();
        var window = new Window { Width = 1000, Height = 760, Content = new ReconstructorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // view -> VM: typing into the Output TextBox writes back to the VM (Paths tab is the default).
        TextBox output = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "OutputTextBox");
        output.Text = @"C:\rel\out";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(@"C:\rel\out", vm.OutputPath);

        // VM -> view: the WinRAR TextBox mirrors WinRARPath.
        TextBox winrar = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "WinRARTextBox");
        vm.WinRARPath = @"C:\WinRAR";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(@"C:\WinRAR", winrar.Text);

        // The merged run log: one virtualized logList ListBox bound to LogEntries (the WPF-era
        // System/Phase 1/Phase 2 log tabs are gone), named by the "Log" header via LabeledBy (4.1.2).
        TabControl[] tabControls = [.. window.GetVisualDescendants().OfType<TabControl>()];
        ListBox runLog = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "RunLogList");
        Assert.Same(vm.LogEntries, runLog.ItemsSource);
        var logLabel = Assert.IsType<TextBlock>(AutomationProperties.GetLabeledBy(runLog));
        Assert.Equal("Log", logLabel.Text);
        // Long [P2] command lines must stay reachable (the old TextBox had a horizontal scrollbar).
        Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetHorizontalScrollBarVisibility(runLog));
        vm.LogEntries.Add("hello log");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "hello log");

        // The non-editable VolumeSizeUnits ComboBox is populated once the Options tab is realized.
        TabControl settingsTabs = tabControls.Single(t => t.ItemCount == 6);
        settingsTabs.SelectedIndex = 4; // Options
        Dispatcher.UIThread.RunJobs();
        ComboBox unitsCombo = window.GetVisualDescendants().OfType<ComboBox>().Single();
        Assert.Equal(ReconstructorViewModel.VolumeSizeUnits.Length, unitsCombo.ItemCount);
        Assert.Equal(vm.VolumeSizeUnitIndex, unitsCombo.SelectedIndex);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void SettingsTabsAndRunLog_ScrollbarsReserveLayoutSpace()
    {
        // All six settings-tab scrollers keep Auto visibility WITH AllowAutoHide=false so the
        // Fluent overlay bar never draws over the right-edge Browse/TextBox controls (Linux
        // especially); the run log gets the same via the shared logList style.
        ReconstructorViewModel vm = CreateVm();

        using var sink = new BindingErrorSink();
        var window = new Window { Width = 1000, Height = 760, Content = new ReconstructorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TabControl settingsTabs = window.GetVisualDescendants().OfType<TabControl>().Single(t => t.ItemCount == 6);
        for (int i = 0; i < settingsTabs.ItemCount; i++)
        {
            settingsTabs.SelectedIndex = i;
            Dispatcher.UIThread.RunJobs();
            ScrollViewer scroll = window.GetVisualDescendants().OfType<ScrollViewer>()
                .Single(sv => sv.TemplatedParent is null); // the tab's declared scroller, not TextBox internals
            Assert.Equal(ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
            Assert.False(ScrollViewer.GetAllowAutoHide(scroll));
        }

        ListBox runLog = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "RunLogList");
        Assert.False(ScrollViewer.GetAllowAutoHide(runLog));

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void VersionsTree_WpfClassicExpander_TogglesAndNamesEverything()
    {
        // The versionGroup re-template restores the WPF-classic chrome. Pins: the header toggle
        // carries the group's accessible name (Avalonia derives none from control content); the
        // template's IsChecked binding is TWO-WAY (one-way — TemplateBinding's default — would
        // silently stop expansion); collapsed content leaves the tree; the left chevron Path
        // exists; and both checkbox tiers expose their 4.1.2 names.
        ReconstructorViewModel vm = CreateVm();
        var leaf = new RARVersionLeaf(390, "wrar390");
        vm.VersionGroups.Add(new RARVersionGroup(3, [leaf]));

        using var sink = new BindingErrorSink();
        var window = new Window { Width = 1000, Height = 760, Content = new ReconstructorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TabControl settingsTabs = window.GetVisualDescendants().OfType<TabControl>().Single(t => t.ItemCount == 6);
        settingsTabs.SelectedIndex = 1; // Versions
        Dispatcher.UIThread.RunJobs();

        Expander group = window.GetVisualDescendants().OfType<Expander>().Single();
        Assert.Contains("versionGroup", group.Classes);

        ToggleButton toggle = group.GetVisualDescendants().OfType<ToggleButton>()
            .Single(t => t is not CheckBox);
        Assert.Equal("RAR 3.x versions", AutomationProperties.GetName(toggle));
        Assert.Single(group.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>(),
            p => p.Name == "ChevronGlyph");

        // Collapsed by default (no leaf ticked): the leaf checkbox is not realized.
        Assert.False(group.IsExpanded);
        Assert.DoesNotContain(group.GetVisualDescendants().OfType<CheckBox>(),
            c => AutomationProperties.GetName(c) == leaf.AccessibleName);

        // Toggle via the template's button — the two-way pin: a one-way binding leaves IsExpanded false.
        toggle.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(group.IsExpanded);

        CheckBox headerBox = group.GetVisualDescendants().OfType<CheckBox>()
            .Single(c => AutomationProperties.GetName(c) == "Select all RAR 3.x versions");
        Assert.NotNull(headerBox);
        CheckBox leafBox = group.GetVisualDescendants().OfType<CheckBox>()
            .Single(c => AutomationProperties.GetName(c) == leaf.AccessibleName);
        Assert.Equal("3.90 (wrar390)", AutomationProperties.GetName(leafBox));

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void Header_ShowsWinRarPackDownloadLinks_MatchingWizard()
    {
        // The header's three pack-download links must identify identically to the Beginner
        // Reconstruct wizard's step 1 (WCAG 3.2.4 Consistent Identification) — both sides assert
        // against ResourceLinkExpectations so editing one surface without the other fails its twin.
        ReconstructorViewModel vm = CreateVm();

        using var sink = new BindingErrorSink();
        var window = new Window { Width = 1000, Height = 760, Content = new ReconstructorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        (string?, string?)[] links =
        [
            .. window.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("link"))
                .Select(b => (b.Content as string, b.Tag as string)),
        ];
        Assert.Equal(
            ResourceLinkExpectations.WinRarPackLinks.Select(p => ((string?)p.Label, (string?)p.Url)),
            links);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void LogAutoScroll_BehaviorBindsToVmToggle_NoBindingErrors()
    {
        // The per-TextBox caret trick is gone: the merged log ListBox binds the logList style's
        // stick-to-bottom behavior (ListBoxAutoScroll.AutoScrollToEnd) to the Auto-scroll checkbox's
        // AutoScrollLog — unchecking disables even the at-bottom auto-scroll, re-checking restores it.
        ReconstructorViewModel vm = CreateVm();

        using var sink = new BindingErrorSink();
        var window = new Window { Width = 1000, Height = 760, Content = new ReconstructorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        ListBox runLog = window.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "RunLogList");

        Assert.True(vm.AutoScrollLog);
        Assert.True(ListBoxAutoScroll.GetAutoScrollToEnd(runLog));

        vm.AutoScrollLog = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(ListBoxAutoScroll.GetAutoScrollToEnd(runLog));

        vm.AutoScrollLog = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(ListBoxAutoScroll.GetAutoScrollToEnd(runLog));

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void VersionsTab_RealizesSeededVersionGroupRows_NoBindingErrors()
    {
        // T4.4b restructured the WPF Run-based version rows into horizontal TextBlocks; this seeds the
        // VM's VersionGroups (a couple of major groups, each with sub-version leaves) and asserts the
        // restructured group-header and leaf TextBlocks realize their bound values with no binding
        // errors. A ticked leaf makes its group start expanded, so the leaf rows render.
        ReconstructorViewModel vm = CreateVm();
        vm.VersionGroups.Add(new RARVersionGroup(5,
            [new RARVersionLeaf(500, "winrar-500") { IsChecked = true }, new RARVersionLeaf(501, "winrar-501")]));
        vm.VersionGroups.Add(new RARVersionGroup(6,
            [new RARVersionLeaf(600, "winrar-600") { IsChecked = true }]));

        using var sink = new BindingErrorSink();
        var window = new Window { Width = 1000, Height = 760, Content = new ReconstructorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The Versions sub-tab is the second tab of the six-tab settings TabControl; select it so its
        // content realizes.
        TabControl settingsTabs = window.GetVisualDescendants().OfType<TabControl>().Single(t => t.ItemCount == 6);
        settingsTabs.SelectedIndex = 1; // Versions
        Dispatcher.UIThread.RunJobs();

        TextBlock[] textBlocks = [.. window.GetVisualDescendants().OfType<TextBlock>()];

        // Group-header row (restructured horizontal TextBlocks: "RAR" + Header + CountText).
        Assert.Contains(textBlocks, t => t.Text == "5.x");
        Assert.Contains(textBlocks, t => t.Text == "6.x");
        Assert.Contains(textBlocks, t => t.Text == "(1 of 2)");
        Assert.Contains(textBlocks, t => t.Text == "(1 of 1)");

        // Leaf rows (LabelWithTag + parenthesised FolderDisplay), realized because each group with a
        // ticked leaf starts expanded.
        Assert.Contains(textBlocks, t => t.Text == "5.00");
        Assert.Contains(textBlocks, t => t.Text == "5.01");
        Assert.Contains(textBlocks, t => t.Text == "6.00");
        Assert.Contains(textBlocks, t => t.Text == "(winrar-500)");
        Assert.Contains(textBlocks, t => t.Text == "(winrar-600)");

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void IsRunningTrue_WithoutOwnerWindow_IsSafeNoOp()
    {
        ReconstructorViewModel vm = CreateVm();

        using var sink = new BindingErrorSink();

        // Not attached to a shown top-level window: the IsRunning -> BruteForceProgressWindow open is
        // null-owner guarded, so flipping IsRunning must be a safe no-op (no window, no throw).
        var view = new ReconstructorView { DataContext = vm };
        vm.IsRunning = true;
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(view);
        Assert.Empty(sink.Messages);
    }
}
