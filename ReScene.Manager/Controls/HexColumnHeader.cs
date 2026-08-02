using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace ReScene.Manager.Controls;

/// <summary>
/// Pinned column header for the composite <see cref="HexView"/>, ported from the private WPF
/// <c>HexColumnHeader</c>. Draws the <c>"Offset"</c> label, the byte-index ruler (<c>00 01 02 …</c>)
/// and the <c>"ASCII"</c> label aligned to the inner <see cref="HexViewControl"/>'s columns, plus two
/// draggable divider lines that resize the inner control's <see cref="HexViewControl.Gap1"/>/
/// <see cref="HexViewControl.Gap2"/> gaps. It is a thin view over the inner control: all geometry
/// (<see cref="HexViewControl.HexStartX"/>, <see cref="HexViewControl.AsciiStartX"/>, char width and
/// gaps) is read from <see cref="Inner"/>, which <see cref="HexView"/> assigns after load.
/// </summary>
internal sealed class HexColumnHeader : Control
{
    private const double DefaultFontSize = 12;

    // Pointer must be within this many DIPs of a divider to grab it (matches the WPF header).
    private const double HitTolerance = 4;

    // Visual nudge so a divider line sits just left of the column it precedes, capped so a small gap
    // does not push the line past the previous column.
    private const double DividerVisualInset = 3;

    // Vertical baseline padding for the header text (matches the drawing surface's "+ 2").
    private const double TextTopPadding = 2;

    private static readonly FontFamily _fallbackMonoFont =
        new("Cascadia Mono, Consolas, Courier New, monospace");

    private static readonly Cursor _resizeCursor = new(StandardCursorType.SizeWestEast);

    // 0 = not dragging, 1 = dragging divider 1 (Gap1), 2 = dragging divider 2 (Gap2).
    private int _dragIndex;
    private double _dragStartX;
    private double _dragStartGap;

    /// <summary>
    /// Byte count per row, forwarded from the composite. Marked <see cref="Visual.AffectsRender{T}"/>
    /// so the ruler repaints when it changes; the divider/label positions are read live from
    /// <see cref="Inner"/>.
    /// </summary>
    internal static readonly StyledProperty<int> BytesPerLineProperty =
        AvaloniaProperty.Register<HexColumnHeader, int>(nameof(BytesPerLine), 16);

    static HexColumnHeader()
    {
        AffectsRender<HexColumnHeader>(BytesPerLineProperty);
    }

    /// <summary>The inner drawing surface this header aligns to, assigned by <see cref="HexView"/>.</summary>
    internal HexViewControl? Inner
    {
        get;
        set;
    }

    internal int BytesPerLine
    {
        get => GetValue(BytesPerLineProperty);
        set => SetValue(BytesPerLineProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        HexViewControl? inner = Inner;
        if (inner is null)
        {
            return;
        }

        IBrush labelBrush = GetBrush("ForegroundSecondary", Brushes.Gray);
        (Typeface typeface, double fontSize) = ResolveFont();

        var offsetText = new FormattedText("Offset", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, fontSize, labelBrush);
        context.DrawText(offsetText, new Point(0, TextTopPadding));

        int bytesPerLine = BytesPerLine;
        var ruler = new StringBuilder(bytesPerLine * 3);
        for (int i = 0; i < bytesPerLine; i++)
        {
            if (i > 0)
            {
                ruler.Append(' ');
            }

            ruler.Append(i.ToString("X2", CultureInfo.InvariantCulture));
        }

        var rulerText = new FormattedText(ruler.ToString(), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, fontSize, labelBrush);
        context.DrawText(rulerText, new Point(inner.HexStartX, TextTopPadding));

        var asciiText = new FormattedText("ASCII", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, fontSize, labelBrush);
        context.DrawText(asciiText, new Point(inner.AsciiStartX, TextTopPadding));

        IBrush dividerBrush = GetBrush("BorderMedium", Brushes.Gray);
        var dividerPen = new Pen(dividerBrush, 1);
        double height = Bounds.Height;
        double line1X = inner.HexStartX - Math.Min(DividerVisualInset, inner.Gap1 / 2);
        double line2X = inner.AsciiStartX - Math.Min(DividerVisualInset, inner.Gap2 / 2);
        context.DrawLine(dividerPen, new Point(line1X, 0), new Point(line1X, height));
        context.DrawLine(dividerPen, new Point(line2X, 0), new Point(line2X, height));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        HexViewControl? inner = Inner;
        if (inner is null)
        {
            return;
        }

        Point pos = e.GetPosition(this);

        if (_dragIndex != 0)
        {
            double newGap = _dragStartGap + (pos.X - _dragStartX);
            if (_dragIndex == 1)
            {
                inner.Gap1 = newGap;
            }
            else
            {
                inner.Gap2 = newGap;
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        bool nearDivider = Math.Abs(pos.X - inner.HexStartX) <= HitTolerance
            || Math.Abs(pos.X - inner.AsciiStartX) <= HitTolerance;
        Cursor = nearDivider ? _resizeCursor : Cursor.Default;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        HexViewControl? inner = Inner;
        if (inner is null)
        {
            return;
        }

        PointerPoint point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        double posX = point.Position.X;
        if (Math.Abs(posX - inner.HexStartX) <= HitTolerance)
        {
            BeginDrag(e, dragIndex: 1, posX, inner.Gap1);
        }
        else if (Math.Abs(posX - inner.AsciiStartX) <= HitTolerance)
        {
            BeginDrag(e, dragIndex: 2, posX, inner.Gap2);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_dragIndex != 0)
        {
            _dragIndex = 0;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void BeginDrag(PointerPressedEventArgs e, int dragIndex, double posX, double startGap)
    {
        _dragIndex = dragIndex;
        _dragStartX = posX;
        _dragStartGap = startGap;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private (Typeface Typeface, double FontSize) ResolveFont()
    {
        FontFamily font = this.TryFindResource("MonoFontFamily", out object? fontResource) && fontResource is FontFamily ff
            ? ff
            : _fallbackMonoFont;
        double size = this.TryFindResource("MonoFontSize", out object? sizeResource) && sizeResource is double d
            ? d
            : DefaultFontSize;
        return (new Typeface(font), size);
    }

    private IBrush GetBrush(string resourceKey, IBrush fallback) =>
        this.TryFindResource(resourceKey, out object? resource) && resource is IBrush brush ? brush : fallback;
}
