using ReScene.SRS;

namespace ReScene.App.Core.Services;

public interface ISRSReconstructionService
{
    public event EventHandler<SRSReconstructionProgressEventArgs>? Progress;

    public event EventHandler<SRSScanProgressEventArgs>? ScanProgress;

    public Task<SRSReconstructionResult> RebuildAsync(
        string srsFilePath, string mediaFilePath, string outputPath, CancellationToken ct);
}
