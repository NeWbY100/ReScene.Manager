using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ReScene.App.Core.ViewModels;

namespace ReScene.Manager.Views.Wizards;

/// <summary>
/// The Beginner "Reconstruct RAR archives" wizard body, ported from the WPF
/// <c>ReScene.NET.Views.Wizards.ReconstructWizardBody</c>. A <see cref="UserControl"/> whose DataContext
/// is the wizard's dedicated <see cref="ReconstructorViewModel"/>; it stacks the three step panels in one
/// grid and shows only the one matching the hosting
/// <see cref="ReScene.App.Core.ViewModels.Wizards.WizardViewModel.CurrentStepIndex"/>.
/// </summary>
/// <remarks>
/// A reconstruction run drives the modal <see cref="BruteForceProgressWindow"/> (and, through it, the
/// nested copy/CRC dialogs) — exactly as the RAR Reconstructor tab does when <c>IsRunning</c> turns true.
/// The beginner wizard runs without that tab realized, so the body opens the window itself (owned by the
/// hosting <c>WizardWindow</c>, DataContext = the shared <see cref="ReconstructorViewModel"/>). Mirrors
/// <see cref="ReconstructorView"/>'s open-once-on-IsRunning behaviour.
/// </remarks>
public partial class ReconstructWizardBody : UserControl
{
    private ReconstructorViewModel? _subscribedVm;

    public ReconstructWizardBody()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

    // Avalonia's DataContextChanged carries no old/new values, so the previously-subscribed VM is
    // tracked in a field (mirrors ReconstructorView).
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

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReconstructorViewModel.IsRunning)
            && sender is ReconstructorViewModel { IsRunning: true })
        {
            OpenBruteForceProgressWindow();
        }
    }

    // Opens a WinRAR-pack download link in the OS default browser; the URL travels on the Button's
    // Tag. Shared behavior with the Reconstructor tab and Settings via ResourceLink.
    private void OnResourceLinkClick(object? sender, RoutedEventArgs e) => ResourceLink.OpenFromTag(sender);

    // IsRunning only raises a change notification on transition, so this fires exactly once per run.
    // Deferred to the dispatcher (Avalonia's ShowDialog is async; the returned Task is fire-and-forget)
    // and null-owner guarded so it is a safe no-op headless / before the wizard is shown.
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
