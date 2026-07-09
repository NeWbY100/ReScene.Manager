using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ReScene.App.Core.Helpers;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Views;

namespace ReScene.Manager.Services;

/// <summary>
/// Avalonia implementation of <see cref="IFilePreviewService"/>. Decodes the bytes as an image only
/// when the file name has a previewable image extension (via <see cref="ImagePreviewSupport"/>), then
/// opens a <see cref="FilePreviewWindow"/>.
/// </summary>
/// <remarks>
/// The <see cref="IFilePreviewService.Preview"/> contract is synchronous (<c>void</c>) but Avalonia's
/// <c>ShowDialog</c> is async-only, so the preview is shown modeless via <c>Show(owner)</c> — the
/// correct equivalent of the WPF modal <c>ShowDialog</c> here.
/// </remarks>
public sealed class AvaloniaFilePreviewService : IFilePreviewService
{
    private readonly IImageLoader _imageLoader;
    private readonly Func<Window?> _owner;

    /// <param name="imageLoader">Decodes previewable image bytes to an Avalonia <see cref="Bitmap"/>.</param>
    /// <param name="owner">Resolves the window that owns the preview; may return <see langword="null"/>.</param>
    public AvaloniaFilePreviewService(IImageLoader imageLoader, Func<Window?> owner)
    {
        ArgumentNullException.ThrowIfNull(imageLoader);
        ArgumentNullException.ThrowIfNull(owner);
        _imageLoader = imageLoader;
        _owner = owner;
    }

    /// <inheritdoc />
    public void Preview(byte[] data, string fileName)
    {
        ArgumentNullException.ThrowIfNull(data);

        Bitmap? image = ImagePreviewSupport.IsSupported(fileName)
            ? _imageLoader.Load(new MemoryStream(data)) as Bitmap
            : null;

        var window = new FilePreviewWindow(
            new FilePreviewViewModel(data, fileName, image, image?.PixelSize.Width, image?.PixelSize.Height));

        Window? owner = _owner();
        if (owner is not null)
        {
            window.Show(owner);
        }
        else
        {
            window.Show();
        }
    }
}
