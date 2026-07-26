using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.App.Core.ViewModels.Wizards;
using ReScene.Core;
using ReScene.Manager.Services;
using ReScene.Manager.Views;
using ReScene.Manager.Views.Wizards;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported Beginner "Reconstruct RAR archives" wizard body
/// (<see cref="ReconstructWizardBody"/>). The body's DataContext is a <see cref="ReconstructorViewModel"/>;
/// its three step panels are <c>IsVisible</c>-bound to the hosting Window's
/// <see cref="WizardViewModel.CurrentStepIndex"/> via <c>$parent[Window]</c> + the
/// <c>IndexEqualsConverter</c>. The central gate is <b>zero binding errors</b> (via
/// <see cref="BindingErrorSink"/>); the tests also confirm the panels toggle with the index and that
/// the run step hosts the merged <c>LogEntries</c> log list. The live reconstruction run and the modal
/// progress window are the Reconstructor's launch-smoke — never exercised here.
/// </summary>
public class ReconstructWizardBodyTests
{
    // ── Inert service doubles (no run is ever actually started) ──

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

    /// <summary>No-op timer factory: the elapsed-time timer never ticks in these tests.</summary>
    private sealed class InertUiTimerFactory : IUiTimerFactory
    {
        public IUiTimer Create(TimeSpan interval, Action onTick) => new NoOpTimer();

        private sealed class NoOpTimer : IUiTimer
        {
            public void Start() { }
            public void Stop() { }
        }
    }

    /// <summary>Counts save-dialog invocations (returning null = user cancels); everything else no-ops.</summary>
    private sealed class RecordingFileDialogService : IFileDialogService
    {
        public int SaveFileCalls { get; private set; }

        public Task<string?> OpenFileAsync(string title, IReadOnlyList<string> filters) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<string> filters) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<string> filters, string? defaultFileName = null) { SaveFileCalls++; return Task.FromResult<string?>(null); }
        public Task<string?> OpenFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<bool> ShowConfirmAsync(string title, string message) => Task.FromResult(false);
        public Task<string?> PromptForTextAsync(string title, string message, string initialValue) => Task.FromResult<string?>(null);
        public void ShowError(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowInfo(string title, string message) { }
        public bool Confirm(string title, string message) => false;
    }

    private static ReconstructorViewModel CreateViewModel(IFileDialogService? dialog = null) =>
        new(
            new InertBruteForceService(),
            dialog ?? new AvaloniaFileDialogService(static () => null), // headless: dialogs no-op, never block
            new InlineUiDispatcher(),
            new InertUiTimerFactory());

    private static WizardViewModel CreateWizard(ReconstructorViewModel content) =>
        new("Reconstruct RAR archives", content,
        [
            new WizardStep { Title = "Import SRR" },
            new WizardStep { Title = "Files & folders" },
            new WizardStep { Title = "Run" },
        ]);

    // Mirror how WizardWindow wires them: the Window's DataContext is the WizardViewModel; the body's
    // DataContext is the task VM (its Content). Set the Window's DataContext (reached via
    // $parent[Window]) before parenting the body, so its ancestor binding never sees a null.
    private static (Window window, ReconstructWizardBody body, WizardViewModel wizard) Show(ReconstructorViewModel vm)
    {
        WizardViewModel wizard = CreateWizard(vm);
        var body = new ReconstructWizardBody { DataContext = wizard.Content };
        var window = new Window { Width = 1000, Height = 760, DataContext = wizard, Content = body };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, body, wizard);
    }

    [AvaloniaFact]
    public void RunStart_OpensBruteForceProgressModal_OwnedByWizard()
    {
        // Regression: the beginner "Reconstruct RAR archives" wizard must open the brute-force progress
        // dialog when a run starts, exactly as the RAR Reconstructor tab does on IsRunning=true. The
        // wizard runs without that tab realized, so ReconstructWizardBody opens the window itself, owned
        // by the hosting WizardWindow.
        ReconstructorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (Window window, _, _) = Show(vm);

        vm.IsRunning = true;
        Dispatcher.UIThread.RunJobs();
        BruteForceProgressWindow modal = Assert.Single(window.OwnedWindows.OfType<BruteForceProgressWindow>());
        Assert.Same(vm, modal.DataContext);

        Assert.Empty(sink.Messages);
        modal.Close();
        window.Close();
    }

    [AvaloniaFact]
    public void StepPanels_ToggleWithCurrentStepIndex_NoBindingErrors()
    {
        ReconstructorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (_, ReconstructWizardBody body, WizardViewModel wizard) = Show(vm);

        // The root grid's direct children are the three step panels, in order.
        Grid root = Assert.IsType<Grid>(body.Content);
        Assert.Equal(3, root.Children.Count);

        wizard.CurrentStepIndex = 0;
        Dispatcher.UIThread.RunJobs();
        Assert.True(root.Children[0].IsVisible);
        Assert.False(root.Children[1].IsVisible);
        Assert.False(root.Children[2].IsVisible);

        wizard.CurrentStepIndex = 1;
        Dispatcher.UIThread.RunJobs();
        Assert.False(root.Children[0].IsVisible);
        Assert.True(root.Children[1].IsVisible);
        Assert.False(root.Children[2].IsVisible);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void Step1_ShowsWinRarPackDownloadLinks_MatchingAdvancedTab()
    {
        // The wizard's WinRAR-folder field offers the same three pack-download links as the RAR
        // Reconstructor tab header. Both sides assert against ResourceLinkExpectations so editing one
        // surface without the other fails its twin test (WCAG 3.2.4 Consistent Identification).
        ReconstructorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (_, ReconstructWizardBody body, WizardViewModel wizard) = Show(vm);
        wizard.CurrentStepIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Grid root = Assert.IsType<Grid>(body.Content);
        var step1 = root.Children[1];

        (string?, string?)[] links =
        [
            .. step1.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("link"))
                .Select(b => (b.Content as string, b.Tag as string)),
        ];
        Assert.Equal(
            ResourceLinkExpectations.WinRarPackLinks.Select(p => ((string?)p.Label, (string?)p.Url)),
            links);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void Step1_FieldInputsAndBrowseButtons_HaveAccessibleNames()
    {
        // A screen reader must announce each path field by its heading (4.1.2) and tell the four
        // visually identical Browse buttons apart (2.4.6); each Browse name contains the visible
        // "Browse" label (2.5.3 Label in Name).
        ReconstructorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (_, ReconstructWizardBody body, WizardViewModel wizard) = Show(vm);
        wizard.CurrentStepIndex = 1;
        Dispatcher.UIThread.RunJobs();

        Grid root = Assert.IsType<Grid>(body.Content);
        var step1 = root.Children[1];

        string?[] fieldLabels =
        [
            .. step1.GetVisualDescendants().OfType<TextBox>()
                .Select(t => Assert.IsType<TextBlock>(AutomationProperties.GetLabeledBy(t)).Text),
        ];
        string?[] expectedLabels =
            ["WinRAR versions folder", "Extracted release files", "Output folder", "Verification file (.sfv or .sha1)"];
        Assert.Equal(expectedLabels, fieldLabels);

        string?[] browseNames =
        [
            .. step1.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Content as string == "Browse")
                .Select(AutomationProperties.GetName),
        ];
        string?[] expectedBrowseNames =
            ["Browse for WinRAR versions folder", "Browse for extracted release files", "Browse for output folder", "Browse for verification file"];
        Assert.Equal(expectedBrowseNames, browseNames);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void RunStep_HasMergedLogList_NamedDetails_NoBindingErrors()
    {
        ReconstructorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        (Window window, _, WizardViewModel wizard) = Show(vm);

        wizard.CurrentStepIndex = 2;
        Dispatcher.UIThread.RunJobs();

        // The run step shows the merged chronological log (same LogEntries the Advanced tab binds).
        ListBox log = window.GetVisualDescendants().OfType<ListBox>()
            .Single(l => ReferenceEquals(l.ItemsSource, vm.LogEntries));
        vm.LogEntries.Add("hello log");
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "hello log");

        // 4.1.2: the log list is named by the "Details" header via LabeledBy, so a screen reader
        // announces what the list is instead of a nameless "list".
        var label = Assert.IsType<TextBlock>(AutomationProperties.GetLabeledBy(log));
        Assert.Equal("Details", label.Text);

        // Long [P2] command lines must stay reachable (the old TextBox wrapped; the list scrolls).
        Assert.Equal(ScrollBarVisibility.Auto, ScrollViewer.GetHorizontalScrollBarVisibility(log));

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void RunStep_SaveLogButton_BindsAndInvokesSaveLog()
    {
        // The wizard's run log can be saved to a .txt (incl. the [P2] failure lines). The button mirrors
        // the sibling operation views and the Create-SRR wizard, bound to the VM's SaveLogCommand,
        // which writes the merged chronological log verbatim.
        var dialog = new RecordingFileDialogService();
        ReconstructorViewModel vm = CreateViewModel(dialog);

        using var sink = new BindingErrorSink();
        (Window window, _, WizardViewModel wizard) = Show(vm);

        wizard.CurrentStepIndex = 2;
        Dispatcher.UIThread.RunJobs();

        Button saveLog = window.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Content is string s && s.StartsWith("Save log", StringComparison.Ordinal));
        Assert.Same(vm.SaveLogCommand, saveLog.Command);

        // With log content present, executing routes to the save dialog (no-ops when the log is empty).
        vm.LogEntries.Add("a line");
        saveLog.Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, dialog.SaveFileCalls);

        Assert.Empty(sink.Messages);
    }
}
