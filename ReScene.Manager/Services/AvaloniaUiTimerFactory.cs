using ReScene.App.Core.Services;

namespace ReScene.Manager.Services;

/// <summary>Avalonia implementation of <see cref="IUiTimerFactory"/>; creates <see cref="AvaloniaUiTimer"/>s.</summary>
public sealed class AvaloniaUiTimerFactory : IUiTimerFactory
{
    /// <inheritdoc />
    public IUiTimer Create(TimeSpan interval, Action onTick) => new AvaloniaUiTimer(interval, onTick);
}
