using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Core;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Verifies Browse commands forward their bound field's current value as the picker's
/// <c>initialPath</c>, so re-browsing starts where the user already navigated instead of the
/// platform default (on Linux always $HOME — WCAG 3.3.7 Redundant Entry adjacency). One folder
/// field and one file field are pinned representatively, plus the empty-field fallback anchor.
/// </summary>
public class FileDialogInitialPathTests
{
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

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, UiDispatcherPriority priority) => action();
        public bool CheckAccess() => true;
    }

    /// <summary>Records the initialPath of every open-style call; always "cancels" (returns null/empty).</summary>
    private sealed class RecordingDialogService : NoOpFileDialogService
    {
        public string? LastOpenFileInitialPath { get; private set; }
        public string? LastOpenFolderInitialPath { get; private set; }

        public override Task<string?> OpenFileAsync(string title, IReadOnlyList<string> filters, string? initialPath = null)
        {
            LastOpenFileInitialPath = initialPath;
            return Task.FromResult<string?>(null);
        }

        public override Task<string?> OpenFolderAsync(string title, string? initialPath = null)
        {
            LastOpenFolderInitialPath = initialPath;
            return Task.FromResult<string?>(null);
        }
    }

    private static (ReconstructorViewModel Vm, RecordingDialogService Dialog) CreateVm()
    {
        var dialog = new RecordingDialogService();
        var vm = new ReconstructorViewModel(
            new InertBruteForceService(),
            dialog,
            uiDispatcher: new InlineUiDispatcher(),
            timerFactory: new TestUiTimerFactory());
        return (vm, dialog);
    }

    [Fact]
    public async Task BrowseWinRAR_ForwardsCurrentFolderField()
    {
        (ReconstructorViewModel vm, RecordingDialogService dialog) = CreateVm();
        vm.WinRARPath = Path.Combine(Path.GetTempPath(), "winrar-versions");

        await vm.BrowseWinRARCommand.ExecuteAsync(null);

        Assert.Equal(vm.WinRARPath, dialog.LastOpenFolderInitialPath);
    }

    [Fact]
    public async Task BrowseVerification_ForwardsOwnField_WhenSet()
    {
        (ReconstructorViewModel vm, RecordingDialogService dialog) = CreateVm();
        vm.VerificationPath = Path.Combine(Path.GetTempPath(), "rel", "release.sfv");
        vm.ReleasePath = Path.Combine(Path.GetTempPath(), "other");

        await vm.BrowseVerificationCommand.ExecuteAsync(null);

        Assert.Equal(vm.VerificationPath, dialog.LastOpenFileInitialPath);
    }

    [Fact]
    public async Task BrowseVerification_FallsBackToReleasePath_WhenOwnFieldEmpty()
    {
        // The .sfv lives in the release folder — an empty verification field anchors there.
        (ReconstructorViewModel vm, RecordingDialogService dialog) = CreateVm();
        vm.VerificationPath = string.Empty;
        vm.ReleasePath = Path.Combine(Path.GetTempPath(), "rel");

        await vm.BrowseVerificationCommand.ExecuteAsync(null);

        Assert.Equal(vm.ReleasePath, dialog.LastOpenFileInitialPath);
    }
}
