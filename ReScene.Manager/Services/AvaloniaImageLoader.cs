using Avalonia.Media.Imaging;
using ReScene.App.Core.Services;

namespace ReScene.Manager.Services;

/// <summary>
/// Avalonia implementation of <see cref="IImageLoader"/>. Decodes to a Skia-backed
/// <see cref="Bitmap"/> that a View can bind straight onto an <c>Image.Source</c>. Mirrors
/// <c>WpfImageLoader</c>'s null-on-failure contract: returns <see langword="null"/> when the bytes
/// are not a decodable image (or the file cannot be read).
/// </summary>
public sealed class AvaloniaImageLoader : IImageLoader
{
    /// <inheritdoc />
    public object? Load(string path)
    {
        try
        {
            return new Bitmap(path);
        }
        catch (Exception)
        {
            // Non-image bytes, unreadable / missing file, or a Skia decode failure — all "not an image".
            return null;
        }
    }

    /// <inheritdoc />
    public object? Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            return new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
