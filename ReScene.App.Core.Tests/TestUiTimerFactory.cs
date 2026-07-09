using ReScene.App.Core.Services;

namespace ReScene.App.Core.Tests;

/// <summary>
/// No-op <see cref="IUiTimerFactory"/> for tests: the created timer never ticks (matching a
/// <c>DispatcherTimer</c> constructed without a running dispatcher, as in the pre-seam code).
/// </summary>
public sealed class TestUiTimerFactory : IUiTimerFactory
{
    public IUiTimer Create(TimeSpan interval, Action onTick) => new NoOpTimer();

    private sealed class NoOpTimer : IUiTimer
    {
        public void Start() { }
        public void Stop() { }
    }
}
