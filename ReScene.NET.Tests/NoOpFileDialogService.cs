using ReScene.App.Core.Models;

using ReScene.App.Core.Services;
namespace ReScene.NET.Tests;

/// <summary>
/// Reusable no-op <see cref="IFileDialogService"/> double. Every member returns an empty/cancelled
/// result (null, empty list), and both confirmation seams default to <c>false</c>. Tests derive from
/// this and <c>override</c> only the members they actually exercise.
/// </summary>
public class NoOpFileDialogService : IFileDialogService
{
    public virtual Task<string?> OpenFileAsync(string title, IReadOnlyList<string> filters) => Task.FromResult<string?>(null);
    public virtual Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<string> filters) => Task.FromResult<IReadOnlyList<string>>([]);
    public virtual Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<string> filters, string? defaultFileName = null) => Task.FromResult<string?>(null);
    public virtual Task<string?> OpenFolderAsync(string title) => Task.FromResult<string?>(null);
    public virtual Task<bool> ShowConfirmAsync(string title, string message) => Task.FromResult(false);
    public virtual Task<string?> PromptForTextAsync(string title, string message, string initialValue) => Task.FromResult<string?>(null);
    public virtual void ShowError(string title, string message) { }
    public virtual void ShowWarning(string title, string message) { }
    public virtual void ShowInfo(string title, string message) { }
    public virtual bool Confirm(string title, string message) => false;
}
