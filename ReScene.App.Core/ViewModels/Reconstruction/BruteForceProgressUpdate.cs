namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>Display text computed from a brute-force progress event.</summary>
internal sealed record BruteForceProgressUpdate
{
    public double ProgressPercent { get; init; }
    public string PhaseDescription { get; init; } = string.Empty;
    public string ProgressMessage { get; init; } = string.Empty;
    public string TestCountText { get; init; } = string.Empty;
    public string ProgressPercentText { get; init; } = string.Empty;
    public string CurrentDetailText { get; init; } = string.Empty;
    public string ElapsedText { get; init; } = string.Empty;

    /// <summary>True when timing fields below were computed (operation has progressed).</summary>
    public bool HasTiming { get; init; }
    public string RemainingText { get; init; } = string.Empty;
    public string SpeedText { get; init; } = string.Empty;
    public string EtaText { get; init; } = string.Empty;
}
