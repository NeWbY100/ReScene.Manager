using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Core;
using ReScene.Manager.Helpers;
using ReScene.Manager.Services;
using ReScene.Manager.Views;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported <see cref="CRCValidationProgressWindow"/>. The window's
/// central gate is <b>zero binding errors</b> (via <see cref="BindingErrorSink"/>) with a
/// <see cref="ReconstructorViewModel"/> DataContext, plus the heading/progress/stats text and the
/// Cancel button render and reflect the VM's <c>Verify*</c> properties. Live open-on-IsVerifying is
/// <see cref="ModalProgressWindowController{TWindow}"/>'s job and needs a live owning Window, so it's
/// the Reconstructor tab's (T4.4b) launch-smoke, not exercised here. The Cancel grace period wired by
/// <see cref="Helpers.ProgressWindowLifecycle"/> on <c>Loaded</c> is exercised: while verifying, Cancel
/// (and a native close) relabels/disables the button and does NOT close; when not verifying, a close is
/// allowed.
/// </summary>
public class CRCValidationProgressWindowTests
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

    private static void SeedVerifyProgress(ReconstructorViewModel vm)
    {
        vm.VerifyHeadingText = "Verifying release files...";
        vm.VerifyProgressPercent = 80;
        vm.VerifyProgressPercentText = "80%";
        vm.VerifyCurrentFileText = "file.rar";
        vm.VerifyRemainingText = "1 file remaining";
        vm.VerifyElapsedText = "00:08";
        vm.VerifyTimeRemainingText = "00:02";
        vm.VerifySpeedText = "30 MB/s";
        vm.VerifyEtaText = "00:02";
    }

    [AvaloniaFact]
    public void Renders_HeadingProgressStatsAndCancel_NoBindingErrors()
    {
        ReconstructorViewModel vm = CreateVm();
        SeedVerifyProgress(vm);

        using var sink = new BindingErrorSink();
        var window = new CRCValidationProgressWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Verifying Files", window.Title);

        TextBlock[] textBlocks = [.. window.GetVisualDescendants().OfType<TextBlock>()];
        Assert.Contains(textBlocks, t => t.Text == vm.VerifyHeadingText);
        Assert.Contains(textBlocks, t => t.Text == vm.VerifyProgressPercentText);
        Assert.Contains(textBlocks, t => t.Text == vm.VerifyCurrentFileText);
        Assert.Contains(textBlocks, t => t.Text == vm.VerifyRemainingText);
        Assert.Contains(textBlocks, t => t.Text == vm.VerifyElapsedText);
        Assert.Contains(textBlocks, t => t.Text == vm.VerifyTimeRemainingText);
        Assert.Contains(textBlocks, t => t.Text == vm.VerifySpeedText);
        Assert.Contains(textBlocks, t => t.Text == vm.VerifyEtaText);

        ProgressBar bar = window.GetVisualDescendants().OfType<ProgressBar>().Single();
        Assert.Equal(80, bar.Value);

        Button cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Cancel");
        Assert.NotNull(cancel);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void CancelClick_WhileVerifying_RelabelsAndDisables_AndDoesNotClose()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.IsVerifying = true;

        var window = new CRCValidationProgressWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        bool closed = false;
        window.Closed += (_, _) => closed = true;

        Button cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelButton");
        cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // Grace period: relabel + disable, no close. The controller (not the button) closes the dialog
        // once IsVerifying clears.
        Assert.Equal("Cancelling...", cancel.Content);
        Assert.False(cancel.IsEnabled);
        Assert.False(closed);
        // StopCommand ran (it logs "Cancellation requested..." to the system log).
        Assert.Contains("Cancellation requested", vm.SystemLog, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void NativeClose_WhileVerifying_IsBlocked_AndTurnedIntoCancel()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.IsVerifying = true;

        var window = new CRCValidationProgressWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        bool closed = false;
        window.Closed += (_, _) => closed = true;

        window.Close();
        Dispatcher.UIThread.RunJobs();

        // The Closing guard cancelled the native close and ran the same Cancel action instead.
        Assert.False(closed);
        Button cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelButton");
        Assert.Equal("Cancelling...", cancel.Content);
        Assert.False(cancel.IsEnabled);
    }

    [AvaloniaFact]
    public void NativeClose_WhenNotVerifying_IsAllowed()
    {
        ReconstructorViewModel vm = CreateVm(); // IsVerifying defaults false

        var window = new CRCValidationProgressWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        bool closed = false;
        window.Closed += (_, _) => closed = true;

        window.Close();
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed);
    }

    [AvaloniaFact]
    public void OnBusyChanged_OnFreshController_IsSafeNoOpWhenOwnerIsUnattached()
    {
        // The owner here is never added to a Window, so TopLevel.GetTopLevel(owner) returns null —
        // exactly the headless/not-attached case the controller's null-owner guard exists for. Both
        // branches (open and close) must be safe no-ops; no window should ever be shown.
        var owner = new UserControl();
        var controller = new ModalProgressWindowController<CRCValidationProgressWindow>(owner, () => false, () => { });

        controller.OnBusyChanged(false);
        Dispatcher.UIThread.RunJobs();

        controller.OnBusyChanged(true);
        Dispatcher.UIThread.RunJobs();

        controller.OnBusyChanged(false);
        Dispatcher.UIThread.RunJobs();

        // No assertion beyond "did not throw" — there is no owning Window to show a modal over, and
        // the guard in OnBusyChanged must have skipped it rather than crashing.
    }
}
