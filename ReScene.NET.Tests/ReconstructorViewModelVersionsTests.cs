using ReScene.Core;
using ReScene.NET.Services;
using ReScene.NET.ViewModels;
using ReScene.NET.ViewModels.Reconstruction;

namespace ReScene.NET.Tests;

public sealed class ReconstructorViewModelVersionsTests
{
    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, System.Windows.Threading.DispatcherPriority priority) => action();
        public bool CheckAccess() => true;
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
    public void ApplyScanResult_EmptyFolder_ShowsHint_NoGroups()
    {
        ReconstructorViewModel vm = CreateVm();

        vm.ApplyScanResult([], folderScanned: false);

        Assert.Empty(vm.VersionGroups);
        Assert.True(vm.ShowNoVersionsHint);
        Assert.False(vm.HasScannedVersions);
    }
}
