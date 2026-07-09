using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ReScene.App.Core.Helpers;

namespace ReScene.Manager.Views;

/// <summary>
/// Single-image preview window, ported from the WPF <c>ReScene.NET.Views.ImagePreviewWindow</c>.
/// Its DataContext is a small private <see cref="PreviewData"/> record (image + title + status) and
/// it sizes itself to the image, capped to the screen work area (see <see cref="SizeToImage"/>).
/// </summary>
public partial class ImagePreviewWindow : Window
{
    /// <summary>Parameterless constructor for the XAML loader / designer only.</summary>
    public ImagePreviewWindow() => AvaloniaXamlLoader.Load(this);

    public ImagePreviewWindow(Bitmap image, string fileName, long byteSize)
        : this()
    {
        ArgumentNullException.ThrowIfNull(image);

        PixelSize size = image.PixelSize;
        DataContext = new PreviewData(
            image,
            $"Image Preview — {fileName}",
            $"{fileName}  •  {size.Width}×{size.Height}  •  {FormatUtilities.FormatSize(byteSize)}");

        SizeToImage(size);
    }

    // Fit the window to the image, capped to the working area (with a margin) and the window minimums.
    // In headless / no-screen contexts the screen lookup returns null, so sizing is skipped.
    private void SizeToImage(PixelSize image)
    {
        Screen? screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        PixelRect work = screen.WorkingArea;
        double maxW = Math.Max(work.Width - 80, MinWidth);
        double maxH = Math.Max(work.Height - 120, MinHeight);
        Width = Math.Clamp(image.Width + 40, MinWidth, maxW);
        Height = Math.Clamp(image.Height + 90, MinHeight, maxH);
    }

    private sealed record PreviewData(Bitmap Image, string TitleText, string StatusText);
}
