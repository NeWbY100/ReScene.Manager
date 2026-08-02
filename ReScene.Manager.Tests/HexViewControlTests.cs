using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using ReScene.App.Core.Services;
using ReScene.Manager.Controls;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render/behavior tests for <see cref="HexViewControl"/>. The gate for the WPF→Avalonia
/// port of this control is "it draws + core layout/hit-test math holds"; full visual/interaction
/// validation is deferred to Phase 4 (a hosting view drives it via the MCP bridge).
/// </summary>
public class HexViewControlTests
{
    private const int RenderWidth = 480;
    private const int RenderHeight = 160;

    private static HexViewControl BuildControl(int length = 64, int bytesPerLine = 16)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < length; i++)
        {
            data[i] = (byte)i;
        }

        return new HexViewControl
        {
            DataSource = new ByteArrayDataSource(data),
            BlockOffset = 0,
            BlockLength = length,
            BytesPerLine = bytesPerLine,
        };
    }

    private static int CountNonTransparentPixels(RenderTargetBitmap bitmap, int width, int height)
    {
        byte[] buffer = new byte[width * height * 4];
        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), buffer.Length, width * 4);
        }
        finally
        {
            handle.Free();
        }

        int count = 0;
        for (int i = 3; i < buffer.Length; i += 4) // alpha byte of each BGRA pixel
        {
            if (buffer[i] != 0)
            {
                count++;
            }
        }

        return count;
    }

    [AvaloniaFact]
    public void Render_DrawsPixels_AndDesiredHeightScalesWithRowCount()
    {
        HexViewControl control = BuildControl(length: 64, bytesPerLine: 16);

        control.Measure(Size.Infinity);

        // Layout invariant: rows = ceil(64 / 16) = 4, each LineHeight (18px) tall.
        Assert.Equal(4 * 18d, control.DesiredSize.Height);
        Assert.True(control.DesiredSize.Width > 100, "content should be wider than the offset column alone");

        control.Arrange(new Rect(0, 0, RenderWidth, RenderHeight));

        using var bitmap = new RenderTargetBitmap(new PixelSize(RenderWidth, RenderHeight), new Vector(96, 96));
        bitmap.Render(control); // must not throw

        Assert.True(CountNonTransparentPixels(bitmap, RenderWidth, RenderHeight) > 0,
            "rendering hex bytes should paint non-background pixels");
    }

    [AvaloniaFact]
    public void DesiredHeight_TracksBytesPerLine()
    {
        // 64 bytes at 8/line = 8 rows (twice as many as at 16/line), proving the row math is live.
        HexViewControl control = BuildControl(length: 64, bytesPerLine: 8);

        control.Measure(Size.Infinity);

        Assert.Equal(8 * 18d, control.DesiredSize.Height);
    }

    [AvaloniaFact]
    public void HitTestByte_MapsHexAndAsciiPointsToExpectedByteIndex()
    {
        HexViewControl control = BuildControl(length: 64, bytesPerLine: 16);
        control.Measure(Size.Infinity); // forces metric resolution used by the geometry getters

        double charWidth = control.CharWidth;

        // Hex column, second row (y in row 1), the 3rd byte (index 2) of the line.
        double hexX = control.HexStartX + ((3 * 2) + 0.5) * charWidth;
        long hexIndex = control.HitTestByte(new Point(hexX, (1 * 18) + 4), out bool hexIsAscii);
        Assert.Equal((1 * 16) + 2L, hexIndex);
        Assert.False(hexIsAscii);

        // ASCII gutter, first row, the 4th byte (index 3) of the line.
        double asciiX = control.AsciiStartX + (3 + 0.5) * charWidth;
        long asciiIndex = control.HitTestByte(new Point(asciiX, 4), out bool asciiIsAscii);
        Assert.Equal(3L, asciiIndex);
        Assert.True(asciiIsAscii);

        // A point left of the hex column (in the offset column) is not a byte.
        Assert.Equal(-1L, control.HitTestByte(new Point(0, 4), out _));
    }

    [AvaloniaFact]
    public void BytesPerLine_IsCoercedIntoValidRange()
    {
        var control = new HexViewControl();

        control.BytesPerLine = 0;
        Assert.Equal(1, control.BytesPerLine);

        control.BytesPerLine = 999;
        Assert.Equal(128, control.BytesPerLine);

        control.BytesPerLine = 16;
        Assert.Equal(16, control.BytesPerLine);
    }

    [AvaloniaFact]
    public void EmptyBlock_RendersNothing_WithoutThrowing()
    {
        var control = new HexViewControl { BlockLength = 0 };

        control.Measure(Size.Infinity);
        Assert.Equal(1d, control.DesiredSize.Height); // Math.Max(0 rows, 1)

        control.Arrange(new Rect(0, 0, RenderWidth, RenderHeight));

        using var bitmap = new RenderTargetBitmap(new PixelSize(RenderWidth, RenderHeight), new Vector(96, 96));
        bitmap.Render(control); // no data -> early return, must not throw

        Assert.Equal(0, CountNonTransparentPixels(bitmap, RenderWidth, RenderHeight));
    }
}
