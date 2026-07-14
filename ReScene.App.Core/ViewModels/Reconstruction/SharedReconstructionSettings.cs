using ReScene.Core;
using ReScene.Core.Cryptography;
using ReScene.Core.Diagnostics;
using ReScene.SRR;

namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>
/// The non-per-set reconstruction settings shared across every archive set in a run: the global
/// switch toggles, version ranges, command-line matrix, the release-wide comment/CMT data, and the
/// paths. Per-set data (content, volume names, CRCs, detected metadata) is read from each
/// <see cref="SRRArchiveSet"/> instead.
/// </summary>
internal sealed record SharedReconstructionSettings
{
    public required string WinRARPath { get; init; }
    public required string ReleasePath { get; init; }
    public required string OutputPath { get; init; }
    public required IReadOnlyList<VersionRange> RARVersions { get; init; }

    /// <summary>
    /// The WinRAR version folder NAMES the user ticked in the version tree (e.g. "winrar-390-beta1").
    /// Carried to the engine as <see cref="RAROptions.AllowedVersionFolders"/> so that unticking one
    /// same-version variant leaf actually excludes its folder — the version ranges alone cannot
    /// distinguish two folders that parse to the same version. Empty means no folder filter (the
    /// no-scan fallback path, which uses broad ranges).
    /// </summary>
    public IReadOnlyList<string> SelectedVersionFolders { get; init; } = [];
    public required IReadOnlyList<RARCommandLineArgument[]> CommandLineArguments { get; init; }

    /// <summary>
    /// The switch-toggle snapshot <see cref="CommandLineArguments"/>/<see cref="RARVersions"/> were
    /// built from. <see cref="ArchiveSetPlanner.BuildOptionsForSet"/> reuses it (#6) to rebuild a
    /// per-set matrix that replaces only the switch groups a set's own header metadata specifies
    /// (compression/dictionary/solid/format), leaving every other group exactly as the global run
    /// carries it.
    /// </summary>
    public RARSwitchSettings Switches { get; init; } = new();

    /// <summary>
    /// The installed WinRAR executables from the last completed folder scan — scan-state-guarded
    /// (empty for a no-scan run, or for a stale scan left behind by a <c>WinRARPath</c> change) the
    /// same way as <see cref="SelectedVersionFolders"/>. Intersected with a set's own format
    /// requirement via <see cref="RarFormatCompatibility.SelectFor"/>.
    /// </summary>
    public IReadOnlyList<InstalledRARVersion> InstalledVersions { get; init; } = [];

    public required HashType HashType { get; init; }

    /// <summary>
    /// Every hash from the verification file (CRC32 for .sfv, SHA1 for .sha1). Seeded into each set's
    /// <see cref="BruteForceOptions.Hashes"/> so the engine's cheap first-volume gate works even when
    /// per-volume CRCs are unavailable (e.g. a .sha1 run with no embedded/user SFV).
    /// </summary>
    public IReadOnlyCollection<string> VerificationHashes { get; init; } = [];

    /// <summary>
    /// The one-time parse of the user's verification file, taken at Start <em>before</em> the
    /// destructive output-directory cleanup. The sole source for every downstream verification read
    /// (per-set expected CRCs, the flat set's fallback volume names) — the file itself is never
    /// re-read after Start (#14).
    /// </summary>
    public required VerificationSnapshot Verification { get; init; }
    public TriState SetFileArchiveAttribute { get; init; }
    public TriState SetFileNotContentIndexedAttribute { get; init; }
    public bool DeleteRARFiles { get; init; }
    public bool DeleteDuplicateCRCFiles { get; init; }
    public bool StopOnFirstMatch { get; init; }
    public bool CompleteAllVolumes { get; init; }
    public bool RenameToReleaseNames { get; init; }
    public bool EnableHostOSPatching { get; init; }
    public bool UseOldVolumeNaming { get; init; }

    // ── Release-wide (non-per-set) data carried from the imported SRR ──
    public string? ArchiveComment { get; init; }
    public byte[]? ArchiveCommentBytes { get; init; }
    public byte[]? CmtCompressedData { get; init; }
    public byte? CmtCompressionMethod { get; init; }
    public byte? DetectedCmtHostOS { get; init; }
    public uint? DetectedCmtFileTime { get; init; }
    public uint? DetectedCmtFileAttributes { get; init; }
    public CustomPackerType CustomPackerDetected { get; init; }
    public string? SRRFilePath { get; init; }

    // Release-wide directory entries + timestamps (subdirectories live in the release root, not in
    // any single set), preserved so produced RARs carry the original subdir modified/created/access
    // times. Empty for the synthetic flat set when no SRR was imported.
    public IReadOnlyCollection<string> ArchiveDirectories { get; init; } = [];
    public IReadOnlyDictionary<string, DateTime> DirectoryTimestamps { get; init; } = new Dictionary<string, DateTime>();
    public IReadOnlyDictionary<string, DateTime> DirectoryCreationTimes { get; init; } = new Dictionary<string, DateTime>();
    public IReadOnlyDictionary<string, DateTime> DirectoryAccessTimes { get; init; } = new Dictionary<string, DateTime>();
}
