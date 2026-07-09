using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ReScene.Manager.Views.Wizards;

/// <summary>
/// The Beginner "Create an SRS" wizard body, ported from the WPF
/// <c>ReScene.NET.Views.Wizards.CreateSRSWizardBody</c>. A plain <see cref="UserControl"/> whose
/// DataContext is the wizard's dedicated <see cref="ReScene.App.Core.ViewModels.SRSCreatorViewModel"/>;
/// it stacks the three step panels in one grid and shows only the one matching the hosting
/// <see cref="ReScene.App.Core.ViewModels.Wizards.WizardViewModel.CurrentStepIndex"/>.
/// </summary>
public partial class CreateSRSWizardBody : UserControl
{
    public CreateSRSWizardBody() => AvaloniaXamlLoader.Load(this);
}
