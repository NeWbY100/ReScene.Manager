using ReScene.App.Core.ViewModels.Reconstruction;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Pins <see cref="RarMetadataNormalizer"/>, the shared normalization <see cref="SRRSwitchMapper"/>
/// and <see cref="SRRImportParser"/> both route through so a RAR5-sourced SRR (which reports its
/// compression method as the ASCII digit '0'..'5', i.e. 0x30..0x35) maps to the same switches and
/// log/display text as an equivalent RAR4 one (raw method index 0..5), and so large (8 MiB..1 GiB)
/// dictionary sizes resolve to their own switch instead of being dropped (#11, #12).
/// </summary>
public sealed class RarMetadataNormalizerTests
{
    // ── NormalizeCompressionMethod ───────────────────────────────────────

    [Fact]
    public void NormalizeCompressionMethod_RAR5AsciiBest_NormalizesTo5()
    {
        // RAR5 archives encode method "Best" as the ASCII digit '5' (0x35), not the raw index 5.
        Assert.Equal(5, RarMetadataNormalizer.NormalizeCompressionMethod(0x35));
    }

    [Fact]
    public void NormalizeCompressionMethod_RawIndexInRange_PassesThroughUnchanged()
    {
        // RAR4 archives encode the method directly as 0..5; that must pass through unchanged.
        Assert.Equal(3, RarMetadataNormalizer.NormalizeCompressionMethod(3));
    }

    [Fact]
    public void NormalizeCompressionMethod_UnrecognizedValue_ReturnsMinusOne()
    {
        // Neither a raw 0..5 index nor an ASCII '0'..'5' digit: not a valid method.
        Assert.Equal(-1, RarMetadataNormalizer.NormalizeCompressionMethod(0x99));
    }

    [Fact]
    public void NormalizeCompressionMethod_RAR5AsciiStore_NormalizesTo0()
    {
        // Lower boundary of the RAR5 ASCII range: '0' (0x30) normalizes to method 0.
        Assert.Equal(0, RarMetadataNormalizer.NormalizeCompressionMethod(0x30));
    }

    [Fact]
    public void NormalizeCompressionMethod_NegativeValue_ReturnsMinusOne()
    {
        Assert.Equal(-1, RarMetadataNormalizer.NormalizeCompressionMethod(-1));
    }

    // ── DictionarySwitchFor ──────────────────────────────────────────────

    [Fact]
    public void DictionarySwitchFor_1048576Kb_MapsToMD1G()
    {
        // 1048576 KB (1 GiB) is the largest RAR5/RAR7 dictionary size; must map to MD1G, not None (#12).
        Assert.Equal(SRRSwitchMapper.DictionarySwitch.MD1G, RarMetadataNormalizer.DictionarySwitchFor(1048576));
    }

    [Fact]
    public void DictionarySwitchFor_64Kb_MapsToMD64K()
    {
        // Lower boundary of the covered range still resolves correctly.
        Assert.Equal(SRRSwitchMapper.DictionarySwitch.MD64K, RarMetadataNormalizer.DictionarySwitchFor(64));
    }

    [Fact]
    public void DictionarySwitchFor_8192Kb_MapsToMD8M()
    {
        // 8192 KB (8 MiB) is the smallest of the previously-dropped large sizes.
        Assert.Equal(SRRSwitchMapper.DictionarySwitch.MD8M, RarMetadataNormalizer.DictionarySwitchFor(8192));
    }

    [Fact]
    public void DictionarySwitchFor_UnmappedSize_ReturnsNone()
    {
        // A size outside the known switch table still yields None rather than throwing.
        Assert.Equal(SRRSwitchMapper.DictionarySwitch.None, RarMetadataNormalizer.DictionarySwitchFor(100));
    }

    // ── SRRImportParser.DescribeCompression via the shared normalizer ────

    [Fact]
    public void DescribeCompression_RAR5AsciiBest_DescribesAsBest()
    {
        // Same normalization bug as the switch mapper: RAR5's ASCII '5' (0x35) must describe as
        // "Best (-m5)", not fall through to the "Method 53" catch-all.
        Assert.Equal("Best (-m5)", SRRImportParser.DescribeCompression(0x35));
    }

    [Fact]
    public void DescribeCompression_RawIndexInRange_UnaffectedByNormalization()
    {
        Assert.Equal("Normal (-m3)", SRRImportParser.DescribeCompression(3));
    }

    [Fact]
    public void DescribeCompression_NullMethod_ReturnsUnknown()
    {
        Assert.Equal("Unknown", SRRImportParser.DescribeCompression(null));
    }

    [Fact]
    public void DescribeCompression_UnrecognizedValue_FallsBackToRawMethodLabel()
    {
        Assert.Equal("Method 153", SRRImportParser.DescribeCompression(0x99));
    }
}
