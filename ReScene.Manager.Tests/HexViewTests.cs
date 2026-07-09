using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Services;
using ReScene.Manager.Controls;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless tests for the composite <see cref="HexView"/> (scroll host + pinned draggable header +
/// inner drawing surface). The gate is "the chrome composes, forwards its DP surface, and its
/// scroll/culling/auto-scroll math holds", with <b>zero binding errors</b> on render. Live pointer
/// dragging, context-menu popups and real scrollbar interaction are the controller's launch-smoke.
/// </summary>
public class HexViewTests
{
    private static ByteArrayDataSource RampSource(int length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)i;
        }

        return new ByteArrayDataSource(data);
    }

    private static HexViewControl BuildInner(int length = 64, int bytesPerLine = 16) =>
        new()
        {
            DataSource = RampSource(length),
            BlockOffset = 0,
            BlockLength = length,
            BytesPerLine = bytesPerLine,
        };

    [AvaloniaFact]
    public void Composes_AndForwardsDataSurface_ToInner_NoBindingErrors()
    {
        const int length = 256;
        const int bytesPerLine = 16;
        ByteArrayDataSource source = RampSource(length);

        using var sink = new BindingErrorSink();

        var hexView = new HexView
        {
            DataSource = source,
            BlockLength = length,
            BytesPerLine = bytesPerLine,
        };
        var window = new Window { Width = 900, Height = 500, Content = hexView };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The composite realizes the scroll host, the pinned header and the inner drawing surface.
        Assert.True(hexView.GetVisualDescendants().OfType<ScrollViewer>().Any());
        Assert.Single(hexView.GetVisualDescendants().OfType<HexColumnHeader>());
        HexViewControl inner = hexView.GetVisualDescendants().OfType<HexViewControl>().Single();

        // The DP surface flowed down to the inner control.
        Assert.Same(source, inner.DataSource);
        Assert.Equal(length, inner.BlockLength);
        Assert.Equal(bytesPerLine, inner.BytesPerLine);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void Inner_ContextMenu_HasCopyAndSelectAllCommands()
    {
        using var sink = new BindingErrorSink();
        var hexView = new HexView { DataSource = RampSource(64), BlockLength = 64 };
        var window = new Window { Width = 900, Height = 500, Content = hexView };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        HexViewControl inner = hexView.GetVisualDescendants().OfType<HexViewControl>().Single();
        ContextMenu menu = Assert.IsType<ContextMenu>(inner.ContextMenu);

        string?[] headers = [.. menu.Items.OfType<MenuItem>().Select(i => i.Header?.ToString())];
        Assert.Contains("Copy as Hex", headers);
        Assert.Contains("Copy as Text", headers);
        Assert.Contains("Select All", headers);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void GapDrag_WidensAsciiColumn_AndReMeasures()
    {
        HexViewControl inner = BuildInner(length: 256, bytesPerLine: 16);
        inner.Measure(Size.Infinity);

        double asciiBefore = inner.AsciiStartX;
        double widthBefore = inner.DesiredSize.Width;

        // Drive the same state the header's divider drag mutates: enlarge the address→hex gap.
        inner.Gap1 += 40;
        inner.Measure(Size.Infinity);

        Assert.True(inner.AsciiStartX > asciiBefore, "ASCII column start should shift right");
        Assert.True(inner.DesiredSize.Width > widthBefore, "content should re-measure wider");
    }

    [AvaloniaFact]
    public void ViewportCulling_IsOptIn_FullHeightRegardless_AndRenderDoesNotThrow()
    {
        HexViewControl inner = BuildInner(length: 16 * 200, bytesPerLine: 16);

        inner.Measure(Size.Infinity);
        double fullHeight = inner.DesiredSize.Height;
        Assert.Equal(200 * 18d, fullHeight);

        // Opting a small viewport in must not change the desired (full) height...
        inner.ViewportTop = 0;
        inner.ViewportHeight = 40;
        inner.Measure(Size.Infinity);
        Assert.Equal(fullHeight, inner.DesiredSize.Height);

        // ...and culled rendering must still succeed.
        inner.Arrange(new Rect(0, 0, 480, fullHeight));
        using var bitmap = new RenderTargetBitmap(new PixelSize(480, 160), new Vector(96, 96));
        bitmap.Render(inner); // culls to the 40px viewport — must not throw
    }

    [AvaloniaFact]
    public void AutoScroll_Math_ScrollsWhenSelectionOffscreen_NotWhenVisible()
    {
        // 16 bytes/line × 200 lines: selecting the last row (offset 3190 → line 199, targetY 3582)
        // with a 300px viewport at the top scrolls so the row lands ~1/3 down: 3582 - 100 = 3482.
        bool scrolled = HexView.TryComputeAutoScroll(
            selectionOffset: 3190, blockOffset: 0, blockLength: 3200, bytesPerLine: 16,
            currentY: 0, viewportHeight: 300, out double newY);
        Assert.True(scrolled);
        Assert.Equal(3482d, newY);

        // Already visible (viewport already at the row) → no scroll.
        bool visible = HexView.TryComputeAutoScroll(
            selectionOffset: 3190, blockOffset: 0, blockLength: 3200, bytesPerLine: 16,
            currentY: 3480, viewportHeight: 300, out double unchanged);
        Assert.False(visible);
        Assert.Equal(3480d, unchanged);

        // No selection / out-of-block → no scroll.
        Assert.False(HexView.TryComputeAutoScroll(-1, 0, 3200, 16, 0, 300, out _));
        Assert.False(HexView.TryComputeAutoScroll(5000, 0, 3200, 16, 0, 300, out _));
    }

    [AvaloniaFact]
    public void AutoScroll_Live_ScrollsToRevealSelectionNearEnd()
    {
        const int bytesPerLine = 16;
        const int lines = 200;
        const int length = bytesPerLine * lines;

        using var sink = new BindingErrorSink();

        var hexView = new HexView
        {
            DataSource = RampSource(length),
            BlockLength = length,
            BytesPerLine = bytesPerLine,
        };
        var window = new Window { Width = 900, Height = 500, Content = hexView };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        ScrollViewer scroll = hexView.GetVisualDescendants().OfType<ScrollViewer>()
            .First(s => s.GetVisualDescendants().OfType<HexViewControl>().Any());

        Assert.Equal(0d, scroll.Offset.Y); // precondition: at the top

        hexView.SelectionOffset = length - bytesPerLine; // last row
        hexView.SelectionLength = 1;
        Dispatcher.UIThread.RunJobs();

        Assert.True(scroll.Offset.Y > 0, "selecting the last row should scroll the viewport down");
        Assert.Empty(sink.Messages);
    }
}
