using ReScene.App.Core.Models;
using ReScene.SRR;

namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>
/// Maps the in-memory <see cref="ReconstructionImportState"/> to and from the serializable
/// <see cref="ImportedSRRState"/> DTO used by import/export configuration. Pure: it copies data
/// only and never touches WPF binding (the bound <c>CustomPackerWarning</c> is handled by the
/// view-model).
/// </summary>
internal static class ImportedSRRStateMapper
{
    /// <summary>
    /// Captures the state as a DTO, or returns null when no meaningful SRR state has been imported.
    /// The bound custom-packer warning is supplied separately by the caller.
    /// </summary>
    public static ImportedSRRState? Capture(ReconstructionImportState state, string? customPackerWarning)
    {
        bool hasState = state.ArchiveFiles.Count > 0
            || state.ArchiveDirectories.Count > 0
            || state.FileTimestamps.Count > 0
            || state.ArchiveFileCrcs.Count > 0
            || state.SRRFilePath is not null
            || state.CmtCompressedData is { Length: > 0 }
            || state.ArchiveSets.Count > 0;

        if (!hasState)
        {
            return null;
        }

        return new ImportedSRRState
        {
            SchemaVersion = ImportedSRRState.CurrentSchemaVersion,
            SRRFilePath = state.SRRFilePath,
            ArchiveFiles = [.. state.ArchiveFiles],
            ArchiveDirectories = [.. state.ArchiveDirectories],
            DirTimestamps = new Dictionary<string, DateTime>(state.DirTimestamps),
            DirCreationTimes = new Dictionary<string, DateTime>(state.DirCreationTimes),
            DirAccessTimes = new Dictionary<string, DateTime>(state.DirAccessTimes),
            FileTimestamps = new Dictionary<string, DateTime>(state.FileTimestamps),
            FileCreationTimes = new Dictionary<string, DateTime>(state.FileCreationTimes),
            FileAccessTimes = new Dictionary<string, DateTime>(state.FileAccessTimes),
            ArchiveFileCrcs = new Dictionary<string, string>(state.ArchiveFileCrcs),
            OriginalRARFileNames = [.. state.OriginalRARFileNames],
            ArchiveSets = [.. state.ArchiveSets.Select(ToDto)],
            ArchiveComment = state.ArchiveComment,
            ArchiveCommentBytes = state.ArchiveCommentBytes,
            CmtCompressedData = state.CmtCompressedData,
            CmtCompressionMethod = state.CmtCompressionMethod,
            DetectedFileHostOS = state.DetectedFileHostOS,
            DetectedFileAttributes = state.DetectedFileAttributes,
            DetectedCmtHostOS = state.DetectedCmtHostOS,
            DetectedCmtFileTime = state.DetectedCmtFileTime,
            DetectedCmtFileAttributes = state.DetectedCmtFileAttributes,
            DetectedLargeFlag = state.DetectedLargeFlag,
            DetectedHighPackSize = state.DetectedHighPackSize,
            DetectedHighUnpSize = state.DetectedHighUnpSize,
            CustomPackerType = state.CustomPackerType.ToString(),
            CustomPackerWarning = customPackerWarning
        };
    }

    /// <summary>
    /// Builds an import state from a DTO (or a fully-empty state when the DTO is null, meaning
    /// "no SRR imported"). The caller applies the bound custom-packer warning and any logging.
    /// </summary>
    public static ReconstructionImportState Apply(ImportedSRRState? s)
    {
        if (s is null)
        {
            return new ReconstructionImportState();
        }

        // The restored set list is trusted only when SchemaVersion marks the DTO complete (a
        // presence marker, not a "non-empty" check — empty dirs/null metadata on a set are still
        // complete). Legacy DTOs (no set list, older/absent SchemaVersion) leave ArchiveSets empty
        // here; the existing runtime fallback (ArchiveSetPlanner.ResolveSets, called before each
        // run) re-derives them from SRRFilePath instead.
        bool archiveSetsComplete = s.SchemaVersion >= ImportedSRRState.CurrentSchemaVersion;

        return new ReconstructionImportState
        {
            SRRFilePath = s.SRRFilePath,
            ArchiveFiles = new HashSet<string>(s.ArchiveFiles, StringComparer.OrdinalIgnoreCase),
            ArchiveDirectories = new HashSet<string>(s.ArchiveDirectories, StringComparer.OrdinalIgnoreCase),
            DirTimestamps = ToCi(s.DirTimestamps),
            DirCreationTimes = ToCi(s.DirCreationTimes),
            DirAccessTimes = ToCi(s.DirAccessTimes),
            FileTimestamps = ToCi(s.FileTimestamps),
            FileCreationTimes = ToCi(s.FileCreationTimes),
            FileAccessTimes = ToCi(s.FileAccessTimes),
            ArchiveFileCrcs = new Dictionary<string, string>(s.ArchiveFileCrcs, StringComparer.OrdinalIgnoreCase),
            OriginalRARFileNames = s.OriginalRARFileNames is { } names ? [.. names] : [],
            ArchiveSets = archiveSetsComplete ? [.. s.ArchiveSets.Select(ToLiveSet)] : [],
            ArchiveComment = s.ArchiveComment,
            ArchiveCommentBytes = s.ArchiveCommentBytes,
            CmtCompressedData = s.CmtCompressedData,
            CmtCompressionMethod = s.CmtCompressionMethod,
            DetectedFileHostOS = s.DetectedFileHostOS,
            DetectedFileAttributes = s.DetectedFileAttributes,
            DetectedCmtHostOS = s.DetectedCmtHostOS,
            DetectedCmtFileTime = s.DetectedCmtFileTime,
            DetectedCmtFileAttributes = s.DetectedCmtFileAttributes,
            DetectedLargeFlag = s.DetectedLargeFlag,
            DetectedHighPackSize = s.DetectedHighPackSize,
            DetectedHighUnpSize = s.DetectedHighUnpSize,
            CustomPackerType = Enum.TryParse(s.CustomPackerType, out CustomPackerType packer) ? packer : CustomPackerType.None,
        };

        static Dictionary<string, DateTime> ToCi(Dictionary<string, DateTime>? src) =>
            src is null
                ? new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, DateTime>(src, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Captures one live archive set's complete per-set data into its DTO.</summary>
    private static ArchiveSetDto ToDto(SRRArchiveSet s) => new()
    {
        Key = s.Key,
        Directory = s.Directory,
        VolumeNames = [.. s.VolumeNames],
        ArchivedFiles = [.. s.ArchivedFiles],
        ArchivedFileCrcs = new Dictionary<string, string>(s.ArchivedFileCrcs),
        ArchivedFileTimestamps = new Dictionary<string, DateTime>(s.ArchivedFileTimestamps),
        ArchivedFileCreationTimes = new Dictionary<string, DateTime>(s.ArchivedFileCreationTimes),
        ArchivedFileAccessTimes = new Dictionary<string, DateTime>(s.ArchivedFileAccessTimes),
        ArchivedDirectories = [.. s.ArchivedDirectories],
        ArchivedDirectoryTimestamps = new Dictionary<string, DateTime>(s.ArchivedDirectoryTimestamps),
        ArchivedDirectoryCreationTimes = new Dictionary<string, DateTime>(s.ArchivedDirectoryCreationTimes),
        ArchivedDirectoryAccessTimes = new Dictionary<string, DateTime>(s.ArchivedDirectoryAccessTimes),
        CompressionMethod = s.CompressionMethod,
        DictionarySize = s.DictionarySize,
        RARVersion = s.RARVersion,
        IsSolid = s.IsSolid,
        HasRecoveryRecord = s.HasRecoveryRecord,
        DetectedHostOS = s.DetectedHostOS,
        DetectedFileAttributes = s.DetectedFileAttributes,
        HasLargeFiles = s.HasLargeFiles,
        DetectedHighPackSize = s.DetectedHighPackSize,
        DetectedHighUnpSize = s.DetectedHighUnpSize,
    };

    /// <summary>
    /// Rebuilds a live <see cref="SRRArchiveSet"/> from its DTO via the type's public
    /// get-only-mutable collections and settable metadata — its config-restore seam — never by
    /// re-parsing the SRR. Those collections are always constructed with
    /// <see cref="StringComparer.OrdinalIgnoreCase"/> internally, so populating them here preserves
    /// case-insensitive lookups regardless of the DTO's own (ordinal) dictionary comparer.
    /// </summary>
    private static SRRArchiveSet ToLiveSet(ArchiveSetDto d)
    {
        var set = new SRRArchiveSet { Key = d.Key, Directory = d.Directory };

        foreach (string v in d.VolumeNames)
        {
            set.VolumeNames.Add(v);
        }

        foreach (string f in d.ArchivedFiles)
        {
            set.ArchivedFiles.Add(f);
        }

        foreach (KeyValuePair<string, string> kv in d.ArchivedFileCrcs)
        {
            set.ArchivedFileCrcs[kv.Key] = kv.Value;
        }

        foreach (KeyValuePair<string, DateTime> kv in d.ArchivedFileTimestamps)
        {
            set.ArchivedFileTimestamps[kv.Key] = kv.Value;
        }

        foreach (KeyValuePair<string, DateTime> kv in d.ArchivedFileCreationTimes)
        {
            set.ArchivedFileCreationTimes[kv.Key] = kv.Value;
        }

        foreach (KeyValuePair<string, DateTime> kv in d.ArchivedFileAccessTimes)
        {
            set.ArchivedFileAccessTimes[kv.Key] = kv.Value;
        }

        foreach (string dir in d.ArchivedDirectories)
        {
            set.ArchivedDirectories.Add(dir);
        }

        foreach (KeyValuePair<string, DateTime> kv in d.ArchivedDirectoryTimestamps)
        {
            set.ArchivedDirectoryTimestamps[kv.Key] = kv.Value;
        }

        foreach (KeyValuePair<string, DateTime> kv in d.ArchivedDirectoryCreationTimes)
        {
            set.ArchivedDirectoryCreationTimes[kv.Key] = kv.Value;
        }

        foreach (KeyValuePair<string, DateTime> kv in d.ArchivedDirectoryAccessTimes)
        {
            set.ArchivedDirectoryAccessTimes[kv.Key] = kv.Value;
        }

        set.CompressionMethod = d.CompressionMethod;
        set.DictionarySize = d.DictionarySize;
        set.RARVersion = d.RARVersion;
        set.IsSolid = d.IsSolid;
        set.HasRecoveryRecord = d.HasRecoveryRecord;
        set.DetectedHostOS = d.DetectedHostOS;
        set.DetectedFileAttributes = d.DetectedFileAttributes;
        set.HasLargeFiles = d.HasLargeFiles;
        set.DetectedHighPackSize = d.DetectedHighPackSize;
        set.DetectedHighUnpSize = d.DetectedHighUnpSize;

        return set;
    }
}
