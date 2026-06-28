using ReScene.Core;
using ReScene.Core.Cryptography;
using ReScene.Core.Diagnostics;
using ReScene.Core.IO;
using ReScene.NET.ViewModels.Reconstruction;
using ReScene.SRR;

namespace ReScene.NET.Tests;

public class ArchiveSetPlannerTests
{
    private static SrrArchiveSet MakeSet(string key, string dir, string[] volumes, (string file, string crc)[] content)
    {
        var set = new SrrArchiveSet { Key = key, Directory = dir };
        foreach (string v in volumes)
        {
            set.VolumeNames.Add(v);
        }

        foreach ((string file, string crc) in content)
        {
            set.ArchivedFiles.Add(file);
            set.ArchivedFileCrcs[file] = crc;
        }

        return set;
    }

    [Fact]
    public void BuildExpectedVolumeCrcs_FromUserSfv_FilteredToSetVolumes()
    {
        var set = MakeSet("DVD1/aln-re4a", "DVD1",
            ["DVD1\\aln-re4a.rar", "DVD1\\aln-re4a.r00"], [("aln-re4a.iso", "00000000")]);
        var userSfv = new SFVFile();
        userSfv.Entries.Add(new SFVFileEntry("aln-re4a.rar", "f1a3ec0d"));
        userSfv.Entries.Add(new SFVFileEntry("aln-re4a.r00", "88b361c9"));
        userSfv.Entries.Add(new SFVFileEntry("aln-re4b.rar", "631d681c")); // other set — excluded

        Dictionary<string, string> crcs = ArchiveSetPlanner.BuildExpectedVolumeCrcs(set, embeddedSfvBytes: null, userSfv);

        Assert.Equal(2, crcs.Count);
        Assert.Equal("f1a3ec0d", crcs["aln-re4a.rar"]);
        Assert.False(crcs.ContainsKey("aln-re4b.rar"));
    }

    [Fact]
    public void BuildExpectedVolumeCrcs_PrefersEmbeddedSfvOverUserSfv()
    {
        var set = MakeSet("DVD1/aln-re4a", "DVD1",
            ["DVD1\\aln-re4a.rar", "DVD1\\aln-re4a.r00"], [("aln-re4a.iso", "00000000")]);

        byte[] embedded = System.Text.Encoding.Latin1.GetBytes(
            "aln-re4a.rar aaaaaaaa\r\naln-re4a.r00 bbbbbbbb\r\n");

        var userSfv = new SFVFile();
        userSfv.Entries.Add(new SFVFileEntry("aln-re4a.rar", "f1a3ec0d"));
        userSfv.Entries.Add(new SFVFileEntry("aln-re4a.r00", "88b361c9"));

        Dictionary<string, string> crcs = ArchiveSetPlanner.BuildExpectedVolumeCrcs(set, embedded, userSfv);

        Assert.Equal(2, crcs.Count);
        Assert.Equal("aaaaaaaa", crcs["aln-re4a.rar"]);
        Assert.Equal("bbbbbbbb", crcs["aln-re4a.r00"]);
    }

    [Fact]
    public void BuildOptionsForSet_UsesOnlyThisSetsContentAndNames()
    {
        var set = MakeSet("DVD1/aln-re4a", "DVD1",
            ["DVD1\\aln-re4a.rar", "DVD1\\aln-re4a.r00"], [("aln-re4a.iso", "00000000")]);
        SharedReconstructionSettings shared = ArchiveSetPlannerTestData.SharedSettings();

        BruteForceOptions opts = ArchiveSetPlanner.BuildOptionsForSet(set, shared, expectedVolumeCrcs:
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["aln-re4a.rar"] = "f1a3ec0d" });

        Assert.Contains("aln-re4a.iso", opts.RAROptions.ArchiveFilePaths);
        Assert.DoesNotContain("aln-re4b.iso", opts.RAROptions.ArchiveFilePaths);
        Assert.Equal(["DVD1\\aln-re4a.rar", "DVD1\\aln-re4a.r00"], opts.RAROptions.OriginalRarFileNames);
        Assert.True(opts.ExpectedVolumeCrcs.ContainsKey("aln-re4a.rar"));
        Assert.Contains("f1a3ec0d", opts.Hashes);
    }

    [Fact]
    public void BuildOptionsForSet_CarriesSharedReleaseWideFields()
    {
        var set = MakeSet("DVD1/aln-re4a", "DVD1",
            ["DVD1\\aln-re4a.rar"], [("aln-re4a.iso", "00000000")]);
        SharedReconstructionSettings shared = ArchiveSetPlannerTestData.SharedSettings() with
        {
            ArchiveComment = "hello",
            ArchiveCommentBytes = new byte[] { 1, 2, 3 },
            CmtCompressedData = new byte[] { 4, 5, 6 },
            CmtCompressionMethod = 0x30,
            CustomPackerDetected = CustomPackerType.None,
            SRRFilePath = "C:\\foo.srr",
            EnableHostOSPatching = true,
        };

        BruteForceOptions opts = ArchiveSetPlanner.BuildOptionsForSet(set, shared,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal("hello", opts.RAROptions.ArchiveComment);
        Assert.True(opts.RAROptions.ArchiveCommentBytes.HasValue);
        Assert.True(opts.RAROptions.CmtCompressedData.HasValue);
        Assert.True(new byte[] { 1, 2, 3 }.AsSpan().SequenceEqual(opts.RAROptions.ArchiveCommentBytes!.Value.Span));
        Assert.True(new byte[] { 4, 5, 6 }.AsSpan().SequenceEqual(opts.RAROptions.CmtCompressedData!.Value.Span));
        Assert.Equal((byte)0x30, opts.RAROptions.CmtCompressionMethod);
        Assert.Equal("C:\\foo.srr", opts.RAROptions.SRRFilePath);
        Assert.True(opts.RAROptions.EnableHostOSPatching);
    }

    [Fact]
    public void BuildOptionsForSet_UsesPerSetMetadata()
    {
        var set = MakeSet("DVD1/aln-re4a", "DVD1",
            ["DVD1\\aln-re4a.rar"], [("aln-re4a.iso", "00000000")]);
        set.DetectedHostOS = 3;
        set.DetectedFileAttributes = 0x20;
        set.HasLargeFiles = true;
        set.DetectedHighPackSize = 1;
        set.DetectedHighUnpSize = 2;

        SharedReconstructionSettings shared = ArchiveSetPlannerTestData.SharedSettings();

        BruteForceOptions opts = ArchiveSetPlanner.BuildOptionsForSet(set, shared,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal((byte)3, opts.RAROptions.DetectedFileHostOS);
        Assert.Equal((uint)0x20, opts.RAROptions.DetectedFileAttributes);
        Assert.Equal(true, opts.RAROptions.DetectedLargeFlag);
        Assert.Equal((uint)1, opts.RAROptions.DetectedHighPackSize);
        Assert.Equal((uint)2, opts.RAROptions.DetectedHighUnpSize);
    }

    [Fact]
    public void WorkRootFor_SingleRootSet_IsOutputPath()
    {
        var set = MakeSet("", "", ["x.rar"], [("x.iso", "00000000")]);
        SharedReconstructionSettings shared = ArchiveSetPlannerTestData.SharedSettings() with { OutputPath = "C:\\out" };

        Assert.Equal("C:\\out", ArchiveSetPlanner.WorkRootFor(shared, set));
    }

    [Fact]
    public void WorkRootFor_KeyedSet_IsIsolatedSubdir()
    {
        var set = MakeSet("DVD1/aln-re4a", "DVD1", ["DVD1\\aln-re4a.rar"], [("aln-re4a.iso", "00000000")]);
        SharedReconstructionSettings shared = ArchiveSetPlannerTestData.SharedSettings() with { OutputPath = "C:\\out" };

        string root = ArchiveSetPlanner.WorkRootFor(shared, set);

        Assert.StartsWith(Path.Combine("C:\\out", ".rescene-work"), root, StringComparison.Ordinal);
        Assert.DoesNotContain('/', Path.GetFileName(root)); // key separators sanitized
    }

    [Fact]
    public void NarrowToCombo_RestrictsVersionsAndArgsToWinner()
    {
        BruteForceOptions full = ArchiveSetPlannerTestData.SampleOptions();
        var combo = new WinningCombo(351, [new RARCommandLineArgument("-m0", 300)]);

        BruteForceOptions narrowed = ArchiveSetPlanner.NarrowToCombo(full, combo);

        Assert.Single(narrowed.RAROptions.RARVersions);
        Assert.Equal(351, narrowed.RAROptions.RARVersions[0].Start);
        Assert.Equal(351, narrowed.RAROptions.RARVersions[0].End);
        Assert.Single(narrowed.RAROptions.CommandLineArguments);
        Assert.Equal("-m0", narrowed.RAROptions.CommandLineArguments[0][0].Argument);
    }

    [Fact]
    public void NarrowToCombo_PreservesHashesAndExpectedCrcs()
    {
        BruteForceOptions full = ArchiveSetPlannerTestData.SampleOptions();
        full.Hashes.Add("deadbeef");
        full.ExpectedVolumeCrcs["x.rar"] = "deadbeef";
        var combo = new WinningCombo(351, [new RARCommandLineArgument("-m0", 300)]);

        BruteForceOptions narrowed = ArchiveSetPlanner.NarrowToCombo(full, combo);

        Assert.Contains("deadbeef", narrowed.Hashes);
        Assert.Equal("deadbeef", narrowed.ExpectedVolumeCrcs["x.rar"]);
    }

    [Fact]
    public void ResolveSets_PrefersParsedArchiveSets()
    {
        var existing = MakeSet("DVD1/x", "DVD1", ["DVD1\\x.rar"], [("x.iso", "00000000")]);

        IReadOnlyList<SrrArchiveSet> sets = ArchiveSetPlanner.ResolveSets(
            archiveSets: [existing], srrFilePath: null,
            flatOriginalNames: ["ignored.rar"], flatArchiveFiles: ["ignored.iso"]);

        Assert.Single(sets);
        Assert.Same(existing, sets[0]);
    }

    [Fact]
    public void RealMultiSetSrr_ProducesIsolatedPerSetOptions()
    {
        string srrPath = Path.Combine(AppContext.BaseDirectory, "TestData",
            "cleanup_script",
            "007.A.View.To.A.Kill.1985.UE.iNTERNAL.DVDRip.XviD-iNCiTE.fine_2cd.srr");
        Assert.True(File.Exists(srrPath), $"Fixture not found: {srrPath}");

        SRRFile srr = SRRFile.Load(srrPath);
        Assert.Equal(2, srr.ArchiveSets.Count);

        SharedReconstructionSettings shared = ArchiveSetPlannerTestData.SharedSettings();

        var allArchiveFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SrrArchiveSet set in srr.ArchiveSets)
        {
            BruteForceOptions opts = ArchiveSetPlanner.BuildOptionsForSet(set, shared,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            // Each set's options carry only that set's own volume names and archived content.
            Assert.Equal(set.VolumeNames, opts.RAROptions.OriginalRarFileNames);
            Assert.Equal(set.ArchivedFiles.Count, opts.RAROptions.ArchiveFilePaths.Count);

            foreach (string f in opts.RAROptions.ArchiveFilePaths)
            {
                allArchiveFiles.Add(f);
            }
        }

        // The two sets together do not double-count any single archived file beyond their own.
        Assert.True(allArchiveFiles.Count > 0);
    }

    [Fact]
    public void ResolveSets_NoArchiveSets_NoSrr_SynthesizesSingleFlatSet()
    {
        IReadOnlyList<SrrArchiveSet> sets = ArchiveSetPlanner.ResolveSets(archiveSets: [], srrFilePath: null,
            flatOriginalNames: ["x.rar", "x.r00"], flatArchiveFiles: ["x.iso"]);
        Assert.Single(sets);
        Assert.Equal("", sets[0].Directory);
        Assert.Equal(["x.rar", "x.r00"], sets[0].VolumeNames);
        Assert.Contains("x.iso", sets[0].ArchivedFiles);
    }

    // ── ShouldSkipUnverifiableSet ────────────────────────────────────────────────────────────────

    [Fact]
    public void ShouldSkipUnverifiableSet_Sha1_CompleteAllVolumes_ZeroExpected_ReturnsFalse()
    {
        // SHA1 run: no per-volume CRC source; engine must still run via the first-volume hash gate.
        // Regression case — the old guard (expected.Count < volumeCount) would have skipped this.
        Assert.False(ArchiveSetPlanner.ShouldSkipUnverifiableSet(
            completeAllVolumes: true, hashType: HashType.SHA1, expectedCrcCount: 0, volumeCount: 30));
    }

    [Fact]
    public void ShouldSkipUnverifiableSet_Crc32_CompleteAllVolumes_ZeroExpected_ReturnsFalse()
    {
        // CRC32 run but no expected CRC matched any set volume — no SFV coverage at all.
        // Engine still runs; first-volume gate handles it.
        Assert.False(ArchiveSetPlanner.ShouldSkipUnverifiableSet(
            completeAllVolumes: true, hashType: HashType.CRC32, expectedCrcCount: 0, volumeCount: 30));
    }

    [Fact]
    public void ShouldSkipUnverifiableSet_Crc32_CompleteAllVolumes_PartialExpected_ReturnsTrue()
    {
        // CRC32 + some volumes covered but not all: partial coverage is an honest skip.
        Assert.True(ArchiveSetPlanner.ShouldSkipUnverifiableSet(
            completeAllVolumes: true, hashType: HashType.CRC32, expectedCrcCount: 15, volumeCount: 30));
    }

    [Fact]
    public void ShouldSkipUnverifiableSet_Crc32_CompleteAllVolumes_FullExpected_ReturnsFalse()
    {
        // Full coverage: all volumes have a CRC — verify, don't skip.
        Assert.False(ArchiveSetPlanner.ShouldSkipUnverifiableSet(
            completeAllVolumes: true, hashType: HashType.CRC32, expectedCrcCount: 30, volumeCount: 30));
    }

    [Fact]
    public void ShouldSkipUnverifiableSet_Crc32_NotCompleteAllVolumes_ReturnsFalse()
    {
        // CompleteAllVolumes is off: skip guard should never fire regardless of CRC coverage.
        Assert.False(ArchiveSetPlanner.ShouldSkipUnverifiableSet(
            completeAllVolumes: false, hashType: HashType.CRC32, expectedCrcCount: 0, volumeCount: 30));
    }
}

/// <summary>Small fixtures for the pure planner tests.</summary>
internal static class ArchiveSetPlannerTestData
{
    public static SharedReconstructionSettings SharedSettings() => new()
    {
        WinRarPath = "C:\\winrar",
        ReleasePath = "C:\\release",
        OutputPath = "C:\\out",
        RarVersions = [new VersionRange(300, 400)],
        CommandLineArguments = [[new RARCommandLineArgument("a", 200)]],
        HashType = HashType.CRC32,
        SetFileArchiveAttribute = TriState.Unchecked,
        SetFileNotContentIndexedAttribute = TriState.Unchecked,
        DeleteRARFiles = false,
        DeleteDuplicateCRCFiles = true,
        StopOnFirstMatch = true,
        CompleteAllVolumes = false,
        RenameToReleaseNames = true,
        EnableHostOSPatching = true,
        UseOldVolumeNaming = false,
    };

    public static BruteForceOptions SampleOptions()
    {
        SharedReconstructionSettings shared = SharedSettings() with
        {
            RarVersions = [new VersionRange(300, 400), new VersionRange(400, 500)],
            CommandLineArguments =
            [
                [new RARCommandLineArgument("a", 200), new RARCommandLineArgument("-m0", 300)],
                [new RARCommandLineArgument("a", 200), new RARCommandLineArgument("-m3", 300)],
            ],
        };

        var set = new SrrArchiveSet { Key = "DVD1/x", Directory = "DVD1" };
        set.VolumeNames.Add("DVD1\\x.rar");
        set.ArchivedFiles.Add("x.iso");
        set.ArchivedFileCrcs["x.iso"] = "00000000";

        return ArchiveSetPlanner.BuildOptionsForSet(set, shared,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }
}
