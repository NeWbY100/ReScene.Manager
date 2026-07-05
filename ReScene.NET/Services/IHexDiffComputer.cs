using ReScene.Hex;

namespace ReScene.NET.Services;

/// <summary>
/// Computes byte-level differences between two slices of hex data, position-aligned.
/// </summary>
public interface IHexDiffComputer
{
    /// <summary>
    /// Compares two byte ranges position-aligned and produces coalesced diff ranges
    /// for each side. Bytes past the shorter slice's length on the longer side are
    /// emitted as a trailing diff range. Computation runs on a background task and
    /// can be cancelled at chunk boundaries.
    /// </summary>
    /// <param name="leftSource">
    /// Data source backing the left slice.
    /// </param>
    /// <param name="leftOffset">
    /// Absolute offset within <paramref name="leftSource"/> where the slice starts.
    /// Emitted ranges use this as their coordinate base.
    /// </param>
    /// <param name="leftLength">
    /// Length of the left slice in bytes.
    /// </param>
    /// <param name="rightSource">
    /// Data source backing the right slice.
    /// </param>
    /// <param name="rightOffset">
    /// Absolute offset within <paramref name="rightSource"/> where the slice starts.
    /// </param>
    /// <param name="rightLength">
    /// Length of the right slice in bytes.
    /// </param>
    /// <param name="progress">
    /// Optional progress reporter; debounced at roughly 10 updates per second.
    /// </param>
    /// <param name="ct">
    /// Cancellation token; honored at chunk boundaries.
    /// </param>
    public Task<HexDiffResult> ComputeAsync(
        IHexDataSource leftSource, long leftOffset, long leftLength,
        IHexDataSource rightSource, long rightOffset, long rightLength,
        IProgress<HexDiffProgress>? progress,
        CancellationToken ct);
}
