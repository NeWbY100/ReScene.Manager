using ReScene.SRR;

namespace ReScene.App.Core.Services;

/// <summary>
/// Default implementation of <see cref="ISRRCreationService"/> that delegates to <see cref="SRRWriter"/>.
/// </summary>
public class SRRCreationService : ISRRCreationService
{
    private readonly SRRWriter _writer = new();

    /// <inheritdoc />
    public event EventHandler<SRRCreationProgressEventArgs>? Progress
    {
        add => _writer.Progress += value;
        remove => _writer.Progress -= value;
    }

    /// <inheritdoc />
    public Task<SRRCreationResult> CreateFromRARAsync(
        string outputPath,
        IReadOnlyList<string> rarVolumePaths,
        IReadOnlyList<StoredFileEntry>? storedFiles,
        SRRCreationOptions options,
        CancellationToken ct) => _writer.CreateAsync(outputPath, rarVolumePaths, storedFiles, options, ct);

    /// <inheritdoc />
    public Task<SRRCreationResult> CreateFromSFVAsync(
        string outputPath,
        string sfvFilePath,
        IReadOnlyList<StoredFileEntry>? additionalFiles,
        SRRCreationOptions options,
        CancellationToken ct) => _writer.CreateFromSFVAsync(outputPath, sfvFilePath, additionalFiles, options, ct);

    /// <inheritdoc />
    public Task<SRRCreationResult> CreateFromInputsAsync(
        string outputPath,
        IReadOnlyList<string> inputFiles,
        string? rootFolder,
        bool storeRelativePaths,
        IReadOnlyList<StoredFileEntry>? additionalFiles,
        SRRCreationOptions options,
        CancellationToken ct) => _writer.CreateFromInputsAsync(
            outputPath, inputFiles, rootFolder, storeRelativePaths, additionalFiles, options, ct);
}
