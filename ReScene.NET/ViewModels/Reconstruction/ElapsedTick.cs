namespace ReScene.NET.ViewModels.Reconstruction;

/// <summary>Elapsed/remaining text extrapolated by the per-second timer tick.</summary>
internal sealed record ElapsedTick
{
    public string ElapsedText { get; init; } = string.Empty;
    public bool HasTiming { get; init; }
    public string RemainingText { get; init; } = string.Empty;
    public string EtaText { get; init; } = string.Empty;
}
