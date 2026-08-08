using ReScene.Core;
using ReScene.Core.Cryptography;
using ReScene.Core.Diagnostics;
using ReScene.Core.IO;
using ReScene.SRR;

namespace ReScene.App.Core.ViewModels.Reconstruction;

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
    /// Builds the per-volume expected CRC32 map for a set, keyed by each volume's OWN canonical
    /// dir-qualified key (never a basename alias — exactly one canonical key per volume, #9):
    /// embedded SFV bytes first (when present), then the user verification <paramref name="snapshot"/>
    /// fills any volume the embedded SFV did not cover. Empty when <paramref name="snapshot"/> is a
    /// SHA1 snapshot and there is no embedded SFV — SHA1 entries feed <c>options.Hashes</c> only.
    /// </summary>
    public static Dictionary<string, string> BuildExpectedVolumeCrcs(
        SRRArchiveSet set, byte[]? embeddedSfvBytes, VerificationSnapshot? snapshot)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (embeddedSfvBytes is { Length: > 0 })
        {
            var embedded = SFVFile.ParseBytes(embeddedSfvBytes, tolerant: true);
            var embeddedSnapshot = new VerificationSnapshot(HashType.CRC32,
                [.. embedded.Entries.Select(e => (e.FileName, e.CRC))]);

            foreach (KeyValuePair<string, string> kv in embeddedSnapshot.HashesForVolumes(set.VolumeNames))
            {
                result[kv.Key] = kv.Value;
            }
        }

        if (result.Count < set.VolumeNames.Count && snapshot is not null)
        {
            foreach (KeyValuePair<string, string> kv in snapshot.HashesForVolumes(set.VolumeNames))
            {
                result.TryAdd(kv.Key, kv.Value);
            }
        }

        return result;
    }

    /// <summary>
    /// Builds the brute-force options for one set, using only its content/names/metadata. The
    /// command/version matrix is this set's own (#6): built via <see cref="ResolveSetMatrix"/>, which
    /// replaces only the switch groups the set's header metadata specifies, off the UI thread when the
    /// caller wraps this call in <c>Task.Run</c> — <paramref name="ct"/> is forwarded to the matrix
    /// builder so a cancelled run is honoured promptly. The first-volume hash gate (#8) and the
    /// archive directories/timestamps (#7) are likewise scoped to this set, falling back to the
    /// release-wide union only for the legacy flat single-set path (<see cref="SRRArchiveSet.Key"/>
    /// empty).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The set's <see cref="SRRArchiveSet.RARVersion"/> is known but no version in the user's
    /// selection can produce its archive format — see <see cref="ResolveSetMatrix"/>. Raised so the
    /// run loop's per-set <c>try</c> records this set as failed without aborting its siblings.
    /// </exception>
    public static BruteForceOptions BuildOptionsForSet(
        SRRArchiveSet set,
        SharedReconstructionSettings shared,
        Dictionary<string, string> expectedVolumeCrcs,
        CancellationToken ct = default)
    {
        (IReadOnlyList<RARCommandLineArgument[]> commandLineArguments,
            IReadOnlyList<VersionRange> versions,
            IReadOnlyList<string> versionFolders) = ResolveSetMatrix(set, shared, ct);

        // The legacy flat single set (no SRR / no per-set parse) has no directory data of its own —
        // it keeps the release-wide union exactly as before (#7).
        bool isFlatSet = string.IsNullOrEmpty(set.Key);

        var options = new BruteForceOptions(shared.WinRARPath, shared.ReleasePath, WorkRootFor(shared, set))
        {
            HashType = shared.HashType,
            RAROptions = new RAROptions
            {
                SetFileArchiveAttribute = shared.SetFileArchiveAttribute,
                SetFileNotContentIndexedAttribute = shared.SetFileNotContentIndexedAttribute,
                CommandLineArguments = commandLineArguments,
                RARVersions = versions,
                AllowedVersionFolders = versionFolders,
                DeleteRARFiles = shared.DeleteRARFiles,
                DeleteDuplicateCRCFiles = shared.DeleteDuplicateCRCFiles,
                StopOnFirstMatch = shared.StopOnFirstMatch,
                CompleteAllVolumes = shared.CompleteAllVolumes,
                RenameToOriginalNames = shared.RenameToReleaseNames,
                OriginalRARFileNames = [.. set.VolumeNames],
                OrderedArchiveFiles = [.. set.ArchivedFilesInOrder],
                ArchiveFileCrcs = new Dictionary<string, string>(set.ArchivedFileCrcs, StringComparer.OrdinalIgnoreCase),
                ArchiveFilePaths = new HashSet<string>(set.ArchivedFiles, StringComparer.OrdinalIgnoreCase),
                ArchiveDirectoryPaths = new HashSet<string>(
                    isFlatSet ? shared.ArchiveDirectories : set.ArchivedDirectories, StringComparer.OrdinalIgnoreCase),
                DirectoryTimestamps = new Dictionary<string, DateTime>(
                    isFlatSet ? shared.DirectoryTimestamps : set.ArchivedDirectoryTimestamps, StringComparer.OrdinalIgnoreCase),
                DirectoryCreationTimes = new Dictionary<string, DateTime>(
                    isFlatSet ? shared.DirectoryCreationTimes : set.ArchivedDirectoryCreationTimes, StringComparer.OrdinalIgnoreCase),
                DirectoryAccessTimes = new Dictionary<string, DateTime>(
                    isFlatSet ? shared.DirectoryAccessTimes : set.ArchivedDirectoryAccessTimes, StringComparer.OrdinalIgnoreCase),
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

        // Seed the cheap first-volume gate (Hashes) with THIS SET's own verification hashes (#8) —
        // pouring every release verification hash into every set's gate would let a produced first
        // volume matching ANOTHER set's CRC be falsely accepted. HashesForVolumes only resolves
        // CRC32 entries (Crc32ByName is empty for a SHA1 snapshot, by design — see
        // VerificationSnapshot), so a SHA1 run has no per-set filter available; it keeps seeding
        // every SHA1 hash exactly as before rather than starving the gate. Then seed this set's own
        // per-volume CRCs, and the full per-volume verification map (ExpectedVolumeCrcs) with this
        // set's CRCs, so the engine's gates are consistent.
        if (shared.HashType == HashType.CRC32)
        {
            foreach (string h in shared.Verification.HashesForVolumes(set.VolumeNames).Values)
            {
                options.Hashes.Add(h);
            }
        }
        else
        {
            foreach (string h in shared.Verification.AllHashes)
            {
                options.Hashes.Add(h);
            }
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
    /// Resolves this set's own command-line matrix and version/folder selection (#6): replaces each
    /// switch group in the global snapshot with the set's own header metadata, field by field, ONLY
    /// for the groups the set's metadata actually specifies — compression, dictionary, and solid each
    /// independently (a compression raw value that normalizes to invalid, T4, counts as not known —
    /// same as the SRR-import diff), plus format/version together (gated on
    /// <see cref="SRRArchiveSet.RARVersion"/>). Every other group — including the version/folder
    /// selection when no format applies — is left exactly as the global snapshot carries it. A set
    /// with no relevant metadata at all returns the global matrix untouched, regardless of
    /// <see cref="SRRArchiveSet.Key"/>.
    /// </summary>
    private static (IReadOnlyList<RARCommandLineArgument[]> Args, IReadOnlyList<VersionRange> Versions, IReadOnlyList<string> Folders)
        ResolveSetMatrix(SRRArchiveSet set, SharedReconstructionSettings shared, CancellationToken ct)
    {
        int normalizedCompression = set.CompressionMethod is int rawCompression
            ? RarMetadataNormalizer.NormalizeCompressionMethod(rawCompression)
            : -1;
        bool hasCompression = normalizedCompression >= 0;
        bool hasDictionary = set.DictionarySize.HasValue;
        bool hasSolid = set.IsSolid.HasValue;
        bool hasFormat = set.RARVersion.HasValue;

        if (!hasCompression && !hasDictionary && !hasSolid && !hasFormat)
        {
            return (shared.CommandLineArguments, shared.RARVersions, shared.SelectedVersionFolders);
        }

        RARSwitchSettings switches = shared.Switches;
        IReadOnlyList<VersionRange> versions = shared.RARVersions;
        IReadOnlyList<string> folders = shared.SelectedVersionFolders;

        if (hasCompression)
        {
            switches = switches with
            {
                SwitchM0 = normalizedCompression == 0,
                SwitchM1 = normalizedCompression == 1,
                SwitchM2 = normalizedCompression == 2,
                SwitchM3 = normalizedCompression == 3,
                SwitchM4 = normalizedCompression == 4,
                SwitchM5 = normalizedCompression == 5,
            };
        }

        if (hasDictionary)
        {
            SRRSwitchMapper.DictionarySwitch which = RarMetadataNormalizer.DictionarySwitchFor(set.DictionarySize!.Value);
            switches = switches with
            {
                SwitchMD64K = which == SRRSwitchMapper.DictionarySwitch.MD64K,
                SwitchMD128K = which == SRRSwitchMapper.DictionarySwitch.MD128K,
                SwitchMD256K = which == SRRSwitchMapper.DictionarySwitch.MD256K,
                SwitchMD512K = which == SRRSwitchMapper.DictionarySwitch.MD512K,
                SwitchMD1024K = which == SRRSwitchMapper.DictionarySwitch.MD1024K,
                SwitchMD2048K = which == SRRSwitchMapper.DictionarySwitch.MD2048K,
                SwitchMD4096K = which == SRRSwitchMapper.DictionarySwitch.MD4096K,
                SwitchMD8M = which == SRRSwitchMapper.DictionarySwitch.MD8M,
                SwitchMD16M = which == SRRSwitchMapper.DictionarySwitch.MD16M,
                SwitchMD32M = which == SRRSwitchMapper.DictionarySwitch.MD32M,
                SwitchMD64M = which == SRRSwitchMapper.DictionarySwitch.MD64M,
                SwitchMD128M = which == SRRSwitchMapper.DictionarySwitch.MD128M,
                SwitchMD256M = which == SRRSwitchMapper.DictionarySwitch.MD256M,
                SwitchMD512M = which == SRRSwitchMapper.DictionarySwitch.MD512M,
                SwitchMD1G = which == SRRSwitchMapper.DictionarySwitch.MD1G,
            };
        }

        if (hasSolid)
        {
            switches = switches with { SwitchS = set.IsSolid!.Value, SwitchSDash = !set.IsSolid!.Value };
        }

        if (hasFormat)
        {
            RarFormatCompatibility.RarFormat format = RarFormatCompatibility.FormatForUnpackVersion(set.RARVersion!.Value);
            RarFormatCompatibility.FormatSelection selection = RarFormatCompatibility.SelectFor(
                format, shared.RARVersions, shared.SelectedVersionFolders, shared.InstalledVersions);

            if (selection.Empty)
            {
                throw new InvalidOperationException($"no selected WinRAR version can produce {FormatLabel(format)}");
            }

            versions = selection.Ranges;
            folders = selection.Folders;
            switches = switches with { SwitchMA4 = selection.NeedsMa4, SwitchMA5 = selection.NeedsMa5 };
        }

        // TODO(-rr): recovery-record (set.HasRecoveryRecord) is not yet threaded into the per-set
        // matrix — deferred, no -rr switch exists in RARCommandLineBuilder.

        IReadOnlyList<RARCommandLineArgument[]> args = RARCommandLineBuilder.BuildCommandLineArguments(switches, ct);
        return (args, versions, folders);
    }

    private static string FormatLabel(RarFormatCompatibility.RarFormat format) => format switch
    {
        RarFormatCompatibility.RarFormat.Rar4 => "RAR4",
        RarFormatCompatibility.RarFormat.Rar5 => "RAR5",
        RarFormatCompatibility.RarFormat.Rar7 => "RAR7",
        _ => format.ToString(),
    };

    /// <summary>
    /// True when full per-volume verification was requested and is genuinely incomplete:
    /// CompleteAllVolumes is on, the verification is CRC32-based, at least one expected per-volume
    /// CRC was found, but it does not cover every volume. For SHA1 runs (no per-volume CRC source)
    /// or when no expected CRC matched at all, this returns false so the engine still runs and
    /// gates on the first volume exactly as before (no regression).
    /// </summary>
    public static bool ShouldSkipUnverifiableSet(bool completeAllVolumes, HashType hashType, int expectedCrcCount, int volumeCount)
        => completeAllVolumes && hashType == HashType.CRC32 && expectedCrcCount > 0 && expectedCrcCount < volumeCount;

    /// <summary>
    /// The working directory for a set's run: <c>OutputPath</c> for the legacy single root set (empty
    /// key — byte-identical behaviour, its output already lands under <c>OutputPath\output</c>), else a
    /// guarded, per-key isolated scratch child under <c>OutputPath\.rescene-work</c> that its verified
    /// output is later relocated out of.
    /// </summary>
    public static string WorkRootFor(SharedReconstructionSettings shared, SRRArchiveSet set) =>
        string.IsNullOrEmpty(set.Key)
            ? shared.OutputPath
            : ReconstructionPathGuard.ResolveScratchChild(shared.OutputPath, set.Key);

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
            OrderedArchiveFiles = [.. src.OrderedArchiveFiles],
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
}
