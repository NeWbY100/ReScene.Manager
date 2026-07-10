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
/// The WPF original opened the preview modally (<c>ShowDialog</c>). Avalonia's <c>ShowDialog</c>
/// returns a <see cref="Task"/> while <see cref="IFilePreviewService.Preview"/> is synchronous
/// (<c>void</c>), so — when there is an owner — the modal dialog is started fire-and-forget
/// (<c>_ = window.ShowDialog(owner)</c>): the owner's input is blocked (the modality invariant that
/// matters) without blocking the calling thread. With no owner (headless) it falls back to
/// <c>Show()</c>, which needs no visible parent.
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
            // Modal (like WPF's ShowDialog), started fire-and-forget so the void contract holds.
            _ = window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }
}
