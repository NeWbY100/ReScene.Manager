using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ReScene.App.Core.Models;
using ReScene.App.Core.ViewModels;

namespace ReScene.Manager.Views.Wizards;

/// <summary>
/// The stored-file management UI (grid + toolbar) shared by the Beginner "Edit an SRR" and
/// "Create an SRR" wizards, ported from the WPF <c>ReScene.NET.Views.Wizards.StoredFilesManagePanel</c>.
/// Its <see cref="StyledElement.DataContext"/> is a <see cref="SRREditorViewModel"/> (inherited from the
/// host wizard body). This code-behind carries the three non-MVVM grid behaviors the WPF view kept in
/// code-behind: forwarding the multi-selection to the VM (<c>DataGrid.SelectedItems</c> is not
/// bindable), clearing the selection when empty space is left-clicked, and opening the preview on a
/// double-click of a row.
/// </summary>
public partial class StoredFilesManagePanel : UserControl
{
    private readonly DataGrid _storedFilesGrid;

    public StoredFilesManagePanel()
    {
        AvaloniaXamlLoader.Load(this);

        _storedFilesGrid = this.FindControl<DataGrid>("StoredFilesGrid")!;

        // Avalonia has no WPF PreviewMouseDown tunnel event, so a tunnel PointerPressed handler stands
        // in: left-clicking empty grid space (not a row/header/scrollbar) clears the selection. It does
        // not mark the event Handled, so right/middle clicks keep working for a future context menu.
        _storedFilesGrid.AddHandler(PointerPressedEvent, OnStoredFilesPointerPressed, RoutingStrategies.Tunnel);
    }

    // DataGrid.SelectedItems isn't bindable, so the view forwards the multi-selection to the VM.
    private void StoredFilesGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid grid && DataContext is SRREditorViewModel vm)
        {
            vm.SetSelection(grid.SelectedItems.OfType<StoredFileInfo>().ToList());
        }
    }

    // Left-clicking empty space in the grid (not a row, header, or scrollbar) clears the selection.
    // Right/middle clicks are left alone so they don't fight a future context menu.
    private void OnStoredFilesPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_storedFilesGrid).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var source = e.Source as Visual;
        if (FindAncestor<DataGridRow>(source) is not null
            || FindAncestor<ScrollBar>(source) is not null
            || FindAncestor<DataGridColumnHeader>(source) is not null)
        {
            return;
        }

        _storedFilesGrid.SelectedItems.Clear();
    }

    // Double-clicking a row opens the preview, mirroring the Preview… button.
    private void StoredFilesGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        // Only act on double-clicks that land on a data row (not column headers or empty space).
        if (FindAncestor<DataGridRow>(e.Source as Visual) is null)
        {
            return;
        }

        if (DataContext is SRREditorViewModel vm && vm.PreviewStoredFileCommand.CanExecute(null))
        {
            vm.PreviewStoredFileCommand.Execute(null);
            e.Handled = true;
        }
    }

    // Walks the visual tree from the hit element (inclusive) toward the root, returning the nearest
    // ancestor of type T. Mirrors the WPF FindAncestor helper the original used to classify the hit.
    private static T? FindAncestor<T>(Visual? origin) where T : Visual =>
        origin?.GetSelfAndVisualAncestors().OfType<T>().FirstOrDefault();
}
