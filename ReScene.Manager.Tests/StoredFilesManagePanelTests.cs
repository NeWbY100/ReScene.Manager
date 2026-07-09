using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Models;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Services;
using ReScene.Manager.Views.Wizards;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported shared <see cref="StoredFilesManagePanel"/> (the stored-file
/// grid + toolbar hosted by the Edit/Create SRR wizards). Its DataContext is a
/// <see cref="SRREditorViewModel"/>. The central gate is <b>zero binding errors</b> (via
/// <see cref="BindingErrorSink"/>), plus: the toolbar's seven command buttons and the two-column grid
/// render, and the multi-select-forwarding <c>SelectionChanged</c> handler pushes the grid selection
/// into the VM without throwing. (Clear-on-empty-click and double-click-preview are pointer interaction
/// paths verified by the controller's launch-smoke.)
/// </summary>
public class StoredFilesManagePanelTests
{
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

    private static readonly string[] _expectedButtons =
        ["Add files…", "Remove", "Rename", "Extract…", "Preview…", "Move up", "Move down"];

    private static (Window window, StoredFilesManagePanel panel) Show(SRREditorViewModel vm)
    {
        var panel = new StoredFilesManagePanel { DataContext = vm };
        var window = new Window { Width = 800, Height = 500, Content = panel };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, panel);
    }

    [AvaloniaFact]
    public void RendersToolbarAndTwoColumnGrid_NoBindingErrors()
    {
        SRREditorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (Window window, StoredFilesManagePanel panel) = Show(vm);

        // The two-column grid (Name / Size) reflects the VM's StoredFiles.
        DataGrid grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();
        Assert.Equal(2, grid.Columns.Count);
        Assert.Equal("Name", grid.Columns[0].Header);
        Assert.Equal("Size", grid.Columns[1].Header);
        Assert.Same(vm.StoredFiles, grid.ItemsSource);

        // The toolbar's seven command buttons render, in order.
        string[] buttons = [.. window.GetVisualDescendants().OfType<Button>().Select(b => (string)b.Content!)];
        Assert.Equal(_expectedButtons, buttons);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void SelectingRow_ForwardsSelectionToViewModel_NoBindingErrors()
    {
        SRREditorViewModel vm = CreateViewModel();
        var info = new StoredFileInfo("release-group.nfo", 1024);
        vm.StoredFiles.Add(info);
        vm.StoredFiles.Add(new StoredFileInfo("movie.sfv", 512));

        using var sink = new BindingErrorSink();
        (_, StoredFilesManagePanel panel) = Show(vm);

        DataGrid grid = panel.GetVisualDescendants().OfType<DataGrid>().Single();

        // Selecting a row raises SelectionChanged, which the code-behind forwards to the VM (since
        // DataGrid.SelectedItems is not bindable). The multi-selection mirror is updated without throwing.
        grid.SelectedItem = info;
        Dispatcher.UIThread.RunJobs();

        Assert.Single(vm.SelectedStoredFiles);
        Assert.Same(info, vm.SelectedStoredFiles[0]);

        Assert.Empty(sink.Messages);
    }
}
