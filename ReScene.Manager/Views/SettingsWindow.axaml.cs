using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ReScene.App.Core.ViewModels;

namespace ReScene.Manager.Views;

/// <summary>
/// Settings form, ported from the WPF <c>ReScene.NET.Views.SettingsWindow</c>. Bound to the shared
/// <see cref="SettingsViewModel"/> (from <c>ReScene.App.Core</c>) — same VM the WPF app uses, so no
/// settings logic is duplicated here.
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>Parameterless constructor for the XAML designer / loader only.</summary>
    public SettingsWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public SettingsWindow(SettingsViewModel viewModel)
        : this()
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        DataContext = viewModel;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    // Opens a WinRAR-pack download link in the OS default browser; the URL travels on the Button's
    // Tag. Shared behavior with the Reconstructor tab and wizard via ResourceLink.
    private void OnResourceLinkClick(object? sender, RoutedEventArgs e) => ResourceLink.OpenFromTag(sender);

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        // Mirrors the WPF code-behind: the save must be driven from the Click handler (Command
        // execution alone wouldn't let us gate the dialog result on vm.DialogResult afterwards).
        // Only close(true) when the save actually succeeded; otherwise leave the window open, same
        // as WPF only setting DialogResult when vm.DialogResult is true.
        if (DataContext is SettingsViewModel vm)
        {
            vm.SaveCommand.Execute(null);
            if (vm.DialogResult)
            {
                Close(true);
            }
        }
    }
}
