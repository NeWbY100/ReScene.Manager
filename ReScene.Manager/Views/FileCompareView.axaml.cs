using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ReScene.App.Core.ViewModels;

namespace ReScene.Manager.Views;

/// <summary>
/// The Compare tab, ported from the WPF <c>ReScene.NET.Views.FileCompareView</c>. Bound to a
/// <see cref="FileCompareViewModel"/> (supplied by the shell via <c>DataContext="{Binding FileCompare}"</c>).
/// This code-behind carries the view's non-MVVM behaviors: pushing each <see cref="TreeView"/>'s
/// selection into the VM, per-side drag-and-drop with an active-side highlight overlay, and the
/// property-grid copy context menu. The hex chrome, tree/property/hex layout and diff highlighting are
/// declarative in the XAML.
/// </summary>
public partial class FileCompareView : UserControl
{
    // Keep in sync with FileDialogFilters.CompareFiles: the Browse picker offers these formats,
    // so drag-and-drop must accept the same set (Compare supports MKV/WebM too).
    private static readonly string[] _supportedExtensions = [".srr", ".srs", ".rar", ".mkv", ".webm"];

    // Active/inactive drop-highlight fills (#60 / #30 over the accent RGB), matching the WPF original.
    private static readonly IBrush _activeDropBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x00, 0x78, 0xD4));
    private static readonly IBrush _inactiveDropBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x00, 0x78, 0xD4));

    private readonly Border _leftDropOverlay;
    private readonly Border _rightDropOverlay;
    private readonly DataGrid _leftPropertiesGrid;
    private readonly DataGrid _rightPropertiesGrid;

    public FileCompareView()
    {
        AvaloniaXamlLoader.Load(this);

        _leftDropOverlay = this.FindControl<Border>("LeftDropOverlay")!;
        _rightDropOverlay = this.FindControl<Border>("RightDropOverlay")!;
        _leftPropertiesGrid = this.FindControl<DataGrid>("LeftPropertiesGrid")!;
        _rightPropertiesGrid = this.FindControl<DataGrid>("RightPropertiesGrid")!;

        // Avalonia has no WPF PreviewDragOver/PreviewDrop tunnel and no XAML AllowDrop property, so the
        // whole view opts into drops and the handlers are wired here (mirroring the other ported views).
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
    }

    // ── Tree selection → VM (mirrors the WPF SelectedItemChanged handlers) ──

    private void OnLeftTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TreeView tree && DataContext is FileCompareViewModel vm)
        {
            vm.SelectedLeftTreeNode = tree.SelectedItem as TreeNodeViewModel;
        }
    }

    private void OnRightTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TreeView tree && DataContext is FileCompareViewModel vm)
        {
            vm.SelectedRightTreeNode = tree.SelectedItem as TreeNodeViewModel;
        }
    }

    // ── Drag-and-drop (11.3.18 DataTransfer API) ──

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (GetSupportedPath(e) is null)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;

        // Show both overlays and highlight the active (drop-target) side.
        bool isLeft = IsOnLeftSide(e);
        _leftDropOverlay.IsVisible = true;
        _rightDropOverlay.IsVisible = true;
        _leftDropOverlay.Background = isLeft ? _activeDropBrush : _inactiveDropBrush;
        _rightDropOverlay.Background = isLeft ? _inactiveDropBrush : _activeDropBrush;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        _leftDropOverlay.IsVisible = false;
        _rightDropOverlay.IsVisible = false;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        _leftDropOverlay.IsVisible = false;
        _rightDropOverlay.IsVisible = false;

        string? path = GetSupportedPath(e);
        if (path is null || DataContext is not FileCompareViewModel vm)
        {
            return;
        }

        if (IsOnLeftSide(e))
        {
            _ = vm.LoadLeftFileAsync(path);
        }
        else
        {
            _ = vm.LoadRightFileAsync(path);
        }

        e.Handled = true;
    }

    private bool IsOnLeftSide(DragEventArgs e) => e.GetPosition(this).X < Bounds.Width / 2;

    private static string? GetSupportedPath(DragEventArgs e)
    {
        IStorageItem[]? files = e.DataTransfer.TryGetFiles();
        string? path = files is { Length: > 0 } ? files[0].TryGetLocalPath() : null;
        return !string.IsNullOrEmpty(path) && IsSupportedFile(path) ? path : null;
    }

    private static bool IsSupportedFile(string path)
    {
        string ext = Path.GetExtension(path);
        foreach (string supported in _supportedExtensions)
        {
            if (ext.Equals(supported, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ── Property-grid copy context menu ──

    private void OnCopyPropertyNameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (GetSelectedProperty(sender) is PropertyItem item)
        {
            CopyToClipboard(item.Name);
        }
    }

    private void OnCopyPropertyValueClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (GetSelectedProperty(sender) is PropertyItem item)
        {
            CopyToClipboard(item.Value);
        }
    }

    private void OnCopyPropertyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (GetSelectedProperty(sender) is PropertyItem item)
        {
            CopyToClipboard($"{item.Name}: {item.Value}");
        }
    }

    // Resolve the property row the menu acted on. Avalonia auto-opens a DataGrid.ContextMenu without
    // ever setting ContextMenu.PlacementTarget (it sets the popup's target instead), so the old
    // PlacementTarget probe was always null and Copy did nothing. Instead map the clicked MenuItem's
    // owning ContextMenu (by identity) to its grid, then read that side's bound VM selection
    // (SelectedLeftProperty/SelectedRightProperty) — one handler set serving both grids.
    private PropertyItem? GetSelectedProperty(object? sender)
    {
        if (DataContext is not FileCompareViewModel vm || sender is not MenuItem item)
        {
            return null;
        }

        ContextMenu? menu = item.FindLogicalAncestorOfType<ContextMenu>();
        if (ReferenceEquals(menu, _leftPropertiesGrid.ContextMenu))
        {
            return vm.SelectedLeftProperty;
        }

        if (ReferenceEquals(menu, _rightPropertiesGrid.ContextMenu))
        {
            return vm.SelectedRightProperty;
        }

        return null;
    }

    private void CopyToClipboard(string text)
    {
        IClipboard? clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            _ = clipboard.SetTextAsync(text);
        }
    }
}
