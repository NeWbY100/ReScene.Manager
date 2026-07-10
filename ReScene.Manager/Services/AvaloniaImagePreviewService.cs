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
/// The WPF original opened the preview modally (<c>ShowDialog</c>). Avalonia's <c>ShowDialog</c>
/// returns a <see cref="Task"/> while <see cref="IImagePreviewService.Preview"/> is synchronous
/// (<c>void</c>), so — when there is an owner — the modal dialog is started fire-and-forget
/// (<c>_ = window.ShowDialog(owner)</c>): the owner's input is blocked (the modality invariant that
/// matters) without blocking the calling thread. With no owner (headless) it falls back to
/// <c>Show()</c>, which needs no visible parent.
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
            // Modal (like WPF's ShowDialog), started fire-and-forget so the void contract holds.
            _ = window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }
}
