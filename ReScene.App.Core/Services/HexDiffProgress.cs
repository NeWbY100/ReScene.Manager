using ReScene.Hex;

namespace ReScene.App.Core.Services;

/// <summary>
/// Periodic update emitted while a diff computation is running.
/// </summary>
/// <param name="Percent">
/// Approximate completion percentage (0 to 100).
/// </param>
/// <param name="Left">
/// Snapshot of left-side diff ranges produced so far.
/// </param>
/// <param name="Right">
/// Snapshot of right-side diff ranges produced so far.
/// </param>
public sealed record HexDiffProgress(
    double Percent,
    IReadOnlyList<HexMatchRange> Left,
    IReadOnlyList<HexMatchRange> Right);
