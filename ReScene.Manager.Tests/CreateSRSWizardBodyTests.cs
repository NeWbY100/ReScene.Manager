using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Models;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.App.Core.ViewModels.Wizards;
using ReScene.Manager.Services;
using ReScene.Manager.Views;
using ReScene.Manager.Views.Wizards;
using ReScene.SRS;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported Beginner "Create an SRS" wizard body
/// (<see cref="CreateSRSWizardBody"/>). The body's DataContext is an <see cref="SRSCreatorViewModel"/>;
/// its three step panels are <c>IsVisible</c>-bound to the hosting Window's
/// <see cref="WizardViewModel.CurrentStepIndex"/> via <c>$parent[Window]</c> + the
/// <c>IndexEqualsConverter</c>. The central gate is <b>zero binding errors</b> (via
/// <see cref="BindingErrorSink"/>); the tests also confirm the panels toggle with the index and that
/// the sample step hosts the ISO member-selection combo. The creation pipeline and file dialogs are
/// inert fakes — only the view wiring is exercised.
/// </summary>
public class CreateSRSWizardBodyTests
{
    // ── Inert service doubles (the view test never runs a creation) ──

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

    private static SRSCreatorViewModel CreateViewModel() =>
        new(
            new InertSrsCreationService(),
            new AvaloniaFileDialogService(static () => null), // headless: dialogs no-op, never block
            new InertTempDirectoryService(),
            new DefaultAppSettingsService(),
            new InlineUiDispatcher());

    private static WizardViewModel CreateWizard(SRSCreatorViewModel content) =>
        new("Create an SRS", content,
        [
            new WizardStep { Title = "Sample" },
            new WizardStep { Title = "Save as" },
            new WizardStep { Title = "Create" },
        ]);

    // Mirror how WizardWindow wires them: the Window's DataContext is the WizardViewModel; the body's
    // DataContext is the task VM (its Content). Set the Window's DataContext (reached via
    // $parent[Window]) before parenting the body, so its ancestor binding never sees a null.
    private static (Window window, CreateSRSWizardBody body, WizardViewModel wizard) Show(SRSCreatorViewModel vm)
    {
        WizardViewModel wizard = CreateWizard(vm);
        var body = new CreateSRSWizardBody { DataContext = wizard.Content };
        var window = new Window { Width = 900, Height = 700, DataContext = wizard, Content = body };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, body, wizard);
    }

    [AvaloniaFact]
    public void MediaScan_OpensIsoProgressModal_OwnedByWizard()
    {
        // Regression: the beginner "Create a sample SRS" wizard must show the ISO/media-scan progress
        // modal while scanning the source, exactly as the SRS Creator tab does. The wizard runs without
        // that tab realized, so CreateSRSWizardBody wires the controller itself, owned by the WizardWindow.
        SRSCreatorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (Window window, _, _) = Show(vm);

        vm.ISOProcessing = true;
        Dispatcher.UIThread.RunJobs();
        ISOProgressWindow modal = Assert.Single(window.OwnedWindows.OfType<ISOProgressWindow>());
        Assert.Same(vm, modal.DataContext);

        vm.ISOProcessing = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(window.OwnedWindows.OfType<ISOProgressWindow>());

        Assert.Empty(sink.Messages);
        window.Close();
    }

    [AvaloniaFact]
    public void StepPanels_ToggleWithCurrentStepIndex_NoBindingErrors()
    {
        SRSCreatorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (_, CreateSRSWizardBody body, WizardViewModel wizard) = Show(vm);

        // The root grid's direct children are the three step panels, in order.
        Grid root = Assert.IsType<Grid>(body.Content);
        Assert.Equal(3, root.Children.Count);

        wizard.CurrentStepIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.True(root.Children[0].IsVisible);
        Assert.False(root.Children[1].IsVisible);
        Assert.False(root.Children[2].IsVisible);

        wizard.CurrentStepIndex = 2;
        Dispatcher.UIThread.RunJobs();
        Assert.False(root.Children[0].IsVisible);
        Assert.False(root.Children[1].IsVisible);
        Assert.True(root.Children[2].IsVisible);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void SampleStep_HasIsoCombo_TracksShowISOSelection_NoBindingErrors()
    {
        SRSCreatorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (Window window, _, WizardViewModel wizard) = Show(vm);

        wizard.CurrentStepIndex = 0;
        Dispatcher.UIThread.RunJobs();

        ComboBox isoCombo = window.GetVisualDescendants().OfType<ComboBox>().Single();
        // IsVisible is not inherited down the visual tree in Avalonia, so the row's own IsVisible
        // (bound to ShowISOSelection) is asserted on its containing DockPanel, not the ComboBox itself.
        DockPanel isoRow = Assert.IsType<DockPanel>(isoCombo.GetVisualParent());

        Assert.False(vm.ShowISOSelection);
        Assert.False(isoRow.IsVisible);

        vm.ISOMediaFiles.Add("VIDEO_TS/VTS_01_1.VOB");
        vm.IsISOSource = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.ShowISOSelection);
        Assert.True(isoRow.IsVisible);
        Assert.Same(vm.ISOMediaFiles, isoCombo.ItemsSource);

        Assert.Empty(sink.Messages);
    }
}
