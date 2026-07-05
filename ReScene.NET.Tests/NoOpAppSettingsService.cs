using ReScene.NET.Models;
using ReScene.NET.Services;

namespace ReScene.NET.Tests;

/// <summary>
/// Reusable no-op <see cref="IAppSettingsService"/> double: <see cref="Load"/> returns fresh
/// defaults, <see cref="Save"/> does nothing, and <see cref="Changed"/> is inert.
/// </summary>
public class NoOpAppSettingsService : IAppSettingsService
{
    public event EventHandler? Changed { add { } remove { } }
    public virtual AppSettings Load() => new();
    public virtual void Save(AppSettings settings) { }
}
