using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Core.Comparison;
using ReScene.Hex;
using ReScene.Manager.Controls;
using ReScene.Manager.Converters;
using ReScene.Manager.Services;
using ReScene.Manager.Views;
using ReScene.RAR;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported <see cref="FileCompareView"/> (Compare tab — the app's most
/// complex view: two symmetric panels, each a structure <see cref="TreeView"/> + a properties
/// <see cref="DataGrid"/> + an embedded <see cref="HexView"/>). The central gate is <b>zero binding
/// errors</b> (via <see cref="BindingErrorSink"/>): both empty and with seeded properties/tree nodes
/// (including diff/indent rows) so the diff/indent converter bindings realize. The compare pipeline,
/// file dialogs and hex-diff computer are inert fakes — only the view wiring is exercised; a live
/// compare, drag-drop and clipboard copy are the controller's launch-smoke.
/// </summary>
public class FileCompareViewTests
{
    // ── Inert service doubles (the view test never runs a compare) ──

    private sealed class InertFileCompareService : IFileCompareService
    {
        public object? LoadFileData(string filePath) => null;

        public IReadOnlyList<RARDetailedBlock>? ParseDetailedBlocks(string filePath) => null;

        public CompareResult Compare(object? leftData, object? rightData,
            IReadOnlyList<RARDetailedBlock>? leftBlocks = null, IReadOnlyList<RARDetailedBlock>? rightBlocks = null,
            IHexDataSource? leftSource = null, IHexDataSource? rightSource = null) => new();
    }

    private sealed class InertHexDiffComputer : IHexDiffComputer
    {
        public Task<HexDiffResult> ComputeAsync(
            IHexDataSource leftSource, long leftOffset, long leftLength,
            IHexDataSource rightSource, long rightOffset, long rightLength,
            IProgress<HexDiffProgress>? progress,
            CancellationToken ct) => Task.FromResult(new HexDiffResult([], []));
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, UiDispatcherPriority priority) => action();
        public bool CheckAccess() => true;
    }

    private static FileCompareViewModel CreateViewModel() =>
        new(
            new InertFileCompareService(),
            new AvaloniaFileDialogService(static () => null), // headless: dialogs no-op, never block
            new InertHexDiffComputer(),
            new InlineUiDispatcher());

    private static (Window window, FileCompareViewModel vm) Show(FileCompareViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 900, Content = new FileCompareView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    [AvaloniaFact]
    public void EmptyView_NoFilesLoaded_NoBindingErrors()
    {
        FileCompareViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (Window window, _) = Show(vm);

        // The two symmetric panels realize.
        Assert.Equal(2, window.GetVisualDescendants().OfType<TreeView>().Count());
        Assert.Equal(2, window.GetVisualDescendants().OfType<DataGrid>().Count());

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void SymmetricPanels_HaveGridsColumnsTreesHexViewsAndBytesPerRowSelectors_NoBindingErrors()
    {
        FileCompareViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (Window window, _) = Show(vm);

        // Both property DataGrids exist, each with the two expected columns (Property template + Value).
        DataGrid[] grids = [.. window.GetVisualDescendants().OfType<DataGrid>()];
        Assert.Equal(2, grids.Length);
        foreach (DataGrid grid in grids)
        {
            Assert.Equal(2, grid.Columns.Count);
            Assert.IsType<DataGridTemplateColumn>(grid.Columns[0]);
            Assert.Equal("Property", grid.Columns[0].Header);
            Assert.IsType<DataGridTextColumn>(grid.Columns[1]);
            Assert.Equal("Value", grid.Columns[1].Header);
        }

        // Both structure trees exist.
        Assert.Equal(2, window.GetVisualDescendants().OfType<TreeView>().Count());

        // Two embedded HexView composites (one per side).
        Assert.Equal(2, window.GetVisualDescendants().OfType<HexView>().Count());

        // Two ComboBox bytes/row selectors (fixed-choice preset dropdowns), both bound to the shared
        // HexBytesPerLine (default 16).
        ComboBox[] selectors = [.. window.GetVisualDescendants().OfType<ComboBox>()];
        Assert.Equal(2, selectors.Length);
        Assert.All(selectors, cb => Assert.Equal(16, cb.SelectedItem));

        // VM → both selectors reflect a changed value.
        vm.HexBytesPerLine = 32;
        Dispatcher.UIThread.RunJobs();
        Assert.All(selectors, cb => Assert.Equal(32, cb.SelectedItem));

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void SeededPropertiesAndTreeNode_RealizeRowsAndNodes_NoBindingErrors()
    {
        FileCompareViewModel vm = CreateViewModel();

        // A normal row, a diff row (IsDifferent) and an indented row (IsIndented) exercise every
        // diff/indent converter binding on the property grid; a diff tree node exercises the tree
        // foreground converter.
        vm.LeftProperties.Add(new PropertyItem { Name = "Format", Value = "RAR4" });
        vm.LeftProperties.Add(new PropertyItem { Name = "CRC", Value = "DEADBEEF", IsDifferent = true });
        vm.LeftProperties.Add(new PropertyItem { Name = "  Method", Value = "Store", IsIndented = true });
        vm.RightProperties.Add(new PropertyItem { Name = "Format", Value = "RAR5" });
        vm.LeftTreeRoots.Add(new TreeNodeViewModel { Text = "Archive", IsDifferent = true });

        using var sink = new BindingErrorSink();
        (Window window, _) = Show(vm);

        // The left grid realized a row per seeded property.
        DataGrid leftGrid = window.GetVisualDescendants().OfType<DataGrid>()
            .Single(g => g.Name == "LeftPropertiesGrid");
        Assert.Same(vm.LeftProperties, leftGrid.ItemsSource);
        int leftRows = window.GetVisualDescendants().OfType<DataGridRow>()
            .Count(r => r.DataContext is PropertyItem p && vm.LeftProperties.Contains(p));
        Assert.Equal(vm.LeftProperties.Count, leftRows);

        // The seeded tree node realized as a TreeViewItem.
        Assert.Contains(window.GetVisualDescendants().OfType<TreeViewItem>(),
            i => i.DataContext is TreeNodeViewModel { Text: "Archive" });

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void BoolToBrushConverter_MapsTrueToTokenBrush_AndFalseOrNonBoolToUnset()
    {
        var converter = new BoolToBrushConverter();

        // true + a real Tokens key → the exact token brush (locks the diff-tint contract).
        object? on = converter.Convert(true, typeof(IBrush), "AccentError", CultureInfo.InvariantCulture);
        ISolidColorBrush brush = Assert.IsAssignableFrom<ISolidColorBrush>(on);
        Assert.Equal(Color.Parse("#FFF44747"), brush.Color);

        // false, null, non-bool, and unknown keys all fall back so the target inherits its theme default.
        Assert.Equal(AvaloniaProperty.UnsetValue, converter.Convert(false, typeof(IBrush), "AccentError", CultureInfo.InvariantCulture));
        Assert.Equal(AvaloniaProperty.UnsetValue, converter.Convert(null, typeof(IBrush), "AccentError", CultureInfo.InvariantCulture));
        Assert.Equal(AvaloniaProperty.UnsetValue, converter.Convert("nope", typeof(IBrush), "AccentError", CultureInfo.InvariantCulture));
        Assert.Equal(AvaloniaProperty.UnsetValue, converter.Convert(true, typeof(IBrush), "NoSuchKey", CultureInfo.InvariantCulture));
    }

    [AvaloniaFact]
    public void BytesPerRowComboBox_ExposesFixedPresets_AndSelectionRoundTripsToVm()
    {
        FileCompareViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (Window window, _) = Show(vm);

        // The ComboBox is non-editable (Avalonia has no free-text entry, unlike WPF's NumericUpDown), so
        // the only way to change HexBytesPerLine is picking one of the fixed presets.
        ComboBox selector = window.GetVisualDescendants().OfType<ComboBox>().First();
        Assert.Equal([8, 16, 24, 32, 48, 64], selector.Items.OfType<int>());

        selector.SelectedItem = 32;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(32, vm.HexBytesPerLine);
        Assert.InRange(vm.HexBytesPerLine, 1, 128);
        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void PropertyGridCopyValue_LeftPane_CopiesSelectedValueToClipboard()
    {
        FileCompareViewModel vm = CreateViewModel();
        var item = new PropertyItem { Name = "CRC", Value = "DEADBEEF" };
        vm.LeftProperties.Add(item);
        vm.SelectedLeftProperty = item;

        var view = new FileCompareView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        DataGrid leftGrid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "LeftPropertiesGrid");
        var menu = Assert.IsType<ContextMenu>(leftGrid.ContextMenu);

        // Open the menu the way the framework auto-opens a DataGrid.ContextMenu: Avalonia sets the
        // POPUP's PlacementTarget but never the ContextMenu's own PlacementTarget property. The old
        // resolver read ContextMenu.PlacementTarget, which stays null → Copy did nothing.
        menu.Open(leftGrid);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(menu.PlacementTarget); // documents the dead-menu root cause

        MenuItem copyValue = menu.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "Copy Value");
        copyValue.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        IClipboard clipboard = TopLevel.GetTopLevel(view)!.Clipboard!;
        string? copied = clipboard.TryGetTextAsync().GetAwaiter().GetResult();
        Assert.Equal("DEADBEEF", copied);
    }

    [AvaloniaFact]
    public void PropertyGridCopyValue_RightPane_CopiesThatPanesSelection()
    {
        // Two panes share one handler set, so the resolver must read the RIGHT pane's selection when
        // the right grid's menu is used — not the left's.
        FileCompareViewModel vm = CreateViewModel();
        vm.LeftProperties.Add(new PropertyItem { Name = "CRC", Value = "LEFTVALUE" });
        vm.SelectedLeftProperty = vm.LeftProperties[0];
        var rightItem = new PropertyItem { Name = "CRC", Value = "RIGHTVALUE" };
        vm.RightProperties.Add(rightItem);
        vm.SelectedRightProperty = rightItem;

        var view = new FileCompareView { DataContext = vm };
        var window = new Window { Width = 1200, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        DataGrid rightGrid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "RightPropertiesGrid");
        var menu = Assert.IsType<ContextMenu>(rightGrid.ContextMenu);
        menu.Open(rightGrid);
        Dispatcher.UIThread.RunJobs();

        MenuItem copyValue = menu.Items.OfType<MenuItem>().Single(m => (string?)m.Header == "Copy Value");
        copyValue.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Dispatcher.UIThread.RunJobs();

        IClipboard clipboard = TopLevel.GetTopLevel(view)!.Clipboard!;
        Assert.Equal("RIGHTVALUE", clipboard.TryGetTextAsync().GetAwaiter().GetResult());
    }

    [AvaloniaFact]
    public void LeftFilePathTextBox_ReflectsViewModel_NoBindingErrors()
    {
        FileCompareViewModel vm = CreateViewModel();
        vm.LeftFilePath = @"D:\rel\left.srr";

        using var sink = new BindingErrorSink();
        (Window window, _) = Show(vm);

        TextBox left = window.GetVisualDescendants().OfType<TextBox>()
            .Single(t => t.Name == "LeftFilePathTextBox");
        Assert.Equal(@"D:\rel\left.srr", left.Text);

        // VM → view updates flow through.
        vm.LeftFilePath = @"D:\rel\other.srr";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(@"D:\rel\other.srr", left.Text);

        Assert.Empty(sink.Messages);
    }
}
