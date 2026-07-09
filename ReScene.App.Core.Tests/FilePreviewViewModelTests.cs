using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ReScene.App.Core.ViewModels;

namespace ReScene.App.Core.Tests;

public class FilePreviewViewModelTests
{
    private static WriteableBitmap DummyImage()
        => new(new PixelSize(2, 3), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    [Fact]
    public void NonImage_HasNoImageTab_AndDecodesText()
    {
        byte[] data = Encoding.ASCII.GetBytes("HELLO_NFO");
        var vm = new FilePreviewViewModel(data, "readme.nfo", image: null);

        Assert.False(vm.HasImageTab);
        Assert.Null(vm.Image);
        Assert.Equal(data.Length, vm.HexBlockLength);
        Assert.Equal("UTF-8", vm.SelectedEncoding.DisplayName);
        Assert.Equal("HELLO_NFO", vm.TextViewContent);
        Assert.False(vm.TextViewTruncated);
    }

    [AvaloniaFact]
    public void Image_HasImageTab()
    {
        var vm = new FilePreviewViewModel([0x01, 0x02], "proof.jpg", image: DummyImage());

        Assert.True(vm.HasImageTab);
        Assert.NotNull(vm.Image);
    }

    [Fact]
    public void ChangingEncoding_Redecodes()
    {
        // 0xC9 → CP437 '╔' (U+2554) vs Latin-1 'É' (U+00C9).
        var vm = new FilePreviewViewModel([0xC9], "enc.bin", image: null);

        vm.SelectedEncoding = vm.TextEncodings.First(e => e.DisplayName == "CP437 (DOS)");
        Assert.Contains('╔', vm.TextViewContent);

        vm.SelectedEncoding = vm.TextEncodings.First(e => e.DisplayName == "ISO-8859-1 (Latin-1)");
        Assert.Contains('É', vm.TextViewContent);
        Assert.DoesNotContain('╔', vm.TextViewContent);
    }
}
