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
/// <see cref="SRRArchiveSet"/> instead.
/// </summary>
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
    public static IReadOnlyList<SRRArchiveSet> ResolveSets(
        IReadOnlyList<SRRArchiveSet> archiveSets,
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
            IReadOnlyList<SRRArchiveSet> reloaded = SRRFile.Load(srrFilePath).ArchiveSets;
            if (reloaded.Count > 0)
            {
                return reloaded;
            }
        }

        var flat = new SRRArchiveSet { Key = "", Directory = "" };
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
        SRRArchiveSet set, byte[]? embeddedSfvBytes, SFVFile? userSfv)
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
        SRRArchiveSet set,
        SharedReconstructionSettings shared,
        Dictionary<string, string> expectedVolumeCrcs)
    {
        var options = new BruteForceOptions(shared.WinRARPath, shared.ReleasePath, WorkRootFor(shared, set))
        {
            HashType = shared.HashType,
            RAROptions = new RAROptions
            {
                SetFileArchiveAttribute = shared.SetFileArchiveAttribute,
                SetFileNotContentIndexedAttribute = shared.SetFileNotContentIndexedAttribute,
                CommandLineArguments = [.. shared.CommandLineArguments],
                RARVersions = [.. shared.RARVersions],
                AllowedVersionFolders = [.. shared.SelectedVersionFolders],
                DeleteRARFiles = shared.DeleteRARFiles,
                DeleteDuplicateCRCFiles = shared.DeleteDuplicateCRCFiles,
                StopOnFirstMatch = shared.StopOnFirstMatch,
                CompleteAllVolumes = shared.CompleteAllVolumes,
                RenameToOriginalNames = shared.RenameToReleaseNames,
                OriginalRARFileNames = [.. set.VolumeNames],
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
    public static string WorkRootFor(SharedReconstructionSettings shared, SRRArchiveSet set) =>
        string.IsNullOrEmpty(set.Key)
            ? shared.OutputPath
            : Path.Combine(shared.OutputPath, ".rescene-work", Sanitize(set.Key));

    /// <summary>Narrows options to a single winning combo (one version, one args set) for seeding.</summary>
    public static BruteForceOptions NarrowToCombo(BruteForceOptions full, WinningCombo combo)
    {
        var narrowed = new BruteForceOptions(full.RARInstallationsDirectoryPath, full.ReleaseDirectoryPath, full.OutputDirectoryPath)
        {
            HashType = full.HashType,
            // VersionRange end is exclusive (InRange is `>= Start && < End`), so a single version
            // is [v, v+1) — matching RARCommandLineBuilder.BuildVersionRanges. A [v, v) range is
            // empty and would exclude the winning version's own folder, making the seed run test
            // nothing and always fall back to the full matrix.
            RAROptions = CloneWith(full.RAROptions,
                versions: [new VersionRange(combo.Version, combo.Version + 1)],
                args: [[.. combo.Args]]),
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
            // Preserve the folder allow-list: the narrowed seed run must still respect the user's
            // folder selection (now narrowed to the winning version's folder).
            AllowedVersionFolders = [.. src.AllowedVersionFolders],
            DeleteRARFiles = src.DeleteRARFiles,
            DeleteDuplicateCRCFiles = src.DeleteDuplicateCRCFiles,
            StopOnFirstMatch = src.StopOnFirstMatch,
            CompleteAllVolumes = src.CompleteAllVolumes,
            RenameToOriginalNames = src.RenameToOriginalNames,
            OriginalRARFileNames = [.. src.OriginalRARFileNames],
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
