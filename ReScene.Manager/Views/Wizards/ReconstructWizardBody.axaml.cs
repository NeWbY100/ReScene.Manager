using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ReScene.Manager.Views.Wizards;

/// <summary>
/// The Beginner "Reconstruct RAR archives" wizard body, ported from the WPF
/// <c>ReScene.NET.Views.Wizards.ReconstructWizardBody</c>. A plain <see cref="UserControl"/> whose
/// DataContext is the wizard's dedicated <see cref="ReScene.App.Core.ViewModels.ReconstructorViewModel"/>;
/// it stacks the three step panels in one grid and shows only the one matching the hosting
/// <see cref="ReScene.App.Core.ViewModels.Wizards.WizardViewModel.CurrentStepIndex"/>.
/// </summary>
public partial class ReconstructWizardBody : UserControl
{
    public ReconstructWizardBody() => AvaloniaXamlLoader.Load(this);
}
