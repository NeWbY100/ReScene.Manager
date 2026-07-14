namespace ReScene.App.Core.Models;

/// <summary>
/// Serializable snapshot of one <see cref="ReScene.SRR.SRRArchiveSet"/>: the complete per-set data
/// (volumes, archived content, timestamps, header-derived metadata) needed to restore the set
/// exactly on config-import, without re-parsing the SRR. Carried by
/// <see cref="ImportedSRRState.ArchiveSets"/>; mapped to/from the live
/// <see cref="ReScene.SRR.SRRArchiveSet"/> by
/// <see cref="ReScene.App.Core.ViewModels.Reconstruction.ImportedSRRStateMapper"/>.
/// </summary>
public sealed class ArchiveSetDto
{
    public string Key { get; set; } = "";
    public string Directory { get; set; } = "";

    /// <summary>Volume file names in SRR order.</summary>
    public List<string> VolumeNames { get; set; } = [];

    public List<string> ArchivedFiles { get; set; } = [];
    public Dictionary<string, string> ArchivedFileCrcs { get; set; } = [];
    public Dictionary<string, DateTime> ArchivedFileTimestamps { get; set; } = [];
    public Dictionary<string, DateTime> ArchivedFileCreationTimes { get; set; } = [];
    public Dictionary<string, DateTime> ArchivedFileAccessTimes { get; set; } = [];

    public List<string> ArchivedDirectories { get; set; } = [];
    public Dictionary<string, DateTime> ArchivedDirectoryTimestamps { get; set; } = [];
    public Dictionary<string, DateTime> ArchivedDirectoryCreationTimes { get; set; } = [];
    public Dictionary<string, DateTime> ArchivedDirectoryAccessTimes { get; set; } = [];

    // Header-derived metadata, from this set's first headers.
    public int? CompressionMethod { get; set; }
    public int? DictionarySize { get; set; }
    public int? RARVersion { get; set; }
    public bool? IsSolid { get; set; }
    public bool? HasRecoveryRecord { get; set; }
    public byte? DetectedHostOS { get; set; }
    public uint? DetectedFileAttributes { get; set; }
    public bool? HasLargeFiles { get; set; }
    public uint? DetectedHighPackSize { get; set; }
    public uint? DetectedHighUnpSize { get; set; }
}
