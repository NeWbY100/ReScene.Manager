using ReScene.App.Core.Helpers;

namespace ReScene.App.Core.Models;

/// <summary>
/// User-editable app defaults persisted to settings.json.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Gets or sets the default app name used when creating SRR or SRS files.
    /// </summary>
    public string DefaultAppName { get; set; } = FormatUtilities.GetDefaultAppName();

    /// <summary>
    /// Gets or sets the default output directory pre-filled into Output paths.
    /// </summary>
    public string DefaultOutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum number of entries kept in the recent files list.
    /// </summary>
    public int RecentFilesLimit { get; set; } = 10;

    /// <summary>
    /// Gets or sets the maximum number of EBML elements parsed when opening an MKV/WebM file in
    /// the Inspector or Compare views. Higher values show more of huge files but load slower.
    /// </summary>
    public int MKVMaxElements { get; set; } = ReScene.Core.Comparison.MKVFileData.DefaultMaxElements;

    /// <summary>
    /// Gets or sets the default WinRAR versions folder pre-filled into the RAR Reconstructor.
    /// </summary>
    public string ReconstructWinRARPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the default output folder pre-filled into the RAR Reconstructor. Deliberately
    /// separate from <see cref="DefaultOutputDirectory"/>: reconstruction wipes this folder's
    /// contents before starting.
    /// </summary>
    public string ReconstructOutputPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether a reconstruction run's per-set scratch work folders (input copies,
    /// attempted archives, and per-attempt rar process logs under the output folder's
    /// <c>.rescene-work</c>) are deleted as each set finishes. Off by default: the work files are
    /// kept for diagnostics; they can use significant disk space.
    /// </summary>
    public bool CleanupReconstructionWorkFiles { get; set; }

    /// <summary>
    /// Gets or sets the persisted UI mode. Null means "not yet chosen" — resolved at load time.
    /// </summary>
    public UserMode? Mode { get; set; }
}
