using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Helpers;
using ReScene.Manager.Services;
using ReScene.Manager.Views;
using ReScene.SRS;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported <see cref="ISOProgressWindow"/> and (the safe subset of)
/// <see cref="IsoProgressWindowController"/>. The window's central gate is <b>zero binding errors</b>
/// (via <see cref="BindingErrorSink"/>) when its DataContext is one of the SRS view models, plus the
/// heading/progress/stat text and the Cancel button are present and reflect the VM. Full modal
/// open-on-processing/close-on-done/cancel-on-close behavior needs a live owning Window and is the
/// controller's Phase-4 launch-smoke, not exercised here — this only checks that
/// <see cref="IsoProgressWindowController.OnProcessingChanged"/> is a safe no-op for a headless,
/// unattached owner (no crash, no window shown).
/// </summary>
public class ISOProgressWindowTests
{
    private sealed class InertSrsReconstructionService : ISRSReconstructionService
    {
        public event EventHandler<SRSReconstructionProgressEventArgs>? Progress { add { } remove { } }
        public event EventHandler<SRSScanProgressEventArgs>? ScanProgress { add { } remove { } }

        public Task<SRSReconstructionResult> RebuildAsync(string srsFilePath, string mediaFilePath, string outputPath, CancellationToken ct)
            => Task.FromResult(new SRSReconstructionResult(true, true, 0, 0, 0, 0, null));
    }

    private sealed class InertTempDirectoryService : ITempDirectoryService
    {
        public string CreateTempDirectory() => Path.GetTempPath();
        public void Cleanup(string? tempDir) { }
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, UiDispatcherPriority priority) => action();
        public bool CheckAccess() => true;
    }

    private static SRSReconstructorViewModel CreateReconstructorViewModel() =>
        new(
            new InertSrsReconstructionService(),
            new AvaloniaFileDialogService(static () => null),
            new InertTempDirectoryService(),
            new InlineUiDispatcher());

    [AvaloniaFact]
    public void Renders_HeadingProgressAndCancel_NoBindingErrors()
    {
        SRSReconstructorViewModel vm = CreateReconstructorViewModel();
        vm.ISOProgressHeading = "Scanning ISO";
        vm.ISOFileCountText = "File 2 of 5";
        vm.ISOOverallPercent = 40;
        vm.ISOCurrentPercent = 70;
        vm.ISOCurrentFileText = "VIDEO_TS/VTS_02_1.VOB";
        vm.ISOProcessedText = "1.2 GB";
        vm.ISORemainingText = "800 MB";
        vm.ISOSpeedText = "45 MB/s";
        vm.ISOEtaText = "18s";

        using var sink = new BindingErrorSink();
        var window = new ISOProgressWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("ISO Processing", window.Title);

        TextBlock heading = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Scanning ISO");
        Assert.Equal("Scanning ISO", heading.Text);

        TextBlock fileCount = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "File 2 of 5");
        Assert.Equal("File 2 of 5", fileCount.Text);

        ProgressBar[] bars = [.. window.GetVisualDescendants().OfType<ProgressBar>()];
        Assert.Equal(2, bars.Length);
        Assert.Contains(bars, b => b.Value == 40);
        Assert.Contains(bars, b => b.Value == 70);

        Button cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Cancel");
        Assert.NotNull(cancel);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void CancelClick_ClosesTheWindow()
    {
        SRSReconstructorViewModel vm = CreateReconstructorViewModel();
        var window = new ISOProgressWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        bool closed = false;
        window.Closed += (_, _) => closed = true;

        Button cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Cancel");
        cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        Assert.True(closed);
    }

    [AvaloniaFact]
    public void OnProcessingChanged_False_OnFreshController_IsSafeNoOpWhenOwnerIsUnattached()
    {
        // The owner here is never added to a Window, so TopLevel.GetTopLevel(owner) returns null —
        // exactly the headless/not-attached case the controller's null-owner guard exists for. Both
        // branches (open and close) must be safe no-ops; no window should ever be shown.
        var owner = new UserControl();
        var controller = new IsoProgressWindowController(owner, () => false, () => { });

        controller.OnProcessingChanged(false);
        Dispatcher.UIThread.RunJobs();

        controller.OnProcessingChanged(true);
        Dispatcher.UIThread.RunJobs();

        controller.OnProcessingChanged(false);
        Dispatcher.UIThread.RunJobs();

        // No assertion beyond "did not throw" — there is no owning Window to show a modal over, and
        // the guard in OnProcessingChanged must have skipped it rather than crashing.
    }

    [AvaloniaFact]
    public void OnProcessingChanged_True_AgainstShownWindowOwner_OpensDialog_ClosesOnFalse()
    {
        // Positive path mirroring real usage: the owner is a control inside a SHOWN window (the SRS
        // Reconstructor tab under MainWindow). Flipping processing true must open the ISOProgressWindow
        // modally over it; flipping false must close it. This is the "progress dialog for a rebuild"
        // the SRS Reconstructor relies on (the VM sets ISOProcessing=true before every RebuildAsync).
        var owner = new Window();
        var content = new UserControl();
        owner.Content = content;
        owner.Show();
        Dispatcher.UIThread.RunJobs();

        var controller = new IsoProgressWindowController(content, () => true, () => { });

        controller.OnProcessingChanged(true);
        Dispatcher.UIThread.RunJobs();
        Assert.Single(owner.OwnedWindows.OfType<ISOProgressWindow>());

        controller.OnProcessingChanged(false);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(owner.OwnedWindows.OfType<ISOProgressWindow>());

        owner.Close();
    }

    [AvaloniaFact]
    public void OnProcessingChanged_True_AgainstUnshownWindowOwner_SkipsAndOpensNothing()
    {
        // The owner is a real Window but is never shown. A Window is its own TopLevel, so the old bare
        // "is not Window" guard would NOT skip, and the fire-and-forget ShowDialog over an unshown
        // window would throw on the dispatcher (surfacing here when RunJobs pumps the posted job). The
        // visible-owner guard skips.
        var owner = new Window();
        var controller = new IsoProgressWindowController(owner, () => true, () => { });

        controller.OnProcessingChanged(true);
        Dispatcher.UIThread.RunJobs(); // must not throw

        Assert.Empty(owner.OwnedWindows);
    }
}
