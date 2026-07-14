namespace ReScene.App.Core.Models;

/// <summary>
/// Persisted snapshot of state captured by Import-from-SRR.
/// </summary>
public sealed class ImportedSRRState
{
    /// <summary>
    /// Schema version marking that <see cref="ArchiveSets"/> carries the COMPLETE per-set snapshot.
    /// This is a presence marker, not a "non-empty" check: a DTO whose <see cref="SchemaVersion"/>
    /// is at least <see cref="CurrentSchemaVersion"/> is trusted as-is (empty directories or null
    /// metadata on a set are legitimate captured values). Configs written before this feature have
    /// <see cref="SchemaVersion"/> 0 (absent from older JSON) and no set list; those are legacy and
    /// restore by re-parsing the SRR at <see cref="SRRFilePath"/> instead — see
    /// <see cref="ReScene.App.Core.ViewModels.Reconstruction.ImportedSRRStateMapper"/>.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; }

    public string? SRRFilePath { get; set; }

    public List<string> ArchiveFiles { get; set; } = [];
    public List<string> ArchiveDirectories { get; set; } = [];

    public Dictionary<string, DateTime> DirTimestamps { get; set; } = [];
    public Dictionary<string, DateTime> DirCreationTimes { get; set; } = [];
    public Dictionary<string, DateTime> DirAccessTimes { get; set; } = [];
    public Dictionary<string, DateTime> FileTimestamps { get; set; } = [];
    public Dictionary<string, DateTime> FileCreationTimes { get; set; } = [];
    public Dictionary<string, DateTime> FileAccessTimes { get; set; } = [];

    public Dictionary<string, string> ArchiveFileCrcs { get; set; } = [];

    public List<string> OriginalRARFileNames { get; set; } = [];

    /// <summary>
    /// Complete per-archive-set snapshot (volumes, archived content, timestamps, header-derived
    /// metadata). Only trustworthy as a full restore when <see cref="SchemaVersion"/> marks it
    /// complete — see <see cref="ReScene.App.Core.ViewModels.Reconstruction.ImportedSRRStateMapper"/>.
    /// </summary>
    public List<ArchiveSetDto> ArchiveSets { get; set; } = [];

    public string? ArchiveComment { get; set; }
    public byte[]? ArchiveCommentBytes { get; set; }
    public byte[]? CmtCompressedData { get; set; }
    public byte? CmtCompressionMethod { get; set; }

    public byte? DetectedFileHostOS { get; set; }
    public uint? DetectedFileAttributes { get; set; }
    public byte? DetectedCmtHostOS { get; set; }
    public uint? DetectedCmtFileTime { get; set; }
    public uint? DetectedCmtFileAttributes { get; set; }
    public bool? DetectedLargeFlag { get; set; }
    public uint? DetectedHighPackSize { get; set; }
    public uint? DetectedHighUnpSize { get; set; }

    public string CustomPackerType { get; set; } = "None";
    public string? CustomPackerWarning { get; set; }
}
