using ReScene.SRS;

namespace ReScene.App.Core.Services;

/// <summary>
/// Default implementation of <see cref="ISRSCreationService"/> that delegates to <see cref="SRSWriter"/>.
/// </summary>
public class SRSCreationService : ISRSCreationService
{
    private readonly SRSWriter _writer = new();

    /// <inheritdoc />
    public event EventHandler<SRSCreationProgressEventArgs>? Progress
    {
        add => _writer.Progress += value;
        remove => _writer.Progress -= value;
    }

    /// <inheritdoc />
    public event EventHandler<SRSScanProgressEventArgs>? ScanProgress
    {
        add => _writer.ScanProgress += value;
        remove => _writer.ScanProgress -= value;
    }

    /// <inheritdoc />
    public Task<SRSCreationResult> CreateAsync(
        string outputPath,
        string sampleFilePath,
        SRSCreationOptions options,
        CancellationToken ct) => _writer.CreateAsync(outputPath, sampleFilePath, options, ct);
}
