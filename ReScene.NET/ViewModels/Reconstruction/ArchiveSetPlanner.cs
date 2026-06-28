using ReScene.Core;
using ReScene.Core.Cryptography;
using ReScene.Core.Diagnostics;
using ReScene.Core.IO;
using ReScene.SRR;

namespace ReScene.NET.ViewModels.Reconstruction;

/// <summary>
/// The non-per-set reconstruction settings shared across every archive set in a run: the global
/// switch toggles, version ranges, command-line matrix, the release-wide comment/CMT data, and the
/// paths. Per-set data (content, volume names, CRCs, detected metadata) is read from each
/// <see cref="SrrArchiveSet"/> instead.
/// </summary>
internal sealed record SharedReconstructionSettings
{
    public required string WinRarPath { get; init; }
    public required string ReleasePath { get; init; }
    public required string OutputPath { get; init; }
    public required IReadOnlyList<VersionRange> RarVersions { get; init; }
    public required IReadOnlyList<RARCommandLineArgument[]> CommandLineArguments { get; init; }
    public required HashType HashType { get; init; }

    /// <summary>
    /// Every hash from the verification file (CRC32 for .sfv, SHA1 for .sha1). Seeded into each set's
    /// <see cref="BruteForceOptions.Hashes"/> so the engine's cheap first-volume gate works even when
    /// per-volume CRCs are unavailable (e.g. a .sha1 run with no embedded/user SFV).
    /// </summary>
    public IReadOnlyCollection<string> VerificationHashes { get; init; } = [];
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

/// <summary>
/// Pure planner for the multi-archive-set reconstruction loop: resolves the sets to reconstruct,
/// assembles each set's expected per-volume CRC map, builds the per-set <see cref="BruteForceOptions"/>
/// from the set plus shared settings, and narrows a set's options to a single winning combo for
/// cross-set seeding. No I/O beyond the explicit byte/SFV inputs the caller supplies (plus the
/// SRR re-parse in <see cref="ResolveSets"/> when only a path is available).
/// </summary>
internal static class ArchiveSetPlanner
{
    /// <summary>
    /// Resolves the archive sets to reconstruct. Prefers the parsed <paramref name="archiveSets"/>;
    /// else re-parses the SRR at <paramref name="srrFilePath"/>; else synthesizes one flat set from
    /// the flat names/files (legacy / no-SRR single-set path).
    /// </summary>
    public static IReadOnlyList<SrrArchiveSet> ResolveSets(
        IReadOnlyList<SrrArchiveSet> archiveSets,
        string? srrFilePath,
        IReadOnlyList<string> flatOriginalNames,
        IReadOnlyCollection<string> flatArchiveFiles)
    {
        if (archiveSets.Count > 0)
        {
            return archiveSets;
        }

        if (!string.IsNullOrWhiteSpace(srrFilePath) && File.Exists(srrFilePath))
        {
            IReadOnlyList<SrrArchiveSet> reloaded = SRRFile.Load(srrFilePath).ArchiveSets;
            if (reloaded.Count > 0)
            {
                return reloaded;
            }
        }

        var flat = new SrrArchiveSet { Key = "", Directory = "" };
        foreach (string v in flatOriginalNames)
        {
            flat.VolumeNames.Add(v);
        }

        foreach (string f in flatArchiveFiles)
        {
            flat.ArchivedFiles.Add(f);
        }

        return [flat];
    }

    /// <summary>
    /// Builds the per-volume expected CRC map (base filename -> CRC) for a set: embedded SFV bytes
    /// first (when present), else the user verification SFV, filtered to this set's volume base names.
    /// </summary>
    public static Dictionary<string, string> BuildExpectedVolumeCrcs(
        SrrArchiveSet set, byte[]? embeddedSfvBytes, SFVFile? userSfv)
    {
        var wanted = new HashSet<string>(set.VolumeNames.Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Take(SFVFile sfv)
        {
            foreach (SFVFileEntry e in sfv.Entries)
            {
                string name = Path.GetFileName(e.FileName);
                if (wanted.Contains(name) && !result.ContainsKey(name))
                {
                    result[name] = e.CRC;
                }
            }
        }

        if (embeddedSfvBytes is { Length: > 0 })
        {
            Take(SFVFile.ParseBytes(embeddedSfvBytes, tolerant: true));
        }

        if (result.Count < wanted.Count && userSfv != null)
        {
            Take(userSfv);
        }

        return result;
    }

    /// <summary>Builds the brute-force options for one set, using only its content/names/metadata.</summary>
    public static BruteForceOptions BuildOptionsForSet(
        SrrArchiveSet set,
        SharedReconstructionSettings shared,
        Dictionary<string, string> expectedVolumeCrcs)
    {
        var options = new BruteForceOptions(shared.WinRarPath, shared.ReleasePath, WorkRootFor(shared, set))
        {
            HashType = shared.HashType,
            RAROptions = new RAROptions
            {
                SetFileArchiveAttribute = shared.SetFileArchiveAttribute,
                SetFileNotContentIndexedAttribute = shared.SetFileNotContentIndexedAttribute,
                CommandLineArguments = [.. shared.CommandLineArguments],
                RARVersions = [.. shared.RarVersions],
                DeleteRARFiles = shared.DeleteRARFiles,
                DeleteDuplicateCRCFiles = shared.DeleteDuplicateCRCFiles,
                StopOnFirstMatch = shared.StopOnFirstMatch,
                CompleteAllVolumes = shared.CompleteAllVolumes,
                RenameToOriginalNames = shared.RenameToReleaseNames,
                OriginalRarFileNames = [.. set.VolumeNames],
                ArchiveFileCrcs = new Dictionary<string, string>(set.ArchivedFileCrcs, StringComparer.OrdinalIgnoreCase),
                ArchiveFilePaths = new HashSet<string>(set.ArchivedFiles, StringComparer.OrdinalIgnoreCase),
                ArchiveDirectoryPaths = new HashSet<string>(shared.ArchiveDirectories, StringComparer.OrdinalIgnoreCase),
                DirectoryTimestamps = new Dictionary<string, DateTime>(shared.DirectoryTimestamps, StringComparer.OrdinalIgnoreCase),
                DirectoryCreationTimes = new Dictionary<string, DateTime>(shared.DirectoryCreationTimes, StringComparer.OrdinalIgnoreCase),
                DirectoryAccessTimes = new Dictionary<string, DateTime>(shared.DirectoryAccessTimes, StringComparer.OrdinalIgnoreCase),
                FileTimestamps = new Dictionary<string, DateTime>(set.ArchivedFileTimestamps, StringComparer.OrdinalIgnoreCase),
                FileCreationTimes = new Dictionary<string, DateTime>(set.ArchivedFileCreationTimes, StringComparer.OrdinalIgnoreCase),
                FileAccessTimes = new Dictionary<string, DateTime>(set.ArchivedFileAccessTimes, StringComparer.OrdinalIgnoreCase),
                EnableHostOSPatching = shared.EnableHostOSPatching,
                DetectedFileHostOS = set.DetectedHostOS,
                DetectedFileAttributes = set.DetectedFileAttributes,
                DetectedLargeFlag = set.HasLargeFiles,
                DetectedHighPackSize = set.DetectedHighPackSize,
                DetectedHighUnpSize = set.DetectedHighUnpSize,
                UseOldVolumeNaming = shared.UseOldVolumeNaming,

                // Release-wide comment / CMT / custom-packer / SRR path (not per-set).
                ArchiveComment = shared.ArchiveComment,
                ArchiveCommentBytes = shared.ArchiveCommentBytes,
                CmtCompressedData = shared.CmtCompressedData,
                CmtCompressionMethod = shared.CmtCompressionMethod,
                DetectedCmtHostOS = shared.DetectedCmtHostOS,
                DetectedCmtFileTime = shared.DetectedCmtFileTime,
                DetectedCmtFileAttributes = shared.DetectedCmtFileAttributes,
                CustomPackerDetected = shared.CustomPackerDetected,
                SRRFilePath = shared.SRRFilePath,
            },
        };

        // Seed the cheap first-volume gate (Hashes) with every verification hash plus this set's
        // per-volume CRCs, and the full per-volume verification map (ExpectedVolumeCrcs) with this
        // set's CRCs, so the engine's gates are consistent.
        foreach (string h in shared.VerificationHashes)
        {
            options.Hashes.Add(h);
        }

        foreach (string crc in expectedVolumeCrcs.Values)
        {
            options.Hashes.Add(crc);
        }

        foreach (KeyValuePair<string, string> kv in expectedVolumeCrcs)
        {
            options.ExpectedVolumeCrcs[kv.Key] = kv.Value;
        }

        return options;
    }

    /// <summary>
    /// True when full per-volume verification was requested and is genuinely incomplete:
    /// CompleteAllVolumes is on, the verification is CRC32-based, at least one expected per-volume
    /// CRC was found, but it does not cover every volume. For SHA1 runs (no per-volume CRC source)
    /// or when no expected CRC matched at all, this returns false so the engine still runs and
    /// gates on the first volume exactly as before (no regression).
    /// </summary>
    public static bool ShouldSkipUnverifiableSet(bool completeAllVolumes, HashType hashType, int expectedCrcCount, int volumeCount)
        => completeAllVolumes && hashType == HashType.CRC32 && expectedCrcCount > 0 && expectedCrcCount < volumeCount;

    /// <summary>The working directory for a set's run: OutputPath for a single root set, else an isolated subdir.</summary>
    public static string WorkRootFor(SharedReconstructionSettings shared, SrrArchiveSet set) =>
        string.IsNullOrEmpty(set.Key)
            ? shared.OutputPath
            : Path.Combine(shared.OutputPath, ".rescene-work", Sanitize(set.Key));

    /// <summary>Narrows options to a single winning combo (one version, one args set) for seeding.</summary>
    public static BruteForceOptions NarrowToCombo(BruteForceOptions full, WinningCombo combo)
    {
        var narrowed = new BruteForceOptions(full.RARInstallationsDirectoryPath, full.ReleaseDirectoryPath, full.OutputDirectoryPath)
        {
            HashType = full.HashType,
            RAROptions = CloneWith(full.RAROptions,
                versions: [new VersionRange(combo.Version, combo.Version)],
                args: [combo.Args.ToArray()]),
        };

        foreach (string h in full.Hashes)
        {
            narrowed.Hashes.Add(h);
        }

        foreach (KeyValuePair<string, string> kv in full.ExpectedVolumeCrcs)
        {
            narrowed.ExpectedVolumeCrcs[kv.Key] = kv.Value;
        }

        return narrowed;
    }

    private static RAROptions CloneWith(RAROptions src, IReadOnlyList<VersionRange> versions, IReadOnlyList<RARCommandLineArgument[]> args) =>
        new()
        {
            SetFileArchiveAttribute = src.SetFileArchiveAttribute,
            SetFileNotContentIndexedAttribute = src.SetFileNotContentIndexedAttribute,
            CommandLineArguments = [.. args],
            RARVersions = [.. versions],
            DeleteRARFiles = src.DeleteRARFiles,
            DeleteDuplicateCRCFiles = src.DeleteDuplicateCRCFiles,
            StopOnFirstMatch = src.StopOnFirstMatch,
            CompleteAllVolumes = src.CompleteAllVolumes,
            RenameToOriginalNames = src.RenameToOriginalNames,
            OriginalRarFileNames = [.. src.OriginalRarFileNames],
            ArchiveFileCrcs = new Dictionary<string, string>(src.ArchiveFileCrcs, StringComparer.OrdinalIgnoreCase),
            ArchiveFilePaths = new HashSet<string>(src.ArchiveFilePaths, StringComparer.OrdinalIgnoreCase),
            ArchiveDirectoryPaths = new HashSet<string>(src.ArchiveDirectoryPaths, StringComparer.OrdinalIgnoreCase),
            DirectoryTimestamps = new Dictionary<string, DateTime>(src.DirectoryTimestamps, StringComparer.OrdinalIgnoreCase),
            DirectoryCreationTimes = new Dictionary<string, DateTime>(src.DirectoryCreationTimes, StringComparer.OrdinalIgnoreCase),
            DirectoryAccessTimes = new Dictionary<string, DateTime>(src.DirectoryAccessTimes, StringComparer.OrdinalIgnoreCase),
            FileTimestamps = new Dictionary<string, DateTime>(src.FileTimestamps, StringComparer.OrdinalIgnoreCase),
            FileCreationTimes = new Dictionary<string, DateTime>(src.FileCreationTimes, StringComparer.OrdinalIgnoreCase),
            FileAccessTimes = new Dictionary<string, DateTime>(src.FileAccessTimes, StringComparer.OrdinalIgnoreCase),
            EnableHostOSPatching = src.EnableHostOSPatching,
            DetectedFileHostOS = src.DetectedFileHostOS,
            DetectedFileAttributes = src.DetectedFileAttributes,
            DetectedLargeFlag = src.DetectedLargeFlag,
            DetectedHighPackSize = src.DetectedHighPackSize,
            DetectedHighUnpSize = src.DetectedHighUnpSize,
            UseOldVolumeNaming = src.UseOldVolumeNaming,
            ArchiveComment = src.ArchiveComment,
            ArchiveCommentBytes = src.ArchiveCommentBytes,
            CmtCompressedData = src.CmtCompressedData,
            CmtCompressionMethod = src.CmtCompressionMethod,
            DetectedCmtHostOS = src.DetectedCmtHostOS,
            DetectedCmtFileTime = src.DetectedCmtFileTime,
            DetectedCmtFileAttributes = src.DetectedCmtFileAttributes,
            CustomPackerDetected = src.CustomPackerDetected,
            SRRFilePath = src.SRRFilePath,
        };

    private static string Sanitize(string key)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            key = key.Replace(c, '_');
        }

        return key.Replace('/', '_');
    }
}
