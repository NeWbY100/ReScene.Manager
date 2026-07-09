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
/// The remaining code-behind carries the three non-MVVM behaviors the WPF view kept in code-behind:
/// <list type="bullet">
///   <item>opening the resource download links through the <see cref="SystemLauncherService"/>
///     (replacing the WPF inline <c>Hyperlink</c> + <c>Process.Start</c>);</item>
///   <item>auto-scrolling the three log TextBoxes to the end as their bound text grows (Avalonia's
///     TextBox has no <c>ScrollToEnd()</c>, so the caret is moved to the end instead); and</item>
///   <item>opening the shared <see cref="BruteForceProgressWindow"/> modally once when a run starts
///     (<c>IsRunning</c> turns true).</item>
/// </list>
/// </summary>
public partial class ReconstructorView : UserControl
{
    private readonly TextBox _systemLogBox;
    private readonly TextBox _phase1LogBox;
    private readonly TextBox _phase2LogBox;

    // Avalonia's DataContextChanged carries no old/new values (unlike WPF's
    // DependencyPropertyChangedEventArgs), so the previously-subscribed VM is tracked in a field.
    private ReconstructorViewModel? _subscribedVm;

    public ReconstructorView()
    {
        AvaloniaXamlLoader.Load(this);

        // x:CompileBindings="False" means x:Name elements aren't wired to generated fields, matching
        // every other ported view/window in this project — resolve the log boxes once via FindControl.
        _systemLogBox = this.FindControl<TextBox>("SystemLogBox")!;
        _phase1LogBox = this.FindControl<TextBox>("Phase1LogBox")!;
        _phase2LogBox = this.FindControl<TextBox>("Phase2LogBox")!;

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

        switch (e.PropertyName)
        {
            case nameof(ReconstructorViewModel.SystemLog):
                ScrollLogToEnd(vm, _systemLogBox);
                return;

            case nameof(ReconstructorViewModel.Phase1Log):
                ScrollLogToEnd(vm, _phase1LogBox);
                return;

            case nameof(ReconstructorViewModel.Phase2Log):
                ScrollLogToEnd(vm, _phase2LogBox);
                return;

            case nameof(ReconstructorViewModel.IsRunning):
                if (vm.IsRunning)
                {
                    OpenBruteForceProgressWindow();
                }

                return;
        }
    }

    // Avalonia's TextBox has no ScrollToEnd(); moving the caret to the end brings the last line into
    // view. Deferred to Background so the binding-driven text update lands first (scrolling before the
    // new text is laid out is a no-op).
    private static void ScrollLogToEnd(ReconstructorViewModel vm, TextBox textBox)
    {
        if (!vm.AutoScrollLog)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () => textBox.CaretIndex = textBox.Text?.Length ?? 0,
            DispatcherPriority.Background);
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
