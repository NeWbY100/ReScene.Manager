using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;

namespace ReScene.Manager.Views;

/// <summary>
/// The RAR Reconstructor tab, ported from the WPF <c>ReScene.NET.Views.ReconstructorView</c>. Bound to a
/// <see cref="ReconstructorViewModel"/> (supplied by the shell via <c>DataContext="{Binding Reconstructor}"</c>).
/// Path TextBox folder/file drop is declarative via <c>behaviors:TextBoxDropBehavior.DropMode</c> in the XAML.
/// The remaining code-behind carries the two non-MVVM behaviors the WPF view kept in code-behind:
/// <list type="bullet">
///   <item>opening the resource download links through the <see cref="SystemLauncherService"/>
///     (replacing the WPF inline <c>Hyperlink</c> + <c>Process.Start</c>);</item>
///   <item>opening the shared <see cref="BruteForceProgressWindow"/> modally once when a run starts
///     (<c>IsRunning</c> turns true).</item>
/// </list>
/// Log auto-scroll is declarative: the merged log ListBox binds the logList style's
/// <c>ListBoxAutoScroll.AutoScrollToEnd</c> behavior to <c>AutoScrollLog</c> in the XAML.
/// </summary>
public partial class ReconstructorView : UserControl
{
    // Avalonia's DataContextChanged carries no old/new values (unlike WPF's
    // DependencyPropertyChangedEventArgs), so the previously-subscribed VM is tracked in a field.
    private ReconstructorViewModel? _subscribedVm;

    public ReconstructorView()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
        }

        _subscribedVm = DataContext as ReconstructorViewModel;

        if (_subscribedVm is { } vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    // Opens the resource download link in the OS default browser. Replaces the WPF
    // OnHyperlinkRequestNavigate + Process.Start; the URL travels on the Button's Tag.
    private void OnResourceLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            // OpenUrl already swallows launch failures; the try/catch is belt-and-braces so a
            // click can never surface an unhandled exception.
            try
            {
                new SystemLauncherService().OpenUrl(url);
            }
            catch
            {
                // Best-effort: opening a link should never crash the app.
            }
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ReconstructorViewModel vm)
        {
            return;
        }

        // Log auto-scroll is no longer handled here: the merged LogEntries ListBox uses the logList
        // style's ListBoxAutoScroll behavior, bound to AutoScrollLog in the view.
        switch (e.PropertyName)
        {
            case nameof(ReconstructorViewModel.IsRunning):
                if (vm.IsRunning)
                {
                    OpenBruteForceProgressWindow();
                }

                return;
        }
    }

    // Opens the modal brute-force progress dialog once, when a run begins. IsRunning only raises a
    // change notification on transition, so this fires exactly once per true-transition (mirroring the
    // WPF view, which likewise opened on the change and did not auto-close the window when IsRunning
    // turned false — the window owns its own Stop/Close lifecycle). Deferred to the dispatcher because
    // Avalonia's ShowDialog is async and the owning Window may not be resolvable synchronously; the
    // returned Task is fire-and-forget. Null-owner guarded so it is a safe no-op headless / before the
    // view is attached to a top-level window.
    private void OpenBruteForceProgressWindow() =>
        Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(this) is not Window owner)
            {
                return;
            }

            var window = new BruteForceProgressWindow { DataContext = DataContext };
            _ = window.ShowDialog(owner);
        });
}
