namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>Display text computed from a CRC-validation progress event.</summary>
internal sealed record VerifyProgressUpdate
{
    public string HeadingText { get; init; } = string.Empty;
    public double ProgressPercent { get; init; }
    public string ProgressPercentText { get; init; } = string.Empty;
    public string CurrentFileText { get; init; } = string.Empty;
    public string RemainingText { get; init; } = string.Empty;
    public string ElapsedText { get; init; } = string.Empty;

    public bool HasSpeed { get; init; }
    public string SpeedText { get; init; } = string.Empty;
    public bool HasEta { get; init; }
    public string TimeRemainingText { get; init; } = string.Empty;
    public string EtaText { get; init; } = string.Empty;

    public bool IsComplete { get; init; }
}
