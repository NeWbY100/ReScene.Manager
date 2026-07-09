using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ReScene.App.Core.ViewModels;

namespace ReScene.Manager.Views;

/// <summary>
/// Modal brute-force progress dialog opened by the RAR Reconstructor (a later port task) while it
/// tests WinRAR versions against a release, ported from the WPF
/// <c>ReScene.NET.Views.BruteForceProgressWindow</c>. Its <see cref="Window.DataContext"/> is the same
/// <see cref="ReconstructorViewModel"/> the owning tab uses, so every binding here reads that VM's
/// progress/version-grid state directly.
/// </summary>
/// <remarks>
/// The WPF version also opens nested Copy/CRC-validation progress windows when the VM's
/// <c>IsCopying</c>/<c>IsVerifying</c> flip; those windows don't exist yet on the Avalonia side (a
/// later port task) so that wiring is intentionally left out here — only the progress/stats/grid,
/// clipboard context menu, auto-scroll, and Stop/Close state machine are ported in this task.
/// </remarks>
public partial class BruteForceProgressWindow : Window
{
    // x:CompileBindings="False" (needed since DataContext is set dynamically at runtime, not
    // statically typed) means x:Name elements aren't wired to auto-generated fields, matching every
    // other ported view/window in this project — resolved once via FindControl, like CreatorView's
    // "StoredFilesGrid" and MainWindow's "VersionLink".
    private readonly DataGrid _versionGrid;
    private readonly Button _stopCloseButton;

    private bool _isCompleted;
    private ReconstructorViewModel? _subscribedVm;

    public BruteForceProgressWindow()
    {
        AvaloniaXamlLoader.Load(this);

        _versionGrid = this.FindControl<DataGrid>("VersionGrid")!;
        _stopCloseButton = this.FindControl<Button>("btnStopClose")!;

        // Avalonia's DataContextChanged carries no old/new values (unlike WPF's
        // DependencyPropertyChangedEventArgs), so the previously-subscribed VM is tracked in a field.
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
            _subscribedVm.VersionEntries.CollectionChanged -= OnVersionEntriesChanged;
        }

        _subscribedVm = DataContext as ReconstructorViewModel;

        if (_subscribedVm is not { } vm)
        {
            return;
        }

        vm.PropertyChanged += OnVmPropertyChanged;
        vm.VersionEntries.CollectionChanged += OnVersionEntriesChanged;
    }

    private void OnVersionEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_subscribedVm is not { AutoScrollProgress: true } vm || vm.VersionEntries.Count == 0)
        {
            return;
        }

        // Defer the scroll: when the change came from a Dispatcher-marshalled update earlier in the
        // pipeline, the row container may not exist yet at the moment the event fires.
        Dispatcher.UIThread.Post(
            () =>
            {
                if (vm.VersionEntries.Count == 0)
                {
                    return;
                }

                _versionGrid.ScrollIntoView(vm.VersionEntries[^1], null);
            },
            DispatcherPriority.Background);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ReconstructorViewModel.IsRunning))
        {
            return;
        }

        if (sender is ReconstructorViewModel { IsRunning: false })
        {
            _isCompleted = true;
            _stopCloseButton.Content = "Close";
            _stopCloseButton.IsEnabled = true;
            _stopCloseButton.Classes.Remove("cancel");
            _stopCloseButton.Classes.Add("primary");
        }
    }

    private void OnStopCloseClick(object? sender, RoutedEventArgs e)
    {
        if (_isCompleted)
        {
            Close();
            return;
        }

        if (DataContext is ReconstructorViewModel vm)
        {
            vm.StopCommand.Execute(null);
            _stopCloseButton.IsEnabled = false;
            _stopCloseButton.Content = "Stopping...";
        }
    }

    private void OnCopyArgumentsClick(object? sender, RoutedEventArgs e)
    {
        if (GetSelectedVersionEntry() is { Arguments.Length: > 0 } entry)
        {
            CopyToClipboard(entry.Arguments);
        }
    }

    private void OnCopyFullCommandLineClick(object? sender, RoutedEventArgs e)
    {
        if (GetSelectedVersionEntry() is { FullCommandLine.Length: > 0 } entry)
        {
            CopyToClipboard(entry.FullCommandLine);
        }
    }

    // Avalonia's Clipboard is async and owned by the TopLevel (unlike WPF's static
    // Clipboard.SetText); fire-and-forget it here, guarded against a headless/detached TopLevel.
    private void CopyToClipboard(string text) =>
        _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);

    // The grid is resolved once in the constructor, so the selected row is read straight off it
    // rather than walking the WPF MenuItem -> ContextMenu -> PlacementTarget chain.
    private ReconstructorViewModel.VersionEntry? GetSelectedVersionEntry() =>
        _versionGrid.SelectedItem as ReconstructorViewModel.VersionEntry;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_isCompleted)
        {
            e.Cancel = true;
            if (DataContext is ReconstructorViewModel vm)
            {
                vm.StopCommand.Execute(null);
                _stopCloseButton.IsEnabled = false;
                _stopCloseButton.Content = "Stopping...";
            }

            return;
        }

        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
            _subscribedVm.VersionEntries.CollectionChanged -= OnVersionEntriesChanged;
        }

        base.OnClosing(e);
    }
}
