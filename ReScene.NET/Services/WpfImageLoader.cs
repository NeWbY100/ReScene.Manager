using System.Windows.Media.Imaging;
using ReScene.NET.Helpers;

using ReScene.App.Core.Services;
namespace ReScene.NET.Services;

/// <summary>
/// WPF implementation of <see cref="IImageLoader"/>. Decodes to a frozen <see cref="BitmapSource"/>
/// (reusing <see cref="ImageDecoder"/>) that a View can bind straight onto an <c>Image.Source</c>.
/// Returns <see langword="null"/> when the bytes are not a decodable image.
/// </summary>
public sealed class WpfImageLoader : IImageLoader
{
    /// <inheritdoc />
    public object? Load(string path)
    {
        try
        {
            return ImageDecoder.TryDecode(File.ReadAllBytes(path));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public object? Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return ImageDecoder.TryDecode(buffer.ToArray());
    }
}
