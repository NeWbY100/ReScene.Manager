using ReScene.App.Core.Models;
using ReScene.NET.Services;

using ReScene.App.Core.Services;
namespace ReScene.NET.Tests;

/// <summary>
/// Configurable <see cref="IAppSettingsService"/> double: <see cref="Load"/> returns the current
/// <see cref="Settings"/>, and <see cref="RaiseChanged"/> fires <see cref="Changed"/> so tests can
/// simulate a settings save.
/// </summary>
public sealed class FakeAppSettingsService : IAppSettingsService
{
    public AppSettings Settings { get; set; } = new();
    public event EventHandler? Changed;
    public AppSettings Load() => Settings;
    public void Save(AppSettings settings) { Settings = settings; Changed?.Invoke(this, EventArgs.Empty); }
    public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
