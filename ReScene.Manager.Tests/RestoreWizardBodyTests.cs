using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.App.Core.ViewModels.Wizards;
using ReScene.Manager.Services;
using ReScene.Manager.Views;
using ReScene.Manager.Views.Wizards;
using ReScene.SRS;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported Beginner "Restore a sample" wizard body
/// (<see cref="RestoreWizardBody"/>). The body's DataContext is a <see cref="BeginnerRestoreViewModel"/>;
/// its three step panels are <c>IsVisible</c>-bound to the hosting Window's
/// <see cref="WizardViewModel.CurrentStepIndex"/> via <c>$parent[Window]</c> + the
/// <c>IndexEqualsConverter</c>. Each of steps 1/2 holds a bulk (.srr) and a single (.srs) sub-panel
/// gated by <c>IsBulk</c>/<c>IsSingle</c>.
/// </summary>
/// <remarks>
/// The <see cref="BeginnerRestoreViewModel.BulkRestorer"/> / <see cref="BeginnerRestoreViewModel.SingleRebuilder"/>
/// sub-VMs are assigned here with inert doubles, mirroring how the T5.2 wizard controller wires them
/// (the VM's own null-checks are defensive). This departs from the brief's assumption that leaving them
/// null still yields zero binding errors: in practice a reflection binding whose intermediate step
/// (<c>BulkRestorer.*</c>) is null <b>does</b> log a binding warning, so the doubles are supplied to
/// keep the zero-binding-errors gate meaningful. On step 0 <c>Kind=Unknown</c>, so <c>IsBulk</c>/
/// <c>IsSingle</c> are both false and the sub-panels stay collapsed. The live bulk/single restore runs
/// (which need a picked file to route the input) are the controller's launch-smoke.
/// </remarks>
public class RestoreWizardBodyTests
{
    // ── Inert service doubles (the view test never runs a restore/rebuild) ──

    private sealed class InertSampleRestorerService : ISampleRestorerService
    {
        public event EventHandler<SRSReconstructionProgressEventArgs>? Progress { add { } remove { } }

        public List<SRSEntryInfo> GetSRSEntries(string srrFilePath) => [];

        public Task<SRSReconstructionResult> RestoreSampleAsync(
            string srrFilePath, string srsFileName, string mediaFilePath, string outputPath, CancellationToken ct)
            => Task.FromResult(new SRSReconstructionResult(true, true, 0, 0, 0, 0, null));
    }

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

    private static BeginnerRestoreViewModel CreateViewModel()
    {
        var fileDialog = new AvaloniaFileDialogService(static () => null); // headless: dialogs no-op, never block
        var dispatcher = new InlineUiDispatcher();
        return new BeginnerRestoreViewModel(fileDialog)
        {
            // Mirror the T5.2 controller: both sub-flows are wired up front; the input file later routes
            // to one of them. Kind stays Unknown until a file is picked, so both stay collapsed.
            BulkRestorer = new SampleRestorerViewModel(new InertSampleRestorerService(), fileDialog, dispatcher),
            SingleRebuilder = new SRSReconstructorViewModel(
                new InertSrsReconstructionService(), fileDialog, new InertTempDirectoryService(), dispatcher),
        };
    }

    private static WizardViewModel CreateWizard(BeginnerRestoreViewModel content) =>
        new("Restore a sample", content,
        [
            new WizardStep { Title = "Pick file" },
            new WizardStep { Title = "Media & output" },
            new WizardStep { Title = "Run" },
        ]);

    // Mirror how WizardWindow wires them: the Window's DataContext is the WizardViewModel; the body's
    // DataContext is the task VM (its Content). Set the Window's DataContext (reached via
    // $parent[Window]) before parenting the body, so its ancestor binding never sees a null.
    private static (Window window, RestoreWizardBody body, WizardViewModel wizard) Show(BeginnerRestoreViewModel vm)
    {
        WizardViewModel wizard = CreateWizard(vm);
        var body = new RestoreWizardBody { DataContext = wizard.Content };
        var window = new Window { Width = 900, Height = 700, DataContext = wizard, Content = body };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, body, wizard);
    }

    [AvaloniaFact]
    public void SingleRebuild_OpensIsoProgressModal_OwnedByWizard_WithSingleRebuilderContext()
    {
        // Regression: the beginner "Restore a sample" wizard must show the ISO/media-scan progress modal
        // during a single-.srs rebuild — exactly as the SRS Reconstructor tab does. The wizard runs
        // without that tab realized, so RestoreWizardBody wires the controller for SingleRebuilder, owned
        // by the hosting WizardWindow, with the SingleRebuilder VM as the modal's DataContext.
        BeginnerRestoreViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (Window window, _, _) = Show(vm);

        vm.SingleRebuilder!.ISOProcessing = true;
        Dispatcher.UIThread.RunJobs();
        ISOProgressWindow modal = Assert.Single(window.OwnedWindows.OfType<ISOProgressWindow>());
        // The modal binds the SRSReconstructor VM's ISO* properties, so it must carry that VM, not the
        // BeginnerRestoreViewModel that is the body's own DataContext.
        Assert.Same(vm.SingleRebuilder, modal.DataContext);

        vm.SingleRebuilder!.ISOProcessing = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(window.OwnedWindows.OfType<ISOProgressWindow>());

        Assert.Empty(sink.Messages);
        window.Close();
    }

    [AvaloniaFact]
    public void StepPanels_ToggleWithCurrentStepIndex_NoBindingErrors()
    {
        BeginnerRestoreViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (_, RestoreWizardBody body, WizardViewModel wizard) = Show(vm);

        // The root grid's direct children are the three step panels, in order.
        Grid root = Assert.IsType<Grid>(body.Content);
        Assert.Equal(3, root.Children.Count);

        // Step 0 renders with Kind=Unknown; both bulk/single sub-panels are collapsed.
        wizard.CurrentStepIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.False(vm.IsBulk);
        Assert.False(vm.IsSingle);
        Assert.True(root.Children[0].IsVisible);
        Assert.False(root.Children[1].IsVisible);
        Assert.False(root.Children[2].IsVisible);

        wizard.CurrentStepIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.False(root.Children[0].IsVisible);
        Assert.True(root.Children[1].IsVisible);
        Assert.False(root.Children[2].IsVisible);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void MediaStep_BulkGrid_HasTwoColumns_NoBindingErrors()
    {
        BeginnerRestoreViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (Window window, _, WizardViewModel wizard) = Show(vm);

        // Reveal the media/output step; the bulk sub-panel is collapsed (Kind=Unknown) but its grid is
        // still realized in the visual tree with its two declared columns.
        wizard.CurrentStepIndex = 1;
        Dispatcher.UIThread.RunJobs();

        DataGrid grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
        Assert.Equal(2, grid.Columns.Count);
        Assert.Equal("Sample", grid.Columns[0].Header);
        Assert.Equal("Status", grid.Columns[1].Header);
        Assert.Same(vm.BulkRestorer!.SRSEntries, grid.ItemsSource);

        Assert.Empty(sink.Messages);
    }
}
