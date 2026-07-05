namespace ReScene.NET.Models;

/// <summary>
/// Persisted snapshot of state captured by Import-from-SRR.
/// </summary>
public sealed class ImportedSrrState
{
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

    public List<string> OriginalRarFileNames { get; set; } = [];

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
