using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ReScene.Manager.Views.Wizards;

/// <summary>
/// The Beginner "Create an SRR" wizard body, ported from the WPF
/// <c>ReScene.NET.Views.Wizards.CreateSRRWizardBody</c>. A plain <see cref="UserControl"/> whose
/// DataContext is the wizard's dedicated <see cref="ReScene.App.Core.ViewModels.CreatorViewModel"/>;
/// it stacks all five step panels in one grid and shows only the one matching the hosting
/// <see cref="ReScene.App.Core.ViewModels.Wizards.WizardViewModel.CurrentStepIndex"/>.
/// </summary>
public partial class CreateSRRWizardBody : UserControl
{
    public CreateSRRWizardBody()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
