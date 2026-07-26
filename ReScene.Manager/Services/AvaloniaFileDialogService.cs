using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ReScene.App.Core.Services;
using ReScene.Manager.Views;

namespace ReScene.Manager.Services;

/// <summary>
/// Avalonia implementation of <see cref="IFileDialogService"/>. File/folder pickers go through the
/// active window's <see cref="TopLevel.StorageProvider"/>; message dialogs use the custom
/// <see cref="MessageDialog"/>/<see cref="PromptDialog"/> windows (Avalonia has no MessageBox).
///
/// The synchronous <see cref="IFileDialogService"/> members (<see cref="ShowError"/>,
/// <see cref="ShowWarning"/>, <see cref="ShowInfo"/>, <see cref="Confirm"/>) have no native
/// counterpart — Avalonia's <c>ShowDialog</c> is async only — so they are made synchronous by
/// pumping a nested <see cref="DispatcherFrame"/> until the dialog closes (see <see cref="Pump"/>).
/// When there is no active window (headless tests, or before the main window is shown) the sync
/// members are guarded off: the void methods no-op and <see cref="Confirm"/> returns
/// <see langword="false"/>, so nothing blocks and there is no deadlock.
/// </summary>
public sealed class AvaloniaFileDialogService : IFileDialogService
{
    private readonly Func<Window?> _activeWindow;

    /// <param name="activeWindow">
    /// Resolves the window that owns dialogs and provides the <c>StorageProvider</c>. May return
    /// <see langword="null"/> before the main window exists or in headless contexts.
    /// </param>
    public AvaloniaFileDialogService(Func<Window?> activeWindow)
    {
        ArgumentNullException.ThrowIfNull(activeWindow);
        _activeWindow = activeWindow;
    }

    private IStorageProvider? GetStorageProvider() => TopLevel.GetTopLevel(_activeWindow())?.StorageProvider;

    /// <summary>
    /// Resolves the directory an open/save picker should start in from a field's current value:
    /// an existing directory is itself the answer; anything else (an existing file, or a stale
    /// leaf that was renamed/deleted) falls back to its containing directory when that exists;
    /// otherwise <see langword="null"/> — the picker keeps the platform default. Never throws:
    /// Browse must work no matter what garbage sits in the field (Exists checks and
    /// <see cref="Path.GetDirectoryName(string)"/> swallow invalid input rather than throwing).
    /// </summary>
    internal static string? ResolveStartDirectory(string? initialPath)
    {
        if (string.IsNullOrWhiteSpace(initialPath))
        {
            return null;
        }

        if (Directory.Exists(initialPath))
        {
            return initialPath;
        }

        string? parent = null;
        try
        {
            parent = Path.GetDirectoryName(initialPath);
        }
        catch (PathTooLongException)
        {
            // Unreachable on .NET 10 (GetDirectoryName returns empty for over-long garbage — the
            // IsNullOrEmpty check below is the real guard); kept as belt-and-braces for other
            // runtimes/framework changes.
        }

        return !string.IsNullOrEmpty(parent) && Directory.Exists(parent) ? parent : null;
    }

    /// <summary>Maps a field value to the picker's <c>SuggestedStartLocation</c> (null = default).</summary>
    private static async Task<IStorageFolder?> GetStartLocationAsync(IStorageProvider storage, string? initialPath)
    {
        string? directory = ResolveStartDirectory(initialPath);
        return directory is null ? null : await storage.TryGetFolderFromPathAsync(directory);
    }

    public async Task<string?> OpenFileAsync(string title, IReadOnlyList<string> filters, string? initialPath = null)
    {
        IStorageProvider? storage = GetStorageProvider();
        if (storage is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = FilePickerFilters.ToFileTypes(filters),
            SuggestedStartLocation = await GetStartLocationAsync(storage, initialPath),
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<string> filters, string? initialPath = null)
    {
        IStorageProvider? storage = GetStorageProvider();
        if (storage is null)
        {
            return [];
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = FilePickerFilters.ToFileTypes(filters),
            SuggestedStartLocation = await GetStartLocationAsync(storage, initialPath),
        });

        return files
            .Select(file => file.TryGetLocalPath())
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path => path!)
            .ToList();
    }

    public async Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<string> filters, string? defaultFileName = null)
    {
        IStorageProvider? storage = GetStorageProvider();
        if (storage is null)
        {
            return null;
        }

        var options = new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = defaultExtension,
            SuggestedFileName = Path.GetFileName(defaultFileName ?? string.Empty),
            FileTypeChoices = FilePickerFilters.ToFileTypes(filters),
        };

        // Callers may suggest a full path; open in that folder but show only the file name.
        options.SuggestedStartLocation = await GetStartLocationAsync(storage, defaultFileName);

        IStorageFile? file = await storage.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }

    public async Task<string?> OpenFolderAsync(string title, string? initialPath = null)
    {
        IStorageProvider? storage = GetStorageProvider();
        if (storage is null)
        {
            return null;
        }

        IReadOnlyList<IStorageFolder> folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await GetStartLocationAsync(storage, initialPath),
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<bool> ShowConfirmAsync(string title, string message)
    {
        Window? owner = _activeWindow();
        if (owner is null)
        {
            return false;
        }

        var dialog = new MessageDialog(DialogSeverity.Confirm, title, message);
        return await dialog.ShowDialog<bool>(owner);
    }

    public async Task<string?> PromptForTextAsync(string title, string message, string initialValue)
    {
        Window? owner = _activeWindow();
        if (owner is null)
        {
            return null;
        }

        var dialog = new PromptDialog(title, message, initialValue);
        return await dialog.ShowDialog<string?>(owner);
    }

    /// <inheritdoc />
    public void ShowError(string title, string message) => ShowMessageSync(DialogSeverity.Error, title, message);

    /// <inheritdoc />
    public void ShowWarning(string title, string message) => ShowMessageSync(DialogSeverity.Warning, title, message);

    /// <inheritdoc />
    public void ShowInfo(string title, string message) => ShowMessageSync(DialogSeverity.Info, title, message);

    /// <inheritdoc />
    public bool Confirm(string title, string message)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return Dispatcher.UIThread.Invoke(() => Confirm(title, message));
        }

        Window? owner = _activeWindow();
        if (owner is null)
        {
            // Headless / no window yet: never block, mirror "Cancel".
            return false;
        }

        var dialog = new MessageDialog(DialogSeverity.Confirm, title, message);
        Task<bool> showTask = dialog.ShowDialog<bool>(owner);
        Pump(showTask);
        return showTask.GetAwaiter().GetResult();
    }

    private void ShowMessageSync(DialogSeverity severity, string title, string message)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(() => ShowMessageSync(severity, title, message));
            return;
        }

        Window? owner = _activeWindow();
        if (owner is null)
        {
            // Headless / no window yet: no-op so callers never deadlock without a UI.
            return;
        }

        var dialog = new MessageDialog(severity, title, message);
        Pump(dialog.ShowDialog(owner));
    }

    /// <summary>
    /// Runs a nested dispatcher loop on the UI thread until <paramref name="dialogTask"/> completes,
    /// turning Avalonia's async <c>ShowDialog</c> into a synchronous call. Setting
    /// <see cref="DispatcherFrame.Continue"/> to <see langword="false"/> cancels the frame's inner
    /// run-loop token, so <see cref="Dispatcher.PushFrame"/> returns as soon as the dialog closes.
    /// Must be called on the UI thread (the caller guarantees this).
    /// </summary>
    private static void Pump(Task dialogTask)
    {
        var frame = new DispatcherFrame();
        _ = dialogTask.ContinueWith(
            static (_, state) => ((DispatcherFrame)state!).Continue = false,
            frame,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        Dispatcher.UIThread.PushFrame(frame);
    }
}
