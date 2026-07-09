using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ReScene.Hex;

namespace ReScene.Manager.Controls;

/// <summary>
/// Custom-drawn hex viewer, ported from the WPF <c>ReScene.NET.Controls.HexViewControl</c>.
/// Draws an offset column, a hex-bytes column and an ASCII gutter for the block
/// <c>[BlockOffset, BlockOffset + BlockLength)</c> of a <see cref="IHexDataSource"/> via
/// <see cref="Render"/>/<see cref="FormattedText"/>, and supports pointer drag-selection,
/// keyboard navigation (Ctrl+A select-all, Ctrl+C copy) and selection/match/diff highlighting.
/// </summary>
/// <remarks>
/// This is the drawing surface only. In the WPF original the control was a composite
/// <c>UserControl</c> that wrapped this canvas in a <c>ScrollViewer</c> and pinned a draggable
/// column header on top; that scrolling/header chrome is a composition concern supplied by the
/// hosting view (Phase 4). Because there is no owning <c>ScrollViewer</c> here, the control sizes
/// itself to the full content via <see cref="MeasureOverride"/> (a host <c>ScrollViewer</c> scrolls
/// it) and draws every row rather than culling to a viewport.
/// </remarks>
public class HexViewControl : Control
{
    // Row height in DIPs. Exposed to the composite HexView (same assembly) so it can compute the
    // scroll-to-selection target and viewport math against the exact same value.
    internal const double LineHeight = 18;
    private const double DefaultFontSize = 12;
    private const double ContentRightMargin = 20;
    private const int MaxCopyBytes = 10 * 1024 * 1024; // 10 MB

    // Used only when the MonoFontFamily resource cannot be resolved (e.g. control not yet attached).
    private static readonly FontFamily _fallbackMonoFont =
        new("Cascadia Mono, Consolas, Courier New, monospace");

    private Typeface _typeface;
    private double _fontSize = DefaultFontSize;
    private double _charWidth;
    private double _addressWidth;

    // Column gaps (address→hex, hex→ASCII), draggable via the composite's HexColumnHeader.
    // NaN means "auto" — resolved to _charWidth on read (EffectiveGap1/2). Kept as NaN sentinels
    // (rather than being seeded in EnsureMetrics) so a user/header-set gap survives a metrics
    // recompute (e.g. on re-attach) while an unset gap keeps tracking the current char width.
    private double _gap1 = double.NaN;
    private double _gap2 = double.NaN;
    private bool _metricsValid;

    private byte[] _lineBuffer = new byte[16];

    private long _mouseSelAnchor = -1;
    private long _mouseSelCurrent = -1;
    private bool _isMouseSelecting;
    private bool _isAsciiAreaSelection;

    public static readonly StyledProperty<IHexDataSource?> DataSourceProperty =
        AvaloniaProperty.Register<HexViewControl, IHexDataSource?>(nameof(DataSource));

    public static readonly StyledProperty<long> BlockOffsetProperty =
        AvaloniaProperty.Register<HexViewControl, long>(nameof(BlockOffset));

    public static readonly StyledProperty<long> BlockLengthProperty =
        AvaloniaProperty.Register<HexViewControl, long>(nameof(BlockLength));

    public static readonly StyledProperty<long> SelectionOffsetProperty =
        AvaloniaProperty.Register<HexViewControl, long>(nameof(SelectionOffset), -1L);

    public static readonly StyledProperty<long> SelectionLengthProperty =
        AvaloniaProperty.Register<HexViewControl, long>(nameof(SelectionLength));

    public static readonly StyledProperty<int> BytesPerLineProperty =
        AvaloniaProperty.Register<HexViewControl, int>(nameof(BytesPerLine), 16, coerce: CoerceBytesPerLine);

    public static readonly StyledProperty<IReadOnlyList<HexMatchRange>?> HighlightRangesProperty =
        AvaloniaProperty.Register<HexViewControl, IReadOnlyList<HexMatchRange>?>(nameof(HighlightRanges));

    public static readonly StyledProperty<IReadOnlyList<HexMatchRange>?> DiffRangesProperty =
        AvaloniaProperty.Register<HexViewControl, IReadOnlyList<HexMatchRange>?>(nameof(DiffRanges));

    // Viewport window pushed in by the host ScrollViewer (the composite HexView) so Render can cull
    // to the visible rows. Left at 0 for headless/standalone use, where every row is drawn (which
    // keeps the drawing-surface tests, that never set a viewport, rendering all content).
    internal static readonly StyledProperty<double> ViewportTopProperty =
        AvaloniaProperty.Register<HexViewControl, double>(nameof(ViewportTop));

    internal static readonly StyledProperty<double> ViewportHeightProperty =
        AvaloniaProperty.Register<HexViewControl, double>(nameof(ViewportHeight));

    static HexViewControl()
    {
        // A visual change on any of these forces a repaint...
        AffectsRender<HexViewControl>(
            DataSourceProperty, BlockOffsetProperty, BlockLengthProperty,
            SelectionOffsetProperty, SelectionLengthProperty, BytesPerLineProperty,
            HighlightRangesProperty, DiffRangesProperty,
            ViewportTopProperty, ViewportHeightProperty);

        // ...while these two also change the control's desired size (row count / column widths).
        AffectsMeasure<HexViewControl>(BlockLengthProperty, BytesPerLineProperty);
    }

    public HexViewControl()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        _typeface = new Typeface(_fallbackMonoFont);
        ContextMenu = BuildContextMenu();
    }

    /// <summary>
    /// Builds the right-click menu (Copy as Hex / Copy as Text / Select All), mirroring the WPF
    /// original. The two copy items are enabled only while there is a non-empty active selection,
    /// re-evaluated each time the menu opens.
    /// </summary>
    private ContextMenu BuildContextMenu()
    {
        var copyHex = new MenuItem { Header = "Copy as Hex" };
        copyHex.Click += (_, _) => CopyToClipboard(asText: false);

        var copyText = new MenuItem { Header = "Copy as Text" };
        copyText.Click += (_, _) => CopyToClipboard(asText: true);

        var selectAll = new MenuItem { Header = "Select All" };
        selectAll.Click += (_, _) => SelectAll();

        var menu = new ContextMenu();
        menu.Items.Add(copyHex);
        menu.Items.Add(copyText);
        menu.Items.Add(new Separator());
        menu.Items.Add(selectAll);

        menu.Opened += (_, _) =>
        {
            GetActiveSelection(out long selStart, out long selLength);
            bool hasSelection = selStart >= 0 && selLength > 0;
            copyHex.IsEnabled = hasSelection;
            copyText.IsEnabled = hasSelection;
        };

        return menu;
    }

    public IHexDataSource? DataSource
    {
        get => GetValue(DataSourceProperty);
        set => SetValue(DataSourceProperty, value);
    }

    public long BlockOffset
    {
        get => GetValue(BlockOffsetProperty);
        set => SetValue(BlockOffsetProperty, value);
    }

    public long BlockLength
    {
        get => GetValue(BlockLengthProperty);
        set => SetValue(BlockLengthProperty, value);
    }

    public long SelectionOffset
    {
        get => GetValue(SelectionOffsetProperty);
        set => SetValue(SelectionOffsetProperty, value);
    }

    public long SelectionLength
    {
        get => GetValue(SelectionLengthProperty);
        set => SetValue(SelectionLengthProperty, value);
    }

    public int BytesPerLine
    {
        get => GetValue(BytesPerLineProperty);
        set => SetValue(BytesPerLineProperty, value);
    }

    public IReadOnlyList<HexMatchRange>? HighlightRanges
    {
        get => GetValue(HighlightRangesProperty);
        set => SetValue(HighlightRangesProperty, value);
    }

    public IReadOnlyList<HexMatchRange>? DiffRanges
    {
        get => GetValue(DiffRangesProperty);
        set => SetValue(DiffRangesProperty, value);
    }

    /// <summary>
    /// Top of the visible viewport (the host <c>ScrollViewer</c>'s vertical offset), in DIPs. Only
    /// consulted for row culling when <see cref="ViewportHeight"/> is positive.
    /// </summary>
    internal double ViewportTop
    {
        get => GetValue(ViewportTopProperty);
        set => SetValue(ViewportTopProperty, value);
    }

    /// <summary>
    /// Height of the visible viewport, in DIPs. Zero (the default) disables culling so every row is
    /// drawn; a positive value restricts <see cref="Render"/> to the visible rows (± one row).
    /// </summary>
    internal double ViewportHeight
    {
        get => GetValue(ViewportHeightProperty);
        set => SetValue(ViewportHeightProperty, value);
    }

    private long MouseSelStart => _mouseSelAnchor < 0 ? -1 : Math.Min(_mouseSelAnchor, _mouseSelCurrent);
    private long MouseSelEnd => _mouseSelAnchor < 0 ? -1 : Math.Max(_mouseSelAnchor, _mouseSelCurrent);
    private long MouseSelLength => _mouseSelAnchor < 0 ? 0 : MouseSelEnd - MouseSelStart + 1;

    // Width of one monospace glyph, resolved from the mono font/size (exposed for tests).
    internal double CharWidth
    {
        get
        {
            EnsureMetrics();
            return _charWidth;
        }
    }

    private double HexWidth => (BytesPerLine * 3 - 1) * _charWidth;
    private double AsciiWidth => BytesPerLine * _charWidth;

    // NaN gap → "auto" → current char width.
    private double EffectiveGap1 => double.IsNaN(_gap1) ? _charWidth : _gap1;
    private double EffectiveGap2 => double.IsNaN(_gap2) ? _charWidth : _gap2;

    /// <summary>
    /// Gap between the address column and the hex column, in DIPs. Assigning a value clamps it to a
    /// floor of <c>0.5 * CharWidth</c> (the single authoritative clamp — the header relies on it) and
    /// re-measures/re-paints. Reads resolve the "auto" default to the current char width.
    /// </summary>
    internal double Gap1
    {
        get
        {
            EnsureMetrics();
            return EffectiveGap1;
        }
        set => SetGap(ref _gap1, value);
    }

    /// <summary>
    /// Gap between the hex column and the ASCII gutter, in DIPs. See <see cref="Gap1"/> for clamp and
    /// invalidation semantics.
    /// </summary>
    internal double Gap2
    {
        get
        {
            EnsureMetrics();
            return EffectiveGap2;
        }
        set => SetGap(ref _gap2, value);
    }

    internal double HexStartX
    {
        get
        {
            EnsureMetrics();
            return _addressWidth + EffectiveGap1;
        }
    }

    internal double AsciiStartX
    {
        get
        {
            EnsureMetrics();
            return _addressWidth + EffectiveGap1 + HexWidth + EffectiveGap2;
        }
    }

    private void SetGap(ref double field, double value)
    {
        EnsureMetrics();
        double clamped = Math.Max(0.5 * _charWidth, value);
        if (field.Equals(clamped))
        {
            return;
        }

        field = clamped;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private double TotalContentWidth => AsciiStartX + AsciiWidth + ContentRightMargin;

    private static int CoerceBytesPerLine(AvaloniaObject sender, int value) => Math.Clamp(value, 1, 128);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Replacing the displayed block, or programmatically moving the primary selection, invalidates
        // any in-progress mouse selection (absolute offsets against the old block/anchor). Clear it so
        // Copy/Ctrl+C can't grab bytes from the new slice at a meaningless offset. Repainting is already
        // handled by AffectsRender; this override only carries the side effect.
        if (change.Property == DataSourceProperty
            || change.Property == BlockOffsetProperty
            || change.Property == BlockLengthProperty
            || change.Property == SelectionOffsetProperty)
        {
            ClearMouseSelection();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Metrics may have been computed with fallback font values while detached; recompute against
        // the real MonoFontFamily/MonoFontSize resources now that the resource host chain is reachable.
        _metricsValid = false;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureMetrics();

        int bytesPerLine = BytesPerLine;
        long blockLen = BlockLength;
        long lineCount = blockLen > 0 ? (blockLen + bytesPerLine - 1) / bytesPerLine : 0;
        double height = Math.Max(lineCount * LineHeight, 1);
        return new Size(TotalContentWidth, height);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        IHexDataSource? source = DataSource;
        long blockLen = BlockLength;
        if (source is null || blockLen <= 0)
        {
            return;
        }

        EnsureMetrics();

        IBrush addressBrush = GetBrush("HexOffsetForeground", Brushes.Gray);
        IBrush hexBrush = GetBrush("HexBytesForeground", Brushes.Black);
        IBrush asciiBrush = GetBrush("HexAsciiForeground", Brushes.DimGray);
        IBrush selectionBrush = GetBrush("HexSelectionBrush", new SolidColorBrush(Color.FromArgb(120, 60, 120, 220)));
        IBrush matchBrush = GetBrush("HexMatchHighlightBrush", new SolidColorBrush(Color.FromArgb(80, 240, 140, 0)));
        IBrush diffBrush = GetBrush("HexDiffHighlightBrush", new SolidColorBrush(Color.FromArgb(85, 244, 71, 71)));

        long blockStart = BlockOffset;
        int bytesPerLine = BytesPerLine;

        // Ensure line buffer is large enough.
        if (_lineBuffer.Length < bytesPerLine)
        {
            _lineBuffer = new byte[bytesPerLine];
        }

        long selStart;
        long selLen;
        if (_mouseSelAnchor >= 0)
        {
            selStart = MouseSelStart;
            selLen = MouseSelLength;
        }
        else
        {
            selStart = SelectionOffset;
            selLen = SelectionLength;
        }

        long totalLines = (blockLen + bytesPerLine - 1) / bytesPerLine;
        double hexStartX = HexStartX;
        double asciiStartX = AsciiStartX;

        // Viewport culling. When the host ScrollViewer has pushed a viewport in (ViewportHeight > 0),
        // draw only the rows overlapping [ViewportTop, ViewportTop + ViewportHeight] plus one row of
        // overscan on each side — the exact bounds the WPF HexCanvas used. When no viewport is set
        // (headless / standalone drawing-surface use), draw every row.
        long firstVisible = 0;
        long lastVisible = totalLines - 1;
        double viewportHeight = ViewportHeight;
        if (viewportHeight > 0)
        {
            double viewportTop = ViewportTop;
            firstVisible = Math.Max(0, (long)(viewportTop / LineHeight) - 1);
            lastVisible = Math.Min(totalLines - 1, (long)((viewportTop + viewportHeight) / LineHeight) + 1);
        }

        IReadOnlyList<HexMatchRange>? highlightRanges = HighlightRanges;
        IReadOnlyList<HexMatchRange>? diffRanges = DiffRanges;

        for (long line = firstVisible; line <= lastVisible; line++)
        {
            double y = line * LineHeight;
            long lineFileOffset = blockStart + line * bytesPerLine;
            long lineDataStart = line * bytesPerLine;
            int lineBytes = (int)Math.Min(bytesPerLine, blockLen - lineDataStart);

            // Diff highlight (drawn first so search-match and selection layers stack on top).
            if (diffRanges is { Count: > 0 })
            {
                foreach (HexMatchRange range in diffRanges)
                {
                    if (TryClampRangeToLine(range.Offset, range.Offset + range.Length,
                            lineFileOffset, bytesPerLine, out int dStart, out int dEnd))
                    {
                        PaintRangeOnLine(context, diffBrush, y, dStart, dEnd);
                    }
                }
            }

            // All-matches highlight (drawn before the primary selection so the
            // current match's brighter color renders on top).
            if (highlightRanges is { Count: > 0 })
            {
                foreach (HexMatchRange range in highlightRanges)
                {
                    if (TryClampRangeToLine(range.Offset, range.Offset + range.Length,
                            lineFileOffset, bytesPerLine, out int hStart, out int hEnd))
                    {
                        PaintRangeOnLine(context, matchBrush, y, hStart, hEnd);
                    }
                }
            }

            // Selection highlight.
            if (selStart >= 0 && selLen > 0
                && TryClampRangeToLine(selStart, selStart + selLen,
                    lineFileOffset, bytesPerLine, out int highlightStart, out int highlightEnd))
            {
                PaintRangeOnLine(context, selectionBrush, y, highlightStart, highlightEnd);
            }

            // Address column.
            string addr = lineFileOffset.ToString("X8", CultureInfo.InvariantCulture);
            var addrText = new FormattedText(addr, CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, _typeface, _fontSize, addressBrush);
            context.DrawText(addrText, new Point(0, y + 2));

            // Read this line's bytes from the data source.
            int read = source.Read(lineDataStart, _lineBuffer, 0, lineBytes);
            if (read <= 0)
            {
                continue;
            }

            var hexBuilder = new StringBuilder(bytesPerLine * 3);
            var asciiBuilder = new StringBuilder(bytesPerLine);

            for (int i = 0; i < read; i++)
            {
                byte b = _lineBuffer[i];
                if (i > 0)
                {
                    hexBuilder.Append(' ');
                }

                hexBuilder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                asciiBuilder.Append(b is >= 0x20 and <= 0x7E ? (char)b : '.');
            }

            var hexText = new FormattedText(hexBuilder.ToString(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, _typeface, _fontSize, hexBrush);
            context.DrawText(hexText, new Point(hexStartX, y + 2));

            var asciiText = new FormattedText(asciiBuilder.ToString(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, _typeface, _fontSize, asciiBrush);
            context.DrawText(asciiText, new Point(asciiStartX, y + 2));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        PointerPoint point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        long byteOffset = HitTestByte(point.Position, out bool isAscii);

        if (byteOffset >= 0)
        {
            _mouseSelAnchor = byteOffset;
            _mouseSelCurrent = byteOffset;
            _isMouseSelecting = true;
            _isAsciiAreaSelection = isAscii;
            e.Pointer.Capture(this);
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isMouseSelecting)
        {
            return;
        }

        long byteOffset = HitTestByte(e.GetCurrentPoint(this).Position, out _);
        if (byteOffset >= 0 && byteOffset != _mouseSelCurrent)
        {
            _mouseSelCurrent = byteOffset;
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isMouseSelecting)
        {
            _isMouseSelecting = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyModifiers == KeyModifiers.Control)
        {
            if (e.Key == Key.C)
            {
                CopyToClipboard(asText: _isAsciiAreaSelection);
                e.Handled = true;
            }
            else if (e.Key == Key.A)
            {
                SelectAll();
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Maps a point in control space to the absolute byte offset under it, or <c>-1</c> if the point
    /// is outside the hex/ASCII columns or past the end of the block. <paramref name="isAsciiArea"/>
    /// reports whether the hit landed in the ASCII gutter (drives Ctrl+C copy-as-text).
    /// </summary>
    internal long HitTestByte(Point pos, out bool isAsciiArea)
    {
        isAsciiArea = false;

        long blockLen = BlockLength;
        if (blockLen <= 0)
        {
            return -1;
        }

        EnsureMetrics();

        int bytesPerLine = BytesPerLine;

        long line = (long)(pos.Y / LineHeight);
        long totalLines = (blockLen + bytesPerLine - 1) / bytesPerLine;
        if (line < 0 || line >= totalLines)
        {
            return -1;
        }

        double hexStartX = HexStartX;
        double hexEndX = hexStartX + HexWidth;
        double asciiStartX = AsciiStartX;
        double asciiEndX = asciiStartX + AsciiWidth;

        int byteInLine;

        if (pos.X >= hexStartX && pos.X < hexEndX)
        {
            byteInLine = (int)((pos.X - hexStartX) / (3 * _charWidth));
            byteInLine = Math.Clamp(byteInLine, 0, bytesPerLine - 1);
        }
        else if (pos.X >= asciiStartX && pos.X <= asciiEndX)
        {
            byteInLine = (int)((pos.X - asciiStartX) / _charWidth);
            byteInLine = Math.Clamp(byteInLine, 0, bytesPerLine - 1);
            isAsciiArea = true;
        }
        else
        {
            return -1;
        }

        long lineOffset = line * bytesPerLine + byteInLine;
        if (lineOffset >= blockLen)
        {
            return -1;
        }

        return BlockOffset + lineOffset;
    }

    private void ClearMouseSelection()
    {
        _mouseSelAnchor = -1;
        _mouseSelCurrent = -1;
        _isMouseSelecting = false;
    }

    private void SelectAll()
    {
        if (BlockLength > 0)
        {
            _mouseSelAnchor = BlockOffset;
            _mouseSelCurrent = BlockOffset + BlockLength - 1;
            _isAsciiAreaSelection = false;
            InvalidateVisual();
        }
    }

    private void GetActiveSelection(out long selStart, out long selLength)
    {
        selStart = MouseSelStart;
        selLength = MouseSelLength;

        if (selStart < 0 || selLength <= 0)
        {
            selStart = SelectionOffset;
            selLength = SelectionLength;
        }
    }

    private void CopyToClipboard(bool asText)
    {
        GetActiveSelection(out long selStart, out long selLength);

        IHexDataSource? source = DataSource;
        if (selStart < 0 || selLength <= 0 || source is null)
        {
            return;
        }

        long blockOffset = BlockOffset;
        long relStart = selStart - blockOffset;
        long len = selLength;
        if (relStart < 0)
        {
            // Selection begins before the block: drop the clipped leading bytes from the
            // length too, otherwise we'd read past the intended end of the selection.
            len += relStart;
            relStart = 0;
        }

        len = Math.Min(len, BlockLength - relStart);
        if (len <= 0)
        {
            return;
        }

        int copyLen = (int)Math.Min(len, MaxCopyBytes);
        byte[] buf = new byte[copyLen];
        int read = source.Read(relStart, buf, 0, copyLen);
        if (read <= 0)
        {
            return;
        }

        string text;
        if (asText)
        {
            var sb = new StringBuilder(read);
            for (int i = 0; i < read; i++)
            {
                byte b = buf[i];
                sb.Append(b is >= 0x20 and <= 0x7E ? (char)b : '.');
            }

            text = sb.ToString();
        }
        else
        {
            var sb = new StringBuilder(read * 3);
            for (int i = 0; i < read; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(buf[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            text = sb.ToString();
        }

        // Clipboard requires a hosting TopLevel; guarded so a detached/headless control is a no-op.
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
        {
            _ = clipboard.SetTextAsync(text);
        }
    }

    /// <summary>
    /// Clamps a byte range <c>[rangeStart, rangeEnd)</c> to the visible portion of the line starting
    /// at <paramref name="lineFileOffset"/>. Returns <see langword="false"/> when the range does not
    /// overlap the line.
    /// </summary>
    private static bool TryClampRangeToLine(long rangeStart, long rangeEnd, long lineFileOffset,
        int bytesPerLine, out int startByte, out int endByte)
    {
        long lineEnd = lineFileOffset + bytesPerLine;
        if (rangeStart >= lineEnd || rangeEnd <= lineFileOffset)
        {
            startByte = 0;
            endByte = 0;
            return false;
        }

        startByte = (int)Math.Max(0, rangeStart - lineFileOffset);
        endByte = (int)Math.Min(bytesPerLine, rangeEnd - lineFileOffset);
        return true;
    }

    /// <summary>
    /// Paints a single highlight rectangle (hex and ASCII columns) for the byte range
    /// <c>[startByte, endByte)</c> on the line at vertical position <paramref name="y"/>.
    /// </summary>
    private void PaintRangeOnLine(DrawingContext context, IBrush brush, double y, int startByte, int endByte)
    {
        double hx1 = HexStartX + startByte * 3 * _charWidth;
        double hx2 = HexStartX + (endByte * 3 - 1) * _charWidth;
        context.DrawRectangle(brush, null, new Rect(hx1, y, hx2 - hx1, LineHeight));

        double ax1 = AsciiStartX + startByte * _charWidth;
        double ax2 = AsciiStartX + endByte * _charWidth;
        context.DrawRectangle(brush, null, new Rect(ax1, y, ax2 - ax1, LineHeight));
    }

    private void EnsureMetrics()
    {
        if (_metricsValid)
        {
            return;
        }

        FontFamily font = this.TryFindResource("MonoFontFamily", out object? fontResource) && fontResource is FontFamily ff
            ? ff
            : _fallbackMonoFont;
        double size = this.TryFindResource("MonoFontSize", out object? sizeResource) && sizeResource is double d
            ? d
            : DefaultFontSize;

        _typeface = new Typeface(font);
        _fontSize = size;

        // Measure a run of a fixed monospace glyph and divide, so partial glyph widths average out.
        var probe = new FormattedText("0000000000", CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, _typeface, _fontSize, Brushes.Black);
        _charWidth = probe.Width / 10;
        _addressWidth = 10 * _charWidth;
        _metricsValid = true;
    }

    private IBrush GetBrush(string resourceKey, IBrush fallback) =>
        this.TryFindResource(resourceKey, out object? resource) && resource is IBrush brush ? brush : fallback;
}
