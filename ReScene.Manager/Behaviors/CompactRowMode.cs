namespace ReScene.Manager.Behaviors;

/// <summary>
/// How <see cref="CompactHeightBehavior"/> treats one RowDefinition across modes
/// (RowDefinitions are not styleable, so the behavior owns their values — spec §1).
/// </summary>
internal enum CompactRowMode
{
    /// <summary>Height untouched; only MinHeight swaps per mode (star work rows).</summary>
    MinOnly,

    /// <summary>Compact sets Height = CompactMinHeight px; expand restores
    /// Height = NormalHeight px unless a splitter drag was captured (fixed pixel rows
    /// such as CreatorView's stored-files row).</summary>
    PixelRestore,

    /// <summary>Compact sets Height = 1* with MinHeight = CompactMinHeight; expand
    /// restores Height = Auto, MinHeight = 0 (three-band config rows).</summary>
    AutoToStar,
}
