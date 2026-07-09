using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Models;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Controls;
using ReScene.Manager.Services;
using ReScene.Manager.Views;
using ReScene.SRR;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported <see cref="InspectorView"/> (Inspector tab — the app's richest
/// single-file view: a structure <see cref="TreeView"/> with a rich context menu, a properties
/// <see cref="DataGrid"/> with indent/warning cell converters, a verify-result panel, and a Hex/Text
/// <see cref="TabControl"/> hosting the embedded <see cref="HexView"/> plus a live hex-search bar). The
/// central gate is <b>zero binding errors</b> (via <see cref="BindingErrorSink"/>), on both an empty
/// view and one seeded with properties/tree nodes. The editing/verify/export/image-preview services and
/// file dialogs are inert fakes — only the view wiring is exercised; live SRR editing, verify, export,
/// image preview, and hex-search interaction are the controller's launch-smoke.
/// </summary>
public class InspectorViewTests
{
    // ── Inert service doubles (the view test never edits/verifies/exports/previews) ──

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

    private sealed class InertSRRVerifyService : ISRRVerifyService
    {
        public Task<SRRVerifyResult> VerifyAsync(string srrFilePath, CancellationToken ct = default) =>
            Task.FromResult(new SRRVerifyResult { IsValid = true, Issues = [], BlocksScanned = 0, FileSize = 0 });
    }

    private sealed class InertPropertyExportService : IPropertyExportService
    {
        public Task ExportSelectedAsync(string outputPath, TreeNodeViewModel node, IEnumerable<PropertyItem> properties, CancellationToken ct = default) => Task.CompletedTask;
        public Task ExportTreeAsync(string outputPath, IEnumerable<TreeNodeViewModel> roots, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class InertImagePreviewService : IImagePreviewService
    {
        public void Preview(byte[] data, string fileName) { }
    }

    private static readonly string[] _expectedMenuHeaders =
    [
        "Export...",
        "Add Stored File...",
        "Remove Stored File",
        "Rename...",
        "Move Up",
        "Move Down",
        "Verify integrity",
        "Export properties as JSON...",
        "Export entire tree as JSON...",
    ];

    private static InspectorViewModel CreateViewModel() =>
        new(
            new AvaloniaFileDialogService(static () => null), // headless: dialogs no-op, never block
            new InertSRREditingService(),
            new InertSRRVerifyService(),
            new InertPropertyExportService(),
            new InertImagePreviewService(),
            settingsService: null);

    private static (Window window, InspectorViewModel vm) Show(InspectorViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 900, Content = new InspectorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    [AvaloniaFact]
    public void EmptyView_NoFileLoaded_NoBindingErrors()
    {
        InspectorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (Window window, _) = Show(vm);

        // The structure tree realizes.
        Assert.Single(window.GetVisualDescendants().OfType<TreeView>());

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void View_HasGridColumnsTreeHexViewTabsBytesPerRowAndContextMenu_NoBindingErrors()
    {
        InspectorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (Window window, _) = Show(vm);

        // The properties DataGrid has its two template columns (Property + Value).
        DataGrid grid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "PropertiesGrid");
        Assert.Equal(2, grid.Columns.Count);
        Assert.IsType<DataGridTemplateColumn>(grid.Columns[0]);
        Assert.Equal("Property", grid.Columns[0].Header);
        Assert.IsType<DataGridTemplateColumn>(grid.Columns[1]);
        Assert.Equal("Value", grid.Columns[1].Header);

        // The structure tree exists.
        TreeView tree = window.GetVisualDescendants().OfType<TreeView>().Single();

        // The embedded HexView composite is present (Hex tab is the default-selected tab).
        Assert.Single(window.GetVisualDescendants().OfType<HexView>());

        // The Hex/Text TabControl has both tabs.
        TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
        string[] tabHeaders = [.. tabs.Items.OfType<TabItem>().Select(t => (string)t.Header!)];
        Assert.Equal(["Hex", "Text"], tabHeaders);

        // The Bytes/Row NumericUpDown is bound to HexBytesPerLine (default 16).
        NumericUpDown selector = window.GetVisualDescendants().OfType<NumericUpDown>().Single();
        Assert.Equal(16m, selector.Value);
        vm.HexBytesPerLine = 32;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(32m, selector.Value);

        // The tree ContextMenu carries the expected command items in order.
        var menu = Assert.IsType<ContextMenu>(tree.ContextMenu);
        string[] menuHeaders = [.. menu.Items.OfType<MenuItem>().Select(m => (string)m.Header!)];
        Assert.Equal(_expectedMenuHeaders, menuHeaders);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void SeededPropertiesAndTreeNode_RealizeRowsAndNode_NoBindingErrors()
    {
        InspectorViewModel vm = CreateViewModel();

        // A normal row, an indented row (IsIndented) and a warned row (IsWarning) exercise every
        // indent/warning converter binding on the property grid.
        vm.Properties.Add(new PropertyItem { Name = "Block Type", Value = "SRR Header" });
        vm.Properties.Add(new PropertyItem { Name = "Flags", Value = "0x0001", IsIndented = true });
        vm.Properties.Add(new PropertyItem { Name = "CRC", Value = "mismatch", IsWarning = true });
        vm.TreeRoots.Add(new TreeNodeViewModel { Text = "Archive" });

        using var sink = new BindingErrorSink();
        (Window window, _) = Show(vm);

        DataGrid grid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "PropertiesGrid");
        Assert.Same(vm.Properties, grid.ItemsSource);
        int rows = window.GetVisualDescendants().OfType<DataGridRow>()
            .Count(r => r.DataContext is PropertyItem p && vm.Properties.Contains(p));
        Assert.Equal(vm.Properties.Count, rows);

        // The seeded tree node realized as a TreeViewItem.
        Assert.Contains(window.GetVisualDescendants().OfType<TreeViewItem>(),
            i => i.DataContext is TreeNodeViewModel { Text: "Archive" });

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void HexSearchBar_TogglesVisible_AndFilePathReflects_NoBindingErrors()
    {
        InspectorViewModel vm = CreateViewModel();
        vm.LoadedFilePath = @"D:\rel\sample.srr";

        using var sink = new BindingErrorSink();
        (Window window, _) = Show(vm);

        // The read-only file bar reflects the VM's LoadedFilePath.
        TextBox filePath = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "LoadedFilePathTextBox");
        Assert.Equal(@"D:\rel\sample.srr", filePath.Text);

        // Toggling IsHexSearchVisible reveals the search bar; its search box becomes effectively visible
        // and its Next/Prev/Close buttons realize.
        TextBox searchBox = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "HexSearchBox");
        Assert.False(searchBox.IsEffectivelyVisible);

        vm.IsHexSearchVisible = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(searchBox.IsEffectivelyVisible);
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => (b.Content as string) == "Next");

        Assert.Empty(sink.Messages);
    }
}
