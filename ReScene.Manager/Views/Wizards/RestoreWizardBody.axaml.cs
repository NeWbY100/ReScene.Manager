using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Helpers;

namespace ReScene.Manager.Views.Wizards;

/// <summary>
/// The Beginner "Restore a sample" wizard body, ported from the WPF
/// <c>ReScene.NET.Views.Wizards.RestoreWizardBody</c>. A <see cref="UserControl"/> whose DataContext is
/// the wizard's dedicated <see cref="BeginnerRestoreViewModel"/>; it stacks the three step panels in one
/// grid — each hosting a bulk (.srr) and single (.srs) sub-panel gated by <c>IsBulk</c>/<c>IsSingle</c> —
/// and shows only the one matching the hosting
/// <see cref="ReScene.App.Core.ViewModels.Wizards.WizardViewModel.CurrentStepIndex"/>.
/// </summary>
/// <remarks>
/// The single-.srs rebuild path (<c>SingleRebuilder</c>) drives the shared ISO/media-scan progress modal
/// exactly as the SRS Reconstructor tab does. The beginner wizard runs without that tab realized, so the
/// body wires the <see cref="IsoProgressWindowController"/> itself (owned by the hosting
/// <c>WizardWindow</c>). The body's DataContext is the <see cref="BeginnerRestoreViewModel"/>, so the ISO
/// window's DataContext must be its <c>SingleRebuilder</c> child — supplied explicitly, since that VM
/// carries the ISO* properties the window binds. The bulk path shows its own inline progress.
/// </remarks>
public partial class RestoreWizardBody : UserControl
{
    private IsoProgressWindowController? _isoController;
    private SRSReconstructorViewModel? _subscribedSingle;

    public RestoreWizardBody()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

    // Avalonia's DataContextChanged carries no old/new values, so the previously-subscribed VM is
    // tracked in a field (mirrors SRSReconstructorView).
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedSingle is not null)
        {
            _subscribedSingle.PropertyChanged -= OnSinglePropertyChanged;
        }

        _isoController = null;
        _subscribedSingle = (DataContext as BeginnerRestoreViewModel)?.SingleRebuilder;

        if (_subscribedSingle is not { } single)
        {
            return;
        }

        _isoController = new IsoProgressWindowController(
            this, () => single.ISOProcessing, () => single.CancelRebuildCommand.Execute(null), () => single);
        single.PropertyChanged += OnSinglePropertyChanged;
    }

    private void OnSinglePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SRSReconstructorViewModel.ISOProcessing))
        {
            return;
        }

        if (sender is SRSReconstructorViewModel vm)
        {
            _isoController?.OnProcessingChanged(vm.ISOProcessing);
        }
    }
}
