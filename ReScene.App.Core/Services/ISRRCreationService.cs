using ReScene.SRR;

namespace ReScene.App.Core.Services;

/// <summary>
/// Service for creating SRR files from RAR volumes or SFV file listings.
/// </summary>
public interface ISRRCreationService
{
    /// <summary>
    /// Raised to report progress during SRR creation.
    /// </summary>
    public event EventHandler<SRRCreationProgressEventArgs>? Progress;

    /// <summary>
    /// Creates an SRR file from a list of RAR volume paths.
    /// </summary>
    /// <param name="outputPath">
    /// Destination path for the SRR file.
    /// </param>
    /// <param name="rarVolumePaths">
    /// Ordered list of RAR volume paths.
    /// </param>
    /// <param name="storedFiles">
    /// Optional ordered list of files to embed; blocks are written in this order.
    /// </param>
    /// <param name="options">
    /// Creation options.
    /// </param>
    /// <param name="ct">
    /// Cancellation token.
    /// </param>
    /// <returns>
    /// The creation result including success status and statistics.
    /// </returns>
    public Task<SRRCreationResult> CreateFromRARAsync(
        string outputPath,
        IReadOnlyList<string> rarVolumePaths,
        IReadOnlyList<StoredFileEntry>? storedFiles,
        SRRCreationOptions options,
        CancellationToken ct);

    /// <summary>
    /// Creates an SRR file from an SFV file that references RAR volumes.
    /// </summary>
    /// <param name="outputPath">
    /// Destination path for the SRR file.
    /// </param>
    /// <param name="sfvFilePath">
    /// Path to the SFV file.
    /// </param>
    /// <param name="additionalFiles">
    /// Optional ordered list of additional files to embed; blocks are written in this order.
    /// </param>
    /// <param name="options">
    /// Creation options.
    /// </param>
    /// <param name="ct">
    /// Cancellation token.
    /// </param>
    /// <returns>
    /// The creation result including success status and statistics.
    /// </returns>
    public Task<SRRCreationResult> CreateFromSFVAsync(
        string outputPath,
        string sfvFilePath,
        IReadOnlyList<StoredFileEntry>? additionalFiles,
        SRRCreationOptions options,
        CancellationToken ct);

    /// <summary>
    /// Creates an SRR file from an explicit list of RAR-set inputs (SFVs and/or first-volume
    /// RARs) — the folder-mode counterpart to <see cref="CreateFromRARAsync"/>/<see cref="CreateFromSFVAsync"/>,
    /// used when a <see cref="ReScene.App.Core.ViewModels.CreatorViewModel"/>
    /// input resolves to a release folder rather than a single file.
    /// </summary>
    /// <param name="outputPath">
    /// Destination path for the SRR file.
    /// </param>
    /// <param name="inputFiles">
    /// Ordered list of SFV or first-volume RAR paths, one per detected RAR set.
    /// </param>
    /// <param name="rootFolder">
    /// The release root every input and stored file is relative to; required when
    /// <paramref name="storeRelativePaths"/> is <see langword="true"/>.
    /// </param>
    /// <param name="storeRelativePaths">
    /// Whether stored/input names are recorded root-relative rather than flat basenames.
    /// </param>
    /// <param name="additionalFiles">
    /// Optional ordered list of additional files to embed; blocks are written in this order.
    /// </param>
    /// <param name="options">
    /// Creation options.
    /// </param>
    /// <param name="ct">
    /// Cancellation token.
    /// </param>
    /// <returns>
    /// The creation result including success status and statistics.
    /// </returns>
    public Task<SRRCreationResult> CreateFromInputsAsync(
        string outputPath,
        IReadOnlyList<string> inputFiles,
        string? rootFolder,
        bool storeRelativePaths,
        IReadOnlyList<StoredFileEntry>? additionalFiles,
        SRRCreationOptions options,
        CancellationToken ct);
}
