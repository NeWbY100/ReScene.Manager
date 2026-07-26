using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

using ReScene.App.Core.Helpers;
using ReScene.App.Core.Services;
namespace ReScene.App.Core.ViewModels;

/// <summary>
/// Base for the single-log "operation" ViewModels (SRR/SRS creation, SRS reconstruction,
/// sample restore). Centralizes the log collection, the progress display properties, the
/// log helper, the save-log dialog flow, and the cancellation-token lifecycle. Per-command
/// busy flags and the finally-cleanup remain in each derived ViewModel because they differ.
/// </summary>
public abstract partial class OperationViewModelBase : ViewModelBase
{
    /// <summary>
    /// Backing cancellation source for the current operation. Derived ViewModels assign a
    /// fresh source when starting and dispose it in their own finally block; they read
    /// <c>_cts.Token</c> directly. Use <see cref="Cancel"/> to request cancellation safely.
    /// </summary>
    protected CancellationTokenSource? _cts;

    // Progress
    [ObservableProperty]
    public partial bool ShowProgress { get; set; }

    [ObservableProperty]
    public partial int ProgressPercent { get; set; }

    [ObservableProperty]
    public partial string ProgressMessage { get; set; } = string.Empty;

    // Log
    public ObservableCollection<string> LogEntries { get; } = [];

    /// <summary>
    /// The last Save-log outcome ("Log saved to …", "Could not save …", "Nothing to save …"),
    /// bound by every surface to a visible TextBlock with <c>AutomationProperties.LiveSetting=
    /// Polite</c> so the outcome is announced to screen readers (4.1.3) — the log list itself is
    /// deliberately not a live region. Empty when no save was attempted or the dialog was
    /// cancelled (the cancel is its own feedback; a stale success line would mislead).
    /// </summary>
    [ObservableProperty]
    public partial string SaveLogAnnouncement { get; set; } = string.Empty;

    /// <summary>
    /// Appends a timestamped entry to <see cref="LogEntries"/>.
    /// </summary>
    protected void Log(string message) => AppendLogEntry(LogEntries, message);

    /// <summary>
    /// Requests cancellation of the running operation. Guarded against the
    /// Cancel-vs-dispose race: a token source already disposed by the operation's
    /// finally block is ignored rather than throwing.
    /// </summary>
    protected void Cancel()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The operation already completed and disposed its token source.
        }
    }

    /// <summary>
    /// Prompts for a path and writes <see cref="LogEntries"/> to it. No-op when the log is
    /// empty or the user cancels the dialog. Errors are logged rather than thrown.
    /// </summary>
    protected async Task SaveLogToFileAsync(IFileDialogService fileDialog)
    {
        // Cleared FIRST so every outcome below is a genuine empty-to-message transition: both
        // CommunityToolkit's setter and Avalonia's TextBlock.Text suppress equal-value changes,
        // so a repeat save to the same file would otherwise announce nothing. Do not simplify
        // this away.
        SaveLogAnnouncement = string.Empty;

        if (LogEntries.Count == 0)
        {
            // The button is always enabled (a disabled button could not explain itself), so the
            // empty press must say why nothing happened.
            SaveLogAnnouncement = SaveLogMessages.Empty;
            return;
        }

        string? path = await fileDialog.SaveFileAsync(
            "Save log", ".txt", ["Text Files|*.txt"], "log.txt");

        if (path is null)
        {
            return;
        }

        try
        {
            // Snapshot on the UI thread before exporting: a run may still be appending via the batched
            // drain while the exporter enumerates across awaits — writing the live collection can throw
            // "Collection was modified" mid-write and leave a partial file.
            string[] snapshot = [.. LogEntries];
            await LogExporter.SaveAsync(snapshot, path);
            Log($"Log saved to {Path.GetFileName(path)}");
            SaveLogAnnouncement = SaveLogMessages.Saved(path);
        }
        catch (Exception ex)
        {
            Log($"ERROR saving log: {ex.Message}");
            SaveLogAnnouncement = SaveLogMessages.Failed(ex.Message);
        }
    }
}
