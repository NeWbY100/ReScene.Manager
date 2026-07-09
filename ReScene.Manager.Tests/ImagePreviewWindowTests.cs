using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.Manager.Views;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported <see cref="ImagePreviewWindow"/>: zero binding errors, the
/// image and status text render, and <c>SizeToImage</c> is exercised (it runs in the constructor and
/// is guarded against a null screen, so it must not throw in the headless no-screen context).
/// </summary>
public class ImagePreviewWindowTests
{
    [AvaloniaFact]
    public void RendersImageAndStatus_NoBindingErrors()
    {
        Bitmap image = ImageTestData.CreateBitmap(10, 5);

        using var sink = new BindingErrorSink();
        var window = new ImagePreviewWindow(image, "cover.png", 4096);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Image imageControl = window.GetVisualDescendants().OfType<Image>().Single();
        Assert.Same(image, imageControl.Source);

        TextBlock status = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text is not null && t.Text.Contains("cover.png", StringComparison.Ordinal));
        Assert.Contains("10×5", status.Text, StringComparison.Ordinal);

        Assert.Contains("Image Preview", window.Title, StringComparison.Ordinal);
        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void Constructor_SizesToImage_WithoutThrowing()
    {
        Bitmap image = ImageTestData.CreateBitmap(32, 24);

        // The constructor calls SizeToImage; in headless there may be no screen, which the guard
        // handles. Either way, construction must not throw and the window must be usable.
        var window = new ImagePreviewWindow(image, "sample.bmp", 1234);

        Assert.True(window.Width >= window.MinWidth);
        Assert.True(window.Height >= window.MinHeight);
    }

    [AvaloniaFact]
    public void Constructor_NullImage_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new ImagePreviewWindow(null!, "x.png", 1));
}
