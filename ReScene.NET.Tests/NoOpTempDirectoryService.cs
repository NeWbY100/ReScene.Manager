using ReScene.App.Core.Models;
using ReScene.NET.Services;

namespace ReScene.NET.Tests;

/// <summary>
/// Reusable no-op <see cref="ITempDirectoryService"/> double. <see cref="CreateTempDirectory"/>
/// throws by default — tests that need a real temp directory override it; <see cref="Cleanup"/>
/// is a no-op.
/// </summary>
public class NoOpTempDirectoryService : ITempDirectoryService
{
    public virtual string CreateTempDirectory() => throw new InvalidOperationException("Temp dir should not be created in unit tests.");
    public virtual void Cleanup(string? tempDir) { }
}
