using System.Windows.Threading;

using ReScene.App.Core.Services;
namespace ReScene.NET.Services;

/// <summary>
/// <see cref="IUiTimer"/> backed by a <see cref="DispatcherTimer"/>, so ticks are raised on the
/// WPF UI thread.
/// </summary>
public sealed class WpfUiTimer : IUiTimer
{
    private readonly DispatcherTimer _timer;

    public WpfUiTimer(TimeSpan interval, Action onTick)
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
