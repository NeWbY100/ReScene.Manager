using ReScene.SRS;

namespace ReScene.App.Core.Services;

public interface ISampleRestorerService
{
    public event EventHandler<SRSReconstructionProgressEventArgs>? Progress;

    public List<SRSEntryInfo> GetSRSEntries(string srrFilePath);

    public Task<SRSReconstructionResult> RestoreSampleAsync(
        string srrFilePath, string srsFileName,
        string mediaFilePath, string outputPath, CancellationToken ct);
}
