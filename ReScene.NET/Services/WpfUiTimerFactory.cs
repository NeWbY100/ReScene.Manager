using ReScene.App.Core.Services;

namespace ReScene.NET.Services;

/// <summary>WPF implementation of <see cref="IUiTimerFactory"/>; creates <see cref="WpfUiTimer"/>s.</summary>
public sealed class WpfUiTimerFactory : IUiTimerFactory
{
    /// <inheritdoc />
    public IUiTimer Create(TimeSpan interval, Action onTick) => new WpfUiTimer(interval, onTick);
}
