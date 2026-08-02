using System.Buffers.Binary;
using ReScene.App.Core.Services;
using ReScene.RAR;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Test matrix for the stored-file chain — nfo/m3u/proof-images/proof-RARs/log/cue/
/// pre-existing-srs/fix-RAR/input-SFV passes, one Fact per row (see
/// docs/superpowers/plans/2026-07-19-multiset-srr-creation.md). Extends
/// <see cref="ReleaseScannerMainSetTests"/>/<see cref="ReleaseScannerMediaTests"/> — none of their
/// mechanics are re-tested here. Release folder names deliberately avoid '-' (unlike the
/// "Some.Release-GRP" convention used elsewhere) so <c>similar_to_good_name</c>'s full-path
/// group-name-fallback split (which operates on the WHOLE path, not just the image's own basename)
/// can never accidentally bleed into the release directory's own name.
/// </summary>
public class ReleaseScannerStoredTests : TempDirTestBase
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

    private static string WriteSized(string path, int totalSize, byte[]? header = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = new byte[totalSize];
        if (header is not null)
        {
            header.CopyTo(content, 0);
        }
        else
        {
            Array.Fill(content, (byte)'x');
        }

        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] BuildPngHeader(int width, int height)
    {
        var bytes = new byte[24];
        byte[] sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        sig.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), (uint)height);
        return bytes;
    }

    /// <summary>
    /// A minimal "marker-first" JPEG: bare SOI (FF D8) immediately followed by an SOF0 (FF C0)
    /// segment — no JFIF/Exif/ICC_PROFILE/Adobe marker anywhere. Real pyrescene's imghdr would NOT
    /// recognize this as a JPEG at all (see the TryGetImageSize divergence comment); this scanner's
    /// simplified "any SOI-starting file is a JPEG" probe does.
    /// </summary>
    private static byte[] BuildMarkerFirstJpegSofHeader(int width, int height)
    {
        var bytes = new byte[11];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8; // SOI
        bytes[2] = 0xFF;
        bytes[3] = 0xC0; // SOF0
        bytes[4] = 0x00;
        bytes[5] = 0x0B; // segment length (cosmetic; never re-read after the marker is found)
        bytes[6] = 0x08; // precision (skipped)
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(7, 2), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(9, 2), (ushort)width);
        return bytes;
    }

    // --- nfo pass (generate_srr) ------------------------------------------------------------------

    [Fact]
    public void Nfo_ImdbAndTvmazeExcluded_CaseInsensitive_OthersStored()
    {
        string root = CreateRoot("SomeRelease");
        string release = WriteSized(Path.Combine(root, "release.nfo"), 20);
        WriteSized(Path.Combine(root, "imdb.nfo"), 20);
        WriteSized(Path.Combine(root, "TVMAZE.NFO"), 20);

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal([release], result.StoredFiles);
    }

    [Fact]
    public void NoNfo_SizeExactlyEight_Skipped_OffByOneStored()
    {
        // generate_srr: a "no.nfo" that IS exactly 8 bytes (the placeholder text "no.nfo\r\n"-ish
        // content) is skipped; any other size is stored.
        string root = CreateRoot("SomeRelease");
        string skip = WriteSized(Path.Combine(root, "A", "no.nfo"), 8);
        string store = WriteSized(Path.Combine(root, "B", "no.nfo"), 7);

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.DoesNotContain(skip, result.StoredFiles);
        Assert.Equal([store], result.StoredFiles);
    }

    [Fact]
    public void NoNfo_SubstringVariants_EightBytes_Skipped_LongerStored()
    {
        // generate_srr's `in ("no.nfo")` is a parenthesized STRING (no comma), so Python's `in` is
        // SUBSTRING membership, not equality — basenames ".nfo" and "o.nfo" (both substrings of
        // "no.nfo") also enter the size==8 skip, not just "no.nfo" itself.
        string root = CreateRoot("SomeRelease");
        string dotNfoSkip = WriteSized(Path.Combine(root, "A", ".nfo"), 8);
        string oNfoSkip = WriteSized(Path.Combine(root, "B", "o.nfo"), 8);
        string dotNfoStore = WriteSized(Path.Combine(root, "C", ".nfo"), 9);

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.DoesNotContain(dotNfoSkip, result.StoredFiles);
        Assert.DoesNotContain(oNfoSkip, result.StoredFiles);
        Assert.Contains(dotNfoStore, result.StoredFiles);
    }

    // --- m3u / log / cue / pre-existing srs category order -----------------------------------------

    [Fact]
    public void M3uLogCueSrs_AllStored_InCategoryOrder()
    {
        string root = CreateRoot("SomeRelease");
        string m3u = Touch(Path.Combine(root, "playlist.m3u"));
        string log = Touch(Path.Combine(root, "rip.log"));
        string cue = Touch(Path.Combine(root, "disc.cue"));
        string srs = Touch(Path.Combine(root, "old.srs"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal([m3u, log, cue, srs], result.StoredFiles);
    }

    [Fact]
    public void Log_BlacklistedName_NotStored_NonBlacklistedStored()
    {
        // generate_srr: exact blacklist (case-insensitive) + a leading-dot hidden-file check.
        string root = CreateRoot("SomeRelease");
        Touch(Path.Combine(root, "rushchk.log"));
        Touch(Path.Combine(root, ".upchk.log"));
        Touch(Path.Combine(root, "ufxpcrc.log"));
        Touch(Path.Combine(root, ".hidden.log"));
        string kept = Touch(Path.Combine(root, "x.log"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal([kept], result.StoredFiles);
    }

    // --- proof images: keyword bypass (filter_proof_image_files) -----------------------------------

    [Fact]
    public void KeywordPath_ProofFolderJpg_StoredBeforeAlwaysSkip()
    {
        // "Proof/Folder.jpg" would fail always_skip's "stem ends folder" predicate, but the
        // keyword-path bypass runs FIRST and stores it unconditionally.
        string root = CreateRoot("SomeRelease");
        string img = Touch(Path.Combine(root, "Proof", "Folder.jpg"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal([img], result.StoredFiles);
    }

    // --- proof images: always_skip -------------------------------------------------------------------

    [Fact]
    public void AlwaysSkip_AllFivePredicates_Skipped()
    {
        string root = CreateRoot("SomeRelease");
        Touch(Path.Combine(root, "Folder.jpg"));
        Touch(Path.Combine(root, "MyFolder.png"));
        Touch(Path.Combine(root, "AlbumArtSmall.jpg"));
        Touch(Path.Combine(root, "AlbumArt_{ABC}_Large.jpg"));
        Touch(Path.Combine(root, "has space.jpg"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Empty(result.StoredFiles);
    }

    [Fact]
    public void AlbumArtLarge_NotAlwaysSkip_FallsThroughToStoreRlsRoot()
    {
        // "AlbumArtLarge.jpg" contains neither "albumartsmall" nor starts with "albumart_{", so
        // always_skip does not fire — the 00/01-prefix branch of store_rls_root then stores it
        // unconditionally regardless of size.
        string root = CreateRoot("SomeRelease");
        string img = Touch(Path.Combine(root, "00-AlbumArtLarge.jpg"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal([img], result.StoredFiles);
    }

    // --- proof images: store_rls_root prefix branch --------------------------------------------------

    [Fact]
    public void ZeroPrefixedImage_StoredRegardlessOfSize()
    {
        string root = CreateRoot("SomeRelease");
        string img = WriteSized(Path.Combine(root, "00-cover.jpg"), 5_000);

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal([img], result.StoredFiles);
    }

    // --- proof images: similar_to_good_name -----------------------------------------------------

    [Fact]
    public void SimilarToSfv_TenCharSharedPrefix_Stored()
    {
        // "grp-movienight" (14 chars, from the sfv stem) and the image's basename share the first
        // 10 characters ("grp-movien").
        string root = CreateRoot("SomeRelease");
        WriteSfv(Path.Combine(root, "grp-movienight.sfv"), "grp-movienight.rar");
        string img = WriteSized(Path.Combine(root, "grp-movienight-front.jpg"), 150_000);

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Contains(img, result.StoredFiles);
    }

    [Fact]
    public void SimilarToNfo_StripZerosNormalized_Stored()
    {
        // the ONLY known-good name is an nfo with a "00-" prefix; strip_zeros normalizes both
        // sides before the 10-char compare so the shared "grp-movienight" prefix still matches.
        string root = CreateRoot("SomeRelease");
        Touch(Path.Combine(root, "00-grp-movienight.nfo"));
        string img = WriteSized(Path.Combine(root, "grp-movienight-front.jpg"), 150_000);

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Contains(img, result.StoredFiles);
    }

    [Fact]
    public void SimilarToM3u_OnlyGoodNameIsM3u_Stored()
    {
        string root = CreateRoot("SomeRelease");
        Touch(Path.Combine(root, "grp-movienight.m3u"));
        string img = WriteSized(Path.Combine(root, "grp-movienight-front.jpg"), 150_000);

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Contains(img, result.StoredFiles);
    }

    [Fact]
    public void UnrelatedName_NegativeControl_SkippedWithWarning()
    {
        string root = CreateRoot("SomeRelease");
        WriteSfv(Path.Combine(root, "grp-movienight.sfv"), "grp-movienight.rar");
        string img = WriteSized(Path.Combine(root, "unrelated-shot9.jpg"), 150_000);

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.DoesNotContain(img, result.StoredFiles);
        Assert.Contains(result.Warnings, w => w.Contains("unrelated-shot9.jpg", StringComparison.Ordinal));
    }

    [Fact]
    public void SimilarToSfv_NineCharSharedPrefix_BoundaryNegative_Skipped()
    {
        // "grp-movie" is exactly 9 characters — one short of the 10-character slice
        // similar_to_good_name compares, so this must NOT match despite the obvious visual
        // similarity.
        string root = CreateRoot("SomeRelease");
        WriteSfv(Path.Combine(root, "grp-movie.sfv"), "grp-movie.rar");
        string img = WriteSized(Path.Combine(root, "grp-movie-front.jpg"), 150_000);

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.DoesNotContain(img, result.StoredFiles);
    }

    // --- proof images: fixed_resolution_cover ---------------------------------------------------------

    [Fact]
    public void FixedResolutionCover_630x1200_Skipped()
    {
        // PNG-signature bytes in a ".jpg"-named file: fixed_resolution_cover sniffs content, not
        // extension, exactly like pyrescene's imghdr — the file extension only matters for the
        // earlier PROOF_IMAGE_EXTS discovery filter.
        string root = CreateRoot("SomeRelease");
        WriteSfv(Path.Combine(root, "grp-movienight.sfv"), "grp-movienight.rar");
        string img = WriteSized(Path.Combine(root, "grp-movienight-cover.jpg"), 150_000, BuildPngHeader(630, 1200));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.DoesNotContain(img, result.StoredFiles);
    }

    [Fact]
    public void FixedResolutionCover_MarkerFirstJpeg_DivergesFromPyrescene_CurrentlySkipped()
    {
        // A bare-SOI JPEG with an SOF0 segment but no JFIF/Exif/ICC_PROFILE/Adobe marker anywhere —
        // real pyrescene's imghdr would NOT recognize this as a JPEG at all, so
        // fixed_resolution_cover would report False and pyrescene would STORE it. This scanner's
        // simplified "any SOI-starting file is a JPEG" probe DOES recognize it and skips it as a
        // fixed-resolution cover — a deliberate, documented [DIVERGENCE: simplified] (see
        // TryGetImageSize's remarks). This pinning test locks the current, intentional behavior.
        string root = CreateRoot("SomeRelease");
        WriteSfv(Path.Combine(root, "grp-movienight.sfv"), "grp-movienight.rar");
        string img = WriteSized(
            Path.Combine(root, "grp-movienight-cover.jpg"), 150_000, BuildMarkerFirstJpegSofHeader(630, 1200));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.DoesNotContain(img, result.StoredFiles);
    }

    // --- proof images: size boundary (strictly greater than 100000) ---------------------------------

    [Fact]
    public void SizeBoundary_ExactlyOneHundredThousand_Skipped_OneMoreByte_Stored()
    {
        string root = CreateRoot("SomeRelease");
        WriteSfv(Path.Combine(root, "grp-movienight.sfv"), "grp-movienight.rar");
        string skip = WriteSized(Path.Combine(root, "A", "grp-movienight-a.jpg"), 100_000);
        string store = WriteSized(Path.Combine(root, "B", "grp-movienight-b.jpg"), 100_001);

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.DoesNotContain(skip, result.StoredFiles);
        Assert.Contains(store, result.StoredFiles);
    }

    [Fact]
    public void SmallSimilarNamedImage_SkippedForSize()
    {
        string root = CreateRoot("SomeRelease");
        WriteSfv(Path.Combine(root, "grp-movienight.sfv"), "grp-movienight.rar");
        string img = WriteSized(Path.Combine(root, "grp-movienight-front.jpg"), 50_000);

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.DoesNotContain(img, result.StoredFiles);
    }

    [Fact]
    public void RandomUnrelatedImage_SkippedWithWarning()
    {
        string root = CreateRoot("SomeRelease");
        string img = WriteSized(Path.Combine(root, "random.jpg"), 150_000);

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.DoesNotContain(img, result.StoredFiles);
        Assert.Contains(result.Warnings, w => w.Contains("random.jpg", StringComparison.Ordinal));
    }

    // --- proof images: traversal ordering ------------------------------------------------------------

    [Fact]
    public void ImagesInSameDir_FollowTraversalOrder()
    {
        // "a.png" sorts ordinally before "b.jpg" within the same directory — traversal order, not
        // extension-grouped order.
        string root = CreateRoot("SomeRelease");
        string png = Touch(Path.Combine(root, "Proof", "a.png"));
        string jpg = Touch(Path.Combine(root, "Proof", "b.jpg"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal([png, jpg], result.StoredFiles);
    }

    // --- proof RARs: independent pass (filter_proof_rar_files) ---------------------------------------

    [Fact]
    public void ProofRar_ReaderReportsImage_Stored()
    {
        string root = CreateRoot("SomeRelease");
        string rar = Touch(Path.Combine(root, "Proof", "p.rar"));
        var facts = new ProofRarFacts(Readable: true, HasPackedBlocks: true, AnyImage: true, LastPackedIsImage: true);
        var scanner = new ReleaseScanner(sfvEntryReader: null, proofRarReader: (_, _) => facts);

        ReleaseScanResult result = scanner.Scan(root);

        Assert.Equal([rar], result.StoredFiles);
    }

    [Fact]
    public void ProofRar_ReaderReportsNoImage_NotStored()
    {
        string root = CreateRoot("SomeRelease");
        Touch(Path.Combine(root, "Proof", "p.rar"));
        var facts = new ProofRarFacts(Readable: true, HasPackedBlocks: true, AnyImage: false, LastPackedIsImage: false);
        var scanner = new ReleaseScanner(sfvEntryReader: null, proofRarReader: (_, _) => facts);

        ReleaseScanResult result = scanner.Scan(root);

        Assert.Empty(result.StoredFiles);
    }

    [Fact]
    public void ProofRar_Unreadable_WarnsAndNotStored()
    {
        string root = CreateRoot("SomeRelease");
        string rar = Touch(Path.Combine(root, "Proof", "p.rar"));
        var facts = new ProofRarFacts(Readable: false, HasPackedBlocks: false, AnyImage: false, LastPackedIsImage: false);
        var scanner = new ReleaseScanner(sfvEntryReader: null, proofRarReader: (_, _) => facts);

        ReleaseScanResult result = scanner.Scan(root);

        Assert.Empty(result.StoredFiles);
        Assert.Contains(result.Warnings, w => w.Contains(rar, StringComparison.Ordinal));
    }

    [Fact]
    public void ProofRar_AlreadyStoredByRule4_NotDoubleAdded()
    {
        // A Proof/p.rar already stored by rule 4 (its linked singleton proof SFV resolves to an
        // image-ending RAR) must not be added a second time by the independent
        // filter_proof_rar_files pass, even though it independently also matches "proof"-in-path
        // plus AnyImage.
        //
        // Final order: rule 4's proof RAR joins the proof-RAR CATEGORY position (not the front of
        // the list); the proof SFV is picked up by pass-10's final-SFV pass (also not the front —
        // simply never added to `main`). With only these two entries in this tiny tree, the
        // category-ordered result is `[rar, sfv]` before any reorder is even needed; the pass-10
        // reorder confirms it stays that way (the rar's stem matches the sfv, so it would be
        // relocated here regardless).
        string root = CreateRoot("SomeRelease");
        string sfv = WriteSfv(Path.Combine(root, "Proof", "p.sfv"), "p.rar");
        string rar = Touch(Path.Combine(root, "Proof", "p.rar"));
        var facts = new ProofRarFacts(Readable: true, HasPackedBlocks: true, AnyImage: true, LastPackedIsImage: true);
        var scanner = new ReleaseScanner(sfvEntryReader: null, proofRarReader: (_, _) => facts);

        ReleaseScanResult result = scanner.Scan(root);

        Assert.Equal([rar, sfv], result.StoredFiles);
        Assert.Single(result.StoredFiles, f => f == rar);
    }

    [Fact]
    public void ProofRarAndSfv_WithNfo_LandInCorrectCategoryPositions_NotPreSeededAtFront()
    {
        // Before this fix, rule 4 pre-seeded the proof sfv+rar at the FRONT of `stored` (during SFV
        // classification, which runs before any category pass) — for a tree with an nfo too, that
        // produced [rar, sfv, nfo], contradicting generate_srr's category order (nfo is category 1;
        // proof rar is category 3; the sfv, being "not main", is only ever picked up by the FINAL
        // sfv pass, category 10). The pass-10 reorder alone can't fix this: it only relocates a
        // mover relative to its sfv, it can't relocate the sfv (or the whole pair) relative to nfo.
        string root = CreateRoot("SomeRelease");
        string nfo = Touch(Path.Combine(root, "release.nfo"));
        string sfv = WriteSfv(Path.Combine(root, "Proof", "p.sfv"), "p.rar");
        string rar = Touch(Path.Combine(root, "Proof", "p.rar"));
        var facts = new ProofRarFacts(Readable: true, HasPackedBlocks: true, AnyImage: true, LastPackedIsImage: true);
        var scanner = new ReleaseScanner(sfvEntryReader: null, proofRarReader: (_, _) => facts);

        ReleaseScanResult result = scanner.Scan(root);

        Assert.Equal([nfo, rar, sfv], result.StoredFiles);
    }

    // --- fix RAR (is_storable_fix, generate_srr) ------------------------------------------------------

    [Fact]
    public void FixRar_StorableFixName_SingleEntrySfv_Stored()
    {
        string root = CreateRoot("Movie.FIX-GRP");
        string sfv = WriteSfv(Path.Combine(root, "x.sfv"), "x.rar");
        string rar = Touch(Path.Combine(root, "x.rar"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Contains(rar, result.StoredFiles);
        _ = sfv;
    }

    [Fact]
    public void FixRar_NonStorableFixName_NotStored()
    {
        string root = CreateRoot("Movie.NOTAFIX-GRP");
        WriteSfv(Path.Combine(root, "x.sfv"), "x.rar");
        string rar = Touch(Path.Combine(root, "x.rar"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.DoesNotContain(rar, result.StoredFiles);
    }

    [Fact]
    public void FixRar_PartStyleFirstVolume_Part01_Stored()
    {
        // A new-style ".partNN.rar" entry with N == 1 IS a true first volume.
        string root = CreateRoot("Movie.FIX-GRP");
        WriteSfv(Path.Combine(root, "x.sfv"), "x.part01.rar");
        string rar = Touch(Path.Combine(root, "x.part01.rar"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Contains(rar, result.StoredFiles);
    }

    [Fact]
    public void FixRar_PartStyleNonFirstVolume_Part02_NoPart01OnDisk_NotStored()
    {
        // ReleaseScanner.cs previously accepted this — Path.GetExtension("x.part02.rar") is ".rar"
        // and RARVolumeIdentifier.IsRARVolume passes it, so a non-first ".partNN.rar" slipped
        // through. pyrescene's first_rars identifies "first" from the SFV's LISTED ENTRY NAME alone
        // (never by scanning the disk for a lower-numbered sibling), so "part02" must be rejected
        // purely by its own name — this test deliberately does NOT create "x.part01.rar" on disk,
        // proving the rejection isn't a disk chain-walk in disguise.
        string root = CreateRoot("Movie.FIX-GRP");
        WriteSfv(Path.Combine(root, "x.sfv"), "x.part02.rar");
        string rar = Touch(Path.Combine(root, "x.part02.rar"));

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.DoesNotContain(rar, result.StoredFiles);
    }

    // --- generate_srr's pass-10: input SFVs appended ------------------------------------------------

    [Fact]
    public void InputSfvs_Appended_AfterAllOtherCategories()
    {
        string root = CreateRoot("SomeRelease");
        string nfo = Touch(Path.Combine(root, "release.nfo"));
        string sfv = WriteSfv(Path.Combine(root, "CD1", "a.sfv"), "a.rar");

        ReleaseScanResult result = new ReleaseScanner().Scan(root);

        Assert.Equal([nfo, sfv], result.StoredFiles);
    }

    [Fact]
    public void MainSfv_DeferredToBottom_BehindANonMainProofSfv()
    {
        // generate_srr's pass-10 ("add RAR sfv files at the bottom") must NOT store every sfv in
        // plain traversal order — non-main sfvs (here, the Proof/p.sfv, excluded as proof material
        // by rule 4) are appended FIRST, and MAIN sfvs are DEFERRED to the very bottom. "CD1" sorts
        // before "Proof" ordinally, so a plain-traversal bug would put main.sfv first; the correct,
        // deferred order puts it last.
        string root = CreateRoot("SomeRelease");
        string mainSfv = WriteSfv(Path.Combine(root, "CD1", "main.sfv"), "main.rar");
        string proofSfv = WriteSfv(Path.Combine(root, "Proof", "p.sfv"), "p.rar");
        string proofRar = Touch(Path.Combine(root, "Proof", "p.rar"));
        var facts = new ProofRarFacts(Readable: true, HasPackedBlocks: true, AnyImage: true, LastPackedIsImage: true);
        var scanner = new ReleaseScanner(sfvEntryReader: null, proofRarReader: (_, _) => facts);

        ReleaseScanResult result = scanner.Scan(root);

        Assert.Equal([proofRar, proofSfv, mainSfv], result.StoredFiles);
        Assert.Single(result.MainSets);
        Assert.Equal(mainSfv, result.MainSets[0].SfvOrRarPath);
    }

    // --- full category-order mixed tree --------------------------------------------------------------

    [Fact]
    public void MixedTree_FullCategoryOrder()
    {
        // nfo -> m3u -> proof images -> proof RARs -> log -> cue -> pre-existing srs -> fix RAR ->
        // input SFVs, each category internally in traversal order. No rule-4 proof-SFV entries
        // here, so the concatenation starts cleanly at nfo.
        string root = CreateRoot("SomeRelease");
        string nfo = Touch(Path.Combine(root, "release.nfo"));
        string m3u = Touch(Path.Combine(root, "playlist.m3u"));
        string img = Touch(Path.Combine(root, "Proof", "cover.jpg"));
        string rar = Touch(Path.Combine(root, "Proof", "p.rar"));
        string log = Touch(Path.Combine(root, "x.log"));
        string cue = Touch(Path.Combine(root, "disc.cue"));
        string srs = Touch(Path.Combine(root, "old.srs"));
        string sfv = WriteSfv(Path.Combine(root, "CD1", "a.sfv"), "a.rar");

        var facts = new ProofRarFacts(Readable: true, HasPackedBlocks: true, AnyImage: true, LastPackedIsImage: true);
        var scanner = new ReleaseScanner(sfvEntryReader: null, proofRarReader: (_, _) => facts);

        ReleaseScanResult result = scanner.Scan(root);

        Assert.Equal([nfo, m3u, img, rar, log, cue, srs, sfv], result.StoredFiles);
    }
}
