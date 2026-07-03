using ReScene.Core;
using ReScene.NET.Services;
using ReScene.NET.ViewModels;
using ReScene.NET.ViewModels.Reconstruction;
using ReScene.SRR;

namespace ReScene.NET.Tests;

public sealed class ReconstructorViewModelVersionsTests : IDisposable
{
    private readonly List<string> _tempDirs = [];

    public void Dispose()
    {
        foreach (string d in _tempDirs)
        {
            try { Directory.Delete(d, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Creates a real WinRAR versions folder containing one "winrar-NNN" subfolder (with a
    /// rar.exe stub) per version, so setting WinRarPath drives the actual async folder scan.</summary>
    private string MakeWinRarFolder(params int[] versions)
    {
        string root = Path.Combine(Path.GetTempPath(), "rvm-versions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _tempDirs.Add(root);
        foreach (int v in versions)
        {
            string dir = Path.Combine(root, $"winrar-{v}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "rar.exe"), "stub");
        }

        return root;
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, System.Windows.Threading.DispatcherPriority priority) => action();
        public bool CheckAccess() => true;
    }

    /// <summary>
    /// Dispatcher that DEFERS marshalled actions onto a queue instead of running them inline. The
    /// async folder scan marshals its result via Invoke; queueing lets the test drain the scan Task
    /// and then run that continuation on the TEST thread via <see cref="Pump"/> — so nothing mutates
    /// the view-model concurrently and the scan landing is fully deterministic.
    /// </summary>
    private sealed class QueueingUiDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _queue = new();
        public void Invoke(Action action) => _queue.Enqueue(action);
        public void Post(Action action) => _queue.Enqueue(action);
        public void Post(Action action, System.Windows.Threading.DispatcherPriority priority) => _queue.Enqueue(action);
        public bool CheckAccess() => true;

        /// <summary>Runs every queued action on the calling thread, in order.</summary>
        public void Pump()
        {
            while (_queue.Count > 0)
            {
                _queue.Dequeue()();
            }
        }
    }

    private sealed class InertBruteForceService : IBruteForceService
    {
        public event EventHandler<BruteForceProgressEventArgs>? Progress { add { } remove { } }
        public event EventHandler<BruteForceStatusChangedEventArgs>? StatusChanged { add { } remove { } }
        public event EventHandler<LogEventArgs>? LogMessage { add { } remove { } }
        public event EventHandler<FileCopyProgressEventArgs>? FileCopyProgress { add { } remove { } }
        public event EventHandler<CRCValidationProgressEventArgs>? CRCValidationProgress { add { } remove { } }
        public event EventHandler<TimestampPreservationFailedEventArgs>? TimestampPreservationFailed { add { } remove { } }
        public Task<BruteForceRunResult> RunAsync(BruteForceOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(new BruteForceRunResult(true, null));
    }

    private static ReconstructorViewModel CreateVm()
        => new(new InertBruteForceService(), new NoOpFileDialogService(),
               settingsService: null, uiDispatcher: new InlineUiDispatcher());

    private static readonly IReadOnlyList<InstalledRarVersion> Installed =
    [
        new(500, "winrar-500", "p500"),
        new(560, "winrar-560", "p560"),
        new(602, "winrar-602", "p602"),
        new(624, "winrar-624", "p624"),
    ];

    private static int[] Ticked(ReconstructorViewModel vm) =>
        vm.VersionGroups.SelectMany(g => g.Leaves).Where(l => l.IsChecked).Select(l => l.Version).OrderBy(v => v).ToArray();

    [Fact]
    public void ApplyScanResult_ImportIntent_TicksAllInstalledInEnabledMajors()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.Version2 = vm.Version3 = vm.Version4 = vm.Version7 = false;
        vm.Version5 = true; vm.Version6 = true;

        vm.ApplyScanResult(Installed, folderScanned: true);

        Assert.True(vm.HasScannedVersions);
        Assert.Equal(new[] { 500, 560, 602, 624 }, Ticked(vm));
        Assert.Equal(2, vm.VersionGroups.Count);   // 5.x and 6.x
    }

    [Fact]
    public void FolderScannedThenImport_ReTicksToNewMajors()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.Version5 = true; vm.Version6 = true;
        vm.ApplyScanResult(Installed, folderScanned: true);

        // Simulate an SRR import that maps only to 6.x
        vm.Version5 = false; vm.Version6 = true;
        vm.LoadPendingVersionSelection(null);   // import path: no explicit list, reconcile from majors

        Assert.Equal(new[] { 602, 624 }, Ticked(vm));
    }

    [Fact]
    public void ExplicitSelection_TicksSubset_DropsMissing_ThenClears()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.LoadPendingVersionSelection([560, 624, 999]);   // config load sets pending
        vm.ApplyScanResult(Installed, folderScanned: true);

        Assert.Equal(new[] { 560, 624 }, Ticked(vm));

        // A subsequent scan with no new intent must NOT re-apply the (now consumed) pending list;
        // it falls back to majors. With no majors enabled, nothing is ticked.
        vm.Version2 = vm.Version3 = vm.Version4 = vm.Version5 = vm.Version6 = vm.Version7 = false;
        vm.ApplyScanResult(Installed, folderScanned: true);
        Assert.Empty(Ticked(vm));
    }

    [Fact]
    public void ManualLeafToggle_SyncsMajorBooleans()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.Version5 = true; vm.Version6 = true;
        vm.ApplyScanResult(Installed, folderScanned: true);

        foreach (RarVersionLeaf leaf in vm.VersionGroups.First(g => g.Major == 6).Leaves)
        {
            leaf.IsChecked = false;   // untick all of 6.x
        }

        Assert.True(vm.Version5);
        Assert.False(vm.Version6);   // synced from tree
    }

    [Fact]
    public void SelectedLeafVersions_ReflectsTicksAscending()
    {
        ReconstructorViewModel vm = CreateVm();
        vm.LoadPendingVersionSelection([624, 500]);
        vm.ApplyScanResult(Installed, folderScanned: true);

        Assert.Equal(new[] { 500, 624 }, vm.SelectedLeafVersions.ToArray());
    }

    [Fact]
    public async Task ChangingWinRarPath_ResetsScannedState_SoConfigSelectionSurvivesNewScan()
    {
        // Folder A is already scanned (mirrors the automatic startup scan from settings); folder B is
        // a config's target with a disjoint set of versions. Both are real dirs so the WinRarPath
        // changes drive the actual OnWinRarPathChanged / async-scan path — where the pending selection
        // used to be lost. A queueing dispatcher makes each scan's landing deterministic (pumped on
        // the test thread), so no assertion races the scan continuation.
        string folderA = MakeWinRarFolder(400);          // major 4 only
        string folderB = MakeWinRarFolder(560, 624);     // majors 5 and 6

        var dispatcher = new QueueingUiDispatcher();
        ReconstructorViewModel vm = new(new InertBruteForceService(), new NoOpFileDialogService(),
            settingsService: null, uiDispatcher: dispatcher);

        // Folder A scanned: run the scan Task, then pump its queued ApplyScanResult onto this thread.
        vm.WinRarPath = folderA;
        await vm.LastVersionScan!;
        dispatcher.Pump();
        Assert.True(vm.HasScannedVersions);

        // Changing to a different folder must SYNCHRONOUSLY mark the tree as not-yet-scanned; B's scan
        // continuation is only queued (not yet pumped), so this reads the fix's direct effect.
        // Without the fix this stays true (folder A's stale scanned state).
        vm.WinRarPath = folderB;
        Assert.False(vm.HasScannedVersions);

        // Mirror ConfigMapper.Apply's ordering: the pending selection is applied while B's scan is
        // still in flight. Because HasScannedVersions is now false, ApplyReconcile KEEPS the pending
        // list (rather than consuming it against folder A's stale scan and losing it).
        vm.LoadPendingVersionSelection([560, 624]);

        // B's scan lands: drain the Task, then pump its queued ApplyScanResult. The surviving pending
        // selection now ticks exactly the configured versions.
        await vm.LastVersionScan!;
        dispatcher.Pump();
        Assert.Equal(new[] { 560, 624 }, Ticked(vm));
    }

    [Fact]
    public void ApplyScanResult_EmptyFolder_ShowsHint_NoGroups()
    {
        ReconstructorViewModel vm = CreateVm();

        vm.ApplyScanResult([], folderScanned: false);

        Assert.Empty(vm.VersionGroups);
        Assert.True(vm.ShowNoVersionsHint);
        Assert.False(vm.HasScannedVersions);
    }

    /// <summary>Two folders that both parse to version 390, distinguished only by folder name.</summary>
    private static readonly IReadOnlyList<InstalledRarVersion> SameVersionVariants =
    [
        new(390, "winrar-390", "path-390"),
        new(390, "winrar-390-beta1", "path-390-beta1", "beta1"),
    ];

    [Fact]
    public void BuildSharedSettings_UntickedVariantLeaf_ExcludesItsFolder()
    {
        // audit #36: unticking one same-version variant leaf must exclude ONLY that folder, even
        // though both leaves collapse to version 390.
        ReconstructorViewModel vm = CreateVm();
        vm.Version3 = true;                                  // major 3 enabled → both 390 leaves tick
        vm.ApplyScanResult(SameVersionVariants, folderScanned: true);

        RarVersionLeaf beta = vm.VersionGroups.SelectMany(g => g.Leaves).Single(l => l.FolderName == "winrar-390-beta1");
        beta.IsChecked = false;                             // untick the beta variant only

        SharedReconstructionSettings shared = vm.BuildSharedSettings();

        Assert.Equal(["winrar-390"], shared.SelectedVersionFolders);

        // And the folder allow-list flows through the planner into the engine options.
        var set = new SrrArchiveSet { Key = "", Directory = "" };
        set.VolumeNames.Add("x.rar");
        BruteForceOptions opts = ArchiveSetPlanner.BuildOptionsForSet(set, shared,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(["winrar-390"], opts.RAROptions.AllowedVersionFolders);
    }

    [Fact]
    public void BuildSharedSettings_NoScan_LeavesFolderAllowListEmpty()
    {
        // With no real scan (broad fallback ranges), the run must NOT be folder-filtered.
        ReconstructorViewModel vm = CreateVm();

        SharedReconstructionSettings shared = vm.BuildSharedSettings();

        Assert.Empty(shared.SelectedVersionFolders);
    }
}
