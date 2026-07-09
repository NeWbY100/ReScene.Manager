using Avalonia.Controls;
using Avalonia.Media.Imaging;
using ReScene.App.Core.Services;
using ReScene.Manager.Views;

namespace ReScene.Manager.Services;

/// <summary>
/// Avalonia implementation of <see cref="IImagePreviewService"/>. Decodes the bytes via
/// <see cref="IImageLoader"/> and shows them in an <see cref="ImagePreviewWindow"/>; a decode failure
/// is reported through <see cref="IFileDialogService.ShowError"/> and nothing is opened.
/// </summary>
/// <remarks>
/// Shown modeless via <c>Show(owner)</c>: the <see cref="IImagePreviewService.Preview"/> contract is
/// synchronous (<c>void</c>) while Avalonia's <c>ShowDialog</c> is async-only.
/// </remarks>
public sealed class AvaloniaImagePreviewService : IImagePreviewService
{
    private readonly IImageLoader _imageLoader;
    private readonly IFileDialogService _fileDialog;
    private readonly Func<Window?> _owner;

    /// <param name="imageLoader">Decodes the image bytes to an Avalonia <see cref="Bitmap"/>.</param>
    /// <param name="fileDialog">Reports decode failures via <see cref="IFileDialogService.ShowError"/>.</param>
    /// <param name="owner">Resolves the window that owns the preview; may return <see langword="null"/>.</param>
    public AvaloniaImagePreviewService(IImageLoader imageLoader, IFileDialogService fileDialog, Func<Window?> owner)
    {
        ArgumentNullException.ThrowIfNull(imageLoader);
        ArgumentNullException.ThrowIfNull(fileDialog);
        ArgumentNullException.ThrowIfNull(owner);
        _imageLoader = imageLoader;
        _fileDialog = fileDialog;
        _owner = owner;
    }

    /// <inheritdoc />
    public void Preview(byte[] data, string fileName)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (_imageLoader.Load(new MemoryStream(data)) is not Bitmap image)
        {
            _fileDialog.ShowError("Could not display image",
                $"\"{fileName}\" could not be decoded as an image.");
            return;
        }

        var window = new ImagePreviewWindow(image, fileName, data.Length);

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
