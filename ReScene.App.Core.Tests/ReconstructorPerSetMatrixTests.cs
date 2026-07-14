using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.App.Core.ViewModels.Reconstruction;
using ReScene.Core;
using ReScene.SRR;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Drives the reconstruction run loop's per-set matrix build (#6) end to end: a set whose own
/// header metadata demands a format/version no selected WinRAR executable can produce fails that
/// set honestly (raised inside the per-set <c>try</c>) — without aborting its sibling sets.
/// </summary>
public sealed class ReconstructorPerSetMatrixTests : TempDirTestBase
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

    [Fact]
    public async Task Run_OneSetFormatUnsatisfiable_FailsThatSetOnly_SiblingStillCommits()
    {
        var brute = new ScriptedBruteForceService
        {
            OnRun = o => WriteBruteSuccess(o, o.RAROptions.OriginalRARFileNames[0]),
        };
        ReconstructorViewModel vm = CreateVm(brute);

        // No WinRARPath is set (stays "") so no folder scan ever runs — HasScannedVersions is
        // deterministically false and the version fallback is the coarse major toggles below, with
        // no dependence on real scan timing.
        vm.Version5 = false;
        vm.Version6 = false; // only 3.x/4.x (defaults) enabled — nothing in RAR5's 500-699 band
        vm.ReleasePath = TempDir;
        vm.OutputPath = TempDir;
        vm.CompleteAllVolumes = false;

        SRRArchiveSet good = MakeSet("a", "", "a.rar");          // no relevant metadata → global matrix
        SRRArchiveSet bad = MakeSet("b", "", "b.rar");
        bad.RARVersion = 50;                                     // RAR5 — unsatisfiable by 3.x/4.x only
        vm.SetImportStateForTest(ImportWith(good, bad));

        await vm.RunArchiveSetsForTestAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(TempDir, "output", "a.rar")));   // sibling set committed
        Assert.False(File.Exists(Path.Combine(TempDir, "output", "b.rar")));  // failed set never ran
        Assert.Contains("Set b failed: no selected WinRAR version can produce RAR5", vm.SystemLog, StringComparison.Ordinal);
        Assert.False(vm.LastRunSucceeded); // not all sets matched
    }
}
