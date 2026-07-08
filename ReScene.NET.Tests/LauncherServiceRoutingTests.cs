using ReScene.App.Core.Models;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Core;

namespace ReScene.NET.Tests;

/// <summary>
/// Verifies that <see cref="HomeViewModel"/> and <see cref="ReconstructorViewModel"/> route their
/// URL-open / folder-reveal commands through the injected <see cref="ILauncherService"/> seam
/// instead of calling <see cref="System.Diagnostics.Process"/> directly.
/// </summary>
public sealed class LauncherServiceRoutingTests : TempDirTestBase
{
    /// <summary>Records every call so tests can assert on what was launched.</summary>
    private sealed class RecordingLauncherService : ILauncherService
    {
        public List<string> OpenedUrls { get; } = [];
        public List<string> RevealedPaths { get; } = [];

        public void OpenUrl(string url) => OpenedUrls.Add(url);
        public void RevealPath(string path) => RevealedPaths.Add(path);
    }

    private sealed class NoOpRecentFilesService : IRecentFilesService
    {
        public List<RecentFileEntry> LoadEntries() => [];
        public void AddEntry(string filePath) { }
        public void RemoveEntry(string filePath) { }
        public void Clear() { }
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, UiDispatcherPriority priority) => action();
        public bool CheckAccess() => true;
    }

    [Fact]
    public void HomeViewModel_OpenUrlCommand_RoutesThroughLauncher()
    {
        var launcher = new RecordingLauncherService();
        var vm = new HomeViewModel(
            new NoOpRecentFilesService(),
            openFile: _ => { },
            switchToCreator: () => { },
            openDialog: () => Task.CompletedTask,
            fileDialog: new NoOpFileDialogService(),
            launcher: launcher);

        vm.OpenUrlCommand.Execute("https://example.com/test");

        Assert.Equal(["https://example.com/test"], launcher.OpenedUrls);
        Assert.Empty(launcher.RevealedPaths);
    }

    [Fact]
    public void ReconstructorViewModel_OpenOutputFolderCommand_RoutesThroughLauncher()
    {
        var launcher = new RecordingLauncherService();
        var vm = new ReconstructorViewModel(
            new InertBruteForceService(),
            new NoOpFileDialogService(),
            new InlineUiDispatcher(),
            new TestUiTimerFactory(),
            settingsService: null,
            tempDir: null,
            launcher: launcher)
        {
            OutputPath = TempDir,
        };

        vm.OpenOutputFolderCommand.Execute(null);

        Assert.Equal([TempDir], launcher.RevealedPaths);
        Assert.Empty(launcher.OpenedUrls);
    }

    /// <summary>Inert brute-force service — never invoked; this test only exercises OpenOutputFolder.</summary>
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
}
