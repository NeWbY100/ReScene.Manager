using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Core;
using ReScene.Manager.Services;
using ReScene.Manager.Views;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported <see cref="ReconstructorView"/> (the RAR Reconstructor tab).
/// The central gate is <b>zero binding errors</b> (via <see cref="BindingErrorSink"/>) with a
/// <see cref="ReconstructorViewModel"/> DataContext, plus: the WinRAR/Release/Output path TextBoxes are
/// two-way bound, the <c>VolumeSizeUnits</c> ComboBox is populated, the three log TextBoxes are present,
/// and the log auto-scroll moves the caret to the end only while <c>AutoScrollLog</c> is on. The live
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

        // The settings TabControl has six tabs; the logs TabControl has three (System/Phase 1/Phase 2).
        TabControl[] tabControls = [.. window.GetVisualDescendants().OfType<TabControl>()];
        TabControl logsTabs = tabControls.Single(t => t.ItemCount == 3);
        Assert.Equal(["System", "Phase 1", "Phase 2"],
            logsTabs.Items.OfType<TabItem>().Select(i => i.Header).ToArray());

        // The System log TextBox is realized (its tab is selected) and reflects the bound SystemLog.
        TextBox systemLog = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SystemLogBox");
        vm.SystemLog = "hello log";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("hello log", systemLog.Text);

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
    public void LogAutoScroll_MovesCaretToEnd_OnlyWhenEnabled_NoBindingErrors()
    {
        ReconstructorViewModel vm = CreateVm();

        using var sink = new BindingErrorSink();
        var window = new Window { Width = 1000, Height = 760, Content = new ReconstructorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextBox systemLog = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "SystemLogBox");

        // Enabled by default: appending text moves the caret to the end (Avalonia has no ScrollToEnd()).
        Assert.True(vm.AutoScrollLog);
        vm.SystemLog = "line 1\nline 2\nline 3\n";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(vm.SystemLog.Length, systemLog.CaretIndex);

        // Disabled: further appends do NOT jump the caret to the new end.
        vm.AutoScrollLog = false;
        vm.SystemLog += "line 4\nline 5\n";
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual(vm.SystemLog.Length, systemLog.CaretIndex);

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
