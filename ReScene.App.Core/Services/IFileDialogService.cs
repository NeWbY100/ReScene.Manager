namespace ReScene.App.Core.Services;

public interface IFileDialogService
{
    // initialPath: the bound field's current value (file or folder, possibly stale or blank).
    // The picker opens in the nearest existing directory it implies; null keeps the platform
    // default. Windows' own last-folder memory only applies when no initialPath resolves —
    // a populated field deliberately wins, so the picker start always matches visible state
    // (WCAG 3.3.7 Redundant Entry adjacency: never make the user re-navigate a path they
    // already provided).
    public Task<string?> OpenFileAsync(string title, IReadOnlyList<string> filters, string? initialPath = null);
    public Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<string> filters, string? initialPath = null);
    public Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<string> filters, string? defaultFileName = null);
    public Task<string?> OpenFolderAsync(string title, string? initialPath = null);
    public Task<bool> ShowConfirmAsync(string title, string message);
    public Task<string?> PromptForTextAsync(string title, string message, string initialValue);

    /// <summary>Shows a synchronous error dialog (OK button, error icon).</summary>
    public void ShowError(string title, string message);

    /// <summary>Shows a synchronous warning dialog (OK button, warning icon).</summary>
    public void ShowWarning(string title, string message);

    /// <summary>Shows a synchronous informational dialog (OK button, information icon).</summary>
    public void ShowInfo(string title, string message);

    /// <summary>Shows a synchronous OK/Cancel confirmation dialog; returns true when OK is chosen.</summary>
    public bool Confirm(string title, string message);
}
