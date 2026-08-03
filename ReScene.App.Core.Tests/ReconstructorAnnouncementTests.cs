using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Core;

namespace ReScene.App.Core.Tests;

/// <summary>
/// The ViewModel half of the two Reconstructor outcomes the accessibility gate found to be
/// announced through no channel at all: Import/Export Configuration, and the custom-packer warning.
/// Both previously reported only into <c>LogEntries</c>, which is deliberately not a live region.
/// <para>
/// The contract mirrors <see cref="SaveLogAnnouncementTests"/>'s exactly, and for the same reason:
/// the announcement property is CLEARED at the start of every command so each outcome is a genuine
/// empty-to-message transition. Without that, the equal-value suppression in both the toolkit's
/// setter and Avalonia's <c>TextBlock.Text</c> silences a repeat — importing the same config twice,
/// or two SRRs carrying the same warning, would say nothing the second time. The
/// <c>ReAnnounces</c> tests below are what stop that clear being "simplified away"; a comment alone
/// would not fail a build.
/// </para>
/// <para>
/// The view-side half — that these properties reach an always-in-tree
/// <c>AutomationProperties.LiveSetting=Polite</c> TextBlock rather than an IsVisible-toggled one —
/// is <c>ReScene.Manager.Tests.ReconstructorAnnouncementTests</c>.
/// </para>
/// </summary>
public class ReconstructorAnnouncementTests
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

    /// <summary>Returns fixed results for both the open and save dialogs; counts invocations.</summary>
    private sealed class ConfigDialogService(string? openResult, string? saveResult) : NoOpFileDialogService
    {
        public int OpenCalls { get; private set; }
        public int SaveCalls { get; private set; }

        public override Task<string?> OpenFileAsync(string title, IReadOnlyList<string> filters, string? initialPath = null)
        {
            OpenCalls++;
            return Task.FromResult(openResult);
        }

        public override Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<string> filters, string? defaultFileName = null)
        {
            SaveCalls++;
            return Task.FromResult(saveResult);
        }
    }

    private static ReconstructorViewModel CreateVm(NoOpFileDialogService dialog) =>
        new(new InertBruteForceService(), dialog, new InlineUiDispatcher(), new TestUiTimerFactory());

    private static string TempDir() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"reconfig-{Guid.NewGuid():N}")).FullName;

    private static List<string> RecordTransitions(ReconstructorViewModel vm, Func<ReconstructorViewModel, string> read, string propertyName)
    {
        var transitions = new List<string>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == propertyName)
            {
                transitions.Add(read(vm));
            }
        };
        return transitions;
    }

    // ── Export ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportConfig_Success_AnnouncesTheFileName()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "reconstructor-config.json");
            ReconstructorViewModel vm = CreateVm(new ConfigDialogService(null, path));

            await vm.ExportConfigCommand.ExecuteAsync(null);

            Assert.Equal("Configuration exported to reconstructor-config.json", vm.ConfigAnnouncement);
            Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ExportConfig_Failure_AnnouncesCouldNotExport()
    {
        // A path whose directory does not exist — the write throws.
        string path = Path.Combine(Path.GetTempPath(), $"reconfig-{Guid.NewGuid():N}", "config.json");
        ReconstructorViewModel vm = CreateVm(new ConfigDialogService(null, path));

        await vm.ExportConfigCommand.ExecuteAsync(null);

        Assert.StartsWith("Could not export the configuration:", vm.ConfigAnnouncement, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportConfig_CancelledDialog_LeavesAnnouncementBlank()
    {
        var dialog = new ConfigDialogService(null, null);
        ReconstructorViewModel vm = CreateVm(dialog);

        await vm.ExportConfigCommand.ExecuteAsync(null);

        // The cancel is its own feedback; a stale success line would mislead.
        Assert.Equal(string.Empty, vm.ConfigAnnouncement);
        Assert.Equal(1, dialog.SaveCalls);
    }

    // ── Import ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportConfig_Success_AnnouncesTheFileName()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "reconstructor-config.json");
            ReconstructorViewModel exporter = CreateVm(new ConfigDialogService(null, path));
            await exporter.ExportConfigCommand.ExecuteAsync(null);

            ReconstructorViewModel vm = CreateVm(new ConfigDialogService(path, null));
            await vm.ImportConfigCommand.ExecuteAsync(null);

            Assert.Equal("Configuration imported from reconstructor-config.json", vm.ConfigAnnouncement);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ImportConfig_EmptyOrInvalidFile_AnnouncesCouldNotImport()
    {
        string dir = TempDir();
        try
        {
            // "null" deserializes to a null ReconstructorConfig — the branch that logs
            // "file is empty or invalid" without throwing.
            string path = Path.Combine(dir, "empty.json");
            await File.WriteAllTextAsync(path, "null");

            ReconstructorViewModel vm = CreateVm(new ConfigDialogService(path, null));
            await vm.ImportConfigCommand.ExecuteAsync(null);

            Assert.Equal("Could not import the configuration: the file is empty or invalid", vm.ConfigAnnouncement);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task ImportConfig_UnreadableFile_AnnouncesCouldNotImport()
    {
        ReconstructorViewModel vm = CreateVm(new ConfigDialogService(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"), null));

        await vm.ImportConfigCommand.ExecuteAsync(null);

        Assert.StartsWith("Could not import the configuration:", vm.ConfigAnnouncement, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportConfig_CancelledDialog_LeavesAnnouncementBlank()
    {
        var dialog = new ConfigDialogService(null, null);
        ReconstructorViewModel vm = CreateVm(dialog);

        await vm.ImportConfigCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, vm.ConfigAnnouncement);
        Assert.Equal(1, dialog.OpenCalls);
    }

    [Fact]
    public async Task RepeatImportOfTheSameFile_ReAnnouncesViaClearThenSetTransition()
    {
        string dir = TempDir();
        try
        {
            string path = Path.Combine(dir, "reconstructor-config.json");
            ReconstructorViewModel exporter = CreateVm(new ConfigDialogService(null, path));
            await exporter.ExportConfigCommand.ExecuteAsync(null);

            ReconstructorViewModel vm = CreateVm(new ConfigDialogService(path, null));
            await vm.ImportConfigCommand.ExecuteAsync(null);

            List<string> transitions = RecordTransitions(vm, v => v.ConfigAnnouncement, nameof(vm.ConfigAnnouncement));
            await vm.ImportConfigCommand.ExecuteAsync(null);

            Assert.Equal([string.Empty, "Configuration imported from reconstructor-config.json"], transitions);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Custom-packer warning ─────────────────────────────────────────────────

    /// <summary>
    /// A failed SRR import must not leave the PREVIOUS import's warning standing — visually or, now
    /// that the warning drives a live region, in what a screen reader has just been told. Exercised
    /// through the real command against a path that is not an SRR at all, so the parse throws and
    /// the clear has to have happened before the try block to survive.
    /// </summary>
    [Fact]
    public async Task FailedSRRImport_ClearsAPreviousCustomPackerWarning()
    {
        string dir = TempDir();
        try
        {
            string notAnSrr = Path.Combine(dir, "not-an.srr");
            await File.WriteAllTextAsync(notAnSrr, "this is not an SRR file");

            ReconstructorViewModel vm = CreateVm(new ConfigDialogService(notAnSrr, null));
            vm.CustomPackerWarning = "Custom RAR packer detected (from an earlier import).";
            Assert.True(vm.HasCustomPackerWarning, "precondition: a warning is showing before the failed import");

            await vm.ImportSRRCommand.ExecuteAsync(null);

            Assert.False(vm.HasCustomPackerWarning,
                "a failed import left the previous SRR's warning showing — and, with the live region, just re-read as current");
            Assert.Null(vm.CustomPackerWarning);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Paths sub-tab accessible name ─────────────────────────────────────────

    /// <summary>
    /// The Paths sub-tab header's accessible name, which is the only channel the "needs attention"
    /// warning glyph has: a TabItem peer does not expose its header's TextBlocks as children, so a
    /// purely visual glyph reaches an assistive technology through nothing at all.
    /// <para>
    /// Both the values and the CHANGE NOTIFICATION are pinned. The notification is the part that
    /// can rot silently — the name is computed from four separate path properties, and a fifth one
    /// added later without its <c>NotifyPropertyChangedFor</c> would leave the announced name
    /// stale behind the glyph rather than breaking anything visible.
    /// </para>
    /// </summary>
    [Fact]
    public void PathsTabAccessibleName_TracksTheWarningGlyph_AndNotifiesFromEveryPath()
    {
        ReconstructorViewModel vm = CreateVm(new NoOpFileDialogService());

        Assert.True(vm.PathsNeedAttention, "precondition: a fresh VM has no paths set");
        Assert.Equal("Paths — needs attention", vm.PathsTabAccessibleName);

        string root = TempDir();
        try
        {
            // Separate real folders, and the .sfv inside the release folder: PathsNeedAttention
            // also fails Release/Output and Verify/Output OVERLAP, so pointing them all at the same
            // temp directory would never clear the glyph.
            string winRar = Directory.CreateDirectory(Path.Combine(root, "winrar")).FullName;
            string release = Directory.CreateDirectory(Path.Combine(root, "release")).FullName;
            string output = Directory.CreateDirectory(Path.Combine(root, "output")).FullName;
            string sfv = Path.Combine(release, "release.sfv");
            File.WriteAllText(sfv, "; sfv");

            foreach ((string label, Action set) in new (string, Action)[]
            {
                ("WinRARPath", () => vm.WinRARPath = winRar),
                ("ReleasePath", () => vm.ReleasePath = release),
                ("VerificationPath", () => vm.VerificationPath = sfv),
                ("OutputPath", () => vm.OutputPath = output),
            })
            {
                List<string> transitions = RecordTransitions(vm, v => v.PathsTabAccessibleName, nameof(vm.PathsTabAccessibleName));
                set();
                Assert.True(transitions.Count > 0, $"setting {label} raised no PathsTabAccessibleName change — the announced name would go stale behind the glyph");
            }

            Assert.False(vm.PathsNeedAttention, "precondition: all four paths now resolve");
            Assert.Equal("Paths", vm.PathsTabAccessibleName);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
