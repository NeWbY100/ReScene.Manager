using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Models;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.App.Core.ViewModels.Wizards;
using ReScene.Manager.Services;
using ReScene.Manager.Views.Wizards;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported Beginner "Edit an SRR" wizard body
/// (<see cref="EditSRRWizardBody"/>). The body's DataContext is a <see cref="SRREditorViewModel"/>;
/// its four step panels are <c>IsVisible</c>-bound to the hosting Window's
/// <see cref="WizardViewModel.CurrentStepIndex"/> via <c>$parent[Window]</c> + the
/// <c>IndexEqualsConverter</c>, and the stored-files step hosts the shared
/// <see cref="StoredFilesManagePanel"/>. The central gate is <b>zero binding errors</b> (via
/// <see cref="BindingErrorSink"/>). The editing service, dialogs, and preview are inert fakes.
/// </summary>
public class EditSRRWizardBodyTests
{
    // ── Inert service doubles (the view test never edits/previews) ──

    private sealed class InertSRREditingService : ISRREditingService
    {
        public void AddStoredFiles(string srrFilePath, IReadOnlyList<(string StoredName, string FilePath)> files) { }
        public void RemoveStoredFiles(string srrFilePath, IReadOnlyList<string> storedNames) { }
        public Task RenameStoredFileAsync(string srrPath, string oldName, string newName, CancellationToken ct = default) => Task.CompletedTask;
        public Task MoveStoredFileAsync(string srrPath, string storedName, int offset, CancellationToken ct = default) => Task.CompletedTask;
        public IReadOnlyList<StoredFileInfo> GetStoredFiles(string srrFilePath) => [];
        public Task<string?> ExtractStoredFileAsync(string srrFilePath, string outputDirectory, string storedName, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<byte[]?> ReadStoredFileBytesAsync(string srrFilePath, string storedName, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
    }

    private sealed class InertTempDirectoryService : ITempDirectoryService
    {
        public string CreateTempDirectory() => Path.GetTempPath();
        public void Cleanup(string? tempDir) { }
    }

    private sealed class InertFilePreviewService : IFilePreviewService
    {
        public void Preview(byte[] data, string fileName) { }
    }

    private static SRREditorViewModel CreateViewModel() =>
        new(
            new InertSRREditingService(),
            new AvaloniaFileDialogService(static () => null), // headless: dialogs no-op, never block
            new InertTempDirectoryService(),
            new InertFilePreviewService());

    private static WizardViewModel CreateWizard(SRREditorViewModel content) =>
        new("Edit an SRR", content,
        [
            new WizardStep { Title = "Choose SRR" },
            new WizardStep { Title = "Stored files" },
            new WizardStep { Title = "Save as" },
            new WizardStep { Title = "Done" },
        ]);

    // Mirror how WizardWindow wires them: Window.DataContext = WizardViewModel; body.DataContext = its Content.
    private static (Window window, EditSRRWizardBody body, WizardViewModel wizard) Show(SRREditorViewModel vm)
    {
        WizardViewModel wizard = CreateWizard(vm);
        var body = new EditSRRWizardBody { DataContext = wizard.Content };
        // Set the Window's DataContext (the WizardViewModel that the step panels reach via
        // $parent[Window]) before parenting the body, so its ancestor binding never sees a null.
        var window = new Window { Width = 900, Height = 700, DataContext = wizard, Content = body };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, body, wizard);
    }

    [AvaloniaFact]
    public void StepPanels_ToggleWithCurrentStepIndex_NoBindingErrors()
    {
        SRREditorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (_, EditSRRWizardBody body, WizardViewModel wizard) = Show(vm);

        Grid root = Assert.IsType<Grid>(body.Content);
        Assert.Equal(4, root.Children.Count);

        wizard.CurrentStepIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.True(root.Children[0].IsVisible);
        Assert.False(root.Children[1].IsVisible);
        Assert.False(root.Children[3].IsVisible);

        wizard.CurrentStepIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.False(root.Children[0].IsVisible);
        Assert.True(root.Children[1].IsVisible);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void StoredFilesStep_HostsManagePanelWithTwoColumnGrid_NoBindingErrors()
    {
        SRREditorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (Window window, _, WizardViewModel wizard) = Show(vm);

        // Reveal the stored-files step so the hosted panel's grid realizes.
        wizard.CurrentStepIndex = 1;
        Dispatcher.UIThread.RunJobs();

        // The step hosts the shared StoredFilesManagePanel...
        StoredFilesManagePanel panel = window.GetVisualDescendants().OfType<StoredFilesManagePanel>().Single();

        // ...whose grid has the two Name/Size columns.
        DataGrid grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
        Assert.Equal(2, grid.Columns.Count);
        Assert.Equal("Name", grid.Columns[0].Header);
        Assert.Equal("Size", grid.Columns[1].Header);
        Assert.Same(vm.StoredFiles, grid.ItemsSource);

        Assert.Empty(sink.Messages);
    }
}
