using ReScene.Hex;

namespace ReScene.NET.Services;

/// <summary>
/// Outcome of a byte-level diff between two hex data slices.
/// </summary>
/// <param name="Left">
/// Coalesced ranges of bytes on the left side that differ from the right side
/// (or that have no counterpart because the left slice is longer).
/// </param>
/// <param name="Right">
/// Coalesced ranges of bytes on the right side that differ from the left side
/// (or that have no counterpart because the right slice is longer).
/// </param>
public sealed record HexDiffResult(
    IReadOnlyList<HexMatchRange> Left,
    IReadOnlyList<HexMatchRange> Right);
