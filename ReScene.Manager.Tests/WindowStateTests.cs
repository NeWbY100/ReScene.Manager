using Avalonia;
using ReScene.Manager.Views;

namespace ReScene.Manager.Tests;

/// <summary>
/// Pure unit tests for <see cref="MainWindow.IsRectOnAnyScreen"/> — the on-screen guard that keeps a
/// restored window from being positioned off every display. Tested without a live screen (the window
/// code-behind feeds it real <c>Screens.All</c> bounds at runtime).
/// </summary>
public class WindowStateTests
{
    private static readonly PixelRect s_primary = new(0, 0, 1920, 1080);
    private static readonly PixelRect s_secondary = new(1920, 0, 2560, 1440);

    [Fact]
    public void WindowFullyInsideAScreen_IsOnScreen()
    {
        var rect = new PixelRect(100, 100, 1280, 900);
        Assert.True(MainWindow.IsRectOnAnyScreen([s_primary], rect));
    }

    [Fact]
    public void WindowPartiallyOverlappingAScreen_IsOnScreen()
    {
        // Straddles the top-left corner: mostly off-screen but still overlapping.
        var rect = new PixelRect(-200, -200, 1280, 900);
        Assert.True(MainWindow.IsRectOnAnyScreen([s_primary], rect));
    }

    [Fact]
    public void WindowOnSecondMonitor_IsOnScreen()
    {
        var rect = new PixelRect(2200, 200, 1280, 900);
        Assert.True(MainWindow.IsRectOnAnyScreen([s_primary, s_secondary], rect));
    }

    [Fact]
    public void WindowBeyondEveryScreen_IsOffScreen()
    {
        // Far past the right edge of both monitors (a stale rect from an unplugged display).
        var rect = new PixelRect(9000, 9000, 1280, 900);
        Assert.False(MainWindow.IsRectOnAnyScreen([s_primary, s_secondary], rect));
    }

    [Fact]
    public void NoScreens_IsOffScreen()
    {
        var rect = new PixelRect(0, 0, 1280, 900);
        Assert.False(MainWindow.IsRectOnAnyScreen([], rect));
    }
}
