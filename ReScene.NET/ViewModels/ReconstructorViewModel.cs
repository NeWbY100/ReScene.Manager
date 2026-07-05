using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReScene.Core;
using ReScene.Core.Cryptography;
using ReScene.Core.IO;
using ReScene.NET.Helpers;
using ReScene.NET.Models;
using ReScene.NET.Services;
using ReScene.NET.ViewModels.Reconstruction;
using ReScene.RAR;
using ReScene.SRR;

namespace ReScene.NET.ViewModels;

public partial class ReconstructorViewModel : ViewModelBase
{
    private const long DefaultVolumeSizeKb = 15000;

    private readonly IBruteForceService _bruteForceService;
    private readonly IFileDialogService _fileDialog;
    private readonly IAppSettingsService? _settingsService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ITempDirectoryService _tempDir;
    private CancellationTokenSource? _cts;

    // Temp directory holding the SFV extracted from the last imported SRR (VerificationPath points
    // into it, so it must outlive the import). Replaced on the next import and deleted on Cleanup.
    private string? _sfvTempDir;

    // Elapsed timer — ticks every second so the clock doesn't freeze between progress events
    private readonly DispatcherTimer _elapsedTimer;

    // Per-run progress bookkeeping (timing + version table + copy/verify timing).
    private readonly ReconstructionProgressTracker<VersionEntry> _progress;

    // ── Imported SRR state ──
    // All reconstruction state captured from an imported SRR lives in one holder so the options
    // builder and config capture/restore can pass it around as a unit.
    private ReconstructionImportState _import = new();

    // Timestamp-preservation failures accumulated during the current run.
    // Surfaced as a single MessageBox when the operation completes so the
    // user is aware that the resulting RAR's File Time (DOS) may not match
    // the original for those files.
    private readonly List<TimestampPreservationFailedEventArgs> _timestampFailures = [];

    public ReconstructorViewModel(IBruteForceService bruteForceService, IFileDialogService fileDialog, IAppSettingsService? settingsService = null, IUiDispatcher? uiDispatcher = null, ITempDirectoryService? tempDir = null)
    {
        _bruteForceService = bruteForceService;
        _fileDialog = fileDialog;
        _settingsService = settingsService;
        _uiDispatcher = uiDispatcher ?? new WpfDispatcher();
        _tempDir = tempDir ?? new TempDirectoryService();

        _bruteForceService.Progress += OnProgress;
        _bruteForceService.StatusChanged += OnStatusChanged;
        _bruteForceService.LogMessage += OnLogMessage;
        _bruteForceService.FileCopyProgress += OnFileCopyProgress;
        _bruteForceService.CRCValidationProgress += OnCRCValidationProgress;
        _bruteForceService.TimestampPreservationFailed += OnTimestampPreservationFailed;

        _progress = new ReconstructionProgressTracker<VersionEntry>(
            VersionEntries,
            createRow: (label, args, dir) => new VersionEntry { VersionName = label, Arguments = args, VersionDirectory = dir },
            setStatus: (row, status) => row.Status = status,
            setResult: (row, result) => row.Result = result,
            setSetText: (row, setText) => row.SetText = setText,
            getFullCommandLine: row => row.FullCommandLine,
            appendLog: AppendLog);

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => OnElapsedTimerTick();

        ApplyPathDefaultsFromSettings();
        RefreshPathStatuses();

        if (_settingsService is not null)
        {
            _settingsService.Changed += OnSettingsChanged;
        }
    }

    /// <summary>
    /// A settings save (e.g. a new WinRAR versions folder) should reach the Reconstructor without a
    /// restart. ApplyPathDefaultsFromSettings only fills empty paths, so a path the user typed here
    /// is never overwritten.
    /// </summary>
    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        ApplyPathDefaultsFromSettings();
        RefreshPathStatuses();
    }

    /// <summary>
    /// Pre-fills the WinRAR versions folder and output folder from settings, never overwriting
    /// values the user already typed.
    /// </summary>
    private void ApplyPathDefaultsFromSettings()
    {
        if (_settingsService is null)
        {
            return;
        }

        AppSettings settings = _settingsService.Load();
        if (string.IsNullOrWhiteSpace(WinRarPath) && !string.IsNullOrWhiteSpace(settings.ReconstructWinRarPath))
        {
            WinRarPath = settings.ReconstructWinRarPath;
        }

        if (string.IsNullOrWhiteSpace(OutputPath) && !string.IsNullOrWhiteSpace(settings.ReconstructOutputPath))
        {
            OutputPath = settings.ReconstructOutputPath;
        }
    }

    // ── Warning ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCustomPackerWarning))]
    public partial string? CustomPackerWarning { get; set; }

    public bool HasCustomPackerWarning => !string.IsNullOrEmpty(CustomPackerWarning);

    /// <summary>True once an SRR has been successfully imported (drives the Beginner wizard's step gating).</summary>
    [ObservableProperty]
    public partial bool HasImportedSrr { get; set; }

    // ── Imported SRR details (shown after import) ──

    [ObservableProperty]
    public partial string ImportedSrrName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImportedSrrAppName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImportedRarVolumeText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImportedArchivedFilesText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImportedCompressionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImportedStoredFilesText { get; set; } = string.Empty;

    // ── Paths ──

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyPropertyChangedFor(nameof(PathsNeedAttention))]
    public partial string WinRarPath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyPropertyChangedFor(nameof(PathsNeedAttention))]
    public partial string ReleasePath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PathsNeedAttention))]
    public partial string VerificationPath { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyPropertyChangedFor(nameof(PathsNeedAttention))]
    public partial string OutputPath { get; set; } = string.Empty;

    // ── Path status ──

    [ObservableProperty]
    public partial FieldStatus WinRarStatus { get; set; } = FieldStatus.None;

    [ObservableProperty]
    public partial FieldStatus ReleaseStatus { get; set; } = FieldStatus.None;

    [ObservableProperty]
    public partial FieldStatus VerifyStatus { get; set; } = FieldStatus.None;

    [ObservableProperty]
    public partial FieldStatus OutputStatus { get; set; } = FieldStatus.None;

    [ObservableProperty]
    public partial FieldStatus ArchiveSetStatus { get; set; } = FieldStatus.None;

    partial void OnWinRarPathChanged(string value)
    {
        WinRarStatus = ReconstructorFieldGuidance.EvaluateWinRarPath(value);

        // The folder changed, so the previous folder's scan no longer describes the current path.
        // Mark the tree as not-yet-scanned (and invalidate any in-flight scan) BEFORE kicking off the
        // async scan for this folder. Otherwise a config's pending version selection applied right
        // after this (the mapper sets WinRarPath, then LoadPendingVersionSelection) would be consumed
        // by ApplyReconcile against the STALE previous scan and lost before the new folder's scan
        // lands, clearing the restored major toggles too. See audit #39.
        HasScannedVersions = false;
        _scanToken++;
        TriggerVersionScan();
    }

    partial void OnReleasePathChanged(string value) => RefreshReleaseOutputStatuses();

    partial void OnOutputPathChanged(string value) => RefreshReleaseOutputStatuses();

    /// <summary>
    /// Recomputes the Release and Output statuses together: an overlap between the two folders is a
    /// relationship, so a change to either must re-evaluate both (turning both red on overlap, or
    /// clearing both when resolved).
    /// </summary>
    private void RefreshReleaseOutputStatuses()
    {
        ReleaseStatus = ReconstructorFieldGuidance.EvaluateReleasePath(ReleasePath, OutputPath);
        OutputStatus = ReconstructorFieldGuidance.EvaluateOutputPath(OutputPath, ReleasePath);
    }

    partial void OnVerificationPathChanged(string value) =>
        VerifyStatus = ReconstructorFieldGuidance.EvaluateVerificationPath(value);

    /// <summary>
    /// Recomputes all four path statuses from the current path values. Called at construction and
    /// after <see cref="Reset"/> so a blank field shows its "Required" marker immediately — the
    /// per-property change hooks only fire when a value actually changes.
    /// </summary>
    private void RefreshPathStatuses()
    {
        WinRarStatus = ReconstructorFieldGuidance.EvaluateWinRarPath(WinRarPath);
        VerifyStatus = ReconstructorFieldGuidance.EvaluateVerificationPath(VerificationPath);
        RefreshReleaseOutputStatuses();
    }

    /// <summary>
    /// True while any required path (WinRAR, Release, Verify, Output) is empty or invalid —
    /// drives the warning glyph on the Paths sub-tab header.
    /// </summary>
    public bool PathsNeedAttention =>
        ReconstructorFieldGuidance.PathsNeedAttention(WinRarPath, ReleasePath, VerificationPath, OutputPath);

    // ── Progress ──

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    public partial string ProgressMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PhaseDescription { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool IsRunning { get; set; }

    /// <summary>
    /// True after a run completed successfully; reset when a new run starts. The wizard uses this
    /// to hide Back once the reconstruction is done.
    /// </summary>
    [ObservableProperty]
    public partial bool LastRunSucceeded { get; set; }

    /// <summary>
    /// One-shot: set by the wizard after it already asked the "output directory is not empty"
    /// question on the Files &amp; folders step, so Start doesn't ask a second time.
    /// </summary>
    public bool SuppressOutputNotEmptyConfirm { get; set; }

    /// <summary>
    /// One-shot: set by the wizard after it already asked the subdirectory modified-date
    /// warning on the Files &amp; folders step, so Start doesn't ask a second time.
    /// </summary>
    public bool SuppressSubdirTimestampConfirm { get; set; }

    /// <summary>
    /// The subdirectory modified-date warning, shared between Start and the wizard's step.
    /// </summary>
    public const string SubdirTimestampWarningText =
        "Release directory contains one or more subdirectories.\n" +
        "RAR file(s) preserve the modified date of files and subdirectories.\n" +
        "This means that if one or more subdirectories have been created manually, " +
        "the modified date will be different than the modified date of the directory in the original archive.\n" +
        "In this case, there is no chance of properly recreating the RAR file(s).\n\n" +
        "Are you sure the modified date of the file(s) and subdirectories are correct?";

    /// <summary>
    /// Whether Start would show the subdirectory modified-date warning: the release directory
    /// has subdirectories but the imported SRR carried no directory timestamps to restore.
    /// </summary>
    public bool NeedsSubdirTimestampWarning() =>
        ReconstructorFieldGuidance.NeedsSubdirTimestampWarning(ReleasePath, _import.DirTimestamps.Count);

    [ObservableProperty]
    public partial bool ShowProgress { get; set; }

    // ── Progress window state ──

    [ObservableProperty] public partial string TestCountText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ProgressPercentText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CurrentDetailText { get; set; } = string.Empty;
    [ObservableProperty] public partial string ElapsedText { get; set; } = string.Empty;
    [ObservableProperty] public partial string RemainingText { get; set; } = string.Empty;
    [ObservableProperty] public partial string SpeedText { get; set; } = string.Empty;
    [ObservableProperty] public partial string EtaText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool AutoScrollProgress { get; set; } = true;
    [ObservableProperty] public partial bool AutoScrollLog { get; set; } = true;

    public ObservableCollection<VersionEntry> VersionEntries { get; } = [];

    // ── File copy progress window state ──

    [ObservableProperty] public partial bool IsCopying { get; set; }
    [ObservableProperty] public partial string CopyHeadingText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CopySourceText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CopyDestText { get; set; } = string.Empty;
    [ObservableProperty] public partial double CopyProgressPercent { get; set; }
    [ObservableProperty] public partial string CopyProgressPercentText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CopyCurrentFileText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CopyRemainingText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CopyElapsedText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CopySpeedText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CopyTimeRemainingText { get; set; } = string.Empty;
    [ObservableProperty] public partial string CopyEtaText { get; set; } = string.Empty;

    // ── CRC validation progress window state ──

    [ObservableProperty] public partial bool IsVerifying { get; set; }
    [ObservableProperty] public partial string VerifyHeadingText { get; set; } = string.Empty;
    [ObservableProperty] public partial double VerifyProgressPercent { get; set; }
    [ObservableProperty] public partial string VerifyProgressPercentText { get; set; } = string.Empty;
    [ObservableProperty] public partial string VerifyCurrentFileText { get; set; } = string.Empty;
    [ObservableProperty] public partial string VerifyRemainingText { get; set; } = string.Empty;
    [ObservableProperty] public partial string VerifyElapsedText { get; set; } = string.Empty;
    [ObservableProperty] public partial string VerifySpeedText { get; set; } = string.Empty;
    [ObservableProperty] public partial string VerifyTimeRemainingText { get; set; } = string.Empty;
    [ObservableProperty] public partial string VerifyEtaText { get; set; } = string.Empty;

    public partial class VersionEntry : ObservableObject
    {
        [ObservableProperty] public partial string VersionName { get; set; } = "";
        [ObservableProperty] public partial string Status { get; set; } = "Testing";
        [ObservableProperty] public partial string Arguments { get; set; } = "";
        [ObservableProperty] public partial string Result { get; set; } = "";

        /// <summary>Label of the archive set this test belongs to (empty for single-set releases).</summary>
        public string SetText { get; set; } = "";

        /// <summary>
        /// Directory of the WinRAR version this entry tested; the run executes rar.exe inside it.
        /// </summary>
        public string VersionDirectory { get; set; } = "";

        /// <summary>
        /// The complete command line as executed: the quoted rar.exe path followed by the arguments.
        /// </summary>
        public string FullCommandLine => string.IsNullOrEmpty(VersionDirectory)
            ? Arguments
            : $"\"{Path.Combine(VersionDirectory, "rar.exe")}\" {Arguments}";

        // ── Timing ──
        // StartedAt is stamped when the row is created (the tracker constructs a row exactly when
        // its test begins). EndedAt is stamped once, when Status first leaves "Testing".

        /// <summary>When this test started (row construction time).</summary>
        public DateTime StartedAt { get; } = DateTime.Now;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EndText))]
        [NotifyPropertyChangedFor(nameof(DurationText))]
        public partial DateTime? EndedAt { get; set; }

        /// <summary>Wall-clock start time, e.g. "22:13:28".</summary>
        public string StartText => StartedAt.ToString("HH:mm:ss");

        /// <summary>Wall-clock end time, or empty while the test is still running.</summary>
        public string EndText => EndedAt?.ToString("HH:mm:ss") ?? string.Empty;

        /// <summary>
        /// Elapsed test time: counts up live while the test runs, then freezes at the final duration
        /// once the row finishes. Driven once per second by <see cref="RefreshLiveDuration"/>.
        /// </summary>
        public string DurationText =>
            ReconstructorFormatting.FormatTimeSpan((EndedAt ?? DateTime.Now) - StartedAt);

        /// <summary>Raises a change for <see cref="DurationText"/> so the live value re-renders.</summary>
        public void RefreshLiveDuration() => OnPropertyChanged(nameof(DurationText));

        // Stamp the end time the moment the row leaves "Testing" (Complete / Cancelled / Error all
        // flow through this setter, set by the tracker). The null guard makes it idempotent.
        partial void OnStatusChanged(string value)
        {
            if (value != "Testing" && EndedAt is null)
            {
                EndedAt = DateTime.Now;
            }
        }
    }

    // ── Logs ──

    [ObservableProperty] public partial string SystemLog { get; set; } = string.Empty;
    [ObservableProperty] public partial string Phase1Log { get; set; } = string.Empty;
    [ObservableProperty] public partial string Phase2Log { get; set; } = string.Empty;

    // ── RAR Versions ──

    [ObservableProperty] public partial bool Version2 { get; set; }
    [ObservableProperty] public partial bool Version3 { get; set; } = true;
    [ObservableProperty] public partial bool Version4 { get; set; } = true;
    [ObservableProperty] public partial bool Version5 { get; set; } = true;
    [ObservableProperty] public partial bool Version6 { get; set; } = true;
    [ObservableProperty] public partial bool Version7 { get; set; }

    // ── Per-sub-version selection (tree over the installed WinRAR versions) ──

    /// <summary>Installed-version tree grouped by major; the checked leaves drive the brute-force.</summary>
    public ObservableCollection<RarVersionGroup> VersionGroups { get; } = [];

    /// <summary>True once a folder scan has completed for an existing folder (even if it had no versions).</summary>
    [ObservableProperty]
    public partial bool HasScannedVersions { get; set; }

    /// <summary>True when the tree is empty, so the view can show the "no versions found" hint.</summary>
    [ObservableProperty]
    public partial bool ShowNoVersionsHint { get; set; }

    /// <summary>Last folder scan result, reused by import/config reconcile without re-hitting disk.</summary>
    private IReadOnlyList<InstalledRarVersion> _lastScan = [];

    /// <summary>Explicit version list from a config load, consumed by the next scanned reconcile.</summary>
    private List<int>? _pendingVersionSelection;

    /// <summary>Latest-wins guard for overlapping async scans.</summary>
    private int _scanToken;

    /// <summary>Suppresses tree→major sync while the VM is programmatically rebuilding the tree.</summary>
    private bool _suppressGroupSync;

    /// <summary>The currently-ticked leaf versions, ascending. Snapshotted at Start and by config Capture.</summary>
    internal IReadOnlyList<int> SelectedLeafVersions =>
        VersionGroups.SelectMany(g => g.Leaves).Where(l => l.IsChecked).Select(l => l.Version).OrderBy(v => v).ToList();

    /// <summary>
    /// The currently-ticked leaf FOLDER names (e.g. "winrar-390-beta1"). Carried to the engine as the
    /// version-folder allow-list so unticking one same-version variant leaf actually excludes its
    /// folder (two folders can parse to the same version, so version ranges alone cannot distinguish
    /// them).
    /// </summary>
    internal IReadOnlyList<string> SelectedLeafFolders =>
        VersionGroups.SelectMany(g => g.Leaves).Where(l => l.IsChecked).Select(l => l.FolderName).ToList();

    [RelayCommand]
    private void RescanVersions() => TriggerVersionScan();

    [RelayCommand]
    private void SelectAllVersions() => SetAllLeaves(true);

    [RelayCommand]
    private void SelectNoVersions() => SetAllLeaves(false);

    private void SetAllLeaves(bool value)
    {
        _suppressGroupSync = true;
        foreach (RarVersionGroup group in VersionGroups)
        {
            foreach (RarVersionLeaf leaf in group.Leaves)
            {
                leaf.IsChecked = value;
            }
        }

        _suppressGroupSync = false;
        SyncMajorsFromTree();
    }

    /// <summary>The most recent folder-scan Task, exposed so tests can await scan completion
    /// deterministically (production is fire-and-forget and marshals results to the UI thread).</summary>
    internal Task? LastVersionScan { get; private set; }

    /// <summary>Kicks off a folder scan: synchronous empty result for an invalid folder (keeps tests
    /// deterministic), otherwise off-thread with a latest-wins token.</summary>
    private void TriggerVersionScan()
    {
        string folder = WinRarPath;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            // Bump the token so a still-running async scan of a previous folder cannot land later
            // and repopulate the tree (with HasScannedVersions=true) against the now-invalid path.
            _scanToken++;
            ApplyScanResult([], folderScanned: false);
            LastVersionScan = Task.CompletedTask;
            return;
        }

        LastVersionScan = RunVersionScanAsync(folder);
    }

    private async Task RunVersionScanAsync(string folder)
    {
        int token = ++_scanToken;
        IReadOnlyList<InstalledRarVersion> installed;
        try
        {
            installed = await Task.Run(() => WinRarVersionScanner.Scan(folder)).ConfigureAwait(false);
        }
        catch
        {
            installed = [];
        }

        _uiDispatcher.Invoke(() =>
        {
            if (token != _scanToken)
            {
                return;
            }

            ApplyScanResult(installed, folderScanned: installed.Count > 0 || Directory.Exists(folder));
        });
    }

    /// <summary>Stores a scan result and reconciles the tree. Also the test seam for the async scan.</summary>
    internal void ApplyScanResult(IReadOnlyList<InstalledRarVersion> installed, bool folderScanned)
    {
        _lastScan = installed;
        HasScannedVersions = folderScanned;
        ApplyReconcile();
    }

    /// <summary>Sets the pending explicit selection (config load) and reconciles against the last scan.</summary>
    internal void LoadPendingVersionSelection(IReadOnlyList<int>? explicitVersions)
    {
        _pendingVersionSelection = explicitVersions?.ToList();
        ApplyReconcile();
    }

    private void ApplyReconcile()
    {
        HashSet<int> enabledMajors = EnabledMajors();
        HashSet<int> ticked = VersionSelectionReconciler.ComputeTicked(_lastScan, _pendingVersionSelection, enabledMajors);

        // The pending explicit selection is consumed only once a real scan has materialised the tree.
        if (_pendingVersionSelection is not null && HasScannedVersions)
        {
            _pendingVersionSelection = null;
        }

        RebuildVersionGroups(_lastScan, ticked);
        SyncMajorsFromTree();
        ShowNoVersionsHint = VersionGroups.Count == 0;
    }

    private void RebuildVersionGroups(IReadOnlyList<InstalledRarVersion> installed, HashSet<int> ticked)
    {
        _suppressGroupSync = true;
        foreach (RarVersionGroup group in VersionGroups)
        {
            group.SelectionChanged -= OnGroupSelectionChanged;
            group.Detach();
        }

        VersionGroups.Clear();
        foreach (IGrouping<int, InstalledRarVersion> majorGroup in installed.GroupBy(v => v.Version / 100).OrderBy(g => g.Key))
        {
            List<RarVersionLeaf> leaves = majorGroup
                .OrderBy(v => v.Version)
                .Select(v => new RarVersionLeaf(v.Version, v.FolderName, v.Tag) { IsChecked = ticked.Contains(v.Version) })
                .ToList();
            RarVersionGroup group = new(majorGroup.Key, leaves);
            group.SelectionChanged += OnGroupSelectionChanged;
            VersionGroups.Add(group);
        }

        _suppressGroupSync = false;
    }

    private void OnGroupSelectionChanged(object? sender, EventArgs e)
    {
        if (_suppressGroupSync)
        {
            return;
        }

        SyncMajorsFromTree();
    }

    /// <summary>Mirrors "any leaf in this major ticked" onto the coarse major bools — but only when a
    /// tree exists; with no scan the bools remain the fallback/coarse intent.</summary>
    private void SyncMajorsFromTree()
    {
        if (!HasScannedVersions)
        {
            return;
        }

        Version2 = MajorHasTick(2);
        Version3 = MajorHasTick(3);
        Version4 = MajorHasTick(4);
        Version5 = MajorHasTick(5);
        Version6 = MajorHasTick(6);
        Version7 = MajorHasTick(7);
    }

    private bool MajorHasTick(int major) =>
        VersionGroups.FirstOrDefault(g => g.Major == major)?.Leaves.Any(l => l.IsChecked) ?? false;

    private HashSet<int> EnabledMajors()
    {
        HashSet<int> majors = [];
        if (Version2)
        {
            majors.Add(2);
        }

        if (Version3)
        {
            majors.Add(3);
        }

        if (Version4)
        {
            majors.Add(4);
        }

        if (Version5)
        {
            majors.Add(5);
        }

        if (Version6)
        {
            majors.Add(6);
        }

        if (Version7)
        {
            majors.Add(7);
        }

        return majors;
    }

    // ── Compression Method ──

    [ObservableProperty] public partial bool SwitchM0 { get; set; }
    [ObservableProperty] public partial bool SwitchM1 { get; set; }
    [ObservableProperty] public partial bool SwitchM2 { get; set; }
    [ObservableProperty] public partial bool SwitchM3 { get; set; } = true;
    [ObservableProperty] public partial bool SwitchM4 { get; set; }
    [ObservableProperty] public partial bool SwitchM5 { get; set; }

    // ── Archive Format ──

    [ObservableProperty] public partial bool SwitchMA4 { get; set; }
    [ObservableProperty] public partial bool SwitchMA5 { get; set; }

    // ── Dictionary Size ──

    [ObservableProperty] public partial bool SwitchMD64K { get; set; }
    [ObservableProperty] public partial bool SwitchMD128K { get; set; }
    [ObservableProperty] public partial bool SwitchMD256K { get; set; }
    [ObservableProperty] public partial bool SwitchMD512K { get; set; }
    [ObservableProperty] public partial bool SwitchMD1024K { get; set; }
    [ObservableProperty] public partial bool SwitchMD2048K { get; set; }
    [ObservableProperty] public partial bool SwitchMD4096K { get; set; } = true;
    [ObservableProperty] public partial bool SwitchMD8M { get; set; }
    [ObservableProperty] public partial bool SwitchMD16M { get; set; }
    [ObservableProperty] public partial bool SwitchMD32M { get; set; }
    [ObservableProperty] public partial bool SwitchMD64M { get; set; }
    [ObservableProperty] public partial bool SwitchMD128M { get; set; }
    [ObservableProperty] public partial bool SwitchMD256M { get; set; }
    [ObservableProperty] public partial bool SwitchMD512M { get; set; }
    [ObservableProperty] public partial bool SwitchMD1G { get; set; }

    // ── Timestamps ──

    [ObservableProperty] public partial bool SwitchTSM0 { get; set; }
    [ObservableProperty] public partial bool SwitchTSM1 { get; set; }
    [ObservableProperty] public partial bool SwitchTSM2 { get; set; }
    [ObservableProperty] public partial bool SwitchTSM3 { get; set; }
    [ObservableProperty] public partial bool SwitchTSM4 { get; set; }

    [ObservableProperty] public partial bool SwitchTSC0 { get; set; }
    [ObservableProperty] public partial bool SwitchTSC1 { get; set; }
    [ObservableProperty] public partial bool SwitchTSC2 { get; set; }
    [ObservableProperty] public partial bool SwitchTSC3 { get; set; }
    [ObservableProperty] public partial bool SwitchTSC4 { get; set; }

    [ObservableProperty] public partial bool SwitchTSA0 { get; set; }
    [ObservableProperty] public partial bool SwitchTSA1 { get; set; }
    [ObservableProperty] public partial bool SwitchTSA2 { get; set; }
    [ObservableProperty] public partial bool SwitchTSA3 { get; set; }
    [ObservableProperty] public partial bool SwitchTSA4 { get; set; }

    // ── Other Options ──

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFileAttributesEnabled))]
    public partial bool SwitchAI { get; set; }

    [ObservableProperty] public partial bool SwitchR { get; set; } = true;
    [ObservableProperty] public partial bool SwitchDS { get; set; }
    [ObservableProperty] public partial bool SwitchS { get; set; }
    [ObservableProperty] public partial bool SwitchSDash { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMTRangeEnabled))]
    public partial bool SwitchMT { get; set; }

    [ObservableProperty] public partial int SwitchMTStart { get; set; } = 1;
    [ObservableProperty] public partial int SwitchMTEnd { get; set; } = Environment.ProcessorCount;

    // Volume
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVolumeOptionsEnabled))]
    public partial bool SwitchV { get; set; }

    [ObservableProperty] public partial string VolumeSize { get; set; } = DefaultVolumeSizeKb.ToString();
    [ObservableProperty] public partial int VolumeSizeUnitIndex { get; set; } = 1; // default KB
    [ObservableProperty] public partial bool UseOldVolumeNaming { get; set; }

    public static string[] VolumeSizeUnits { get; } = ["Bytes", "KB", "MB", "GB", "KiB", "MiB", "GiB"];

    // File attributes (null = Indeterminate)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSwitchAIEnabled))]
    public partial bool? FileA { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSwitchAIEnabled))]
    public partial bool? FileI { get; set; } = false;

    // Output options
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDeleteDuplicateCRCEnabled))]
    public partial bool DeleteRARFiles { get; set; }

    [ObservableProperty] public partial bool DeleteDuplicateCRCFiles { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRenameEnabled))]
    public partial bool StopOnFirstMatch { get; set; } = true;

    [ObservableProperty] public partial bool CompleteAllVolumes { get; set; }
    [ObservableProperty] public partial bool RenameToReleaseNames { get; set; } = true;

    /// <summary>
    /// The rename option requires Stop-after-first-match, so when it is turned off it is cleared
    /// (not left checked-but-greyed). It cannot be turned on while it is off — the sub-item is
    /// disabled — so no reverse coupling is needed.
    /// </summary>
    partial void OnStopOnFirstMatchChanged(bool value)
    {
        if (!value)
        {
            RenameToReleaseNames = false;
        }
    }

    partial void OnSwitchSChanged(bool value)
    {
        if (value)
        {
            SwitchSDash = false;
        }
    }

    partial void OnSwitchSDashChanged(bool value)
    {
        if (value)
        {
            SwitchS = false;
        }
    }

    // ── Computed enable/disable ──

    public bool IsMTRangeEnabled => SwitchMT;
    public bool IsVolumeOptionsEnabled => SwitchV;
    public bool IsSwitchAIEnabled => FileA == false && FileI == false;
    public bool IsFileAttributesEnabled => !SwitchAI;
    public bool IsDeleteDuplicateCRCEnabled => !DeleteRARFiles;
    public bool IsRenameEnabled => StopOnFirstMatch;

    // Host OS patching
    [ObservableProperty] public partial bool EnableHostOSPatching { get; set; } = true;

    // ── Reset ──

    /// <summary>
    /// Clears the import-gating and UI state back to a freshly-constructed default so a
    /// Beginner wizard opens clean. No-op while a run is in progress (e.g. started from the
    /// Advanced tab) so an active run isn't disrupted.
    /// </summary>
    public void Reset()
    {
        if (IsRunning)
        {
            return;
        }

        // Paths
        WinRarPath = string.Empty;
        ReleasePath = string.Empty;
        VerificationPath = string.Empty;
        OutputPath = string.Empty;

        // Import gating + warning
        HasImportedSrr = false;
        CustomPackerWarning = null;
        LastRunSucceeded = false;

        // Imported SRR details
        ImportedSrrName = string.Empty;
        ImportedSrrAppName = string.Empty;
        ImportedRarVolumeText = string.Empty;
        ImportedArchivedFilesText = string.Empty;
        ImportedCompressionText = string.Empty;
        ImportedStoredFilesText = string.Empty;

        // Imported SRR + detected header state — back to empty/null
        _import.Clear();
        ArchiveSetStatus = FieldStatus.None;

        // Progress
        ProgressPercent = 0;
        ProgressMessage = string.Empty;
        PhaseDescription = string.Empty;
        ShowProgress = false;
        TestCountText = string.Empty;
        ProgressPercentText = string.Empty;
        CurrentDetailText = string.Empty;
        ElapsedText = string.Empty;
        RemainingText = string.Empty;
        SpeedText = string.Empty;
        EtaText = string.Empty;
        _progress.Clear();

        // Logs
        SystemLog = string.Empty;
        Phase1Log = string.Empty;
        Phase2Log = string.Empty;

        // The brute-force option toggles (versions, compression, dictionary, timestamps,
        // volume, etc.) are intentionally left untouched: they are re-applied wholesale by
        // the mandatory Import-from-SRR step that opens the reconstruct wizard.

        // The paths were just cleared; pre-fill the configured defaults again.
        ApplyPathDefaultsFromSettings();
        RefreshPathStatuses();
    }

    // ── Browse Commands ──

    [RelayCommand]
    private async Task BrowseWinRarAsync()
    {
        string? path = await _fileDialog.OpenFolderAsync("Select WinRAR Installations Directory");
        if (path is not null)
        {
            WinRarPath = path;
        }
    }

    [RelayCommand]
    private async Task BrowseReleaseAsync()
    {
        string? path = await _fileDialog.OpenFolderAsync("Select Release Directory");
        if (path is not null)
        {
            ReleasePath = path;
        }
    }

    [RelayCommand]
    private async Task BrowseVerificationAsync()
    {
        string? path = await _fileDialog.OpenFileAsync("Select Verification File",
            FileDialogFilters.VerificationFiles);
        if (path is not null)
        {
            VerificationPath = path;
        }
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        string? path = await _fileDialog.OpenFolderAsync("Select Output Directory");
        if (path is not null)
        {
            OutputPath = path;
        }
    }

    // ── Import SRR ──

    [RelayCommand]
    private async Task ImportSRRAsync()
    {
        string? path = await _fileDialog.OpenFileAsync("Select SRR File",
            FileDialogFilters.SRRFiles);
        if (path is null)
        {
            return;
        }

        HasImportedSrr = false;

        try
        {
            Log(LogTarget.System, $"=== SRR Import: {Path.GetFileName(path)} ===");

            var srr = SRRFile.Load(path);
            Log(LogTarget.System, "SRR loaded successfully");

            // Pure parse: imported/detected state, custom-packer detection, and display strings.
            ImportedSrrInfo info = SrrImportParser.Parse(srr, path);

            // Detect SRRs that carry no RAR reconstruction information
            // (no RAR volume entries, no archived-file metadata, no detected
            // compression method). These can't drive automatic option setup,
            // so warn the user that they'll need to configure things manually.
            if (!info.HasRarReconstructionInfo)
            {
                Log(LogTarget.System,
                    "WARNING: SRR contains no RAR reconstruction information.");
                _fileDialog.ShowInfo(
                    "No RAR Reconstruction Info",
                    "This SRR file does not contain RAR reconstruction information " +
                    "(no RAR volume entries, archived files, or compression metadata).\n\n" +
                    "You will need to configure the RAR options manually before reconstructing.");
            }

            // Remember the imported SRR path for ALL SRRs (not just custom-packer ones). It is the
            // source for each set's embedded per-volume SFV (LoadEmbeddedSfvBytes) and lets
            // ArchiveSetPlanner.ResolveSets re-derive sets from the SRR on config-restore. This is
            // harmless for normal SRRs: RAROptions.SRRFilePath is consumed by the engine only on the
            // custom-packer direct path (Manager guards on CustomPackerDetected != None), so a
            // non-null value is ignored by the brute-force path.
            _import.SRRFilePath = path;

            // Custom packer detection
            if (srr.HasCustomPackerHeaders)
            {
                Log(LogTarget.System, $"Custom RAR packer detected: {srr.CustomPackerDetected}");
                _import.CustomPackerType = info.CustomPackerType;
                string warning = info.CustomPackerWarning ?? string.Empty;
                CustomPackerWarning = warning;

                _fileDialog.ShowWarning("Custom RAR Packer Detected", warning);
            }
            else
            {
                _import.CustomPackerType = CustomPackerType.None;
                CustomPackerWarning = null;
            }

            // Store imported data
            _import.ArchiveFiles = info.ArchiveFiles;
            _import.ArchiveDirectories = info.ArchiveDirectories;
            _import.DirTimestamps = info.DirTimestamps;
            _import.DirCreationTimes = info.DirCreationTimes;
            _import.DirAccessTimes = info.DirAccessTimes;
            _import.FileTimestamps = info.FileTimestamps;
            _import.FileCreationTimes = info.FileCreationTimes;
            _import.FileAccessTimes = info.FileAccessTimes;
            _import.ArchiveFileCrcs = info.ArchiveFileCrcs;
            _import.OriginalRarFileNames = info.OriginalRarFileNames;
            _import.ArchiveSets = info.ArchiveSets;
            ArchiveSetStatus = _import.ArchiveSets.Count > 1
                ? FieldStatus.Info($"This release has {_import.ArchiveSets.Count} archive sets " +
                    $"({string.Join(", ", _import.ArchiveSets.Select(s => string.IsNullOrEmpty(s.Directory) ? s.Key : s.Directory))}); each is reconstructed independently.")
                : FieldStatus.None;
            _import.ArchiveComment = info.ArchiveComment;
            _import.ArchiveCommentBytes = info.ArchiveCommentBytes;
            _import.CmtCompressedData = info.CmtCompressedData;
            _import.CmtCompressionMethod = info.CmtCompressionMethod;

            if (_import.ArchiveFiles.Count > 0 || _import.ArchiveDirectories.Count > 0)
            {
                string dirSuffix = _import.ArchiveDirectories.Count > 0 ? $", {_import.ArchiveDirectories.Count} dirs" : "";
                Log(LogTarget.System, $"Archive entries: {_import.ArchiveFiles.Count} files{dirSuffix}");
            }

            Log(LogTarget.System, $"Per-file timestamps: mtime={_import.FileTimestamps.Count}, ctime={_import.FileCreationTimes.Count}, atime={_import.FileAccessTimes.Count}");

            if (_import.CmtCompressedData is { Length: > 0 })
            {
                Log(LogTarget.System, $"CMT data: {_import.CmtCompressedData.Length} bytes — Phase 1 enabled");
            }

            // Host OS
            _import.DetectedFileHostOS = info.DetectedFileHostOS;
            _import.DetectedFileAttributes = info.DetectedFileAttributes;
            _import.DetectedCmtHostOS = info.DetectedCmtHostOS;
            _import.DetectedCmtFileTime = info.DetectedCmtFileTime;
            _import.DetectedCmtFileAttributes = info.DetectedCmtFileAttributes;
            _import.DetectedLargeFlag = info.DetectedLargeFlag;
            _import.DetectedHighPackSize = info.DetectedHighPackSize;
            _import.DetectedHighUnpSize = info.DetectedHighUnpSize;

            if (srr.HasLargeFiles == true)
            {
                EnableHostOSPatching = true;
                Log(LogTarget.System, "LARGE flag detected — header patching enabled");
            }

            if (srr.DetectedHostOS.HasValue)
            {
                Log(LogTarget.System, $"Host OS: {srr.DetectedHostOSName} (0x{srr.DetectedHostOS:X2})");
                bool isCurrentWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
                bool isRarUnix = srr.DetectedHostOS == 3;
                bool isRarWindows = srr.DetectedHostOS == 2;
                if ((isCurrentWindows && isRarUnix) || (!isCurrentWindows && isRarWindows))
                {
                    EnableHostOSPatching = true;
                    Log(LogTarget.System, "Host OS patching enabled (platform mismatch)");
                }
            }

            // Pure switch mapping: only the toggles the SRR actually specifies (partial diff —
            // unspecified groups stay null and the corresponding toggles are left untouched).
            SrrSwitchMapper.SwitchDiff switches = SrrSwitchMapper.Map(srr);
            ApplySwitchDiff(switches);

            // Timestamp precision
            TimestampPrecision? mtimePrecision = srr.FileMtimePrecision ?? srr.CmtMtimePrecision;
            TimestampPrecision? ctimePrecision = srr.FileCtimePrecision ?? srr.CmtCtimePrecision;
            TimestampPrecision? atimePrecision = srr.FileAtimePrecision ?? srr.CmtAtimePrecision;

            if (mtimePrecision.HasValue)
            {
                SetTimestampFlags(mtimePrecision.Value,
                    v => SwitchTSM0 = v, v => SwitchTSM1 = v, v => SwitchTSM2 = v, v => SwitchTSM3 = v, v => SwitchTSM4 = v);
                Log(LogTarget.System, $"Mtime precision: -tsm{(int)mtimePrecision.Value}");
            }

            if (ctimePrecision.HasValue)
            {
                SetTimestampFlags(ctimePrecision.Value,
                    v => SwitchTSC0 = v, v => SwitchTSC1 = v, v => SwitchTSC2 = v, v => SwitchTSC3 = v, v => SwitchTSC4 = v);
                Log(LogTarget.System, $"Ctime precision: -tsc{(int)ctimePrecision.Value}");
            }

            if (atimePrecision.HasValue)
            {
                SetTimestampFlags(atimePrecision.Value,
                    v => SwitchTSA0 = v, v => SwitchTSA1 = v, v => SwitchTSA2 = v, v => SwitchTSA3 = v, v => SwitchTSA4 = v);
                Log(LogTarget.System, $"Atime precision: -tsa{(int)atimePrecision.Value}");
            }

            // Optimise: single attribute/thread configuration
            FileA = false;
            FileI = false;
            SwitchAI = false;
            SwitchMT = false;
            SwitchR = true;

            // Volume size. The SRR fully determines the volume state, so a single-volume release must
            // actively CLEAR any multi-volume switch left over from a previous import — otherwise a
            // stale -v… would be added to every combination and guarantee a no-match.
            if (srr.RARFiles.Count > 1 && srr.VolumeSizeBytes.HasValue)
            {
                ApplyVolumeSize(srr.VolumeSizeBytes.Value);
            }
            else if (srr.IsVolumeArchive == true)
            {
                SwitchV = true;
                Log(LogTarget.System, "Multi-volume: Yes (size unknown)");
            }
            else if (srr.IsVolumeArchive == false || srr.RARFiles.Count <= 1)
            {
                if (SwitchV)
                {
                    Log(LogTarget.System, "Multi-volume: No");
                }

                SwitchV = false;
                UseOldVolumeNaming = false;
            }

            // Volume naming
            if (srr.IsVolumeArchive == true && srr.HasNewVolumeNaming == false)
            {
                UseOldVolumeNaming = true;
                Log(LogTarget.System, "Volume naming: Old (.rar, .r00)");
            }
            else if (srr.IsVolumeArchive == true && srr.HasNewVolumeNaming == true)
            {
                UseOldVolumeNaming = false;
            }

            // RAR version selection
            SetRARVersionsFromSRR(srr);
            _pendingVersionSelection = null;
            ApplyReconcile();

            // Extract stored SFV for verification
            TryExtractStoredSFV(path, srr);

            Log(LogTarget.System, "=== SRR Import Complete ===");

            PopulateImportedSrrDetails(info);
            HasImportedSrr = true;
        }
        catch (Exception ex)
        {
            Log(LogTarget.System, $"Failed to import SRR: {ex.Message}");
        }
    }

    /// <summary>Maps the parsed SRR summary onto the bound display properties shown on the wizard's import step.</summary>
    private void PopulateImportedSrrDetails(ImportedSrrInfo info)
    {
        ImportedSrrName = info.DisplayName;
        ImportedSrrAppName = info.DisplayAppName;
        ImportedRarVolumeText = info.DisplayRarVolumeText;
        ImportedArchivedFilesText = info.DisplayArchivedFilesText;
        ImportedCompressionText = info.DisplayCompressionText;
        ImportedStoredFilesText = info.DisplayStoredFilesText;
    }

    // ── Import / Export Configuration ──

    private static readonly System.Text.Json.JsonSerializerOptions _configSerializerOptions = new() { WriteIndented = true };

    [RelayCommand]
    private async Task ImportConfigAsync()
    {
        string? path = await _fileDialog.OpenFileAsync("Select Reconstructor Configuration",
            FileDialogFilters.ReconstructorConfig);
        if (path is null)
        {
            return;
        }

        try
        {
            string json = await File.ReadAllTextAsync(path);
            var config = System.Text.Json.JsonSerializer.Deserialize<ReconstructorConfig>(json);
            if (config is null)
            {
                Log(LogTarget.System, "Failed to import configuration: file is empty or invalid");
                return;
            }

            ApplyConfig(config);
            Log(LogTarget.System, $"Configuration imported from {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Log(LogTarget.System, $"Failed to import configuration: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ExportConfigAsync()
    {
        string? path = await _fileDialog.SaveFileAsync("Save Reconstructor Configuration",
            ".json", FileDialogFilters.ReconstructorConfig, "reconstructor-config.json");
        if (path is null)
        {
            return;
        }

        try
        {
            ReconstructorConfig config = CaptureConfig();
            string json = System.Text.Json.JsonSerializer.Serialize(config, _configSerializerOptions);
            await File.WriteAllTextAsync(path, json);
            Log(LogTarget.System, $"Configuration exported to {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Log(LogTarget.System, $"Failed to export configuration: {ex.Message}");
        }
    }

    private ReconstructorConfig CaptureConfig()
    {
        ReconstructorConfig config = ReconstructorConfigMapper.Capture(this);
        config.ImportedSrr = CaptureImportedSrrState();
        return config;
    }

    private ImportedSrrState? CaptureImportedSrrState() =>
        ImportedSrrStateMapper.Capture(_import, CustomPackerWarning);

    private void ApplyConfig(ReconstructorConfig c)
    {
        ReconstructorConfigMapper.Apply(this, c);
        ApplyImportedSrrState(c.ImportedSrr);
    }

    private void ApplyImportedSrrState(ImportedSrrState? s)
    {
        // Always reset — an absent block means "no SRR imported"
        _import = ImportedSrrStateMapper.Apply(s);
        CustomPackerWarning = s?.CustomPackerWarning;

        if (s is not null)
        {
            Log(LogTarget.System, $"Restored SRR state: {_import.ArchiveFiles.Count} files, mtime={_import.FileTimestamps.Count}, CRCs={_import.ArchiveFileCrcs.Count}, CMT={_import.CmtCompressedData?.Length ?? 0} bytes");
        }
    }

    // ── Start / Stop ──

    /// <summary>
    /// Whether the WinRAR, Release, and Output paths are all set and the Release/Output folders do
    /// not overlap — the path preconditions shared by Start (the command) and the Beginner wizard's
    /// "Files &amp; folders" step. Centralised so the two callers cannot drift apart.
    /// </summary>
    public bool PathsReadyToStart =>
        !string.IsNullOrWhiteSpace(WinRarPath)
        && !string.IsNullOrWhiteSpace(ReleasePath)
        && !string.IsNullOrWhiteSpace(OutputPath)
        && !ReconstructorFieldGuidance.PathsOverlap(ReleasePath, OutputPath);

    private bool CanStart() => !IsRunning && PathsReadyToStart;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        // One-shot confirmations the wizard may already have asked on its "Files & folders"
        // step — consume them up front so a stale flag can never suppress a future prompt.
        bool subdirTimestampsConfirmed = SuppressSubdirTimestampConfirm;
        bool outputNotEmptyConfirmed = SuppressOutputNotEmptyConfirm;
        SuppressSubdirTimestampConfirm = false;
        SuppressOutputNotEmptyConfirm = false;

        // ── Path validation ──

        if (string.IsNullOrWhiteSpace(WinRarPath))
        {
            Log(LogTarget.System, "Invalid WinRAR directory.");
            _fileDialog.ShowError("Validation Error", "Invalid WinRAR directory.");
            return;
        }

        if (!Directory.Exists(WinRarPath))
        {
            Log(LogTarget.System, "WinRAR directory does not exist.");
            _fileDialog.ShowError("Validation Error", "WinRAR directory does not exist.");
            return;
        }

        // A real scan that found zero valid version subfolders — block with a clear message so the
        // user knows to add a version subfolder. The no-scan fallback (HasScannedVersions == false)
        // still uses the broad major-version range and must not be blocked here.
        if (HasScannedVersions && VersionGroups.Count == 0)
        {
            Log(LogTarget.System, "No WinRAR versions found in the selected folder.");
            _fileDialog.ShowError("Validation Error",
                "No WinRAR versions were found in the WinRAR versions folder. Add a version subfolder containing rar.exe, then click Rescan.");
            return;
        }

        // A materialised tree with nothing ticked would brute-force zero versions — block it with a
        // clear message. The no-scan case (empty tree) is unaffected and uses the broad fallback.
        if (VersionGroups.Count > 0 && VersionGroups.SelectMany(g => g.Leaves).All(l => !l.IsChecked))
        {
            Log(LogTarget.System, "No WinRAR versions selected.");
            _fileDialog.ShowError("Validation Error", "Select at least one WinRAR version.");
            return;
        }

        if (string.IsNullOrWhiteSpace(ReleasePath))
        {
            Log(LogTarget.System, "Invalid release directory.");
            _fileDialog.ShowError("Validation Error", "Invalid release directory.");
            return;
        }

        if (!Directory.Exists(ReleasePath))
        {
            Log(LogTarget.System, "Release directory does not exist.");
            _fileDialog.ShowError("Validation Error", "Release directory does not exist.");
            return;
        }

        // Output must not be the release folder (or nested with it): the output-not-empty cleanup
        // below deletes the output folder's contents, which would wipe the release input files.
        if (ReconstructorFieldGuidance.PathsOverlap(ReleasePath, OutputPath))
        {
            Log(LogTarget.System, "Output folder overlaps the release folder.");
            _fileDialog.ShowError("Validation Error",
                "The Output folder must be different from the Release folder, and not inside it.");
            return;
        }

        // ── Subdirectory timestamp warning ──

        if (Directory.EnumerateDirectories(ReleasePath).Any() && _import.DirTimestamps.Count == 0)
        {
            bool proceed = subdirTimestampsConfirmed || await _fileDialog.ShowConfirmAsync("Warning: modified date",
                SubdirTimestampWarningText);
            if (!proceed)
            {
                Log(LogTarget.System, "Cancelled: subdirectory timestamp warning.");
                return;
            }
        }

        // ── Verification file validation ──

        if (string.IsNullOrWhiteSpace(VerificationPath))
        {
            Log(LogTarget.System, "Invalid verification file path.");
            _fileDialog.ShowError("Validation Error", "Invalid verification file path.");
            return;
        }

        if (!File.Exists(VerificationPath))
        {
            Log(LogTarget.System, "Verification file does not exist.");
            _fileDialog.ShowError("Validation Error", "Verification file does not exist.");
            return;
        }

        string verificationExt = Path.GetExtension(VerificationPath).ToLowerInvariant();
        if (verificationExt is not ".sfv" and not ".sha1")
        {
            Log(LogTarget.System, "Invalid verification file type.");
            _fileDialog.ShowError("Validation Error", "Invalid verification file type. Use .sfv or .sha1 files.");
            return;
        }

        int hashCount;
        try
        {
            hashCount = verificationExt == ".sfv"
                ? SFVFile.ReadFile(VerificationPath).Entries.Count
                : SHA1File.ReadFile(VerificationPath).Entries.Count;
        }
        catch (Exception ex)
        {
            Log(LogTarget.System, $"Failed to parse verification file: {ex.Message}");
            _fileDialog.ShowError("Validation Error", $"Failed to parse verification file:\n{ex.Message}");
            return;
        }

        if (hashCount == 0)
        {
            Log(LogTarget.System, "No hashes found in verification file.");
            _fileDialog.ShowError("Validation Error", "No hashes found in verification file.");
            return;
        }

        // ── Input file existence check ──
        //
        // The verify file (.sfv/.sha1) lists the OUTPUT archives we're trying to produce,
        // so it isn't useful as an input check. The imported SRR's archived files ARE the
        // expected input contents — verify those exist in the release directory. If no SRR
        // has been imported, skip this pre-flight; Manager.ValidateInputFiles will run later.
        if (_import.ArchiveFiles.Count > 0)
        {
            try
            {
                var missingFiles = new List<string>();
                foreach (string archiveFile in _import.ArchiveFiles)
                {
                    string fullPath = Path.Combine(ReleasePath, archiveFile);
                    if (!File.Exists(fullPath))
                    {
                        missingFiles.Add(archiveFile);
                    }
                }

                if (missingFiles.Count > 0)
                {
                    string fileList = string.Join("\n", missingFiles);
                    Log(LogTarget.System, $"Missing {missingFiles.Count} input file(s) in release directory.");
                    _fileDialog.ShowWarning(
                        "Missing Input Files",
                        $"The following {missingFiles.Count} file(s) listed in the imported SRR are missing from the release directory:\n\n{fileList}\n\nThe release directory should contain the unpacked archive contents (the files that originally went into the RARs).");
                    return;
                }
            }
            catch (Exception ex)
            {
                Log(LogTarget.System, $"Failed to validate input files: {ex.Message}");
            }
        }

        // ── Output directory validation & cleanup ──

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            Log(LogTarget.System, "Invalid output directory.");
            _fileDialog.ShowError("Validation Error", "Invalid output directory.");
            return;
        }

        if (!Directory.Exists(OutputPath))
        {
            try
            {
                Directory.CreateDirectory(OutputPath);
                Log(LogTarget.System, $"Created output directory: {OutputPath}");
            }
            catch (Exception ex)
            {
                Log(LogTarget.System, $"Failed to create output directory: {ex.Message}");
                _fileDialog.ShowError("Validation Error", $"Failed to create output directory:\n{ex.Message}");
                return;
            }
        }
        else if (Directory.EnumerateFileSystemEntries(OutputPath).Any())
        {
            bool proceed = outputNotEmptyConfirmed || await _fileDialog.ShowConfirmAsync("Output Directory Not Empty",
                $"The output directory is not empty:\n\n{OutputPath}\n\nIts contents will be deleted before starting. Continue?");
            if (!proceed)
            {
                Log(LogTarget.System, "Cancelled: output directory not empty.");
                return;
            }

            try
            {
                foreach (string file in Directory.GetFiles(OutputPath))
                {
                    File.Delete(file);
                }

                foreach (string dir in Directory.GetDirectories(OutputPath))
                {
                    Directory.Delete(dir, true);
                }

                Log(LogTarget.System, "Output directory cleaned.");
            }
            catch (Exception ex)
            {
                Log(LogTarget.System, $"Failed to clean output directory: {ex.Message}");
                _fileDialog.ShowError("Error", $"Failed to clean output directory:\n{ex.Message}");
                return;
            }
        }

        // ── Start brute-force ──

        IsRunning = true;
        LastRunSucceeded = false;
        ShowProgress = true;
        ProgressPercent = 0;
        ProgressMessage = "Starting...";
        SystemLog = string.Empty;
        Phase1Log = string.Empty;
        Phase2Log = string.Empty;
        _timestampFailures.Clear();

        // Reset progress window state
        TestCountText = string.Empty;
        ProgressPercentText = string.Empty;
        CurrentDetailText = string.Empty;
        ElapsedText = "00:00";
        RemainingText = string.Empty;
        SpeedText = string.Empty;
        EtaText = string.Empty;
        _progress.StartRun();
        _elapsedTimer.Start();

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        // Yield so the dispatcher can open the progress window before heavy work starts
        await Task.Yield();

        try
        {
            Log(LogTarget.System, "Starting brute-force...");
            Log(LogTarget.System, $"WinRAR: {WinRarPath}");
            Log(LogTarget.System, $"Release: {ReleasePath}");
            Log(LogTarget.System, $"Output: {OutputPath}");

            await RunArchiveSetsAsync(token);

            // A Stop during RAR execution cancels the run but returns normally (the library
            // swallows the process's OperationCanceledException), so detect the cancelled token
            // here and report "Cancelled" rather than the misleading "No match found".
            token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            _progress.SetActiveVersionStatus("Cancelled");

            ProgressMessage = "Cancelled.";
            PhaseDescription = "Cancelled";
            Log(LogTarget.System, "Brute-force cancelled by user.");
        }
        catch (Exception ex)
        {
            _progress.SetActiveVersionStatus("Error");

            ProgressMessage = "Error.";
            PhaseDescription = "Error";
            Log(LogTarget.System, $"Error: {ex.Message}");
        }
        finally
        {
            _elapsedTimer.Stop();
            _progress.StopRun();
            ElapsedText = _progress.FinalElapsedText();
            IsRunning = false;

            // A cancelled/failed run stops mid-copy without a final copy-progress event;
            // clear the flag here so the copy progress window can close.
            if (IsCopying)
            {
                _progress.StopCopy();
                IsCopying = false;
            }

            // Same for input-CRC validation: a cancel during verification throws before the lib's
            // final 100% event, so IsVerifying would otherwise stay true forever and the modal CRC
            // window could never close (its Closing handler cancels while IsVerifying).
            if (IsVerifying)
            {
                _progress.StopVerify();
                IsVerifying = false;
            }

            _cts?.Dispose();
            _cts = null;
        }
    }

    // ── Per-archive-set reconstruction loop ──

    /// <summary>
    /// Reconstructs each archive set independently: per-set input/CRCs/metadata, isolated work dirs,
    /// subfolder-preserving relocation, and seeded-with-fallback cross-set search. A single root set
    /// runs exactly as before (work dir = OutputPath, no relocation, byte-identical output). A
    /// failure in one set is recorded and the loop continues to the next; a cancellation stops the
    /// loop, cleans the in-flight set, and leaves completed sets intact.
    /// </summary>
    private async Task RunArchiveSetsAsync(CancellationToken token)
    {
        SharedReconstructionSettings shared = BuildSharedSettings();

        // For the legacy / no-SRR single flat set the original RAR names may be empty; fall back to
        // the verification SFV's RAR-volume entries so output renaming still works (matches the old
        // ResolveOutputRenameNames behaviour). When an SRR was imported its names take precedence.
        IReadOnlyList<string> flatNames = _import.OriginalRarFileNames.Count > 0
            ? _import.OriginalRarFileNames
            : ResolveSfvVolumeNames();

        IReadOnlyList<SrrArchiveSet> sets = ArchiveSetPlanner.ResolveSets(
            _import.ArchiveSets, _import.SRRFilePath, flatNames, _import.ArchiveFiles);

        SFVFile? userSfv = TryLoadUserSfv(VerificationPath);

        var outcomes = new List<SetOutcome>();
        WinningCombo? seed = null;

        if (sets.Count > 1)
        {
            Log(LogTarget.System, $"Reconstructing {sets.Count} archive sets independently.");
        }

        for (int i = 0; i < sets.Count; i++)
        {
            SrrArchiveSet set = sets[i];
            string label = string.IsNullOrEmpty(set.Key) ? "(release)" : set.Key;
            if (sets.Count > 1)
            {
                Log(LogTarget.System, $"=== Set {i + 1}/{sets.Count}: {label} ===");
            }

            byte[]? embedded = LoadEmbeddedSfvBytes(set);
            Dictionary<string, string> expected = ArchiveSetPlanner.BuildExpectedVolumeCrcs(set, embedded, userSfv);

            // Full-volume verification needs a per-volume CRC for every volume; without them we
            // cannot honestly verify the set, so skip it rather than report a false success.
            // Note: SHA1 runs (no per-volume CRC source) and zero-coverage cases are NOT skipped —
            // the engine still runs and gates on the first-volume hash. Only partial CRC32 coverage
            // (some volumes have CRCs but not all) is an honest skip.
            if (ArchiveSetPlanner.ShouldSkipUnverifiableSet(shared.CompleteAllVolumes, shared.HashType, expected.Count, set.VolumeNames.Count))
            {
                Log(LogTarget.System, $"Set {label}: no per-volume CRCs to verify; supply its .sfv. Skipping.");
                outcomes.Add(new SetOutcome(set, label, Success: false, Skipped: true));
                continue;
            }

            BruteForceOptions options = ArchiveSetPlanner.BuildOptionsForSet(set, shared, expected);

            // Tell the progress tracker which set is active so new rows are stamped with the label.
            _progress.SetActiveSet(sets.Count > 1 ? label : string.Empty);

            bool success;
            WinningCombo? combo;
            try
            {
                (success, combo) = await RunSingleSetAsync(label, options, seed, sets.Count, token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A set's own failure (e.g. an InvalidDataException from input-CRC validation) must
                // not abort the whole run — record it and move on to the next set.
                Log(LogTarget.System, $"Set {label} failed: {ex.Message}");
                CleanupWorkRoot(options.OutputDirectoryPath, set, sets.Count);
                outcomes.Add(new SetOutcome(set, label, Success: false, Skipped: false));
                continue;
            }

            if (token.IsCancellationRequested)
            {
                CleanupWorkRoot(options.OutputDirectoryPath, set, sets.Count);
                break;
            }

            if (success)
            {
                seed ??= combo;
                if (!RelocateVerifiedOutput(options.OutputDirectoryPath, set, sets.Count))
                {
                    // Relocation failure: the set was reconstructed correctly but output could not be
                    // moved to its final location. Report it as failed so the caller is not misled.
                    success = false;
                }
            }

            outcomes.Add(new SetOutcome(set, label, success, Skipped: false));
        }

        ReportSetSummary(outcomes, sets.Count, token.IsCancellationRequested);
    }

    /// <summary>
    /// Runs one set's brute force. For later sets a captured winning combo is tried first (seeding);
    /// only if it fails (and the run was not cancelled) is the full option matrix run. Returns the
    /// set's success and the winning combo (for seeding subsequent sets).
    /// </summary>
    private async Task<(bool Success, WinningCombo? Combo)> RunSingleSetAsync(
        string label, BruteForceOptions options, WinningCombo? seed, int setCount, CancellationToken token)
    {
        BruteForceRunResult result;
        if (seed is not null && setCount > 1)
        {
            BruteForceOptions narrowed = ArchiveSetPlanner.NarrowToCombo(options, seed);
            result = await Task.Run(() => _bruteForceService.RunAsync(narrowed, token), token);
            if (!result.Success && !token.IsCancellationRequested)
            {
                Log(LogTarget.System, $"Seed combo did not reproduce {label}; running full search.");
                result = await Task.Run(() => _bruteForceService.RunAsync(options, token), token);
            }
        }
        else
        {
            result = await Task.Run(() => _bruteForceService.RunAsync(options, token), token);
        }

        return (result.Success, result.Combo);
    }

    /// <summary>One archive set's reconstruction outcome.</summary>
    private readonly record struct SetOutcome(SrrArchiveSet Set, string Label, bool Success, bool Skipped);

    /// <summary>Captures the non-per-set toggles, version ranges, command-line matrix, and release-wide SRR data.</summary>
    internal SharedReconstructionSettings BuildSharedSettings()
    {
        RarSwitchSettings switches = BuildSwitchSettings();
        HashType hashType = Path.GetExtension(VerificationPath).Equals(".sha1", StringComparison.OrdinalIgnoreCase)
            ? HashType.SHA1
            : HashType.CRC32;

        return new SharedReconstructionSettings
        {
            WinRarPath = WinRarPath,
            ReleasePath = ReleasePath,
            OutputPath = OutputPath,
            RarVersions = RarCommandLineBuilder.BuildVersionRanges(switches),
            // Only folder-filter when a real scan produced the tree; the no-scan fallback uses broad
            // major-version ranges and must NOT be restricted to specific folder names.
            SelectedVersionFolders = HasScannedVersions ? SelectedLeafFolders : [],
            CommandLineArguments = RarCommandLineBuilder.BuildCommandLineArguments(switches),
            HashType = hashType,
            VerificationHashes = LoadVerificationHashes(hashType),
            SetFileArchiveAttribute = ToTriState(FileA),
            SetFileNotContentIndexedAttribute = ToTriState(FileI),
            DeleteRARFiles = DeleteRARFiles,
            DeleteDuplicateCRCFiles = DeleteDuplicateCRCFiles,
            StopOnFirstMatch = StopOnFirstMatch,
            CompleteAllVolumes = CompleteAllVolumes,
            RenameToReleaseNames = RenameToReleaseNames,
            EnableHostOSPatching = EnableHostOSPatching,
            UseOldVolumeNaming = UseOldVolumeNaming,
            ArchiveComment = _import.ArchiveComment,
            ArchiveCommentBytes = _import.ArchiveCommentBytes,
            CmtCompressedData = _import.CmtCompressedData,
            CmtCompressionMethod = _import.CmtCompressionMethod,
            DetectedCmtHostOS = _import.DetectedCmtHostOS,
            DetectedCmtFileTime = _import.DetectedCmtFileTime,
            DetectedCmtFileAttributes = _import.DetectedCmtFileAttributes,
            CustomPackerDetected = _import.CustomPackerType,
            SRRFilePath = _import.SRRFilePath,
            ArchiveDirectories = _import.ArchiveDirectories,
            DirectoryTimestamps = _import.DirTimestamps,
            DirectoryCreationTimes = _import.DirCreationTimes,
            DirectoryAccessTimes = _import.DirAccessTimes,
        };
    }

    /// <summary>
    /// Reads every expected output hash from the verification file: CRC32 entries from a .sfv or
    /// SHA1 entries from a .sha1. These seed each set's first-volume gate. Empty when the file is
    /// missing or unreadable (validation has already confirmed it exists and parses by this point).
    /// </summary>
    private IReadOnlyCollection<string> LoadVerificationHashes(HashType hashType)
    {
        if (string.IsNullOrWhiteSpace(VerificationPath) || !File.Exists(VerificationPath))
        {
            return [];
        }

        try
        {
            return hashType == HashType.SHA1
                ? [.. SHA1File.ReadFile(VerificationPath).Entries.Select(e => e.SHA1)]
                : [.. SFVFile.ReadFile(VerificationPath).Entries.Select(e => e.CRC)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log(LogTarget.System, $"Could not read verification hashes: {ex.Message}");
            return [];
        }
    }

    /// <summary>Loads the user-supplied verification SFV (null for .sha1 or any non-SFV path).</summary>
    private static SFVFile? TryLoadUserSfv(string verificationPath)
    {
        if (string.IsNullOrWhiteSpace(verificationPath)
            || !Path.GetExtension(verificationPath).Equals(".sfv", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(verificationPath))
        {
            return null;
        }

        try
        {
            return SFVFile.ReadFile(verificationPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// The RAR-volume filenames listed in the verification SFV, in SFV order. Used as the flat set's
    /// volume/rename names when no SRR supplied them. Empty when there is no readable .sfv.
    /// </summary>
    private List<string> ResolveSfvVolumeNames()
    {
        if (string.IsNullOrWhiteSpace(VerificationPath)
            || !Path.GetExtension(VerificationPath).Equals(".sfv", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(VerificationPath))
        {
            return [];
        }

        try
        {
            return [.. SFVFile.ReadFile(VerificationPath).Entries
                .Select(e => e.FileName)
                .Where(RARVolumeIdentifier.IsRarVolume)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log(LogTarget.System, $"Could not read SFV for output naming: {ex.Message}");
            return [];
        }
    }

    /// <summary>
    /// Reads the embedded SFV bytes for a set from the imported SRR's stored files. For a single
    /// flat set (empty key) any stored .sfv matches. Otherwise a stored .sfv matches this set when
    /// either its archive-set key equals the set key (handles directory-prefixed stored names such
    /// as "DVD1\aln-re4a.sfv" → key "DVD1/aln-re4a"), OR its base name equals the set's base name
    /// (handles a flat "aln-re4a.sfv" matched to key "DVD1/aln-re4a"). Returns null when no SRR
    /// was imported or no stored .sfv matches.
    /// </summary>
    private byte[]? LoadEmbeddedSfvBytes(SrrArchiveSet set)
    {
        string? srrPath = _import.SRRFilePath;
        if (string.IsNullOrWhiteSpace(srrPath) || !File.Exists(srrPath))
        {
            return null;
        }

        try
        {
            SRRFile srr = SRRFile.Load(srrPath);
            return srr.ReadStoredFile(srrPath, name => EmbeddedSfvMatchesSet(name, set));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Log(LogTarget.System, $"Could not read embedded SFV for {set.Key}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Whether a stored file is the .sfv for the given set. See <see cref="LoadEmbeddedSfvBytes"/>
    /// for the matching rules. Shared with the embedded-SFV resolution test so both use one predicate.
    /// </summary>
    internal static bool EmbeddedSfvMatchesSet(string storedName, SrrArchiveSet set)
    {
        if (!storedName.EndsWith(".sfv", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Single flat set: any stored .sfv is its SFV.
        if (string.IsNullOrEmpty(set.Key))
        {
            return true;
        }

        // Key match: handles a directory-prefixed stored name (e.g. "DVD1\aln-re4a.sfv").
        if (RARVolumeIdentifier.GetArchiveSetKey(storedName).Equals(set.Key, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Base-name match: handles a flat stored name (e.g. "aln-re4a.sfv") whose set key carries a
        // directory prefix. The set's base name is the last '/'-segment of its key.
        string setBaseName = set.Key[(set.Key.LastIndexOf('/') + 1)..];
        string storedBaseName = Path.GetFileNameWithoutExtension(storedName);
        return storedBaseName.Equals(setBaseName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Moves a multi-set's verified volumes from its isolated work dir into the subfolder-preserving
    /// final layout (<c>OutputPath\output\&lt;set.Directory&gt;\</c>), then deletes the scratch dir. A
    /// single root set is a no-op: its output already sits at <c>OutputPath\output\</c>.
    /// </summary>
    /// <returns>
    /// True if relocation succeeded (or was a no-op for a single set); false if an I/O or
    /// authorization error prevented the move so the caller can record the set as failed.
    /// </returns>
    private bool RelocateVerifiedOutput(string workRoot, SrrArchiveSet set, int setCount)
    {
        if (setCount <= 1)
        {
            return true;
        }

        try
        {
            string sourceDir = Path.Combine(workRoot, "output");
            if (!Directory.Exists(sourceDir))
            {
                return true;
            }

            string targetDir = Path.Combine(OutputPath, "output", set.Directory.Replace('/', Path.DirectorySeparatorChar));

            // Clean a pre-existing target subfolder so re-runs are deterministic. Only when this set
            // owns a distinct subfolder — multiple root-level sets (e.g. cd1/cd2 with Directory == "")
            // share OutputPath\output\ and are distinguished by filename, so deleting it would wipe a
            // sibling root set's already-relocated volumes.
            if (!string.IsNullOrEmpty(set.Directory) && Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }

            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string dest = Path.Combine(targetDir, Path.GetFileName(file));
                File.Move(file, dest, overwrite: true);
            }

            Directory.Delete(workRoot, recursive: true);
            Log(LogTarget.System, $"Set {set.Key}: output -> {targetDir}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(LogTarget.System, $"Failed to relocate output for {set.Key}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Removes an in-flight multi-set's scratch dir and any partial final subfolder so a cancelled
    /// or failed set leaves no half-written output behind. No-op for a single root set.
    /// </summary>
    private void CleanupWorkRoot(string workRoot, SrrArchiveSet set, int setCount)
    {
        if (setCount <= 1)
        {
            return;
        }

        try
        {
            if (Directory.Exists(workRoot))
            {
                Directory.Delete(workRoot, recursive: true);
            }

            string targetDir = Path.Combine(OutputPath, "output", set.Directory.Replace('/', Path.DirectorySeparatorChar));
            if (!string.IsNullOrEmpty(set.Directory) && Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log(LogTarget.System, $"Failed to clean up work dir for {set.Key}: {ex.Message}");
        }
    }

    /// <summary>
    /// Logs a per-set pass/fail/skip/cancelled summary and sets the overall progress message and
    /// <see cref="LastRunSucceeded"/>. Overall success requires every set to have passed with none
    /// skipped and no cancellation.
    /// </summary>
    private void ReportSetSummary(IReadOnlyList<SetOutcome> outcomes, int totalSets, bool cancelled)
    {
        bool multi = totalSets > 1;

        if (multi)
        {
            Log(LogTarget.System, "=== Reconstruction summary ===");
            foreach (SetOutcome o in outcomes)
            {
                string mark = o.Skipped ? "skipped" : o.Success ? "OK" : "failed";
                Log(LogTarget.System, $"  [{mark}] {o.Label}");
            }

            int notAttempted = totalSets - outcomes.Count;
            if (notAttempted > 0)
            {
                Log(LogTarget.System, $"  [not attempted] {notAttempted} set(s)");
            }
        }

        if (cancelled)
        {
            // The outer cancellation handler owns the final version-row status and progress message.
            return;
        }

        ProgressPercent = 100;
        ProgressPercentText = "100%";
        if (_progress.LastOperationSize > 0)
        {
            TestCountText = $"Test {_progress.LastOperationSize:N0} of {_progress.LastOperationSize:N0}";
        }

        bool attemptedAll = outcomes.Count == totalSets;
        bool allOk = attemptedAll && outcomes.All(o => o is { Success: true, Skipped: false });
        bool anySuccess = outcomes.Any(o => o is { Success: true, Skipped: false });

        _progress.CompleteActiveVersion(anySuccess ? "Match" : "No Match");

        ProgressMessage = allOk ? "Match found!" : "No match found.";
        PhaseDescription = allOk ? "Complete — Match Found!" : "Complete — No Match";
        LastRunSucceeded = allOk;
        Log(LogTarget.System, allOk
            ? "Brute-force completed: all sets matched!"
            : "Brute-force completed: not all sets matched.");
    }

    [RelayCommand]
    private void Stop()
    {
        // Cancelling the token reaches the running RAR processes through the service and
        // Manager (the token is threaded into BruteForceRARVersionAsync).
        _cts?.Cancel();
        Log(LogTarget.System, "Cancellation requested...");
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        try
        {
            // Brute-force runs put the final archives in the "output" subdirectory; direct
            // (custom packer) reconstruction writes to the output folder root.
            string folder = Path.Combine(OutputPath, "output");
            if (!Directory.Exists(folder))
            {
                folder = OutputPath;
            }

            if (Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Log(LogTarget.System, $"Could not open output folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SaveLogAsync()
    {
        bool hasContent = SystemLog.Length > 0 || Phase1Log.Length > 0 || Phase2Log.Length > 0;

        if (!hasContent)
        {
            return;
        }

        string? path = await _fileDialog.SaveFileAsync(
            "Save log", ".txt", ["Text Files|*.txt"], "log.txt");

        if (path is null)
        {
            return;
        }

        try
        {
            var lines = new List<string>();

            if (SystemLog.Length > 0)
            {
                lines.Add("=== System ===");
                lines.AddRange(SystemLog.Split(Environment.NewLine));
            }

            if (Phase1Log.Length > 0)
            {
                if (lines.Count > 0)
                {
                    lines.Add(string.Empty);
                }

                lines.Add("=== Phase 1 ===");
                lines.AddRange(Phase1Log.Split(Environment.NewLine));
            }

            if (Phase2Log.Length > 0)
            {
                if (lines.Count > 0)
                {
                    lines.Add(string.Empty);
                }

                lines.Add("=== Phase 2 ===");
                lines.AddRange(Phase2Log.Split(Environment.NewLine));
            }

            await LogExporter.SaveAsync(lines, path);
            Log(LogTarget.System, $"Log saved to {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            Log(LogTarget.System, $"ERROR saving log: {ex.Message}");
        }
    }

    // ── Build Options ──

    /// <summary>Captures the current RAR switch toggles for <see cref="RarCommandLineBuilder"/>.</summary>
    private RarSwitchSettings BuildSwitchSettings() => new()
    {
        Version2 = Version2,
        Version3 = Version3,
        Version4 = Version4,
        Version5 = Version5,
        Version6 = Version6,
        Version7 = Version7,
        SelectedRarVersions = SelectedLeafVersions,
        HasScannedVersions = HasScannedVersions,

        SwitchM0 = SwitchM0,
        SwitchM1 = SwitchM1,
        SwitchM2 = SwitchM2,
        SwitchM3 = SwitchM3,
        SwitchM4 = SwitchM4,
        SwitchM5 = SwitchM5,

        SwitchMA4 = SwitchMA4,
        SwitchMA5 = SwitchMA5,

        SwitchMD64K = SwitchMD64K,
        SwitchMD128K = SwitchMD128K,
        SwitchMD256K = SwitchMD256K,
        SwitchMD512K = SwitchMD512K,
        SwitchMD1024K = SwitchMD1024K,
        SwitchMD2048K = SwitchMD2048K,
        SwitchMD4096K = SwitchMD4096K,
        SwitchMD8M = SwitchMD8M,
        SwitchMD16M = SwitchMD16M,
        SwitchMD32M = SwitchMD32M,
        SwitchMD64M = SwitchMD64M,
        SwitchMD128M = SwitchMD128M,
        SwitchMD256M = SwitchMD256M,
        SwitchMD512M = SwitchMD512M,
        SwitchMD1G = SwitchMD1G,

        SwitchTSM0 = SwitchTSM0,
        SwitchTSM1 = SwitchTSM1,
        SwitchTSM2 = SwitchTSM2,
        SwitchTSM3 = SwitchTSM3,
        SwitchTSM4 = SwitchTSM4,
        SwitchTSC0 = SwitchTSC0,
        SwitchTSC1 = SwitchTSC1,
        SwitchTSC2 = SwitchTSC2,
        SwitchTSC3 = SwitchTSC3,
        SwitchTSC4 = SwitchTSC4,
        SwitchTSA0 = SwitchTSA0,
        SwitchTSA1 = SwitchTSA1,
        SwitchTSA2 = SwitchTSA2,
        SwitchTSA3 = SwitchTSA3,
        SwitchTSA4 = SwitchTSA4,

        SwitchAI = SwitchAI,
        SwitchR = SwitchR,
        SwitchDS = SwitchDS,
        SwitchS = SwitchS,
        SwitchSDash = SwitchSDash,
        SwitchMT = SwitchMT,
        SwitchMTStart = SwitchMTStart,
        SwitchMTEnd = SwitchMTEnd,

        SwitchV = SwitchV,
        VolumeSize = VolumeSize,
        VolumeSizeUnitIndex = VolumeSizeUnitIndex,
        UseOldVolumeNaming = UseOldVolumeNaming,
    };

    private static TriState ToTriState(bool? value) => value switch
    {
        true => TriState.Checked,
        false => TriState.Unchecked,
        null => TriState.Indeterminate
    };

    // ── Event Handlers ──

    private void OnFileCopyProgress(object? _, FileCopyProgressEventArgs e)
    {
        _uiDispatcher.Post(() =>
        {
            // A queued progress event can arrive after a cancelled run already cleaned up;
            // re-raising IsCopying then would re-open (and strand) the copy progress window.
            if (!IsRunning)
            {
                return;
            }

            if (!IsCopying)
            {
                IsCopying = true;
                _progress.StartCopy();
            }

            CopyProgressUpdate u = _progress.ApplyCopyProgress(e);
            CopyHeadingText = u.HeadingText;
            CopySourceText = u.SourceText;
            CopyDestText = u.DestText;
            CopyProgressPercent = u.ProgressPercent;
            CopyProgressPercentText = u.ProgressPercentText;
            CopyCurrentFileText = u.CurrentFileText;
            CopyRemainingText = u.RemainingText;
            CopyElapsedText = u.ElapsedText;
            if (u.HasSpeed)
            {
                CopySpeedText = u.SpeedText;
                if (u.HasEta)
                {
                    CopyTimeRemainingText = u.TimeRemainingText;
                    CopyEtaText = u.EtaText;
                }
            }

            if (u.IsComplete)
            {
                IsCopying = false;
            }
        });
    }

    private void OnCRCValidationProgress(object? _, CRCValidationProgressEventArgs e)
    {
        _uiDispatcher.Post(() =>
        {
            // A queued progress event can arrive after a cancelled run already cleaned up;
            // re-raising IsVerifying then would re-open (and strand) the CRC progress window.
            if (!IsRunning)
            {
                return;
            }

            if (!IsVerifying)
            {
                IsVerifying = true;
                _progress.StartVerify();
            }

            VerifyProgressUpdate u = _progress.ApplyVerifyProgress(e);
            VerifyHeadingText = u.HeadingText;
            VerifyProgressPercent = u.ProgressPercent;
            VerifyProgressPercentText = u.ProgressPercentText;
            VerifyCurrentFileText = u.CurrentFileText;
            VerifyRemainingText = u.RemainingText;
            VerifyElapsedText = u.ElapsedText;
            if (u.HasSpeed)
            {
                VerifySpeedText = u.SpeedText;
                if (u.HasEta)
                {
                    VerifyTimeRemainingText = u.TimeRemainingText;
                    VerifyEtaText = u.EtaText;
                }
            }

            if (u.IsComplete)
            {
                IsVerifying = false;
            }
        });
    }

    private void OnProgress(object? _, BruteForceProgressEventArgs e)
    {
        _uiDispatcher.Invoke(() =>
        {
            BruteForceProgressUpdate u = _progress.ApplyProgress(e);

            ProgressPercent = u.ProgressPercent;
            PhaseDescription = u.PhaseDescription;
            ProgressMessage = u.ProgressMessage;
            TestCountText = u.TestCountText;
            ProgressPercentText = u.ProgressPercentText;
            CurrentDetailText = u.CurrentDetailText;
            ElapsedText = u.ElapsedText;
            if (u.HasTiming)
            {
                RemainingText = u.RemainingText;
                SpeedText = u.SpeedText;
                EtaText = u.EtaText;
            }
        });
    }

    private void OnElapsedTimerTick()
    {
        ElapsedTick tick = _progress.Tick();
        ElapsedText = tick.ElapsedText;

        if (tick.HasTiming)
        {
            RemainingText = tick.RemainingText;
            EtaText = tick.EtaText;
        }

        if (VersionEntries.Count > 0)
        {
            VersionEntries[^1].RefreshLiveDuration();
        }
    }

    private void OnStatusChanged(object? _, BruteForceStatusChangedEventArgs e)
    {
        _uiDispatcher.Invoke(() =>
        {
            if (e.NewStatus == OperationStatus.Completed)
            {
                ProgressMessage = e.CompletionStatus switch
                {
                    OperationCompletionStatus.Success => "Completed successfully!",
                    OperationCompletionStatus.Error => "Failed.",
                    OperationCompletionStatus.Cancelled => "Cancelled.",
                    _ => "Completed."
                };

                LastRunSucceeded = e.CompletionStatus == OperationCompletionStatus.Success;

                ShowTimestampFailureWarningIfAny();
            }
        });
    }

    private void OnTimestampPreservationFailed(object? _, TimestampPreservationFailedEventArgs e)
    {
        // The library already logs a Warning via its logger (routed through
        // OnLogMessage). Track the failure here so we can show a single
        // summary MessageBox when the run finishes.
        _timestampFailures.Add(e);
    }

    private void ShowTimestampFailureWarningIfAny()
    {
        if (_timestampFailures.Count == 0)
        {
            return;
        }

        const int MaxFilesToList = 10;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Could not copy the source file's modification time onto the working copy " +
                      "for the following file(s):");
        sb.AppendLine();

        int shown = Math.Min(_timestampFailures.Count, MaxFilesToList);
        for (int i = 0; i < shown; i++)
        {
            TimestampPreservationFailedEventArgs f = _timestampFailures[i];
            sb.AppendLine($"  • {f.DestinationPath}");
            sb.AppendLine($"      ({f.ErrorMessage})");
        }

        if (_timestampFailures.Count > MaxFilesToList)
        {
            sb.AppendLine($"  … and {_timestampFailures.Count - MaxFilesToList} more.");
        }

        sb.AppendLine();
        sb.AppendLine("WinRAR will pack these files with the copy time instead of the original " +
                      "modification time, so the resulting RAR's File Time (DOS) may differ " +
                      "from the original release.");

        _fileDialog.ShowWarning("Timestamp Preservation Failed", sb.ToString());
    }

    private void OnLogMessage(object? _, LogEventArgs e) => _uiDispatcher.Invoke(() => AppendLog(e.Target, e.Message));

    private void Log(LogTarget target, string message) => AppendLog(target, message);

    private void AppendLog(LogTarget target, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss} {message}";
        switch (target)
        {
            case LogTarget.Phase1:
                Phase1Log = Phase1Log.Length == 0 ? line : Phase1Log + Environment.NewLine + line;
                break;
            case LogTarget.Phase2:
                Phase2Log = Phase2Log.Length == 0 ? line : Phase2Log + Environment.NewLine + line;
                break;
            default:
                SystemLog = SystemLog.Length == 0 ? line : SystemLog + Environment.NewLine + line;
                break;
        }
    }

    // ── SRR Import Helpers ──

    private void SetRARVersionsFromSRR(SRRFile srr)
    {
        if (!srr.RARVersion.HasValue)
        {
            return;
        }

        int unpVer = srr.RARVersion.Value;
        Version2 = Version3 = Version4 = Version5 = Version6 = Version7 = false;

        if (unpVer >= 70)
        {
            Version7 = true;
            Log(LogTarget.System, "RAR versions: 7.x");
        }
        else if (unpVer >= 50)
        {
            Version5 = true;
            Version6 = true;
            Log(LogTarget.System, "RAR versions: 5.x, 6.x");
        }
        else if (srr.DictionarySize.HasValue && srr.DictionarySize.Value > 4096)
        {
            Version5 = true;
            Version6 = true;
            Log(LogTarget.System, $"Large dictionary ({srr.DictionarySize.Value} KB) — RAR 5.x, 6.x");
        }
        else
        {
            bool isRar2 = unpVer <= 29;
            bool isRar3 = unpVer is >= 20 and <= 36;
            bool isRar4 = unpVer is >= 26 and <= 36;

            if (srr.HasFirstVolumeFlag == true || srr.HasUnicodeNames == true)
            {
                isRar2 = false;
            }

            if (unpVer == 36)
            {
                isRar2 = false;
                isRar3 = true;
                isRar4 = true;
            }

            Version2 = isRar2;
            Version3 = isRar3;
            Version4 = isRar4;
            Version5 = true; // Can create RAR4 format with -ma4
            Version6 = true;

            List<string> selected = [];
            if (isRar2)
            {
                selected.Add("2.x");
            }

            if (isRar3)
            {
                selected.Add("3.x");
            }

            if (isRar4)
            {
                selected.Add("4.x");
            }

            selected.Add("5.x");
            selected.Add("6.x");
            Log(LogTarget.System, $"RAR versions: {string.Join(", ", selected)}");
        }
    }

    private static void SetTimestampFlags(TimestampPrecision precision,
        Action<bool> set0, Action<bool> set1, Action<bool> set2, Action<bool> set3, Action<bool> set4)
    {
        set0(precision == TimestampPrecision.NotSaved);
        set1(precision == TimestampPrecision.OneSecond);
        set2(precision == TimestampPrecision.HighPrecision1);
        set3(precision == TimestampPrecision.HighPrecision2);
        set4(precision == TimestampPrecision.NtfsPrecision);
    }

    /// <summary>
    /// Applies the partial switch diff produced by <see cref="SrrSwitchMapper"/> onto the bound
    /// option toggles, emitting the same log lines in the same order as the original inline mapping.
    /// Groups left null by the mapper (no SRR information) are skipped, so their toggles keep their
    /// current values rather than being reset.
    /// </summary>
    private void ApplySwitchDiff(SrrSwitchMapper.SwitchDiff diff)
    {
        // Compression method
        if (diff.Compression is { } compression)
        {
            int method = compression.Method;
            SwitchM0 = method == 0;
            SwitchM1 = method == 1;
            SwitchM2 = method == 2;
            SwitchM3 = method == 3;
            SwitchM4 = method == 4;
            SwitchM5 = method == 5;
            Log(LogTarget.System, $"Compression: -m{method} ({compression.LogName})");
        }

        // Dictionary size
        if (diff.Dictionary is { } dictionary)
        {
            SwitchMD64K = SwitchMD128K = SwitchMD256K = SwitchMD512K = false;
            SwitchMD1024K = SwitchMD2048K = SwitchMD4096K = false;
            SwitchMD8M = SwitchMD16M = SwitchMD32M = SwitchMD64M = false;
            SwitchMD128M = SwitchMD256M = SwitchMD512M = SwitchMD1G = false;

            switch (dictionary.Switch)
            {
                case SrrSwitchMapper.DictionarySwitch.MD64K:
                    SwitchMD64K = true;
                    break;
                case SrrSwitchMapper.DictionarySwitch.MD128K:
                    SwitchMD128K = true;
                    break;
                case SrrSwitchMapper.DictionarySwitch.MD256K:
                    SwitchMD256K = true;
                    break;
                case SrrSwitchMapper.DictionarySwitch.MD512K:
                    SwitchMD512K = true;
                    break;
                case SrrSwitchMapper.DictionarySwitch.MD1024K:
                    SwitchMD1024K = true;
                    break;
                case SrrSwitchMapper.DictionarySwitch.MD2048K:
                    SwitchMD2048K = true;
                    break;
                case SrrSwitchMapper.DictionarySwitch.MD4096K:
                    SwitchMD4096K = true;
                    break;
            }

            Log(LogTarget.System, $"Dictionary: {dictionary.SizeKb} KB");
        }

        // Solid archive
        if (diff.SwitchS is { } switchS)
        {
            SwitchS = switchS;
        }

        if (diff.SwitchSDash is { } switchSDash)
        {
            SwitchSDash = switchSDash;
        }

        if (diff.SwitchS is { } || diff.SwitchSDash is { })
        {
            Log(LogTarget.System, SwitchS ? "Solid archiving: -s" : "Solid archiving: -s-");
        }

        // Archive format
        if (diff.Format is { } format)
        {
            SwitchMA4 = format.MA4;
            SwitchMA5 = format.MA5;
            Log(LogTarget.System, format.LogLine);
        }
    }

    private void ApplyVolumeSize(long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            return;
        }

        SwitchV = true;

        if (sizeBytes % 1_000_000_000 == 0)
        {
            VolumeSize = (sizeBytes / 1_000_000_000).ToString();
            VolumeSizeUnitIndex = 3;
        }
        else if (sizeBytes % 1_000_000 == 0)
        {
            VolumeSize = (sizeBytes / 1_000_000).ToString();
            VolumeSizeUnitIndex = 2;
        }
        else if (sizeBytes % 1_000 == 0)
        {
            VolumeSize = (sizeBytes / 1_000).ToString();
            VolumeSizeUnitIndex = 1;
        }
        else if (sizeBytes % (1024L * 1024 * 1024) == 0)
        {
            VolumeSize = (sizeBytes / (1024L * 1024 * 1024)).ToString();
            VolumeSizeUnitIndex = 6;
        }
        else if (sizeBytes % (1024L * 1024) == 0)
        {
            VolumeSize = (sizeBytes / (1024L * 1024)).ToString();
            VolumeSizeUnitIndex = 5;
        }
        else if (sizeBytes % 1024 == 0)
        {
            VolumeSize = (sizeBytes / 1024).ToString();
            VolumeSizeUnitIndex = 4;
        }
        else
        {
            VolumeSize = sizeBytes.ToString();
            VolumeSizeUnitIndex = 0;
        }

        Log(LogTarget.System, $"Volume size: {VolumeSize} {VolumeSizeUnits[VolumeSizeUnitIndex]}");
    }

    private void TryExtractStoredSFV(string srrFilePath, SRRFile srr)
    {
        if (srr.StoredFiles.Count == 0)
        {
            return;
        }

        // Delete the SFV temp from a previous import before starting a new one so at most one
        // is ever on disk. If the current VerificationPath points into that dir (i.e. it was the
        // previous import's auto-extracted SFV, not a user-chosen path), clear it too so it never
        // dangles at a file we just deleted.
        if (_sfvTempDir is not null
            && VerificationPath.StartsWith(_sfvTempDir, StringComparison.Ordinal))
        {
            VerificationPath = string.Empty;
        }

        _tempDir.Cleanup(_sfvTempDir);
        _sfvTempDir = null;

        string? tempDir = null;
        try
        {
            tempDir = _tempDir.CreateTempDirectory();

            string? extracted = srr.ExtractStoredFile(srrFilePath, tempDir,
                fileName => Path.GetExtension(fileName).Equals(".sfv", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(extracted))
            {
                _sfvTempDir = tempDir;
                VerificationPath = extracted;
                Log(LogTarget.System, $"Stored SFV extracted: {Path.GetFileName(extracted)}");
            }
            else
            {
                // Nothing extracted — don't leave the empty temp dir behind.
                _tempDir.Cleanup(tempDir);
            }
        }
        catch (Exception ex)
        {
            _tempDir.Cleanup(tempDir);
            Log(LogTarget.System, $"Failed to extract stored SFV: {ex.Message}");
        }
    }

    /// <summary>
    /// Releases the temp directory holding the last import's extracted SFV. Called on app shutdown.
    /// </summary>
    public void Cleanup()
    {
        _tempDir.Cleanup(_sfvTempDir);
        _sfvTempDir = null;
    }
}
