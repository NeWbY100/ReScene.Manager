using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ReScene.Manager.Views.Wizards;

/// <summary>
/// The Beginner "Restore a sample" wizard body, ported from the WPF
/// <c>ReScene.NET.Views.Wizards.RestoreWizardBody</c>. A plain <see cref="UserControl"/> whose
/// DataContext is the wizard's dedicated <see cref="ReScene.App.Core.ViewModels.BeginnerRestoreViewModel"/>;
/// it stacks the three step panels in one grid — each hosting a bulk (.srr) and single (.srs) sub-panel
/// gated by <c>IsBulk</c>/<c>IsSingle</c> — and shows only the one matching the hosting
/// <see cref="ReScene.App.Core.ViewModels.Wizards.WizardViewModel.CurrentStepIndex"/>.
/// </summary>
public partial class RestoreWizardBody : UserControl
{
    public RestoreWizardBody() => AvaloniaXamlLoader.Load(this);
}
