using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.SRR;
using ReScene.SRS;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Task 9's test matrix (design plan 2026-07-19-multiset-srr-creation.md, Task 9 section; brief
/// task-9-brief.md): folder mode's generated-artifact staging on <see cref="CreatorViewModel"/> —
/// extension-swap naming, the excerpt's <c>same_srs_name</c> collision keying, pre-existing-.srs
/// supersede, SRS-failure .txt storage, multi-SRR subtitle append, a RAR-backed .vob sample's
/// nested SRR, working-dir cleanup on cancellation, and the pass-10 proof-before-sfv reorder over
/// the complete merged list. <see cref="CreatorViewModelFolderModeTests"/> covers the surrounding
/// scan/Create-call plumbing this file assumes already works; this file only exercises the staging
/// step (invoked via <see cref="CreatorViewModel.CreateSRRCommand"/> when samples/subtitles exist).
/// </summary>
public sealed class CreatorViewModelArtifactTests : TempDirTestBase
{
    // ── Fakes (follow CreatorViewModelFolderModeTests.cs's patterns, extended with per-call
    // recording and per-path configurability so a single instance can drive a mixed scenario). ──

    private sealed class RecordingSRSCreationService : ISRSCreationService
    {
        public event EventHandler<SRSCreationProgressEventArgs>? Progress { add { } remove { } }
        public event EventHandler<SRSScanProgressEventArgs>? ScanProgress { add { } remove { } }

        public List<string> CallsInOrder { get; } = [];

        private readonly Dictionary<string, (bool Success, string? Error)> _perSample =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Configures a specific sample's outcome; unconfigured samples default to success.</summary>
        public void Configure(string samplePath, bool success, string? error = null) =>
            _perSample[samplePath] = (success, error);

        /// <summary>Configures a specific sample to make the call itself throw (cancellation propagation test).</summary>
        public void ConfigureThrow(string samplePath, Exception exception) => _throws[samplePath] = exception;

        private readonly Dictionary<string, Exception> _throws = new(StringComparer.OrdinalIgnoreCase);

        public Task<SRSCreationResult> CreateAsync(string outputPath, string sampleFilePath, SRSCreationOptions options, CancellationToken ct)
        {
            CallsInOrder.Add(sampleFilePath);

            if (_throws.TryGetValue(sampleFilePath, out Exception? toThrow))
            {
                throw toThrow;
            }

            if (_perSample.TryGetValue(sampleFilePath, out (bool Success, string? Error) cfg))
            {
                if (!cfg.Success)
                {
                    return Task.FromResult(new SRSCreationResult { Success = false, ErrorMessage = cfg.Error });
                }
            }

            File.WriteAllBytes(outputPath, [1, 2, 3]);
            return Task.FromResult(new SRSCreationResult { Success = true, SRSFileSize = 3 });
        }
    }

    private sealed class RecordingSRRCreationService : ISRRCreationService
    {
        public event EventHandler<SRRCreationProgressEventArgs>? Progress { add { } remove { } }

        public IReadOnlyList<StoredFileEntry>? LastAdditionalFiles { get; private set; }
        public List<string> RarCalls { get; } = [];
        public List<string> SfvCalls { get; } = [];
        public bool RarShouldSucceed { get; set; } = true;
        public bool SfvShouldSucceed { get; set; } = true;

        /// <summary>
        /// Every additional file's bytes, captured AT CALL TIME (mirroring what the real writer
        /// does — reads each source before returning) so a test can assert on generated-artifact
        /// content even though CreatorViewModel's own `finally` deletes the working dir right after
        /// this call returns.
        /// </summary>
        public Dictionary<string, byte[]> CapturedContents { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<SRRCreationResult> CreateFromRARAsync(string outputPath, IReadOnlyList<string> rarVolumePaths,
            IReadOnlyList<StoredFileEntry>? storedFiles, SRRCreationOptions options, CancellationToken ct)
        {
            RarCalls.Add(rarVolumePaths[0]);
            if (RarShouldSucceed)
            {
                File.WriteAllBytes(outputPath, [9]);
            }

            return Task.FromResult(new SRRCreationResult { Success = RarShouldSucceed, ErrorMessage = RarShouldSucceed ? null : "boom" });
        }

        public Task<SRRCreationResult> CreateFromSFVAsync(string outputPath, string sfvFilePath,
            IReadOnlyList<StoredFileEntry>? additionalFiles, SRRCreationOptions options, CancellationToken ct)
        {
            SfvCalls.Add(sfvFilePath);
            if (SfvShouldSucceed)
            {
                File.WriteAllBytes(outputPath, [9]);
            }

            return Task.FromResult(new SRRCreationResult { Success = SfvShouldSucceed, ErrorMessage = SfvShouldSucceed ? null : "boom" });
        }

        public Task<SRRCreationResult> CreateFromInputsAsync(string outputPath, IReadOnlyList<string> inputFiles,
            string? rootFolder, bool storeRelativePaths, IReadOnlyList<StoredFileEntry>? additionalFiles,
            SRRCreationOptions options, CancellationToken ct)
        {
            LastAdditionalFiles = additionalFiles;
            if (additionalFiles is not null)
            {
                foreach (StoredFileEntry entry in additionalFiles)
                {
                    if (File.Exists(entry.FullPath))
                    {
                        CapturedContents[entry.StoredName] = File.ReadAllBytes(entry.FullPath);
                    }
                }
            }

            return Task.FromResult(new SRRCreationResult { Success = true });
        }
    }

    private sealed class StubReleaseScanner(ReleaseScanResult result) : IReleaseScanner
    {
        public ReleaseScanResult Scan(string releaseRoot, CancellationToken ct = default) => result;
    }

    // ── Helpers ─────────────────────────────────────────────

    private static string Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    private static string WriteBytes(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private (CreatorViewModel Vm, RecordingSRSCreationService Srs, RecordingSRRCreationService Srr, string WorkDir) CreateVm(
        ReleaseScanResult scan, string? fixedWorkDir = null)
    {
        var srs = new RecordingSRSCreationService();
        var srr = new RecordingSRRCreationService();
        string workDir = fixedWorkDir ?? Path.Combine(TempDir, "work-" + Guid.NewGuid().ToString("N"));
        var vm = new CreatorViewModel(srr, srs, new NoOpFileDialogService(), new NoOpTempDirectoryService(),
            new NoOpAppSettingsService(), new TestUiDispatcher(), new StubReleaseScanner(scan), () => workDir)
        {
            AutoIncludeFiles = false,
            AutoCreateSRS = false,
            CreateVobsubSRR = false,
            StoreFixRAR = false,
        };
        return (vm, srs, srr, workDir);
    }

    private async Task<IReadOnlyList<StoredFileEntry>> RunCreateAsync(CreatorViewModel vm, string root, RecordingSRRCreationService srr)
    {
        vm.InputPath = root;
        await vm.LastFolderScan!;
        vm.OutputPath = Path.Combine(TempDir, "out-" + Guid.NewGuid().ToString("N") + ".srr");
        await vm.CreateSRRCommand.ExecuteAsync(null);
        return srr.LastAdditionalFiles ?? [];
    }

    // ── 1. Extension-swap naming ──────────────────────────────

    [Fact]
    public async Task Sample_NoCollision_SrsNameDropsSourceExtension()
    {
        string root = Path.Combine(TempDir, "release-" + Guid.NewGuid().ToString("N"));
        string sample = Touch(Path.Combine(root, "Sample", "clip.mkv"));
        var scan = new ReleaseScanResult([], [sample], [], [], [], []);
        (CreatorViewModel vm, _, RecordingSRRCreationService srr, _) = CreateVm(scan);

        IReadOnlyList<StoredFileEntry> additionalFiles = await RunCreateAsync(vm, root, srr);

        Assert.Contains(additionalFiles, e => e.StoredName == "Sample/clip.srs");
    }

    // ── 2. Cross-dir same-stem: NOT a collision ───────────────

    [Fact]
    public async Task Samples_SameBasenameStem_DifferentDirs_NoCollision_BothDropExtension()
    {
        string root = Path.Combine(TempDir, "release-" + Guid.NewGuid().ToString("N"));
        string sample1 = Touch(Path.Combine(root, "Sample", "clip.mkv"));
        string sample2 = Touch(Path.Combine(root, "Extras", "clip.avi"));
        var scan = new ReleaseScanResult([], [sample1, sample2], [], [], [], []);
        (CreatorViewModel vm, _, RecordingSRRCreationService srr, _) = CreateVm(scan);

        IReadOnlyList<StoredFileEntry> additionalFiles = await RunCreateAsync(vm, root, srr);

        Assert.Contains(additionalFiles, e => e.StoredName == "Sample/clip.srs");
        Assert.Contains(additionalFiles, e => e.StoredName == "Extras/clip.srs");
    }

    // ── 3. Same-stem, same dir: collision keeps the full source extension ──

    [Fact]
    public async Task Samples_SameRelativeStem_Collision_KeepsFullSourceExtension()
    {
        string root = Path.Combine(TempDir, "release-" + Guid.NewGuid().ToString("N"));
        string sample1 = Touch(Path.Combine(root, "Sample", "clip.mkv"));
        string sample2 = Touch(Path.Combine(root, "Sample", "clip.avi"));
        var scan = new ReleaseScanResult([], [sample1, sample2], [], [], [], []);
        (CreatorViewModel vm, _, RecordingSRRCreationService srr, _) = CreateVm(scan);

        IReadOnlyList<StoredFileEntry> additionalFiles = await RunCreateAsync(vm, root, srr);

        Assert.Contains(additionalFiles, e => e.StoredName == "Sample/clip.mkv.srs");
        Assert.Contains(additionalFiles, e => e.StoredName == "Sample/clip.avi.srs");
        Assert.DoesNotContain(additionalFiles, e => e.StoredName == "Sample/clip.srs");
    }

    // ── 4. Supersede: a freshly-generated SRS replaces a pre-existing one at the same name ──

    [Fact]
    public async Task GeneratedSrs_SupersedesPreExistingSrs_SameLogicalName_NoCollisionError()
    {
        string root = Path.Combine(TempDir, "release-" + Guid.NewGuid().ToString("N"));
        string sample = Touch(Path.Combine(root, "Sample", "clip.mkv"));
        string preExistingSrs = Touch(Path.Combine(root, "Sample", "clip.srs"));
        // The baseline StoredFiles snapshot (as ApplyFolderScanResult would build it) already
        // contains the pre-existing srs at its root-relative name.
        var scan = new ReleaseScanResult([], [sample], [], [preExistingSrs], [], []);
        (CreatorViewModel vm, _, RecordingSRRCreationService srr, string workDir) = CreateVm(scan);

        IReadOnlyList<StoredFileEntry> additionalFiles = await RunCreateAsync(vm, root, srr);

        StoredFileEntry entry = Assert.Single(additionalFiles, e => e.StoredName == "Sample/clip.srs");
        // The surviving entry points at the freshly-generated file (under the working dir), not
        // the original pre-existing one on disk.
        Assert.StartsWith(workDir, entry.FullPath, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(preExistingSrs, entry.FullPath, StringComparer.OrdinalIgnoreCase);
    }

    // ── 5. SRS failure -> .txt stored only when non-empty ──────

    [Fact]
    public async Task SrsFailure_NonEmptyError_TxtStored_EmptyError_NothingStored()
    {
        string root = Path.Combine(TempDir, "release-" + Guid.NewGuid().ToString("N"));
        string failingSample = Touch(Path.Combine(root, "Sample", "bad.mkv"));
        string silentlyFailingSample = Touch(Path.Combine(root, "Sample", "silent.mkv"));
        var scan = new ReleaseScanResult([], [failingSample, silentlyFailingSample], [], [], [], []);
        (CreatorViewModel vm, RecordingSRSCreationService srs, RecordingSRRCreationService srr, _) = CreateVm(scan);
        srs.Configure(failingSample, success: false, error: "SRS creation failed for bad.mkv!");
        srs.Configure(silentlyFailingSample, success: false, error: string.Empty);

        IReadOnlyList<StoredFileEntry> additionalFiles = await RunCreateAsync(vm, root, srr);

        Assert.Contains(additionalFiles, e => e.StoredName == "Sample/bad.mkv.txt");
        Assert.Equal("SRS creation failed for bad.mkv!", System.Text.Encoding.UTF8.GetString(srr.CapturedContents["Sample/bad.mkv.txt"]));
        Assert.DoesNotContain(additionalFiles, e => e.StoredName.StartsWith("Sample/silent", StringComparison.Ordinal));
        Assert.DoesNotContain(additionalFiles, e => e.StoredName == "Sample/silent.mkv.srs");
    }

    // ── 6. Multi-SRR subtitle: all appended, in order ──────────

    [Fact]
    public async Task SubtitleSfv_YieldingMultipleSrrs_AllAppended_InOrder()
    {
        string root = Path.Combine(TempDir, "release-" + Guid.NewGuid().ToString("N"));
        string subSfv = Touch(Path.Combine(root, "Subs", "subs.sfv"));
        var scan = new ReleaseScanResult([], [], [subSfv], [], [], []);
        (CreatorViewModel vm, _, RecordingSRRCreationService srr, string workDir) = CreateVm(scan);

        // Task 9's own writer always folds one SFV's chains into ONE nested SRR (production never
        // needs more than one result) — this test seam simulates the spec's general "one SFV may
        // yield several" contract to prove the merge/append logic handles N > 1 generically.
        vm.NestedSubtitleSrrGeneratorOverride = (sfvPath, dir, index, options, ct) =>
        {
            string first = Path.Combine(dir, $"{index}_a.srr");
            string second = Path.Combine(dir, $"{index}_b.srr");
            File.WriteAllBytes(first, [1]);
            File.WriteAllBytes(second, [2]);
            return Task.FromResult<IReadOnlyList<string>>([first, second]);
        };

        IReadOnlyList<StoredFileEntry> additionalFiles = await RunCreateAsync(vm, root, srr);

        int idx0 = additionalFiles.ToList().FindIndex(e => e.StoredName == "Subs/subs.0.srr");
        int idx1 = additionalFiles.ToList().FindIndex(e => e.StoredName == "Subs/subs.1.srr");
        int idxSfv = additionalFiles.ToList().FindIndex(e => e.StoredName == "Subs/subs.sfv");
        Assert.True(idx0 >= 0 && idx1 >= 0 && idxSfv >= 0);
        Assert.True(idx0 < idx1, "first nested SRR must precede the second");
        Assert.True(idx1 < idxSfv, "both nested SRRs must precede the subtitle SFV itself");
        _ = workDir;
    }

    // ── 7. RAR-backed .vob sample keeps its SRS AND adds a nested SRR ──

    [Fact]
    public async Task RarBackedVobSample_KeepsSrs_AndAddsNestedSrr()
    {
        string root = Path.Combine(TempDir, "release-" + Guid.NewGuid().ToString("N"));
        string vobSample = WriteBytes(Path.Combine(root, "Sample", "clip.vob"), [(byte)'R', (byte)'a', (byte)'r', (byte)'!', 0x1A, 0x07, 0x00]);
        var scan = new ReleaseScanResult([], [vobSample], [], [], [], []);
        (CreatorViewModel vm, _, RecordingSRRCreationService srr, _) = CreateVm(scan);

        IReadOnlyList<StoredFileEntry> additionalFiles = await RunCreateAsync(vm, root, srr);

        Assert.Contains(additionalFiles, e => e.StoredName == "Sample/clip.srs");
        Assert.Contains(additionalFiles, e => e.StoredName == "Sample/clip.srr");
        Assert.Contains(vobSample, srr.RarCalls);
    }

    [Fact]
    public async Task UppercaseVOB_RarBacked_DoesNotGetNestedSrr_CaseSensitiveExtensionCheck()
    {
        // excerpt L744: sample.endswith(".vob") is case-SENSITIVE — a ".VOB" sample never matches,
        // even though its leading bytes are the same RAR marker.
        string root = Path.Combine(TempDir, "release-" + Guid.NewGuid().ToString("N"));
        string vobSample = WriteBytes(Path.Combine(root, "Sample", "clip.VOB"), [(byte)'R', (byte)'a', (byte)'r', (byte)'!', 0x1A, 0x07, 0x00]);
        var scan = new ReleaseScanResult([], [vobSample], [], [], [], []);
        (CreatorViewModel vm, _, RecordingSRRCreationService srr, _) = CreateVm(scan);

        IReadOnlyList<StoredFileEntry> additionalFiles = await RunCreateAsync(vm, root, srr);

        Assert.Contains(additionalFiles, e => e.StoredName == "Sample/clip.srs");
        Assert.DoesNotContain(additionalFiles, e => e.StoredName == "Sample/clip.srr");
        Assert.Empty(srr.RarCalls);
    }

    // ── 8. Cancellation removes the working dir; OCE not swallowed by the staging code ──

    [Fact]
    public async Task Cancellation_DuringArtifactStaging_RemovesWorkingDir_DoesNotSwallowOce()
    {
        string root = Path.Combine(TempDir, "release-" + Guid.NewGuid().ToString("N"));
        string sample = Touch(Path.Combine(root, "Sample", "clip.mkv"));
        var scan = new ReleaseScanResult([], [sample], [], [], [], []);
        string workDir = Path.Combine(TempDir, "cancel-work-" + Guid.NewGuid().ToString("N"));
        (CreatorViewModel vm, RecordingSRSCreationService srs, RecordingSRRCreationService srr, _) = CreateVm(scan, workDir);
        srs.ConfigureThrow(sample, new OperationCanceledException("simulated mid-staging cancellation"));

        vm.InputPath = root;
        await vm.LastFolderScan!;
        vm.OutputPath = Path.Combine(TempDir, "out.srr");

        await vm.CreateSRRCommand.ExecuteAsync(null);

        // The staging code's own `finally` must have deleted the working dir it created — a
        // swallowed OCE (or one that skipped the finally) would leave it behind.
        Assert.False(Directory.Exists(workDir));
        // The VM's own top-level catch (pre-existing, out of this task's scope) is what ultimately
        // absorbs the exception — but if my inner code had SWALLOWED it instead of letting it
        // propagate, the run would incorrectly report success.
        Assert.False(vm.BuildSucceeded);
        Assert.Null(srr.LastAdditionalFiles); // CreateFromInputsAsync never reached
        Assert.Contains(vm.LogEntries, e => e.Contains("ERROR", StringComparison.Ordinal));
    }

    // ── 9. Pass-10 reorder over the complete merged list ────────

    [Fact]
    public async Task SubtitleNestedSrr_NaturallyPrecedesItsSfv_InTheMergedList()
    {
        string root = Path.Combine(TempDir, "release-" + Guid.NewGuid().ToString("N"));
        string nfo = Touch(Path.Combine(root, "release.nfo"));
        string subSfv = Touch(Path.Combine(root, "Subs", "subs.sfv"));
        // Baseline stored files as the scanner would have produced them (nfo, then the pass-10
        // input sfvs) — the subtitle sfv is NOT in StoredFiles yet (it lives in SubtitleSfvs);
        // Task 9's own merge pass 9 is what stores it.
        var scan = new ReleaseScanResult([], [], [subSfv], [nfo], [], []);
        (CreatorViewModel vm, _, RecordingSRRCreationService srr, _) = CreateVm(scan);

        IReadOnlyList<StoredFileEntry> additionalFiles = await RunCreateAsync(vm, root, srr);

        List<string> names = [.. additionalFiles.Select(e => e.StoredName)];
        int srrIndex = names.IndexOf("Subs/subs.srr");
        int sfvIndex = names.IndexOf("Subs/subs.sfv");
        Assert.True(srrIndex >= 0 && sfvIndex >= 0);
        Assert.Equal(sfvIndex - 1, srrIndex);
        // nfo (unrelated to the reorder) keeps its own position ahead of everything else.
        Assert.Equal(0, names.IndexOf("release.nfo"));
    }

    [Fact]
    public async Task BaselineProofPair_ArrivingOutOfOrder_IsCorrectedByTheVmsOwnReorderPass()
    {
        // Defense-in-depth: the REAL ReleaseScanner already reorders a rule-4 proof sfv/rar pair
        // before returning (its OWN internal ApplyProofBeforeSfvReorder call — see
        // ReleaseScannerStoredTests.ProofRar_AlreadyStoredByRule4_NotDoubleAdded), so a genuine
        // scan result never arrives at the VM out of order. This test feeds a hand-built
        // ReleaseScanResult with the pair in the WRONG order anyway (as a user-edited StoredFiles
        // list, or a different IReleaseScanner implementation, might) to prove the VM's OWN
        // ApplyProofBeforeSfvReorder call is what fixes it — not merely inherited for free from the
        // scanner having already done so.
        string root = Path.Combine(TempDir, "release-" + Guid.NewGuid().ToString("N"));
        string proofSfv = Touch(Path.Combine(root, "Proof", "p.sfv"));
        string proofRar = Touch(Path.Combine(root, "Proof", "p.rar"));
        // A sample is present purely so the folder-mode Create branch actually invokes
        // StageFolderArtifactsAsync (it's a no-op when there's nothing to generate).
        string sample = Touch(Path.Combine(root, "Sample", "clip.mkv"));
        var scan = new ReleaseScanResult([], [sample], [], [proofSfv, proofRar], [], []);
        (CreatorViewModel vm, _, RecordingSRRCreationService srr, _) = CreateVm(scan);

        IReadOnlyList<StoredFileEntry> additionalFiles = await RunCreateAsync(vm, root, srr);

        List<string> names = [.. additionalFiles.Select(e => e.StoredName)];
        int sfvIndex = names.IndexOf("Proof/p.sfv");
        int rarIndex = names.IndexOf("Proof/p.rar");
        Assert.True(sfvIndex >= 0 && rarIndex >= 0);
        Assert.Equal(sfvIndex - 1, rarIndex);
    }
}
