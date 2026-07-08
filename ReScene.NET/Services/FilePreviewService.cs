using System.Windows;
using System.Windows.Media.Imaging;
using ReScene.App.Core.ViewModels;
using ReScene.NET.Views;

using ReScene.App.Core.Helpers;
using ReScene.App.Core.Services;
namespace ReScene.NET.Services;

/// <summary>
/// Decodes the image (when applicable, via <see cref="IImageLoader"/>) and shows the file's bytes
/// in a <see cref="FilePreviewWindow"/>.
/// </summary>
public class FilePreviewService(IImageLoader imageLoader) : IFilePreviewService
{
    private readonly IImageLoader _imageLoader = imageLoader;

    /// <inheritdoc />
    public void Preview(byte[] data, string fileName)
    {
        BitmapSource? image = ImagePreviewSupport.IsSupported(fileName)
            ? _imageLoader.Load(new MemoryStream(data)) as BitmapSource
            : null;

        var window = new FilePreviewWindow(
            new FilePreviewViewModel(data, fileName, image, image?.PixelWidth, image?.PixelHeight))
        {
            Owner = ActiveWindow()
        };
        window.ShowDialog();
    }

    // The Edit-SRR wizard runs in its own modal window, so the owner must be the active window.
    private static Window? ActiveWindow() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
        ?? Application.Current?.MainWindow;
}
