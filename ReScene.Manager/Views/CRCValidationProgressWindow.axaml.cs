using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ReScene.Manager.Views;

/// <summary>
/// Modal CRC-validation progress dialog opened by <see cref="BruteForceProgressWindow"/> while the RAR
/// reconstructor verifies the copied files against the release, ported from the WPF
/// <c>ReScene.NET.Views.CRCValidationProgressWindow</c>. Its <see cref="Window.DataContext"/> is the
/// same <c>ReconstructorViewModel</c> the owning window uses, so every binding here reads that VM's
/// <c>Verify*</c> progress properties directly. Opened/closed by a
/// <see cref="Helpers.ModalProgressWindowController{TWindow}"/> keyed off <c>IsVerifying</c>; the WPF
/// <c>DarkTitleBar.Enable</c> call and the button-feedback half of <c>ProgressWindowLifecycle</c> are
/// dropped, same as the earlier <see cref="ISOProgressWindow"/> port — Cancel simply closes the dialog
/// and the controller's <c>Closed</c> handler turns that into a Stop-command invocation.
/// </summary>
public partial class CRCValidationProgressWindow : Window
{
    public CRCValidationProgressWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
