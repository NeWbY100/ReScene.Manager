using ReScene.Core;

namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>
/// Maps RAR archive formats to the WinRAR executable versions that can produce them, and encodes
/// the engine's actual <c>-ma4</c>/<c>-ma5</c> command-line policy for coercing an executable to a
/// non-native format. Pure: no I/O, no dependency on the brute-force engine — <see cref="SelectFor"/>
/// intersects this policy with a caller-supplied installed-version list, or, for a no-scan run,
/// clips the user's ranges to the format-capable bounds. Task 11 consumes <see cref="SelectFor"/>
/// to emit the actual <c>-ma4</c>/<c>-ma5</c> <c>RARCommandLineArgument</c>s (bounded
/// <c>Min=500,Max=699</c>, matching <see cref="RARCommandLineBuilder"/>) so they apply
/// per-executable via <c>RARVersionSelector.FilterArgumentsForVersion</c>.
/// </summary>
internal static class RarFormatCompatibility
{
    // Mirrors ReScene.Core.RARVersionThresholds, which is internal to the lib and cannot be
    // referenced from App.Core.
    private const int Rar5Floor = 500;
    private const int Rar7Floor = 700;

    /// <summary>The RAR archive format a WinRAR executable produces, natively or via <c>-ma4</c>/<c>-ma5</c>.</summary>
    public enum RarFormat
    {
        Rar4,
        Rar5,
        Rar7,
    }

    /// <summary>
    /// The executable versions and folders capable of a requested format, intersected with the
    /// user's selection, plus the aggregate <c>-ma4</c>/<c>-ma5</c> requirement over the surviving
    /// selection. <see cref="Ranges"/> is one tight range per surviving executable version when
    /// <see cref="Folders"/> is populated (a scanned run), or the user's ranges clipped to the
    /// format-capable bounds when <see cref="Folders"/> is empty (a no-scan run).
    /// </summary>
    public readonly record struct FormatSelection(
        IReadOnlyList<VersionRange> Ranges,
        IReadOnlyList<string> Folders,
        bool NeedsMa4,
        bool NeedsMa5,
        bool Empty);

    /// <summary>
    /// Maps an SRR-embedded RAR "unpack version" (e.g. 29, 50, 70) to its archive format —
    /// matching <c>SRRSwitchMapper.MapFormat</c>.
    /// </summary>
    public static RarFormat FormatForUnpackVersion(int unpackVersion)
    {
        if (unpackVersion < 50)
        {
            return RarFormat.Rar4;
        }

        if (unpackVersion < 70)
        {
            return RarFormat.Rar5;
        }

        return RarFormat.Rar7;
    }

    /// <summary>
    /// Whether a WinRAR executable version can produce the given archive format, and, if so,
    /// whether <c>-ma4</c>/<c>-ma5</c> must be added to coerce it — native production needs neither.
    /// </summary>
    public static bool ExecutableSupports(int exeVersion, RarFormat fmt, out bool needsMa4, out bool needsMa5)
    {
        needsMa4 = false;
        needsMa5 = false;

        switch (fmt)
        {
            case RarFormat.Rar4:
                if (exeVersion < Rar5Floor)
                {
                    return true;
                }

                if (exeVersion < Rar7Floor)
                {
                    needsMa4 = true;
                    return true;
                }

                return false;

            case RarFormat.Rar5:
                if (exeVersion is >= Rar5Floor and < Rar7Floor)
                {
                    needsMa5 = true;
                    return true;
                }

                return false;

            case RarFormat.Rar7:
                return exeVersion >= Rar7Floor;

            default:
                return false;
        }
    }

    /// <summary>
    /// Intersects the format-capable executable versions with the user's selected ranges/folders.
    /// When <paramref name="installed"/> is non-empty, keeps each installed executable that is
    /// format-capable, falls within a selected range, and (when <paramref name="userFolders"/> is
    /// non-empty) is explicitly selected by folder name — then collapses the surviving versions
    /// into one tight range each and lists the surviving folder names. When
    /// <paramref name="installed"/> is empty (a no-scan run — decided by the caller; this method
    /// never reads scan state), clips <paramref name="userRanges"/> to the format-capable bounds
    /// instead and returns empty <see cref="FormatSelection.Folders"/>.
    /// </summary>
    public static FormatSelection SelectFor(
        RarFormat fmt,
        IReadOnlyList<VersionRange> userRanges,
        IReadOnlyList<string> userFolders,
        IReadOnlyList<InstalledRARVersion> installed)
    {
        VersionRange capableBand = fmt switch
        {
            RarFormat.Rar4 => new VersionRange(0, Rar7Floor),
            RarFormat.Rar5 => new VersionRange(Rar5Floor, Rar7Floor),
            RarFormat.Rar7 => new VersionRange(Rar7Floor, int.MaxValue),
            _ => throw new ArgumentOutOfRangeException(nameof(fmt), fmt, message: null),
        };

        List<VersionRange> ranges;
        List<string> folders;

        if (installed.Count == 0)
        {
            ranges = [.. userRanges
                .Select(r => new VersionRange(Math.Max(r.Start, capableBand.Start), Math.Min(r.End, capableBand.End)))
                .Where(r => r.Start < r.End)];
            folders = [];
        }
        else
        {
            HashSet<string>? allowedFolders = userFolders.Count > 0
                ? new HashSet<string>(userFolders, StringComparer.OrdinalIgnoreCase)
                : null;

            List<InstalledRARVersion> surviving = [.. installed
                .Where(v => capableBand.InRange(v.Version))
                .Where(v => userRanges.Any(r => r.InRange(v.Version)))
                .Where(v => allowedFolders is null || allowedFolders.Contains(v.FolderName))];

            ranges = [.. surviving
                .Select(v => v.Version)
                .Distinct()
                .OrderBy(v => v)
                .Select(v => new VersionRange(v, v + 1))];
            folders = [.. surviving.Select(v => v.FolderName)];
        }

        bool needsMa4 = fmt == RarFormat.Rar4 && ranges.Any(r => Overlaps(r, new VersionRange(Rar5Floor, Rar7Floor)));
        bool needsMa5 = fmt == RarFormat.Rar5;

        return new FormatSelection(ranges, folders, needsMa4, needsMa5, Empty: ranges.Count == 0);
    }

    private static bool Overlaps(VersionRange a, VersionRange b) => a.Start < b.End && b.Start < a.End;
}
