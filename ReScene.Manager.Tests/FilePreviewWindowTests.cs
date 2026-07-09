using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Controls;
using ReScene.Manager.Views;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render/behavior tests for the ported <see cref="FilePreviewWindow"/>. The central gate is
/// <b>zero binding errors</b> (via <see cref="BindingErrorSink"/>) when the window renders, plus the
/// three-tab structure and the Image tab tracking <see cref="FilePreviewViewModel.HasImageTab"/>.
/// Live modal/preview interaction is the controller's Phase-4 launch-smoke, not exercised here.
/// </summary>
public class FilePreviewWindowTests
{
    private static byte[] SampleBytes()
    {
        byte[] data = new byte[256];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)i;
        }

        return data;
    }

    private static TabControl Tabs(FilePreviewWindow window) =>
        window.GetVisualDescendants().OfType<TabControl>().Single();

    [AvaloniaFact]
    public void WithImage_RendersThreeTabs_ImageTabVisible_NoBindingErrors()
    {
        Bitmap image = ImageTestData.CreateBitmap(8, 6);
        var vm = new FilePreviewViewModel(SampleBytes(), "poster.png", image, image.PixelSize.Width, image.PixelSize.Height);
        Assert.True(vm.HasImageTab); // precondition: image decoded

        using var sink = new BindingErrorSink();
        var window = new FilePreviewWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TabControl tabs = Tabs(window);
        Assert.Equal(3, tabs.Items.Count);
        Assert.Equal(["Hex", "Text", "Image"], tabs.Items.OfType<TabItem>().Select(t => t.Header?.ToString()));

        var imageTab = (TabItem)tabs.Items[2]!;
        Assert.True(imageTab.IsVisible); // tracks HasImageTab == true

        // Hex tab is the default selection, so its HexViewControl is realized.
        Assert.Single(window.GetVisualDescendants().OfType<HexViewControl>());

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void WithoutImage_ImageTabHidden_NoBindingErrors()
    {
        var vm = new FilePreviewViewModel(SampleBytes(), "readme.txt", image: null);
        Assert.False(vm.HasImageTab); // precondition: no image

        using var sink = new BindingErrorSink();
        var window = new FilePreviewWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TabControl tabs = Tabs(window);
        Assert.Equal(3, tabs.Items.Count);

        var imageTab = (TabItem)tabs.Items[2]!;
        Assert.False(imageTab.IsVisible); // tracks HasImageTab == false

        Assert.Empty(sink.Messages);
    }
}
