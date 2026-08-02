using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Helpers;

namespace ReScene.Manager.Views.Wizards;

/// <summary>
/// The Beginner "Create an SRS" wizard body, ported from the WPF
/// <c>ReScene.NET.Views.Wizards.CreateSRSWizardBody</c>. A <see cref="UserControl"/> whose DataContext is
/// the wizard's dedicated <see cref="SRSCreatorViewModel"/>; it stacks the three step panels in one grid
/// and shows only the one matching the hosting
/// <see cref="ReScene.App.Core.ViewModels.Wizards.WizardViewModel.CurrentStepIndex"/>.
/// </summary>
/// <remarks>
/// The media-scan phase of SRS creation drives the shared ISO/media-scan progress modal, exactly as the
/// SRS Creator tab does. The beginner wizard runs without that tab realized, so the body wires the
/// <see cref="IsoProgressWindowController"/> itself (owned by the hosting <c>WizardWindow</c>). The
/// DataContext IS the <see cref="SRSCreatorViewModel"/>, so the modal's DataContext falls out of the
/// owner as in the tab view.
/// </remarks>
public partial class CreateSRSWizardBody : UserControl
{
    private IsoProgressWindowController? _isoController;
    private SRSCreatorViewModel? _subscribedVm;

    public CreateSRSWizardBody()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnDataContextChanged;
    }

    // Avalonia's DataContextChanged carries no old/new values, so the previously-subscribed VM is
    // tracked in a field (mirrors SRSCreatorView).
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _subscribedVm?.PropertyChanged -= OnVmPropertyChanged;

        _isoController = null;
        _subscribedVm = DataContext as SRSCreatorViewModel;

        if (_subscribedVm is not { } vm)
        {
            return;
        }

        _isoController = new IsoProgressWindowController(
            this, () => vm.ISOProcessing, () => vm.CancelCreationCommand.Execute(null));
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SRSCreatorViewModel.ISOProcessing))
        {
            return;
        }

        if (sender is SRSCreatorViewModel vm)
        {
            _isoController?.OnProcessingChanged(vm.ISOProcessing);
        }
    }
}
