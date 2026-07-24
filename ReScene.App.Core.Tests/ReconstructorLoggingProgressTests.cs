using System.Collections.Concurrent;
using System.ComponentModel;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.App.Core.ViewModels.Reconstruction;
using ReScene.Core;
using ReScene.Core.IO;
using ReScene.SRR;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Concurrency- and generation-focused coverage for the RAR Reconstructor's run finalisation:
/// the once-per-run timestamp summary raised from the run's <c>finally</c> (#19), the generation-safe
/// batched log flush (#20), and the set/attempt-labelled progress message (#24). These exercise the
/// thread-safety of <c>_timestampFailures</c>, the atomic flush flag + run-generation token on the log
/// queue, and the seed→full stage labelling that stops progress reading as a rewind within a set.
/// </summary>
public sealed class ReconstructorLoggingProgressTests : TempDirTestBase
{
    // ── Fakes ───────────────────────────────────────────────

    /// <summary>Brute-force service with real events the test can raise, plus a scriptable run handler.</summary>
    private sealed class RaisingBruteForceService : IBruteForceService
    {
        public event EventHandler<BruteForceProgressEventArgs>? Progress;
        public event EventHandler<BruteForceStatusChangedEventArgs>? StatusChanged;
        public event EventHandler<LogEventArgs>? LogMessage;
        public event EventHandler<FileCopyProgressEventArgs>? FileCopyProgress { add { } remove { } }
        public event EventHandler<CRCValidationProgressEventArgs>? CRCValidationProgress { add { } remove { } }
        public event EventHandler<TimestampPreservationFailedEventArgs>? TimestampPreservationFailed;

        public Func<BruteForceOptions, BruteForceRunResult>? OnRun { get; set; }

        public void RaiseProgress(BruteForceProgressEventArgs e) => Progress?.Invoke(this, e);
        public void RaiseStatusCompleted() => StatusChanged?.Invoke(this, new BruteForceStatusChangedEventArgs(OperationStatus.Completed));
        public void RaiseLog(LogTarget target, string message) => LogMessage?.Invoke(this, new LogEventArgs(message, target));
        public void RaiseTimestampFailure(string dest, string error) =>
            TimestampPreservationFailed?.Invoke(this, new TimestampPreservationFailedEventArgs { DestinationPath = dest, ErrorMessage = error });

        public Task<BruteForceRunResult> RunAsync(BruteForceOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(OnRun?.Invoke(options) ?? new BruteForceRunResult(true, new WinningCombo(500, [])));
    }

    /// <summary>Runs everything inline on the calling thread (both Invoke and Post).</summary>
    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, UiDispatcherPriority priority) => action();
        public bool CheckAccess() => true;
    }

    /// <summary>
    /// Counts UI-dispatch calls. <see cref="Invoke"/> always runs inline (and is counted); <see cref="Post"/>
    /// is counted and either runs inline or is deferred onto a queue drained by <see cref="Pump"/> — so a
    /// test can observe how many batched log flushes were scheduled and control exactly when they run.
    /// </summary>
    private sealed class CountingUiDispatcher(bool deferPosts) : IUiDispatcher
    {
        private readonly Queue<Action> _deferred = new();
        public int PostCount { get; private set; }
        public int InvokeCount { get; private set; }

        public void Invoke(Action action)
        {
            InvokeCount++;
            action();
        }

        public void Post(Action action)
        {
            PostCount++;
            if (deferPosts)
            {
                _deferred.Enqueue(action);
            }
            else
            {
                action();
            }
        }

        public void Post(Action action, UiDispatcherPriority priority) => Post(action);
        public bool CheckAccess() => true;

        public void Pump()
        {
            while (_deferred.Count > 0)
            {
                _deferred.Dequeue()();
            }
        }
    }

    /// <summary>Records warning dialogs so a test can assert the timestamp summary fires exactly once.</summary>
    private sealed class RecordingFileDialogService : NoOpFileDialogService
    {
        public List<(string Title, string Message)> Warnings { get; } = [];
        public override void ShowWarning(string title, string message) => Warnings.Add((title, message));
    }

    // ── Helpers ─────────────────────────────────────────────

    private static ReconstructorViewModel CreateVm(RaisingBruteForceService brute, IUiDispatcher dispatcher, IFileDialogService? dialog = null) =>
        new(brute, dialog ?? new NoOpFileDialogService(), dispatcher, new TestUiTimerFactory(), settingsService: null);

    private static SRRArchiveSet MakeSet(string key, params string[] volumes)
    {
        var set = new SRRArchiveSet { Key = key, Directory = "" };
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

    private static BruteForceRunResult WriteBruteSuccess(BruteForceOptions options, string volumeName)
    {
        string dir = Path.Combine(options.OutputDirectoryPath, "output");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, volumeName);
        File.WriteAllText(file, "vol");
        var combo = new WinningCombo(500, []);
        return new BruteForceRunResult(true, combo) { Matches = [new CommittedMatch(combo, [file])] };
    }

    private void ConfigureRunnablePaths(ReconstructorViewModel vm)
    {
        vm.WinRARPath = TempDir;
        vm.ReleasePath = TempDir;
        vm.OutputPath = TempDir;
        vm.CompleteAllVolumes = false;
    }

    private static BruteForceProgressEventArgs MakeProgress(long progressed) =>
        new("release", "winrar-500", "a c -m0", operationSize: 100, operationProgressed: progressed, startDateTime: DateTime.Now)
        {
            PhaseDescription = "Phase 2: Full RAR Creation",
        };

    // Same combo key as MakeProgress, flagged CombinationFailed — mirrors the event the engine fires
    // when it could not run rar (e.g. binary not executable), which marks that row "Error".
    private static BruteForceProgressEventArgs MakeFailedProgress(long progressed) =>
        new("release", "winrar-500", "a c -m0", operationSize: 100, operationProgressed: progressed, startDateTime: DateTime.Now)
        {
            PhaseDescription = "Phase 2: Full RAR Creation",
            CombinationFailed = true,
        };

    // ── #19 — timestamp summary once, from the run's finally, thread-safe ──

    [Fact]
    public async Task TwoSetsEachFailTimestamp_ShowsExactlyOneSummary_FromFinally()
    {
        var brute = new RaisingBruteForceService();
        var dialog = new RecordingFileDialogService();
        ReconstructorViewModel vm = CreateVm(brute, new InlineUiDispatcher(), dialog);
        ConfigureRunnablePaths(vm);
        vm.SetImportStateForTest(ImportWith(MakeSet("a", "a.rar"), MakeSet("b", "b.rar")));

        brute.OnRun = o =>
        {
            string volume = o.RAROptions.OriginalRARFileNames[0];
            // Each underlying engine attempt reports a timestamp failure and a Completed status — the
            // pre-fix code showed the summary once per Completed (per attempt); the fix shows it once.
            brute.RaiseTimestampFailure(volume, "could not set mtime");
            brute.RaiseStatusCompleted();
            return WriteBruteSuccess(o, volume);
        };

        await vm.ExecuteReconstructionForTestAsync(CancellationToken.None);

        Assert.True(vm.LastRunSucceeded, vm.SystemLog);
        (string Title, string Message) warning = Assert.Single(dialog.Warnings);
        Assert.Equal("Timestamp Preservation Failed", warning.Title);
    }

    [Fact]
    public async Task Cancelled_WithTimestampFailures_StillShowsSummaryOnceFromFinally()
    {
        var brute = new RaisingBruteForceService();
        var dialog = new RecordingFileDialogService();
        ReconstructorViewModel vm = CreateVm(brute, new InlineUiDispatcher(), dialog);
        ConfigureRunnablePaths(vm);
        vm.SetImportStateForTest(ImportWith(MakeSet("a", "a.rar")));

        using var cts = new CancellationTokenSource();
        brute.OnRun = o =>
        {
            brute.RaiseTimestampFailure(o.RAROptions.OriginalRARFileNames[0], "could not set mtime");
            cts.Cancel();
            return new BruteForceRunResult(true, new WinningCombo(500, []));
        };

        await vm.ExecuteReconstructionForTestAsync(cts.Token);

        // The summary must fire from the finally on cancellation too, exactly once.
        (string Title, string Message) warning = Assert.Single(dialog.Warnings);
        Assert.Equal("Timestamp Preservation Failed", warning.Title);
        Assert.Equal("Cancelled", vm.PhaseDescription);
    }

    // ── Completion heading carries the run-wide "N could not run" error aggregate (§4a / WCAG 4.1.3) ──

    [Fact]
    public async Task Completion_WithErroredCombo_HeadingIncludesCouldNotRunCount()
    {
        var brute = new RaisingBruteForceService();
        ReconstructorViewModel vm = CreateVm(brute, new InlineUiDispatcher());
        ConfigureRunnablePaths(vm);
        vm.SetImportStateForTest(ImportWith(MakeSet("a", "a.rar")));

        brute.OnRun = _ =>
        {
            // A combination begins testing, then the engine reports it could not run rar → its row is
            // marked "Error"; the set produces no match.
            brute.RaiseProgress(MakeProgress(1));
            brute.RaiseProgress(MakeFailedProgress(1));
            return new BruteForceRunResult(false, null);
        };

        await vm.ExecuteReconstructionForTestAsync(CancellationToken.None);

        Assert.Equal(1, vm.VersionEntries.Count(v => v.Status == "Error"));
        // The completion status (a Polite live region) names the aggregate, not just per-cell "Run failed".
        Assert.StartsWith("Complete — No Match", vm.PhaseDescription, StringComparison.Ordinal);
        Assert.Contains("(1 could not run)", vm.PhaseDescription, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TimestampFailures_ConcurrentAddWhileSummarising_IsLockGuarded()
    {
        var brute = new RaisingBruteForceService();
        ReconstructorViewModel vm = CreateVm(brute, new InlineUiDispatcher());

        const int count = 2000;
        using var start = new ManualResetEventSlim(false);

        Task adder = Task.Run(() =>
        {
            start.Wait();
            for (int i = 0; i < count; i++)
            {
                brute.RaiseTimestampFailure($"file-{i}", "err");
            }
        });

        Task summariser = Task.Run(() =>
        {
            start.Wait();
            // Concurrently snapshot-and-render the summary. An unguarded List<T> would throw
            // "Collection was modified" here; the lock + snapshot-under-lock keeps it safe.
            for (int i = 0; i < count; i++)
            {
                vm.ShowTimestampSummaryForTest();
            }
        });

        start.Set();
        await Task.WhenAll(adder, summariser).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(count, vm.TimestampFailureCountForTest);
    }

    // ── #20 — generation-safe batched log ──

    [Fact]
    public void ManyLogEvents_CoalesceIntoAtMostOneDispatch_ThenFlushWritesEveryLine()
    {
        var brute = new RaisingBruteForceService();
        var dispatcher = new CountingUiDispatcher(deferPosts: true);
        ReconstructorViewModel vm = CreateVm(brute, dispatcher);

        const int n = 300;
        int postsBefore = dispatcher.PostCount;
        for (int i = 0; i < n; i++)
        {
            brute.RaiseLog(LogTarget.System, $"line {i}");
        }

        // N log events schedule at most one pending flush (the atomic flag coalesces the rest) — far
        // below N — and nothing is applied to the bound log until that flush runs.
        Assert.True(dispatcher.PostCount - postsBefore <= 2,
            $"expected <=2 flush dispatches for {n} events, got {dispatcher.PostCount - postsBefore}");
        Assert.DoesNotContain("line 0", vm.SystemLog, StringComparison.Ordinal);

        dispatcher.Pump();

        for (int i = 0; i < n; i++)
        {
            Assert.Contains($"line {i}", vm.SystemLog, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void StaleFlushFromPriorGeneration_DoesNotRepopulate_AfterStartClearsAndBumps()
    {
        var brute = new RaisingBruteForceService();
        var dispatcher = new CountingUiDispatcher(deferPosts: true);
        ReconstructorViewModel vm = CreateVm(brute, dispatcher);

        // Prior run enqueues a line and schedules a flush that has not run yet.
        brute.RaiseLog(LogTarget.System, "stale line");

        // A new run begins: it clears the visible log and bumps the run generation.
        vm.BeginNewLogGenerationForTest();
        Assert.Equal(string.Empty, vm.SystemLog);

        // A fresh line of the NEW generation is enqueued.
        brute.RaiseLog(LogTarget.System, "fresh line");

        // Draining now runs the stale flush (captured the old generation) and the fresh one: the stale
        // batch must be discarded so it cannot repopulate the cleared log; the fresh line lands once.
        dispatcher.Pump();

        Assert.DoesNotContain("stale line", vm.SystemLog, StringComparison.Ordinal);
        Assert.Contains("fresh line", vm.SystemLog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunEnd_SynchronousFinalDrain_LeavesNoQueuedLineUnwritten()
    {
        var brute = new RaisingBruteForceService();
        var dispatcher = new CountingUiDispatcher(deferPosts: true);
        ReconstructorViewModel vm = CreateVm(brute, dispatcher);
        ConfigureRunnablePaths(vm);
        vm.SetImportStateForTest(ImportWith(MakeSet("a", "a.rar")));
        brute.OnRun = o => WriteBruteSuccess(o, o.RAROptions.OriginalRARFileNames[0]);

        await vm.ExecuteReconstructionForTestAsync(CancellationToken.None);

        // Every batched flush was deferred (never pumped); the run's finally must drain the queue
        // synchronously so the run's log lines are present without the dispatcher ever running a flush.
        Assert.Contains("Starting brute-force...", vm.SystemLog, StringComparison.Ordinal);
        Assert.Contains("Brute-force completed", vm.SystemLog, StringComparison.Ordinal);
    }

    [Fact]
    public void BatchedFlush_PreservesPerTargetOrder()
    {
        var brute = new RaisingBruteForceService();
        var dispatcher = new CountingUiDispatcher(deferPosts: true);
        ReconstructorViewModel vm = CreateVm(brute, dispatcher);

        // Interleave targets so a single shared queue must keep each target's lines in enqueue order.
        brute.RaiseLog(LogTarget.System, "sys 1");
        brute.RaiseLog(LogTarget.Phase1, "p1 1");
        brute.RaiseLog(LogTarget.System, "sys 2");
        brute.RaiseLog(LogTarget.Phase2, "p2 1");
        brute.RaiseLog(LogTarget.Phase1, "p1 2");
        brute.RaiseLog(LogTarget.System, "sys 3");

        dispatcher.Pump();

        Assert.True(vm.SystemLog.IndexOf("sys 1", StringComparison.Ordinal) >= 0);
        Assert.True(vm.SystemLog.IndexOf("sys 1", StringComparison.Ordinal)
            < vm.SystemLog.IndexOf("sys 2", StringComparison.Ordinal));
        Assert.True(vm.SystemLog.IndexOf("sys 2", StringComparison.Ordinal)
            < vm.SystemLog.IndexOf("sys 3", StringComparison.Ordinal));
        Assert.True(vm.Phase1Log.IndexOf("p1 1", StringComparison.Ordinal)
            < vm.Phase1Log.IndexOf("p1 2", StringComparison.Ordinal));
        Assert.Contains("p2 1", vm.Phase2Log, StringComparison.Ordinal);
    }

    // ── #24 — set/attempt progress label (seed vs full) ──

    [Fact]
    public async Task SeedThenFullAttempt_ProgressLabelledPerSetAndStage_SoRewindIsExplained()
    {
        var brute = new RaisingBruteForceService();
        ReconstructorViewModel vm = CreateVm(brute, new InlineUiDispatcher());
        ConfigureRunnablePaths(vm);
        vm.SetImportStateForTest(ImportWith(MakeSet("a", "a.rar"), MakeSet("b", "b.rar")));

        var messages = new ConcurrentQueue<string>();
        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName == nameof(ReconstructorViewModel.ProgressMessage))
            {
                messages.Enqueue(vm.ProgressMessage);
            }
        };
        vm.PropertyChanged += handler;

        int call = 0;
        brute.OnRun = o =>
        {
            int n = Interlocked.Increment(ref call);
            if (n == 1)
            {
                // Set 1 succeeds → its winning combo seeds set 2.
                return WriteBruteSuccess(o, "a.rar");
            }

            if (n == 2)
            {
                // Set 2, seeded attempt: progress races to 90%, then the seed misses.
                brute.RaiseProgress(MakeProgress(90));
                return new BruteForceRunResult(false, null);
            }

            // Set 2, full-matrix attempt: progress restarts at 10% — this is the apparent "rewind".
            brute.RaiseProgress(MakeProgress(10));
            return WriteBruteSuccess(o, "b.rar");
        };

        await vm.ExecuteReconstructionForTestAsync(CancellationToken.None);
        vm.PropertyChanged -= handler;

        string[] captured = [.. messages];
        string seedMessage = Assert.Single(captured, m => m.Contains("90/100", StringComparison.Ordinal));
        string fullMessage = Assert.Single(captured, m => m.Contains("10/100", StringComparison.Ordinal));

        // The high-% seed attempt and the low-% full attempt carry distinct Set X/N · <stage> labels,
        // so the % reset reads as a labelled stage change rather than an unexplained rewind.
        Assert.Contains("Set 2/2", seedMessage, StringComparison.Ordinal);
        Assert.Contains("seed", seedMessage, StringComparison.Ordinal);
        Assert.Contains("Set 2/2", fullMessage, StringComparison.Ordinal);
        Assert.Contains("full", fullMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SingleSet_ProgressLabelledSet1Of1()
    {
        var brute = new RaisingBruteForceService();
        ReconstructorViewModel vm = CreateVm(brute, new InlineUiDispatcher());
        ConfigureRunnablePaths(vm);
        vm.SetImportStateForTest(ImportWith(MakeSet("a", "a.rar")));

        var messages = new ConcurrentQueue<string>();
        PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName == nameof(ReconstructorViewModel.ProgressMessage))
            {
                messages.Enqueue(vm.ProgressMessage);
            }
        };
        vm.PropertyChanged += handler;

        brute.OnRun = o =>
        {
            brute.RaiseProgress(MakeProgress(50));
            return WriteBruteSuccess(o, "a.rar");
        };

        await vm.ExecuteReconstructionForTestAsync(CancellationToken.None);
        vm.PropertyChanged -= handler;

        string[] captured = [.. messages];
        Assert.Contains(captured, m => m.Contains("Set 1/1", StringComparison.Ordinal));
    }
}
