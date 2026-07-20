using ReScene.App.Core.Services;
using ReScene.RAR;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Task 5's test matrix (design plan 2026-07-19-multiset-srr-creation.md L879-901): the §2a
/// main-set decision tree (pyrescene-rules-excerpt.txt, <c>remove_unwanted_sfvs</c>), one Fact per
/// row. Proof-related rows drive the injectable <c>proofRarReader</c> seam with fact literals
/// only — <see cref="RarProofInspectorTests"/> (ReScene.Tests) proves the production
/// <see cref="RarProofInspector"/> against real fixture bytes.
/// </summary>
public class ReleaseScannerMainSetTests : TempDirTestBase
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

    [Fact]
    public void TwoCdSfvs_BothBecomeMainSets_InTraversalOrder()
    {
        string root = CreateRoot("Some.Release-GRP");
        WriteSfv(Path.Combine(root, "CD1", "a.sfv"), "a.rar");
        WriteSfv(Path.Combine(root, "CD2", "b.sfv"), "b.rar");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal(2, result.MainSets.Count);
        Assert.Equal("CD1/a.sfv", result.MainSets[0].RelativeName);
        Assert.Equal("CD2/b.sfv", result.MainSets[1].RelativeName);
        Assert.Empty(result.SubtitleSfvs);
        Assert.Empty(result.StoredFiles);
    }

    [Fact]
    public void VobsubName_ReleaseLacksCarveOut_Excluded()
    {
        // excerpt: remove_unwanted_sfvs L312-317 (rule 1)
        string root = CreateRoot("Some.Movie-GRP");
        string sfv = WriteSfv(Path.Combine(root, "x.vobsubs.sfv"), "x.rar");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Empty(result.MainSets);
        Assert.Equal([sfv], result.SubtitleSfvs);
    }

    [Fact]
    public void VobsubName_ReleaseIsSubpack_BecomesMain_AndAlsoQueuedToSubs()
    {
        // rule 1's carve-out (release name contains "subpack") admits the SFV; the release-level
        // subpack/subfix tail then ALSO queues every main SFV to SubtitleSfvs.
        string root = CreateRoot("Some.SUBPACK-GRP");
        string sfv = WriteSfv(Path.Combine(root, "x.vobsubs.sfv"), "x.rar");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Single(result.MainSets);
        Assert.Equal(sfv, result.MainSets[0].SfvOrRarPath);
        Assert.Equal([sfv], result.SubtitleSfvs);
    }

    [Fact]
    public void SubsName_NoCarveOut_Excluded()
    {
        // excerpt: remove_unwanted_sfvs L319-340 (rule 2, no fall-through condition applies)
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "grp-subs.sfv"), "grp.rar");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Empty(result.MainSets);
        Assert.Equal([sfv], result.SubtitleSfvs);
    }

    [Fact]
    public void SubsName_MatchesFalsePositiveRegex_FallsThroughToMain()
    {
        // excerpt: remove_unwanted_sfvs L329-338 — `^000?-` alternative of the false-positive regex
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "00-grp-subs.sfv"), "grp.rar");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Single(result.MainSets);
        Assert.Equal(sfv, result.MainSets[0].SfvOrRarPath);
    }

    [Fact]
    public void SubsName_FallsThrough_ThenRule3ExcludesByCoverDir_ProvesPassSemantics()
    {
        // The `pass` branch does NOT accept the SFV — it only continues the sequential rule walk,
        // and here rule 3 (exact "cover" parent dir) excludes it anyway.
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "Cover", "grp.subs.cd1.sfv"), "grp.rar");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Empty(result.MainSets);
        Assert.Equal([sfv], result.SubtitleSfvs);
    }

    [Fact]
    public void ExactSubsDir_Excluded()
    {
        // excerpt: remove_unwanted_sfvs L342-355 (rule 3)
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "Subs", "x.sfv"), "x.rar");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Empty(result.MainSets);
        Assert.Equal([sfv], result.SubtitleSfvs);
    }

    [Fact]
    public void Proof_SingleRarEntry_LastPackedIsImage_StoresSfvAndRar_NotMainSet()
    {
        // excerpt: remove_unwanted_sfvs L357-379 (rule 4, image match)
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "Proof", "p.sfv"), "p.rar");
        string rar = Touch(Path.Combine(root, "Proof", "p.rar"));

        var facts = new ProofRarFacts(Readable: true, HasPackedBlocks: true, AnyImage: true, LastPackedIsImage: true);
        var scanner = new ReleaseScanner(sfvEntryReader: null, proofRarReader: _ => facts);

        ReleaseScanResult result = scanner.Scan(root);

        Assert.Empty(result.MainSets);
        Assert.Equal([sfv, rar], result.StoredFiles);
        Assert.Empty(result.SubtitleSfvs);
    }

    [Fact]
    public void Proof_LastPackedNotImage_EarlierWasImage_LastBlockWins_NotProof_ContinuesToLaterRules()
    {
        // excerpt: remove_unwanted_sfvs L365-373 — skip is reassigned on every block; the LAST
        // packed block decides, not the first.
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "Proof", "p.sfv"), "p.rar");
        Touch(Path.Combine(root, "Proof", "p.rar"));

        var facts = new ProofRarFacts(Readable: true, HasPackedBlocks: true, AnyImage: true, LastPackedIsImage: false);
        var scanner = new ReleaseScanner(sfvEntryReader: null, proofRarReader: _ => facts);

        ReleaseScanResult result = scanner.Scan(root);

        Assert.Single(result.MainSets);
        Assert.Equal(sfv, result.MainSets[0].SfvOrRarPath);
        Assert.Empty(result.StoredFiles);
    }

    [Fact]
    public void Proof_Unreadable_WarnsAndExcludes_TreatedAsProof()
    {
        // excerpt: remove_unwanted_sfvs L374-377 ("No RAR5 support yet" / caught ValueError)
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "Proof", "p.sfv"), "p.rar");
        string rar = Touch(Path.Combine(root, "Proof", "p.rar"));

        var facts = new ProofRarFacts(Readable: false, HasPackedBlocks: false, AnyImage: false, LastPackedIsImage: false);
        var scanner = new ReleaseScanner(sfvEntryReader: null, proofRarReader: _ => facts);

        ReleaseScanResult result = scanner.Scan(root);

        Assert.Empty(result.MainSets);
        Assert.Equal([sfv], result.StoredFiles);
        Assert.Contains(result.Warnings, w => w.Contains(rar, StringComparison.Ordinal));
    }

    [Fact]
    public void Proof_SingletonEntryNotLowercaseRarExtension_ExcludedAsProof_RarNeverChecked()
    {
        // excerpt: remove_unwanted_sfvs L362-363 — the naming check runs BEFORE any file-existence
        // or content check, so neither the filesystem nor the injected reader is ever touched.
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "Proof", "p.sfv"), "p.RAR");

        var scanner = new ReleaseScanner(
            sfvEntryReader: null,
            proofRarReader: _ => throw new InvalidOperationException("must not be called"));

        ReleaseScanResult result = scanner.Scan(root);

        Assert.Empty(result.MainSets);
        Assert.Equal([sfv], result.StoredFiles);
    }

    [Fact]
    public void Proof_RarMissingOnDisk_WarnsAndExcludes()
    {
        // excerpt: remove_unwanted_sfvs L380-385
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "Proof", "p.sfv"), "p.rar");
        // p.rar deliberately never created.

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Empty(result.MainSets);
        Assert.Equal([sfv], result.StoredFiles);
        Assert.Contains(result.Warnings, w => w.Contains("cannot be found", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Proof_NoPackedBlocks_NotProof_ContinuesToLaterRules()
    {
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "Proof", "p.sfv"), "p.rar");
        Touch(Path.Combine(root, "Proof", "p.rar"));

        var facts = new ProofRarFacts(Readable: true, HasPackedBlocks: false, AnyImage: false, LastPackedIsImage: false);
        var scanner = new ReleaseScanner(sfvEntryReader: null, proofRarReader: _ => facts);

        ReleaseScanResult result = scanner.Scan(root);

        Assert.Single(result.MainSets);
        Assert.Equal(sfv, result.MainSets[0].SfvOrRarPath);
        Assert.Empty(result.StoredFiles);
    }

    [Fact]
    public void Proof_TwoEntries_RequiresSingleton_FallsThroughToLaterRules()
    {
        // excerpt: remove_unwanted_sfvs L360 — the `len(sfvfiles) == 1` gate.
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "Proof", "p.sfv"), "p.rar", "p.r00");

        var scanner = new ReleaseScanner(
            sfvEntryReader: null,
            proofRarReader: _ => throw new InvalidOperationException("must not be called"));

        ReleaseScanResult result = scanner.Scan(root);

        Assert.Single(result.MainSets);
        Assert.Equal(sfv, result.MainSets[0].SfvOrRarPath);
        Assert.Empty(result.StoredFiles);
    }

    [Fact]
    public void SubsCdDirectory_Excluded()
    {
        // excerpt: remove_unwanted_sfvs L387-394 (rule 5)
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "Subs", "CD1", "s.sfv"), "s.rar");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Empty(result.MainSets);
        Assert.Equal([sfv], result.SubtitleSfvs);
    }

    [Fact]
    public void SubpackSubstringDir_ReleaseLacksSubpack_Excluded()
    {
        // excerpt: remove_unwanted_sfvs L396-398 (rule 6a)
        string root = CreateRoot("Movie-GRP");
        string sfv = WriteSfv(Path.Combine(root, "SubpackStuff", "x.sfv"), "x.rar");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Empty(result.MainSets);
        Assert.Equal([sfv], result.SubtitleSfvs);
    }

    [Fact]
    public void FixSubstringDir_ReleaseHasFix_MainSet()
    {
        // excerpt: remove_unwanted_sfvs L402-405 (rule 6c exception: release name also has "fix")
        string root = CreateRoot("Movie.FIX-GRP");
        string sfv = WriteSfv(Path.Combine(root, "MyFix", "x.sfv"), "x.rar");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Single(result.MainSets);
        Assert.Equal(sfv, result.MainSets[0].SfvOrRarPath);
    }

    [Fact]
    public void Rescue_AllSubsNamed_TwoEntrySfvReadmittedAsMain_OtherStaysExcluded()
    {
        // excerpt: remove_unwanted_sfvs L425-429 — rescue re-examines every SFV found, not just
        // the ones the first pass excluded; the destination split (design spec §2a) recomputes
        // SubtitleSfvs against the FINAL (post-rescue) main set.
        string root = CreateRoot("Some.Release-GRP");
        string single = WriteSfv(Path.Combine(root, "a-subs.sfv"), "a.rar");
        string multi = WriteSfv(Path.Combine(root, "b-subs.sfv"), "b.rar", "b.r00");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Single(result.MainSets);
        Assert.Equal(multi, result.MainSets[0].SfvOrRarPath);
        Assert.Equal([single], result.SubtitleSfvs);
    }

    [Fact]
    public void DirfixSubdir_ExcludedSfv_SkippedEntirely_WithWarning()
    {
        // pyrescene: generate_srr's extra_sfvs loop, "not for dirfix releases moved to the main
        // folder" — `"dirfix" in subdir.lower()`, a substring check on the immediate parent dir.
        string root = CreateRoot("Some.Release-GRP");
        string sfv = WriteSfv(Path.Combine(root, "Subs", "dirfix.stuff", "x.sfv"), "x.rar");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Empty(result.MainSets);
        Assert.Empty(result.SubtitleSfvs);
        Assert.Empty(result.StoredFiles);
        Assert.Contains(result.Warnings, w => w.Contains(sfv, StringComparison.Ordinal));
    }

    [Fact]
    public void RootAccessDenied_ReturnsWarningsOnlyResult()
    {
        AclDenyHelper.DenyAccess(TempDir);
        try
        {
            if (!DenyTookEffect(TempDir))
            {
                return; // host does not enforce the deny ACE; nothing to assert
            }

            ReleaseScanResult result = new ReleaseScanner().Scan(TempDir);

            Assert.Empty(result.MainSets);
            Assert.Empty(result.SampleFiles);
            Assert.Empty(result.SubtitleSfvs);
            Assert.Empty(result.StoredFiles);
            Assert.Empty(result.MusicSfvs);
            Assert.Single(result.Warnings);
        }
        finally
        {
            AclDenyHelper.RestoreAccess(TempDir);
        }
    }

    [Fact]
    public void PreCancelledToken_ThrowsOperationCanceled()
    {
        string root = CreateRoot("Some.Release-GRP");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => new ReleaseScanner().Scan(root, cts.Token));
    }

    /// <summary>
    /// Some hosts don't actually enforce an <c>icacls</c> deny ACE. Confirms the deny is real
    /// before an assertion depends on it (same pattern as <c>ReleaseTraversalTests</c>).
    /// </summary>
    private static bool DenyTookEffect(string path)
    {
        try
        {
            Directory.GetFiles(path);
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
