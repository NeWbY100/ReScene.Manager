using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ReScene.App.Core.ViewModels;

namespace ReScene.Manager.Views;

/// <summary>
/// The SRR Creator tab, ported from the WPF <c>ReScene.NET.Views.CreatorView</c>. Bound to a
/// <see cref="CreatorViewModel"/> (supplied by the shell via <c>DataContext="{Binding Creator}"</c>).
/// This code-behind carries the two non-MVVM behaviors the WPF view kept in code-behind: dropping
/// files onto the Stored Files grid to add them, and the inline-edit dedup guard on the editable
/// "Stored As" column. Input/Output TextBox file-drop is declarative via
/// <c>behaviors:TextBoxDropBehavior.DropMode="File"</c> in the XAML.
/// </summary>
public partial class CreatorView : UserControl
{
    // The "Stored As" value before an inline edit, so a duplicate edit can be reverted.
    private string? _storedNameBeforeEdit;

    public CreatorView()
    {
        AvaloniaXamlLoader.Load(this);

        // Avalonia has no WPF PreviewDragOver/PreviewDrop tunnel and no XAML AllowDrop property, so
        // the grid's file-drop is opted in and wired here (mirroring the shell window's drag-drop).
        DataGrid grid = this.FindControl<DataGrid>("StoredFilesGrid")!;
        DragDrop.SetAllowDrop(grid, true);
        grid.AddHandler(DragDrop.DragOverEvent, OnStoredFilesDragOver);
        grid.AddHandler(DragDrop.DropEvent, OnStoredFilesDrop);
    }

    private void OnStoredFilesDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnStoredFilesDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not CreatorViewModel vm)
        {
            return;
        }

        IStorageItem[]? files = e.DataTransfer.TryGetFiles();
        if (files is null)
        {
            return;
        }

        List<string> paths = files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList();

        if (paths.Count > 0)
        {
            vm.AddStoredFiles(paths);
        }
    }

    private void OnStoredNameBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
        => _storedNameBeforeEdit = (e.Row.DataContext as CreatorViewModel.StoredFileItem)?.StoredName;

    private void OnStoredNameCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit
            || DataContext is not CreatorViewModel vm
            || e.Row.DataContext is not CreatorViewModel.StoredFileItem item
            || e.EditingElement is not TextBox editor)
        {
            return;
        }

        string newName = (editor.Text ?? string.Empty).Replace('\\', '/').Trim();

        // Reject a rename onto a name another stored file already uses; otherwise normalize the
        // committed value to the SRR's key space (forward slashes). Avalonia's DataGridTextColumn
        // commits on edit-end, so set both the editor text (which the commit writes back) and the
        // model value, matching the WPF original.
        if (!newName.Equals(_storedNameBeforeEdit, StringComparison.OrdinalIgnoreCase)
            && vm.IsStoredNameTaken(newName, item))
        {
            editor.Text = _storedNameBeforeEdit;
            item.StoredName = _storedNameBeforeEdit ?? item.StoredName;
            vm.WarnDuplicateStoredName(newName);
        }
        else
        {
            editor.Text = newName;
            item.StoredName = newName;
        }
    }
}
