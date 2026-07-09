using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ReScene.Manager.Views;

/// <summary>
/// Modal file-copy progress dialog opened by <see cref="BruteForceProgressWindow"/> while the RAR
/// reconstructor copies a matched release's files, ported from the WPF
/// <c>ReScene.NET.Views.FileCopyProgressWindow</c>. Its <see cref="Window.DataContext"/> is the same
/// <c>ReconstructorViewModel</c> the owning window uses, so every binding here reads that VM's
/// <c>Copy*</c> progress properties directly. Opened/closed by a
/// <see cref="Helpers.ModalProgressWindowController{TWindow}"/> keyed off <c>IsCopying</c>; the WPF
/// <c>DarkTitleBar.Enable</c> call and the button-feedback half of <c>ProgressWindowLifecycle</c> are
/// dropped, same as the earlier <see cref="ISOProgressWindow"/> port — Cancel simply closes the dialog
/// and the controller's <c>Closed</c> handler turns that into a Stop-command invocation.
/// </summary>
public partial class FileCopyProgressWindow : Window
{
    public FileCopyProgressWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
