using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ReScene.App.Core.ViewModels;
using ReScene.App.Core.ViewModels.Wizards;
using ReScene.Manager.Views.Wizards;

namespace ReScene.Manager.Views;

/// <summary>
/// The Beginner-mode card hub, ported from the WPF <c>ReScene.NET.Views.BeginnerShellView</c>. Its
/// DataContext is a <see cref="BeginnerShellViewModel"/> (supplied by the shell via
/// <c>DataContext="{Binding Beginner}"</c>). Each card carries a <see cref="BeginnerCard"/> in its
/// <see cref="Control.Tag"/>; clicking one asks <see cref="BeginnerWizardFactory"/> to assemble the
/// matching wizard and opens it as a modal <see cref="WizardWindow"/> owned by this view's top-level
/// window. The window manages its own close/disposal — the <c>ShowDialog</c> task is fire-and-forget.
/// </summary>
public partial class BeginnerShellView : UserControl
{
    public BeginnerShellView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnCardClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { Tag: BeginnerCard card } || DataContext is not BeginnerShellViewModel shell)
        {
            return;
        }

        (WizardViewModel wizardVm, Control body) = BeginnerWizardFactory.Create(card, shell);
        var window = new WizardWindow(wizardVm, body);

        // Own the wizard to this view's window when there is one (so it centers on and is modal to the
        // shell); with no resolvable top-level (headless), fall back to a non-modal show so the card
        // still opens something rather than throwing.
        if (TopLevel.GetTopLevel(this) is Window owner)
        {
            _ = window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }
}
