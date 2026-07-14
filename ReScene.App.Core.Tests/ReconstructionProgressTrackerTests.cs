using System.Collections.ObjectModel;
using Microsoft.Extensions.Time.Testing;
using ReScene.App.Core.ViewModels.Reconstruction;
using ReScene.Core;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Unit tests for <see cref="ReconstructionProgressTracker{TVersionRow}"/>: per-set outcome-row
/// finalization surviving set boundaries (#23) and the <see cref="TimeProvider"/>-driven ETA
/// countdown between progress events (#25).
/// </summary>
public class ReconstructionProgressTrackerTests
{
    /// <summary>Minimal bound-row stand-in — just the fields the tracker mutates.</summary>
    private sealed class TestRow
    {
        public string VersionName = "";
        public string Arguments = "";
        public string VersionDirectory = "";
        public string Status = "";
        public string Result = "";
        public string SetText = "";
    }

    private static ReconstructionProgressTracker<TestRow> CreateTracker(
        ObservableCollection<TestRow> entries, TimeProvider? timeProvider = null) =>
        new(
            entries,
            createRow: (label, args, dir) => new TestRow { VersionName = label, Arguments = args, VersionDirectory = dir },
            setStatus: (row, status) => row.Status = status,
            setResult: (row, result) => row.Result = result,
            setSetText: (row, setText) => row.SetText = setText,
            getFullCommandLine: row => $"{row.VersionDirectory} {row.Arguments}",
            appendLog: (_, _) => { },
            timeProvider: timeProvider);

    private static BruteForceProgressEventArgs MakeEvent(
        string versionDir, string args, string phase, long progressed, long size, TimeSpan startedAgo) =>
        new(
            releaseDirectoryPath: "release",
            rarVersionDirectoryPath: versionDir,
            rarCommandLineArguments: args,
            operationSize: size,
            operationProgressed: progressed,
            startDateTime: DateTime.Now - startedAgo)
        {
            PhaseDescription = phase,
        };

    // ── #23: per-set outcome rows ──

    [Fact]
    public void CompleteActiveVersion_FinalizesEachSetFromItsOwnOutcome_NotFromASiblingSet()
    {
        var entries = new ObservableCollection<TestRow>();
        ReconstructionProgressTracker<TestRow> tracker = CreateTracker(entries);
        tracker.StartRun();

        // Set 1 tests one combo and succeeds.
        tracker.SetActiveSet("Set1");
        tracker.ApplyProgress(MakeEvent("winrar-370", "-m0", "Phase 1: main", 1, 1, TimeSpan.FromSeconds(1)));
        tracker.CompleteActiveVersion("Match");

        // Set 2 seeds the same combo (mirrors the seeded-combo retry) but the set fails outright.
        tracker.SetActiveSet("Set2");
        tracker.ApplyProgress(MakeEvent("winrar-370", "-m0", "Phase 1: main", 1, 1, TimeSpan.FromSeconds(1)));
        tracker.CompleteActiveVersion("No Match");

        Assert.Equal(2, entries.Count);
        Assert.Equal("Match", entries[0].Result);       // set 1's own outcome
        Assert.Equal("Set1", entries[0].SetText);
        Assert.Equal("No Match", entries[1].Result);     // set 2's own outcome — must NOT read "Match"
        Assert.Equal("Set2", entries[1].SetText);
    }

    [Fact]
    public void ApplyProgress_PhaseChangeAcrossSetBoundary_PreservesPriorSetsFinalizedRow()
    {
        var entries = new ObservableCollection<TestRow>();
        ReconstructionProgressTracker<TestRow> tracker = CreateTracker(entries);
        tracker.StartRun();

        tracker.SetActiveSet("Set1");
        tracker.ApplyProgress(MakeEvent("winrar-370", "-m0", "Phase 1: main", 1, 1, TimeSpan.FromSeconds(1)));
        tracker.CompleteActiveVersion("Match");

        // Set 2 starts on a DIFFERENT phase description than set 1 ended on — must not wipe set 1's
        // already-finalized row out of the collection.
        tracker.SetActiveSet("Set2");
        tracker.ApplyProgress(MakeEvent("winrar-400", "-m1", "Phase 2: other", 1, 1, TimeSpan.FromSeconds(1)));
        tracker.CompleteActiveVersion("No Match");

        Assert.Equal(2, entries.Count);
        Assert.Equal("Complete", entries[0].Status);
        Assert.Equal("Match", entries[0].Result);
        Assert.Equal("Set1", entries[0].SetText);
        Assert.Equal("No Match", entries[1].Result);
        Assert.Equal("Set2", entries[1].SetText);
    }

    // ── #25: TimeProvider-based ETA ──

    [Fact]
    public void Tick_TimeProviderAdvancesWithNoNewEvent_RemainingCountsDownMonotonically()
    {
        var fakeTime = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00+00:00"));
        var entries = new ObservableCollection<TestRow>();
        ReconstructionProgressTracker<TestRow> tracker = CreateTracker(entries, fakeTime);
        tracker.StartRun();

        // Started 10s ago, 1 of 11 done → 10 remaining at ~10s/op → ~100s TimeRemaining.
        tracker.ApplyProgress(MakeEvent("winrar-370", "-m0", "Phase 1: main", 1, 11, TimeSpan.FromSeconds(10)));

        ElapsedTick first = tracker.Tick();
        Assert.True(first.HasTiming);
        Assert.Equal("01:40", first.RemainingText); // ~100s

        // Advance the clock 5s with NO new progress event — the old code re-derived the SAME flat
        // estimate every tick (no decay); the fixed-completion-instant fix must count down instead.
        fakeTime.Advance(TimeSpan.FromSeconds(5));

        ElapsedTick second = tracker.Tick();
        Assert.True(second.HasTiming);
        Assert.Equal("01:35", second.RemainingText); // ~95s
    }
}
