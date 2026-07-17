using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.ViewModels;

namespace ReScene.Manager.Views;

/// <summary>
/// The Inspector tab, ported from the WPF <c>ReScene.NET.Views.InspectorView</c>. Bound to an
/// <see cref="InspectorViewModel"/> (supplied by the shell via <c>DataContext="{Binding Inspector}"</c>).
/// The structure tree, properties grid, verify-result panel, and Hex/Text tabs (including the embedded
/// <see cref="Controls.HexView"/> and the live hex-search bar) are declarative in the XAML; this
/// code-behind carries the non-MVVM behaviors: pushing tree selection into the VM (via the two-way
/// <c>SelectedItem</c> binding plus a right-click-selects-under-pointer tunnel handler and a
/// double-click-to-preview handler), focusing the search box when it appears, and the property-grid
/// copy context menu.
/// </summary>
public partial class InspectorView : UserControl
{
    private readonly TreeView _tree;
    private readonly TextBox _hexSearchBox;
    private readonly DataGrid _propertiesGrid;
    private readonly Controls.HexView _hexViewer;

    // The VM currently subscribed to; tracked across DataContextChanged (which has no old/new args)
    // so the previous VM's PropertyChanged handler is detached before attaching the new one.
    private InspectorViewModel? _viewModel;

    public InspectorView()
    {
        AvaloniaXamlLoader.Load(this);

        _tree = this.FindControl<TreeView>("StructureTree")!;
        _hexSearchBox = this.FindControl<TextBox>("HexSearchBox")!;
        _propertiesGrid = this.FindControl<DataGrid>("PropertiesGrid")!;
        _hexViewer = this.FindControl<Controls.HexView>("HexViewer")!;

        // Right-click selects the tree item under the pointer so the context menu operates on the
        // right-clicked node, not the previously selected one. Avalonia has no WPF PreviewMouse tunnel
        // event, so a tunnel PointerPressed handler stands in; it does not mark the event Handled so
        // the ContextMenu still opens on release.
        _tree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
    }

    // Track the bound VM so search-box focus can react to IsHexSearchVisible turning true (Avalonia has
    // no WPF IsVisibleChanged). DataContextChanged carries no old value, so detach from the field.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as InspectorViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InspectorViewModel.IsHexSearchVisible))
        {
            return;
        }

        if (_viewModel?.IsHexSearchVisible == true)
        {
            // Post so the focus lands after the search bar has become visible/laid out.
            Dispatcher.UIThread.Post(() =>
            {
                _hexSearchBox.Focus();
                _hexSearchBox.SelectAll();
            });
        }
        else
        {
            // Closing the bar (Close button or Esc) collapses the element holding keyboard focus,
            // and Avalonia — unlike WPF — does not relocate focus out of a hidden subtree. Focus
            // would be stranded outside this view, leaving its KeyBindings (including the Ctrl+F
            // that reopens this very bar) dead until the user clicks back in. Hand focus to the
            // hex surface instead.
            Dispatcher.UIThread.Post(() => _hexViewer.FocusContent());
        }
    }

    // Select the tree item under the pointer on right-click (see the ctor). Guards non-item hits.
    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_tree).Properties.IsRightButtonPressed)
        {
            return;
        }

        if (e.Source is Visual visual
            && visual.FindAncestorOfType<TreeViewItem>(includeSelf: true) is { } item)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    // Double-clicking an image stored-file node opens the preview, mirroring the "View Image" button.
    private void OnTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not InspectorViewModel vm)
        {
            return;
        }

        // Only act when the double-click lands on a tree item (not the empty area).
        if (e.Source is Visual visual
            && visual.FindAncestorOfType<TreeViewItem>(includeSelf: true) is not null
            && vm.PreviewStoredImageCommand.CanExecute(null))
        {
            vm.PreviewStoredImageCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── Property-grid copy context menu ──

    private void OnCopyPropertyNameClick(object? sender, RoutedEventArgs e)
    {
        if (_propertiesGrid.SelectedItem is PropertyItem item)
        {
            CopyToClipboard(item.Name);
        }
    }

    private void OnCopyPropertyValueClick(object? sender, RoutedEventArgs e)
    {
        if (_propertiesGrid.SelectedItem is PropertyItem item)
        {
            CopyToClipboard(item.Value);
        }
    }

    private void OnCopyPropertyClick(object? sender, RoutedEventArgs e)
    {
        if (_propertiesGrid.SelectedItem is PropertyItem item)
        {
            CopyToClipboard($"{item.Name}: {item.Value}");
        }
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
