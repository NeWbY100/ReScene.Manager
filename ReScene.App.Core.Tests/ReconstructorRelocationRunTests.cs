using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.App.Core.ViewModels.Reconstruction;
using ReScene.Core;
using ReScene.SRR;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Drives the reconstruction run loop with a fake brute-force service that writes committed files into
/// the guarded scratch work-root exactly as the library would, proving the headline fix (#3): a verified
/// keyed set's output is relocated into <c>OutputPath\output</c> and its scratch removed; the legacy
/// empty-key set stays byte-identical; and a cancelled in-flight set's scratch is cleaned while a
/// committed set is left intact (cases g, byte-identical, headline).
/// </summary>
public sealed class ReconstructorRelocationRunTests : TempDirTestBase
{
    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, UiDispatcherPriority priority) => action();
        public bool CheckAccess() => true;
    }

    /// <summary>Fake brute-force service that runs a supplied handler for each set and writes real files.</summary>
    private sealed class ScriptedBruteForceService : IBruteForceService
    {
        public event EventHandler<BruteForceProgressEventArgs>? Progress { add { } remove { } }
        public event EventHandler<BruteForceStatusChangedEventArgs>? StatusChanged { add { } remove { } }
        public event EventHandler<LogEventArgs>? LogMessage { add { } remove { } }
        public event EventHandler<FileCopyProgressEventArgs>? FileCopyProgress { add { } remove { } }
        public event EventHandler<CRCValidationProgressEventArgs>? CRCValidationProgress { add { } remove { } }
        public event EventHandler<TimestampPreservationFailedEventArgs>? TimestampPreservationFailed { add { } remove { } }

        public required Func<BruteForceOptions, BruteForceRunResult> OnRun { get; init; }

        public Task<BruteForceRunResult> RunAsync(BruteForceOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(OnRun(options));
    }

    private static ReconstructorViewModel CreateVm(ScriptedBruteForceService brute) =>
        new(brute, new NoOpFileDialogService(), new InlineUiDispatcher(), new TestUiTimerFactory(), settingsService: null);

    private static SRRArchiveSet MakeSet(string key, string dir, params string[] volumes)
    {
        var set = new SRRArchiveSet { Key = key, Directory = dir };
        foreach (string v in volumes)
        {
            set.VolumeNames.Add(v);
        }

        return set;
    }

    private static ReconstructionImportState ImportWith(params SRRArchiveSet[] sets) => new()
    {
        ArchiveSets = sets,
        OriginalRARFileNames = [.. sets.SelectMany(s => s.VolumeNames)],
    };

    /// <summary>Writes one brute-force committed volume under the run's scratch <c>output</c> dir.</summary>
    private static BruteForceRunResult WriteBruteSuccess(BruteForceOptions options, string volumeName)
    {
        string dir = Path.Combine(options.OutputDirectoryPath, "output");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, volumeName);
        File.WriteAllText(file, "vol");
        var combo = new WinningCombo(500, []);
        return new BruteForceRunResult(true, combo) { Matches = [new CommittedMatch(combo, [file])] };
    }

    // ── headline (#3): a keyed single set relocates to OutputPath\output and its scratch is removed ──

    [Fact]
    public async Task Run_KeyedSingleSet_RelocatesToOutput_AndRemovesScratch()
    {
        var brute = new ScriptedBruteForceService { OnRun = o => WriteBruteSuccess(o, "store_little.rar") };
        ReconstructorViewModel vm = CreateVm(brute);
        vm.WinRARPath = TempDir;
        vm.ReleasePath = TempDir;
        vm.OutputPath = TempDir;
        vm.CompleteAllVolumes = false;
        vm.SetImportStateForTest(ImportWith(MakeSet("store_little", "", "store_little.rar")));

        await vm.RunArchiveSetsForTestAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(TempDir, "output", "store_little.rar")));
        Assert.False(Directory.Exists(Path.Combine(TempDir, ".rescene-work"))
            && Directory.EnumerateFileSystemEntries(Path.Combine(TempDir, ".rescene-work")).Any());
        Assert.True(vm.LastRunSucceeded);
    }

    // ── byte-identical: the legacy empty-key set keeps output at OutputPath\output, no scratch ──

    [Fact]
    public async Task Run_LegacyEmptyKeySet_KeepsOutputInPlace_NoScratchCreated()
    {
        var brute = new ScriptedBruteForceService { OnRun = o => WriteBruteSuccess(o, "x.rar") };
        ReconstructorViewModel vm = CreateVm(brute);
        vm.WinRARPath = TempDir;
        vm.ReleasePath = TempDir;
        vm.OutputPath = TempDir;
        vm.CompleteAllVolumes = false;
        // No archive sets → ResolveSets synthesizes a single flat set with an empty key (the legacy path).
        vm.SetImportStateForTest(new ReconstructionImportState { OriginalRARFileNames = ["x.rar"] });

        await vm.RunArchiveSetsForTestAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(TempDir, "output", "x.rar"))); // already final, byte-identical
        Assert.False(Directory.Exists(Path.Combine(TempDir, ".rescene-work"))); // no scratch used at all
        Assert.True(vm.LastRunSucceeded);
    }

    // ── (g) cancel mid-set: in-flight scratch removed; the already-committed set is untouched ──

    [Fact]
    public async Task Run_CancelDuringSecondSet_CleansItsScratch_LeavesFirstSetCommitted()
    {
        using var cts = new CancellationTokenSource();
        var brute = new ScriptedBruteForceService
        {
            OnRun = o =>
            {
                if (o.RAROptions.OriginalRARFileNames.Contains("b.rar"))
                {
                    // Second set: write a partial scratch, then cancel — the loop must break and clean it.
                    Directory.CreateDirectory(Path.Combine(o.OutputDirectoryPath, "output"));
                    File.WriteAllText(Path.Combine(o.OutputDirectoryPath, "output", "b.rar"), "partial");
                    cts.Cancel();
                    return new BruteForceRunResult(true, new WinningCombo(500, []));
                }

                return WriteBruteSuccess(o, "a.rar");
            },
        };
        ReconstructorViewModel vm = CreateVm(brute);
        vm.WinRARPath = TempDir;
        vm.ReleasePath = TempDir;
        vm.OutputPath = TempDir;
        vm.CompleteAllVolumes = false;
        vm.SetImportStateForTest(ImportWith(MakeSet("a", "", "a.rar"), MakeSet("b", "", "b.rar")));

        // The loop breaks on cancellation and returns normally (StartAsync raises the OCE afterwards).
        await vm.RunArchiveSetsForTestAsync(cts.Token);

        Assert.True(File.Exists(Path.Combine(TempDir, "output", "a.rar")));      // set 1 committed
        Assert.False(File.Exists(Path.Combine(TempDir, "output", "b.rar")));     // set 2 never relocated
        Assert.False(Directory.Exists(ReconstructionPathGuard.ResolveScratchChild(TempDir, "b"))); // scratch cleaned
    }
}
