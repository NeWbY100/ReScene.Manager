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

    // ── final-review Important: a keyed set whose work-root resolution throws is a per-set failure, ──
    //    not a whole-run abort. WorkRootFor is computed before the per-set try; a throw there must be
    //    recorded as THIS set's failure and the loop must continue to the next set.

    [Fact]
    public async Task Run_WorkRootResolutionThrows_FailsThatSetOnly_SiblingStillCommits()
    {
        var brute = new ScriptedBruteForceService
        {
            OnRun = o => WriteBruteSuccess(o, o.RAROptions.OriginalRARFileNames[0]),
        };
        ReconstructorViewModel vm = CreateVm(brute);
        vm.WinRARPath = TempDir;
        vm.ReleasePath = TempDir;
        vm.OutputPath = TempDir;
        vm.CompleteAllVolumes = false;

        // The failing set is ordered first, so proving the loop still reached and committed the sibling
        // proves it did NOT abort every remaining set on the throw.
        SRRArchiveSet bad = MakeSet("bad", "", "bad.rar");
        SRRArchiveSet good = MakeSet("good", "", "good.rar");
        vm.SetImportStateForTest(ImportWith(bad, good));

        // Make ONLY the first set's guarded scratch child a junction that escapes the reserved scratch
        // root, so its WorkRootFor -> ResolveScratchChild throws (ArgumentException from the escape
        // guard) OUTSIDE the per-set try. The sibling's scratch child does not exist and resolves normally.
        string badScratch = ReconstructionPathGuard.ResolveScratchChild(TempDir, "bad");
        Directory.CreateDirectory(Path.GetDirectoryName(badScratch)!); // the reserved .rescene-work root
        CreateJunction(badScratch, Path.Combine(TempDir, "escape-scratch")); // target is outside .rescene-work

        // Must NOT throw: the WorkRootFor failure is caught per-set, never propagated out of the loop.
        await vm.RunArchiveSetsForTestAsync(CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(TempDir, "output", "good.rar")));   // sibling continued & committed
        Assert.False(File.Exists(Path.Combine(TempDir, "output", "bad.rar")));   // failed set produced no output
        Assert.Contains("Set bad failed:", vm.SystemLog, StringComparison.Ordinal); // recorded as this set's failure
        Assert.False(vm.LastRunSucceeded);                                       // summary ran; not all sets matched
    }

    [Fact]
    public async Task Run_WorkRootResolutionAccessDenied_CaughtPerSet_DoesNotAbortRun()
    {
        var brute = new ScriptedBruteForceService
        {
            OnRun = o => WriteBruteSuccess(o, o.RAROptions.OriginalRARFileNames[0]),
        };
        ReconstructorViewModel vm = CreateVm(brute);
        vm.WinRARPath = TempDir;
        vm.ReleasePath = TempDir;
        vm.OutputPath = TempDir;
        vm.CompleteAllVolumes = false;

        SRRArchiveSet one = MakeSet("one", "", "one.rar");
        SRRArchiveSet two = MakeSet("two", "", "two.rar");
        vm.SetImportStateForTest(ImportWith(one, two));

        // Deny inspection of the shared reserved scratch root itself: every keyed set's WorkRootFor ->
        // ResolveScratchChild -> ResolveReal must throw UnauthorizedAccessException as it descends into
        // it. Each throw must be caught per-set so the run reaches ReportSetSummary rather than aborting.
        // (A shared denied root fails BOTH keyed sets, so sibling-continuation is proven separately by the
        // junction test above; here the point is that an access-denied throw does not escape the loop.)
        string scratchRoot = Path.Combine(TempDir, ReconstructionPathGuard.ScratchDirName);
        Directory.CreateDirectory(scratchRoot);
        AclDenyHelper.DenyAccess(scratchRoot);
        try
        {
            await vm.RunArchiveSetsForTestAsync(CancellationToken.None); // must NOT throw
        }
        finally
        {
            AclDenyHelper.RestoreAccess(scratchRoot); // restore BEFORE temp-dir cleanup
        }

        Assert.Contains("Set one failed:", vm.SystemLog, StringComparison.Ordinal);
        Assert.Contains("Set two failed:", vm.SystemLog, StringComparison.Ordinal);
        Assert.False(vm.LastRunSucceeded); // summary ran and marked the run failed — the run did not abort
    }

    /// <summary>Creates a directory junction (reparse point) at <paramref name="link"/> pointing to a real target.</summary>
    private static void CreateJunction(string link, string target)
    {
        Directory.CreateDirectory(target);
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi)!;
        p.WaitForExit();
        Assert.True(p.ExitCode == 0 && Directory.Exists(link),
            $"Could not create junction '{link}' -> '{target}': {p.StandardError.ReadToEnd()}");
    }
}
