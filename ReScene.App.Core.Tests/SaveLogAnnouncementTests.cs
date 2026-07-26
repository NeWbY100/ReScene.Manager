using ReScene.App.Core.Helpers;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Core;
using ReScene.SRS;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Pins the Save-log outcome announcement contract on BOTH SaveLog implementations
/// (<see cref="OperationViewModelBase.SaveLogToFileAsync"/> via <see cref="SampleRestorerViewModel"/>,
/// and <see cref="ReconstructorViewModel"/>'s own SaveLogAsync): success/failure/empty set the
/// shared <see cref="SaveLogMessages"/> strings, a cancelled dialog leaves the line blank (the
/// cancel is its own feedback), and a REPEAT save re-announces via the clear-at-start transition —
/// the equal-value suppression in both the toolkit setter and Avalonia's TextBlock would otherwise
/// silence the second save.
/// </summary>
public class SaveLogAnnouncementTests
{
    private sealed class FakeSampleRestorerService : ISampleRestorerService
    {
        public event EventHandler<SRSReconstructionProgressEventArgs>? Progress { add { } remove { } }
        public List<SRSEntryInfo> GetSRSEntries(string srrFilePath) => [];
        public Task<SRSReconstructionResult> RestoreSampleAsync(
            string srrFilePath, string srsFileName, string mediaFilePath, string outputPath, CancellationToken ct)
            => throw new NotSupportedException();
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

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, UiDispatcherPriority priority) => action();
        public bool CheckAccess() => true;
    }

    /// <summary>Save dialog returning a fixed result; counts invocations.</summary>
    private sealed class SaveDialogService(string? result) : NoOpFileDialogService
    {
        public int SaveCalls { get; private set; }

        public override Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<string> filters, string? defaultFileName = null)
        {
            SaveCalls++;
            return Task.FromResult(result);
        }
    }

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"savelog-{Guid.NewGuid():N}", "log.txt");

    private static SampleRestorerViewModel CreateBaseVm(SaveDialogService dialog) =>
        new(new FakeSampleRestorerService(), dialog, new TestUiDispatcher());

    private static ReconstructorViewModel CreateReconstructorVm(SaveDialogService dialog) =>
        new(new InertBruteForceService(), dialog, new InlineUiDispatcher(), new TestUiTimerFactory());

    // ── Base implementation (OperationViewModelBase via SampleRestorerViewModel) ──

    [Fact]
    public async Task Base_Success_AnnouncesSavedFileName()
    {
        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var dialog = new SaveDialogService(path);
            SampleRestorerViewModel vm = CreateBaseVm(dialog);
            vm.LogEntries.Add("one line");

            await vm.SaveLogCommand.ExecuteAsync(null);

            Assert.Equal(SaveLogMessages.Saved(path), vm.SaveLogAnnouncement);
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task Base_ExportFailure_AnnouncesCouldNotSave()
    {
        // The dialog "chooses" a path whose directory does not exist — the export throws.
        var dialog = new SaveDialogService(TempFile());
        SampleRestorerViewModel vm = CreateBaseVm(dialog);
        vm.LogEntries.Add("one line");

        await vm.SaveLogCommand.ExecuteAsync(null);

        Assert.StartsWith("Could not save the log:", vm.SaveLogAnnouncement, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Base_EmptyLog_AnnouncesNothingToSave_WithoutOpeningDialog()
    {
        var dialog = new SaveDialogService(TempFile());
        SampleRestorerViewModel vm = CreateBaseVm(dialog);

        await vm.SaveLogCommand.ExecuteAsync(null);

        Assert.Equal(SaveLogMessages.Empty, vm.SaveLogAnnouncement);
        Assert.Equal(0, dialog.SaveCalls);
    }

    [Fact]
    public async Task Base_CancelledDialog_LeavesAnnouncementBlank()
    {
        var dialog = new SaveDialogService(result: null);
        SampleRestorerViewModel vm = CreateBaseVm(dialog);
        vm.LogEntries.Add("one line");

        await vm.SaveLogCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.SaveLogAnnouncement);
        Assert.Equal(1, dialog.SaveCalls);
    }

    [Fact]
    public async Task Base_RepeatSave_ReAnnouncesViaClearThenSetTransition()
    {
        // The re-announce guarantee: a second save to the same file must produce a genuine
        // empty-to-message transition (clear at start), or equal-value suppression silences it.
        // This test protects the clear from future simplification.
        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var dialog = new SaveDialogService(path);
            SampleRestorerViewModel vm = CreateBaseVm(dialog);
            vm.LogEntries.Add("one line");
            await vm.SaveLogCommand.ExecuteAsync(null);

            var transitions = new List<string>();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.SaveLogAnnouncement))
                {
                    transitions.Add(vm.SaveLogAnnouncement);
                }
            };

            await vm.SaveLogCommand.ExecuteAsync(null);

            Assert.Equal([string.Empty, SaveLogMessages.Saved(path)], transitions);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task Base_RepeatEmptyPress_ReAnnounces()
    {
        // The empty path's clear-then-set pair is SYNCHRONOUS (no dialog in between) — the second
        // press must still raise a genuine ""-to-message transition, or the equal-value
        // suppression would silence it.
        var dialog = new SaveDialogService(TempFile());
        SampleRestorerViewModel vm = CreateBaseVm(dialog);
        await vm.SaveLogCommand.ExecuteAsync(null); // first press leaves the Empty message set

        var transitions = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.SaveLogAnnouncement))
            {
                transitions.Add(vm.SaveLogAnnouncement);
            }
        };

        await vm.SaveLogCommand.ExecuteAsync(null);

        Assert.Equal([string.Empty, SaveLogMessages.Empty], transitions);
    }

    // ── Reconstructor implementation (own SaveLogAsync) ──

    [Fact]
    public async Task Reconstructor_Success_AnnouncesSavedFileName()
    {
        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var dialog = new SaveDialogService(path);
            ReconstructorViewModel vm = CreateReconstructorVm(dialog);
            vm.LogEntries.Add("one line");

            await vm.SaveLogCommand.ExecuteAsync(null);

            Assert.Equal(SaveLogMessages.Saved(path), vm.SaveLogAnnouncement);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public async Task Reconstructor_EmptyLog_AnnouncesNothingToSave()
    {
        var dialog = new SaveDialogService(TempFile());
        ReconstructorViewModel vm = CreateReconstructorVm(dialog);

        await vm.SaveLogCommand.ExecuteAsync(null);

        Assert.Equal(SaveLogMessages.Empty, vm.SaveLogAnnouncement);
        Assert.Equal(0, dialog.SaveCalls);
    }

    [Fact]
    public async Task Reconstructor_CancelledDialog_LeavesAnnouncementBlank()
    {
        var dialog = new SaveDialogService(result: null);
        ReconstructorViewModel vm = CreateReconstructorVm(dialog);
        vm.LogEntries.Add("one line");

        await vm.SaveLogCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.SaveLogAnnouncement);
    }

    [Fact]
    public async Task Reconstructor_ExportFailure_AnnouncesCouldNotSave()
    {
        var dialog = new SaveDialogService(TempFile()); // directory never created — export throws
        ReconstructorViewModel vm = CreateReconstructorVm(dialog);
        vm.LogEntries.Add("one line");

        await vm.SaveLogCommand.ExecuteAsync(null);

        Assert.StartsWith("Could not save the log:", vm.SaveLogAnnouncement, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reconstructor_RepeatSave_ReAnnouncesViaClearThenSetTransition()
    {
        // Mirrors the base-site test: protects THIS implementation's clear-at-start line — the
        // comment alone would not fail a build if someone simplified it away.
        string path = TempFile();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var dialog = new SaveDialogService(path);
            ReconstructorViewModel vm = CreateReconstructorVm(dialog);
            vm.LogEntries.Add("one line");
            await vm.SaveLogCommand.ExecuteAsync(null);

            var transitions = new List<string>();
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.SaveLogAnnouncement))
                {
                    transitions.Add(vm.SaveLogAnnouncement);
                }
            };

            await vm.SaveLogCommand.ExecuteAsync(null);

            Assert.Equal([string.Empty, SaveLogMessages.Saved(path)], transitions);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}
