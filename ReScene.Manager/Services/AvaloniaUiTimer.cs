using Avalonia.Threading;
using ReScene.App.Core.Services;

namespace ReScene.Manager.Services;

/// <summary>
/// <see cref="IUiTimer"/> backed by an Avalonia <see cref="DispatcherTimer"/>, so ticks are raised
/// on the UI thread. Mirrors <c>WpfUiTimer</c>.
/// </summary>
public sealed class AvaloniaUiTimer : IUiTimer
{
    private readonly DispatcherTimer _timer;

    public AvaloniaUiTimer(TimeSpan interval, Action onTick)
    {
        ArgumentNullException.ThrowIfNull(onTick);

        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += (_, _) => onTick();
    }

    /// <inheritdoc />
    public void Start() => _timer.Start();

    /// <inheritdoc />
    public void Stop() => _timer.Stop();
}
