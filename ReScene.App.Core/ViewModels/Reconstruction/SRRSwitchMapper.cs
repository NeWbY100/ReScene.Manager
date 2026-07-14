using ReScene.SRR;

namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>
/// Pure mapping from an imported <see cref="SRRFile"/> to the subset of RAR switch toggles the SRR
/// actually specifies (compression method, dictionary size, solid flag, archive format). The result
/// is a <em>partial</em> diff: each group is null when the SRR carries no information for it, so the
/// view-model leaves the corresponding toggles untouched rather than resetting them to defaults.
/// The mapper neither logs nor mutates bound state; the view-model applies the diff and emits the
/// import log lines, preserving their exact text and ordering.
/// </summary>
internal static class SRRSwitchMapper
{
    /// <summary>Compression method (-m0..-m5) the SRR specifies, plus its log label.</summary>
    public readonly record struct CompressionMap(int Method, string LogName);

    /// <summary>Dictionary size selection the SRR specifies, plus the size for the log line.</summary>
    public readonly record struct DictionaryMap(DictionarySwitch Switch, int SizeKb);

    /// <summary>Archive format (-ma4/-ma5) the SRR specifies, plus its log line (null = RAR7, no -ma).</summary>
    public readonly record struct FormatMap(bool MA4, bool MA5, string LogLine);

    /// <summary>Which single dictionary-size toggle to enable, or <see cref="None"/> when the SRR's dictionary size matches no known switch.</summary>
    public enum DictionarySwitch
    {
        None,
        MD64K,
        MD128K,
        MD256K,
        MD512K,
        MD1024K,
        MD2048K,
        MD4096K,
        MD8M,
        MD16M,
        MD32M,
        MD64M,
        MD128M,
        MD256M,
        MD512M,
        MD1G,
    }

    /// <summary>
    /// The partial set of switch values an SRR specifies. Every member is null when the SRR carries
    /// no information for that group, so applying the diff never clobbers an unspecified toggle.
    /// </summary>
    public readonly record struct SwitchDiff(
        CompressionMap? Compression,
        DictionaryMap? Dictionary,
        bool? SwitchS,
        bool? SwitchSDash,
        FormatMap? Format);

    private static readonly string[] _compressionNames = ["Store", "Fastest", "Fast", "Normal", "Good", "Best"];

    /// <summary>Builds the partial switch diff from the SRR's detected metadata.</summary>
    public static SwitchDiff Map(SRRFile srr) => new(
        Compression: MapCompression(srr),
        Dictionary: MapDictionary(srr),
        SwitchS: srr.IsSolidArchive,
        SwitchSDash: srr.IsSolidArchive.HasValue ? !srr.IsSolidArchive.Value : null,
        Format: MapFormat(srr));

    private static CompressionMap? MapCompression(SRRFile srr)
    {
        if (!srr.CompressionMethod.HasValue)
        {
            return null;
        }

        // RAR4 reports the method as a raw 0..5 index; RAR5 reports it as the ASCII digit
        // '0'..'5' (0x30..0x35). Normalize both encodings to the same 0..5 index (#11).
        int method = RarMetadataNormalizer.NormalizeCompressionMethod(srr.CompressionMethod.Value);
        if (method < 0)
        {
            return null;
        }

        return new CompressionMap(method, _compressionNames[method]);
    }

    private static DictionaryMap? MapDictionary(SRRFile srr)
    {
        if (!srr.DictionarySize.HasValue)
        {
            return null;
        }

        int size = srr.DictionarySize.Value;

        // Covers both the small (64K..4096K) and large (8M..1G) dictionary sizes RAR5/RAR7
        // archives can use; unrecognized sizes still emit the group so the clear-then-set runs,
        // just with no switch re-enabled (#12).
        DictionarySwitch which = RarMetadataNormalizer.DictionarySwitchFor(size);

        return new DictionaryMap(which, size);
    }

    private static FormatMap? MapFormat(SRRFile srr)
    {
        if (!srr.RARVersion.HasValue)
        {
            return null;
        }

        if (srr.RARVersion.Value < 50)
        {
            return new FormatMap(MA4: true, MA5: false, "Archive format: RAR4 (-ma4)");
        }

        if (srr.RARVersion.Value < 70)
        {
            return new FormatMap(MA4: false, MA5: true, "Archive format: RAR5 (-ma5)");
        }

        return new FormatMap(MA4: false, MA5: false, "Archive format: RAR7");
    }
}
