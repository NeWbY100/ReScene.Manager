namespace ReScene.Manager.Behaviors;

/// <summary>
/// One RowDefinition's per-mode sizing for <see cref="CompactHeightBehavior"/>.
/// While compact AND the Help body is open, <see cref="HelpOpenMinHeight"/> replaces
/// <see cref="CompactMinHeight"/> (the donation rule).
/// </summary>
internal sealed record CompactRowSize(
    int RowIndex,
    double NormalHeight,
    double CompactMinHeight,
    double HelpOpenMinHeight,
    CompactRowMode Mode);
