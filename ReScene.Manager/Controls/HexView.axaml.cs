using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using ReScene.Hex;

namespace ReScene.Manager.Controls;

/// <summary>
/// Composite hex viewer, ported from the WPF <c>ReScene.NET.Controls.HexViewControl</c> (a
/// <c>UserControl</c>). Wraps the <see cref="HexViewControl"/> drawing surface in a scrolling host,
/// pins a draggable <see cref="HexColumnHeader"/> above it, syncs the header to horizontal scrolling,
/// pushes the visible viewport into the surface for row culling, and auto-scrolls to reveal the
/// primary selection. Its dependency-property surface mirrors the inner control's and is forwarded
/// down to it, so a hosting view embeds the whole thing with a single <c>&lt;controls:HexView/&gt;</c>.
/// </summary>
public partial class HexView : UserControl
{
    private readonly HexColumnHeader _header;
    private readonly ScrollViewer _scroll;
    private readonly HexViewControl _inner;
    private readonly bool _initialized;

    public static readonly StyledProperty<IHexDataSource?> DataSourceProperty =
        AvaloniaProperty.Register<HexView, IHexDataSource?>(nameof(DataSource));

    public static readonly StyledProperty<long> BlockOffsetProperty =
        AvaloniaProperty.Register<HexView, long>(nameof(BlockOffset));

    public static readonly StyledProperty<long> BlockLengthProperty =
        AvaloniaProperty.Register<HexView, long>(nameof(BlockLength));

    public static readonly StyledProperty<long> SelectionOffsetProperty =
        AvaloniaProperty.Register<HexView, long>(nameof(SelectionOffset), -1L);

    public static readonly StyledProperty<long> SelectionLengthProperty =
        AvaloniaProperty.Register<HexView, long>(nameof(SelectionLength));

    public static readonly StyledProperty<int> BytesPerLineProperty =
        AvaloniaProperty.Register<HexView, int>(nameof(BytesPerLine), 16, coerce: CoerceBytesPerLine);

    public static readonly StyledProperty<IReadOnlyList<HexMatchRange>?> HighlightRangesProperty =
        AvaloniaProperty.Register<HexView, IReadOnlyList<HexMatchRange>?>(nameof(HighlightRanges));

    public static readonly StyledProperty<IReadOnlyList<HexMatchRange>?> DiffRangesProperty =
        AvaloniaProperty.Register<HexView, IReadOnlyList<HexMatchRange>?>(nameof(DiffRanges));

    public HexView()
    {
        AvaloniaXamlLoader.Load(this);
        _header = this.FindControl<HexColumnHeader>("Header")!;
        _scroll = this.FindControl<ScrollViewer>("Scroll")!;
        _inner = this.FindControl<HexViewControl>("Inner")!;

        _header.Height = HexViewControl.LineHeight;
        _header.Inner = _inner;

        // Forward the composite's DP surface down to the inner drawing surface (data flows down only;
        // the inner control never writes these back). The header's ruler length tracks BytesPerLine.
        _inner.Bind(HexViewControl.DataSourceProperty, this.GetObservable(DataSourceProperty));
        _inner.Bind(HexViewControl.BlockOffsetProperty, this.GetObservable(BlockOffsetProperty));
        _inner.Bind(HexViewControl.BlockLengthProperty, this.GetObservable(BlockLengthProperty));
        _inner.Bind(HexViewControl.SelectionOffsetProperty, this.GetObservable(SelectionOffsetProperty));
        _inner.Bind(HexViewControl.SelectionLengthProperty, this.GetObservable(SelectionLengthProperty));
        _inner.Bind(HexViewControl.BytesPerLineProperty, this.GetObservable(BytesPerLineProperty));
        _inner.Bind(HexViewControl.HighlightRangesProperty, this.GetObservable(HighlightRangesProperty));
        _inner.Bind(HexViewControl.DiffRangesProperty, this.GetObservable(DiffRangesProperty));
        _header.Bind(HexColumnHeader.BytesPerLineProperty, this.GetObservable(BytesPerLineProperty));

        // Sync the pinned header to horizontal scrolling and push the vertical viewport into the
        // surface for row culling. A lambda (not a named handler) keeps the unused event args as
        // discards.
        _scroll.ScrollChanged += (_, _) => SyncScrollAndViewport();

        _initialized = true;
    }

    /// <summary>The hex data source shown by the embedded drawing surface.</summary>
    public IHexDataSource? DataSource
    {
        get => GetValue(DataSourceProperty);
        set => SetValue(DataSourceProperty, value);
    }

    /// <summary>Absolute file offset of the first byte of the displayed block.</summary>
    public long BlockOffset
    {
        get => GetValue(BlockOffsetProperty);
        set => SetValue(BlockOffsetProperty, value);
    }

    /// <summary>Number of bytes in the displayed block.</summary>
    public long BlockLength
    {
        get => GetValue(BlockLengthProperty);
        set => SetValue(BlockLengthProperty, value);
    }

    /// <summary>Absolute file offset of the primary selection, or <c>-1</c> for none.</summary>
    public long SelectionOffset
    {
        get => GetValue(SelectionOffsetProperty);
        set => SetValue(SelectionOffsetProperty, value);
    }

    /// <summary>Length of the primary selection in bytes.</summary>
    public long SelectionLength
    {
        get => GetValue(SelectionLengthProperty);
        set => SetValue(SelectionLengthProperty, value);
    }

    /// <summary>Bytes rendered per row (clamped to 1..128).</summary>
    public int BytesPerLine
    {
        get => GetValue(BytesPerLineProperty);
        set => SetValue(BytesPerLineProperty, value);
    }

    /// <summary>Additional byte ranges to highlight (e.g. all search matches).</summary>
    public IReadOnlyList<HexMatchRange>? HighlightRanges
    {
        get => GetValue(HighlightRangesProperty);
        set => SetValue(HighlightRangesProperty, value);
    }

    /// <summary>Byte ranges to render as diff highlights.</summary>
    public IReadOnlyList<HexMatchRange>? DiffRanges
    {
        get => GetValue(DiffRangesProperty);
        set => SetValue(DiffRangesProperty, value);
    }

    /// <summary>
    /// Computes the new vertical scroll offset that reveals the selection at <paramref name="selectionOffset"/>,
    /// mirroring the WPF composite: no scroll if the selection row is already fully visible; otherwise
    /// land it ~1/3 of the way down the viewport (never below 0). Returns <see langword="false"/> when
    /// there is nothing to scroll to (no selection, empty block, or selection outside the block).
    /// </summary>
    internal static bool TryComputeAutoScroll(long selectionOffset, long blockOffset, long blockLength,
        int bytesPerLine, double currentY, double viewportHeight, out double newY)
    {
        newY = currentY;

        if (selectionOffset < 0 || blockLength <= 0 || bytesPerLine <= 0)
        {
            return false;
        }

        long relOffset = selectionOffset - blockOffset;
        if (relOffset < 0 || relOffset >= blockLength)
        {
            return false;
        }

        long lineIndex = relOffset / bytesPerLine;
        double targetY = lineIndex * HexViewControl.LineHeight;

        if (targetY < currentY || targetY > currentY + viewportHeight - HexViewControl.LineHeight)
        {
            newY = Math.Max(0, targetY - (viewportHeight / 3));
            return true;
        }

        return false;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (!_initialized)
        {
            return;
        }

        if (change.Property == SelectionOffsetProperty || change.Property == SelectionLengthProperty)
        {
            ScrollToSelection();
        }
        else if (change.Property == BytesPerLineProperty)
        {
            // Header ruler length depends on BytesPerLine (its AffectsRender already covers the bound
            // value change; this mirrors the WPF composite's explicit invalidate).
            _header.InvalidateVisual();
        }
    }

    private static int CoerceBytesPerLine(AvaloniaObject sender, int value) => Math.Clamp(value, 1, 128);

    private void SyncScrollAndViewport()
    {
        Vector offset = _scroll.Offset;
        _header.RenderTransform = new TranslateTransform(-offset.X, 0);
        _inner.ViewportTop = offset.Y;
        _inner.ViewportHeight = _scroll.Viewport.Height;
        _inner.InvalidateVisual();
    }

    private void ScrollToSelection()
    {
        if (TryComputeAutoScroll(SelectionOffset, BlockOffset, BlockLength, BytesPerLine,
                _scroll.Offset.Y, _scroll.Viewport.Height, out double newY))
        {
            _scroll.Offset = _scroll.Offset.WithY(newY);
        }
    }
}
