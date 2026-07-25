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
/// Headless render tests for the ported <see cref="FileCopyProgressWindow"/>. The window's central
/// gate is <b>zero binding errors</b> (via <see cref="BindingErrorSink"/>) with a
/// <see cref="ReconstructorViewModel"/> DataContext, plus the heading/from/to/progress/stats text and
/// the Cancel button render and reflect the VM's <c>Copy*</c> properties. Live open-on-IsCopying is
/// <see cref="ModalProgressWindowController{TWindow}"/>'s job and needs a live owning Window, so it's
/// the Reconstructor tab's (T4.4b) launch-smoke, not exercised here. The Cancel grace period wired by
/// <see cref="Helpers.ProgressWindowLifecycle"/> on <c>Loaded</c> is exercised: while copying, Cancel
/// (and a native close) relabels/disables the button and does NOT close; when not copying, a close is
/// allowed.
/// </summary>
public class FileCopyProgressWindowTests
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

    private static void SeedCopyProgress(ReconstructorViewModel vm)
    {
        vm.CopyHeadingText = "Copying release files...";
        vm.CopySourceText = @"C:\Sets\Set1\file.rar";
        vm.CopyDestText = @"C:\Output\file.rar";
        vm.CopyProgressPercent = 55;
        vm.CopyProgressPercentText = "55%";
        vm.CopyCurrentFileText = "file.rar";
        vm.CopyRemainingText = "3 files remaining";
        vm.CopyElapsedText = "00:05";
        vm.CopyTimeRemainingText = "00:04";
        vm.CopySpeedText = "12 MB/s";
        vm.CopyEtaText = "00:04";
    }

    [AvaloniaFact]
    public void Renders_HeadingSourceDestProgressStatsAndCancel_NoBindingErrors()
    {
        ReconstructorViewModel vm = CreateVm();
        SeedCopyProgress(vm);

        using var sink = new BindingErrorSink();
        var window = new FileCopyProgressWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Copying Files", window.Title);

        TextBlock[] textBlocks = [.. window.GetVisualDescendants().OfType<TextBlock>()];
        Assert.Contains(textBlocks, t => t.Text == vm.CopyHeadingText);
        Assert.Contains(textBlocks, t => t.Text == vm.CopySourceText);
        Assert.Contains(textBlocks, t => t.Text == vm.CopyDestText);
        Assert.Contains(textBlocks, t => t.Text == vm.CopyProgressPercentText);
        Assert.Contains(textBlocks, t => t.Text == vm.CopyCurrentFileText);
        Assert.Contains(textBlocks, t => t.Text == vm.CopyRemainingText);
        Assert.Contains(textBlocks, t => t.Text == vm.CopyElapsedText);
        Assert.Contains(textBlocks, t => t.Text == vm.CopyTimeRemainingText);
        Assert.Contains(textBlocks, t => t.Text == vm.CopySpeedText);
        Assert.Contains(textBlocks, t => t.Text == vm.CopyEtaText);

        ProgressBar bar = window.GetVisualDescendants().OfType<ProgressBar>().Single();
        Assert.Equal(55, bar.Value);

        Button cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Cancel");
        Assert.NotNull(cancel);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void CancelClick_WhileCopying_RelabelsAndDisables_AndDoesNotClose()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.IsCopying = true;

        var window = new FileCopyProgressWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        bool closed = false;
        window.Closed += (_, _) => closed = true;

        Button cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "CancelButton");
        cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        // Grace period: relabel + disable, no close. The controller (not the button) closes the dialog
        // once IsCopying clears.
        Assert.Equal("Cancelling...", cancel.Content);
        Assert.False(cancel.IsEnabled);
        Assert.False(closed);
        // StopCommand ran (it logs "Cancellation requested..." to the system log).
        Assert.Contains(vm.LogEntries, l => l.Contains("Cancellation requested", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void NativeClose_WhileCopying_IsBlocked_AndTurnedIntoCancel()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.IsCopying = true;

        var window = new FileCopyProgressWindow { DataContext = vm };
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
    public void NativeClose_WhenNotCopying_IsAllowed()
    {
        ReconstructorViewModel vm = CreateVm(); // IsCopying defaults false

        var window = new FileCopyProgressWindow { DataContext = vm };
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
        var controller = new ModalProgressWindowController<FileCopyProgressWindow>(owner, () => false, () => { });

        controller.OnBusyChanged(false);
        Dispatcher.UIThread.RunJobs();

        controller.OnBusyChanged(true);
        Dispatcher.UIThread.RunJobs();

        controller.OnBusyChanged(false);
        Dispatcher.UIThread.RunJobs();

        // No assertion beyond "did not throw" — there is no owning Window to show a modal over, and
        // the guard in OnBusyChanged must have skipped it rather than crashing.
    }

    [AvaloniaFact]
    public void OnBusyChanged_True_AgainstUnshownWindowOwner_SkipsAndOpensNothing()
    {
        // The owner is a real Window (as in production, where it is the BruteForceProgressWindow) but
        // is never shown. A Window is its own TopLevel, so the old bare "is not Window" guard would
        // NOT skip, and the fire-and-forget ShowDialog over an unshown window would throw on the
        // dispatcher (surfacing here when RunJobs pumps the posted job). The visible-owner guard skips.
        var owner = new Window();
        var controller = new ModalProgressWindowController<FileCopyProgressWindow>(owner, () => true, () => { });

        controller.OnBusyChanged(true);
        Dispatcher.UIThread.RunJobs(); // must not throw

        Assert.Empty(owner.OwnedWindows);
    }
}
