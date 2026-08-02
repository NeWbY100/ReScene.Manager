using ReScene.App.Core.Services;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Test matrix for rescue-scoped music coverage, sample detection (both phases), and gated
/// loose-RAR discovery, one Fact per row (see
/// docs/superpowers/plans/2026-07-19-multiset-srr-creation.md). Extends
/// <see cref="ReleaseScannerMainSetTests"/>'s decision tree — none of rules 1-7 or the rescue
/// mechanics are re-tested here.
/// </summary>
public class ReleaseScannerMediaTests : TempDirTestBase
{
    private string CreateRoot(string releaseName)
    {
        string root = Path.Combine(TempDir, releaseName);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string WriteSfv(string path, params string[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, entries.Select(e => $"{e} 00000000"));
        return path;
    }

    private static string Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    // --- Rescue-scoped music ---------------------------------------------------------------------

    [Fact]
    public void MusicSfv_SurvivesRules1To7_BecomesMainSet_AlongsideRarSfv()
    {
        // has_music runs ONLY inside the zero-survivor rescue — an SFV that survives rules 1-7 on
        // its own is a MAIN set even though it lists a music file.
        string root = CreateRoot("Some.Release-GRP");
        string rarSfv = WriteSfv(Path.Combine(root, "CD1", "a.sfv"), "a.rar");
        string mp3Sfv = WriteSfv(Path.Combine(root, "x.mp3.sfv"), "t.mp3");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal(2, result.MainSets.Count);
        Assert.Contains(result.MainSets, s => s.SfvOrRarPath == rarSfv);
        Assert.Contains(result.MainSets, s => s.SfvOrRarPath == mp3Sfv);
        Assert.Empty(result.MusicSfvs);
    }

    [Fact]
    public void Rescue_SingleMusicEntry_RoutesToMusicSfvs_WithWarning()
    {
        // remove_unwanted_sfvs rescue fallback [DIVERGENCE]: a single music entry routes to
        // MusicSfvs with a warning instead of being admitted as an ordinary main set.
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "grp-subs.sfv"), "t.mp3");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Empty(result.MainSets);
        Assert.Equal([sfv], result.MusicSfvs);
        Assert.Contains(result.Warnings, w => w.Contains(sfv, StringComparison.Ordinal));
    }

    [Fact]
    public void Rescue_MultiEntry_ReadmittedAsMain()
    {
        // remove_unwanted_sfvs rescue fallback: >1 entry -> main, regardless of content
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "grp-subs.sfv"), "a.rar", "b.rar");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Single(result.MainSets);
        Assert.Equal(sfv, result.MainSets[0].SfvOrRarPath);
        Assert.Empty(result.MusicSfvs);
    }

    [Fact]
    public void Rescue_UppercaseMp3Extension_NotMusic_CaseSensitiveEndsWith_StaysExcluded()
    {
        // has_music: case-SENSITIVE endswith. "t.MP3" doesn't end with ".mp3", so the music branch
        // never fires; with only 1 entry the >1-entry branch doesn't fire either, so the sfv is
        // never rescued and keeps its original excluded destination.
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "grp-subs.sfv"), "t.MP3");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Empty(result.MainSets);
        Assert.Empty(result.MusicSfvs);
        Assert.Equal([sfv], result.SubtitleSfvs);
    }

    // --- Samples, phase 1 (path/sibling heuristic) -------------------------------------------------

    [Fact]
    public void SampleDirectory_VideoFile_IsSample()
    {
        // get_sample_files: "sample" in the path, case-insensitive
        string root = CreateRoot("Some.Release-GRP");
        string clip = Touch(Path.Combine(root, "Sample", "clip.avi"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal([clip], result.SampleFiles);
    }

    [Fact]
    public void FileNameContainsSample_IsSample()
    {
        // get_sample_files (same "sample"-in-path rule, applied to the file name instead of a
        // directory)
        string root = CreateRoot("Some.Release-GRP");
        string clip = Touch(Path.Combine(root, "movie.sample.mkv"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal([clip], result.SampleFiles);
    }

    [Fact]
    public void SiblingSfv_LiteralSliceMatch_ThreeCharExtension_IsSample()
    {
        // get_sample_files: `sample[:-4] + ".sfv"` — for a 3-char ext (".avi"), the 4-char slice
        // strips exactly the extension: "clip.avi" -> "clip.sfv".
        string root = CreateRoot("Some.Release-GRP");
        string clip = Touch(Path.Combine(root, "clip.avi"));
        WriteSfv(Path.Combine(root, "clip.sfv"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal([clip], result.SampleFiles);
    }

    [Fact]
    public void SiblingSfv_LiteralSlice_FourCharExtension_QuirkComputesWrongName_NotSample()
    {
        // get_sample_files: for a 4-char ext (".m2ts"), the slice strips only 4 of its 5
        // characters, computing "clip." + ".sfv" = "clip..sfv" (double dot), NOT the normal
        // "clip.sfv" sibling created here. The quirk is preserved verbatim, so this normal sibling
        // does not satisfy the check.
        string root = CreateRoot("Some.Release-GRP");
        Touch(Path.Combine(root, "clip.m2ts"));
        WriteSfv(Path.Combine(root, "clip.sfv"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Empty(result.SampleFiles);
    }

    [Fact]
    public void SiblingSfv_LiteralSlice_FourCharExtension_DoubleDotSiblingExists_IsSample()
    {
        // get_sample_files: the quirky computed name, actually created on disk, DOES satisfy the
        // check.
        string root = CreateRoot("Some.Release-GRP");
        string clip = Touch(Path.Combine(root, "clip.m2ts"));
        WriteSfv(Path.Combine(root, "clip..sfv"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal([clip], result.SampleFiles);
    }

    // --- Samples, phase 2 (SFV-entry basename cross-reference) --------------------------------------

    [Fact]
    public void Phase2_BasenameListedInSfv_IsSample()
    {
        // get_sample_files: musicvideo/multi-part MKV cross-reference
        string root = CreateRoot("Some.Release-GRP");
        string video = Touch(Path.Combine(root, "video.mkv"));
        WriteSfv(Path.Combine(root, "CD1", "a.sfv"), "video.mkv");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal([video], result.SampleFiles);
    }

    [Fact]
    public void Phase2_CaseMismatch_NotSample()
    {
        // get_sample_files: `os.path.basename(nsample) in sfv_stored_files` — a plain `in`
        // membership test on Python strings is case-sensitive.
        string root = CreateRoot("Some.Release-GRP");
        Touch(Path.Combine(root, "VIDEO.mkv"));
        WriteSfv(Path.Combine(root, "CD1", "a.sfv"), "video.mkv");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Empty(result.SampleFiles);
    }

    // --- Gated loose-RAR discovery -------------------------------------------------------------------

    [Fact]
    public void AnySfvPresent_DisablesLooseRarDiscovery()
    {
        // [DIVERGENCE: extension] loose discovery only fires when zero SFVs exist anywhere under
        // the root; a wholly unrelated SFV elsewhere still disables it.
        string root = CreateRoot("Some.Release-GRP");
        WriteSfv(Path.Combine(root, "CD1", "a.sfv"), "a.rar");
        Touch(Path.Combine(root, "CD9", "x.rar"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Single(result.MainSets);
        Assert.DoesNotContain(result.MainSets, s => s.SfvOrRarPath.EndsWith("x.rar", StringComparison.Ordinal));
    }

    [Fact]
    public void LooseRarDiscovery_FirstVolumeOnly_ExcludesSubsDir()
    {
        // get_start_rar_files: a lone continuation volume (.r00) is never a set on its own, and a
        // RAR in a rule-3-excluded directory (Subs) is not discovered even though it IS a first
        // volume.
        string root = CreateRoot("Some.Release-GRP");
        string aRar = Touch(Path.Combine(root, "CD1", "a.rar"));
        Touch(Path.Combine(root, "CD1", "a.r00"));
        Touch(Path.Combine(root, "Subs", "s.rar"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Single(result.MainSets);
        Assert.Equal(aRar, result.MainSets[0].SfvOrRarPath);
        Assert.Equal("CD1/a.rar", result.MainSets[0].RelativeName);
    }

    [Fact]
    public void LooseRarDiscovery_ExcludesProofDir()
    {
        // The design spec's loose-discovery exclusion rules include rule 4 (proof pardir) — a
        // first-volume .rar living in a Proof/ directory must not be discovered as a loose-RAR set
        // (a proof RAR is never a release set).
        string root = CreateRoot("Some.Release-GRP");
        Touch(Path.Combine(root, "Proof", "p.rar"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Empty(result.MainSets);
    }

    [Fact]
    public void LooseRarDiscovery_ChainGrouping_IsCaseInsensitive()
    {
        // The archive-set key ("a" from "a.part01.rar", "A" from "A.part02.rar") must group
        // case-insensitively, matching SRRWriter.ResolveVolumesAsync's equivalent dictionary — a
        // case-sensitive comparer would split these into two singleton chains, both independently
        // passing the first-volume ".rar" check and wrongly emitting the non-first "A.part02.rar"
        // as its own set.
        string root = CreateRoot("Some.Release-GRP");
        string part1 = Touch(Path.Combine(root, "a.part01.rar"));
        Touch(Path.Combine(root, "A.part02.rar"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Single(result.MainSets);
        Assert.Equal(part1, result.MainSets[0].SfvOrRarPath);
    }

    [Fact]
    public void LooseRarDiscovery_OrdersByFirstVolumeTraversalPosition_NotFirstEncounteredVolume()
    {
        // Chain "a" ({a.r00, a.rar}) is first ENCOUNTERED at a.r00's early traversal position, but
        // chain "a.r00x" ({a.r00x.rar}, a distinct base name) has its own (only) volume sort
        // between a.r00 and a.rar. The emitted order must follow each chain's TRUE FIRST VOLUME's
        // traversal position (a.r00x.rar, then a.rar) — not the position at which each chain was
        // first seen (which would wrongly emit a.rar first).
        string root = CreateRoot("Some.Release-GRP");
        Touch(Path.Combine(root, "a.r00"));
        string aR00x = Touch(Path.Combine(root, "a.r00x.rar"));
        string aRar = Touch(Path.Combine(root, "a.rar"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal(2, result.MainSets.Count);
        Assert.Equal(aR00x, result.MainSets[0].SfvOrRarPath);
        Assert.Equal(aRar, result.MainSets[1].SfvOrRarPath);
    }

    [Fact]
    public void EmptyTree_NoSfvsNoRars_ZeroMainSets_NoCrash()
    {
        string root = CreateRoot("Some.Release-GRP");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Empty(result.MainSets);
        Assert.Empty(result.SampleFiles);
    }
}
