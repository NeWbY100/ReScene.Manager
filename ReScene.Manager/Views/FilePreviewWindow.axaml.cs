using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ReScene.App.Core.ViewModels;

namespace ReScene.Manager.Views;

/// <summary>
/// Tabbed file-preview window (Hex / Text / Image), ported from the WPF
/// <c>ReScene.NET.Views.FilePreviewWindow</c>. Bound to a <see cref="FilePreviewViewModel"/>: the
/// Image tab is collapsed unless the bytes decoded as an image (<see cref="FilePreviewViewModel.HasImageTab"/>).
/// </summary>
public partial class FilePreviewWindow : Window
{
    /// <summary>Parameterless constructor for the XAML loader / designer only.</summary>
    public FilePreviewWindow()
        : this(new FilePreviewViewModel([], string.Empty, null))
    {
    }

    public FilePreviewWindow(FilePreviewViewModel viewModel)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
    }
}
