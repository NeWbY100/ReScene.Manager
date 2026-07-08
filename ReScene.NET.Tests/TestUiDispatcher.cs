using ReScene.App.Core.Services;

namespace ReScene.NET.Tests;

/// <summary>
/// Inline <see cref="IUiDispatcher"/> for tests: runs every action synchronously on the calling
/// thread (matching WpfDispatcher's behaviour when no WPF Application is running).
/// </summary>
public sealed class TestUiDispatcher : IUiDispatcher
{
    public void Invoke(Action action) => action();
    public void Post(Action action) => action();
    public void Post(Action action, UiDispatcherPriority priority) => action();
    public bool CheckAccess() => true;
}
