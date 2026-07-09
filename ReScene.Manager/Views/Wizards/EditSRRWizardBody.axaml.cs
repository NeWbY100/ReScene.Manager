using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ReScene.Manager.Views.Wizards;

/// <summary>
/// The Beginner "Edit an SRR" wizard body, ported from the WPF
/// <c>ReScene.NET.Views.Wizards.EditSRRWizardBody</c>. A plain <see cref="UserControl"/> whose
/// DataContext is the wizard's dedicated <see cref="ReScene.App.Core.ViewModels.SRREditorViewModel"/>;
/// it stacks all four step panels in one grid and shows only the one matching the hosting
/// <see cref="ReScene.App.Core.ViewModels.Wizards.WizardViewModel.CurrentStepIndex"/>. Its stored-file
/// step hosts the shared <see cref="StoredFilesManagePanel"/>.
/// </summary>
public partial class EditSRRWizardBody : UserControl
{
    public EditSRRWizardBody() => AvaloniaXamlLoader.Load(this);
}
