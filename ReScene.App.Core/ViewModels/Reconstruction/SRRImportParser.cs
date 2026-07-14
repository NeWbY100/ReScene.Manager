using ReScene.App.Core.Helpers;
using ReScene.SRR;

namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>
/// Reads an SRR file and derives the imported/detected reconstruction state plus the display
/// strings shown on the wizard's import step. Pure: it neither logs, shows dialogs, nor mutates
/// any bound option — the view-model performs those steps when it applies the result, preserving
/// the exact log/dialog ordering.
/// </summary>
internal static class SRRImportParser
{
    public static ImportedSRRInfo Parse(SRRFile srr, string path)
    {
        bool hasRARReconstructionInfo = srr.RARFiles.Count > 0
            || srr.ArchivedFiles.Count > 0
            || srr.CompressionMethod.HasValue;

        (CustomPackerType packerType, string? packerWarning) = DescribeCustomPacker(srr);

        return new ImportedSRRInfo
        {
            SRR = srr,
            SRRFilePath = path,
            HasRARReconstructionInfo = hasRARReconstructionInfo,

            CustomPackerType = packerType,
            CustomPackerWarning = packerWarning,

            ArchiveFiles = new HashSet<string>(srr.ArchivedFiles, StringComparer.OrdinalIgnoreCase),
            ArchiveDirectories = new HashSet<string>(srr.ArchivedDirectories, StringComparer.OrdinalIgnoreCase),
            DirTimestamps = new Dictionary<string, DateTime>(srr.ArchivedDirectoryTimestamps, StringComparer.OrdinalIgnoreCase),
            DirCreationTimes = new Dictionary<string, DateTime>(srr.ArchivedDirectoryCreationTimes, StringComparer.OrdinalIgnoreCase),
            DirAccessTimes = new Dictionary<string, DateTime>(srr.ArchivedDirectoryAccessTimes, StringComparer.OrdinalIgnoreCase),
            FileTimestamps = new Dictionary<string, DateTime>(srr.ArchivedFileTimestamps, StringComparer.OrdinalIgnoreCase),
            FileCreationTimes = new Dictionary<string, DateTime>(srr.ArchivedFileCreationTimes, StringComparer.OrdinalIgnoreCase),
            FileAccessTimes = new Dictionary<string, DateTime>(srr.ArchivedFileAccessTimes, StringComparer.OrdinalIgnoreCase),
            ArchiveFileCrcs = new Dictionary<string, string>(srr.ArchivedFileCrcs, StringComparer.OrdinalIgnoreCase),
            OriginalRARFileNames = [.. srr.RARFiles.Select(r => r.FileName)],
            ArchiveSets = srr.ArchiveSets,
            ArchiveComment = srr.ArchiveComment,
            ArchiveCommentBytes = srr.ArchiveCommentBytes?.ToArray(),
            CmtCompressedData = srr.CmtCompressedData?.ToArray(),
            CmtCompressionMethod = srr.CmtCompressionMethod,

            DetectedFileHostOS = srr.DetectedHostOS,
            DetectedFileAttributes = srr.DetectedFileAttributes,
            DetectedCmtHostOS = srr.CmtHostOS,
            DetectedCmtFileTime = srr.CmtFileTimeDOS,
            DetectedCmtFileAttributes = srr.CmtFileAttributes,
            DetectedLargeFlag = srr.HasLargeFiles,
            DetectedHighPackSize = srr.DetectedHighPackSize,
            DetectedHighUnpSize = srr.DetectedHighUnpSize,

            DisplayName = Path.GetFileName(path),
            DisplayAppName = DescribeAppName(srr),
            DisplayRARVolumeText = srr.RARFiles.Count == 1 ? "1 volume" : $"{srr.RARFiles.Count} volumes",
            DisplayArchivedFilesText = srr.ArchivedFiles.Count == 1 ? "1 file" : $"{srr.ArchivedFiles.Count} files",
            DisplayCompressionText = DescribeCompression(srr.CompressionMethod),
            DisplayStoredFilesText = DescribeStoredFiles(srr),
        };
    }

    private static (CustomPackerType Type, string? Warning) DescribeCustomPacker(SRRFile srr)
    {
        if (!srr.HasCustomPackerHeaders)
        {
            return (CustomPackerType.None, null);
        }

        string groups = srr.CustomPackerDetected switch
        {
            CustomPackerType.AllOnesWithLargeFlag => "RELOADED, HI2U, 0x0007, 0x0815",
            CustomPackerType.MaxUint32WithoutLargeFlag => "QCF",
            _ => "Unknown"
        };

        string warning = $"Custom RAR packer detected ({srr.CustomPackerDetected}) — brute-forcing is not possible. " +
            $"Direct SRR reconstruction will be used instead. Known groups: {groups}.";

        return (srr.CustomPackerDetected, warning);
    }

    private static string DescribeAppName(SRRFile srr)
    {
        string? app = srr.HeaderBlock?.HasAppName == true ? srr.HeaderBlock?.AppName : null;
        return string.IsNullOrWhiteSpace(app) ? "Unknown" : app;
    }

    private static string DescribeStoredFiles(SRRFile srr) => srr.StoredFiles.Count == 0
        ? "None"
        : string.Join(Environment.NewLine, srr.StoredFiles.Select(
            s => $"{Path.GetFileName(s.FileName)} ({FormatUtilities.FormatSize(s.FileLength)})"));

    /// <summary>
    /// Friendly label for a RAR compression method (0–5), mirroring the import log's names. Routes
    /// through <see cref="RarMetadataNormalizer.NormalizeCompressionMethod"/> so a RAR5-reported
    /// method (ASCII '0'..'5', i.e. 0x30..0x35) describes the same as an equivalent raw 0..5 one,
    /// instead of falling through to the "Method N" catch-all (#11).
    /// </summary>
    public static string DescribeCompression(int? method)
    {
        if (method is null)
        {
            return "Unknown";
        }

        return RarMetadataNormalizer.NormalizeCompressionMethod(method.Value) switch
        {
            0 => "Store / no compression (-m0)",
            1 => "Fastest (-m1)",
            2 => "Fast (-m2)",
            3 => "Normal (-m3)",
            4 => "Good (-m4)",
            5 => "Best (-m5)",
            _ => $"Method {method}",
        };
    }
}
