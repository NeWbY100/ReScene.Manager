using Avalonia.Headless.XUnit;
using ReScene.App.Core.Services;
using ReScene.Manager.Services;

namespace ReScene.Manager.Tests;

/// <summary>
/// Tests for <see cref="AvaloniaUiTimer"/> / <see cref="AvaloniaUiTimerFactory"/>. Ticking is
/// wall-clock and UI-loop driven, so — per the brief — these assert construction and Start/Stop
/// safety rather than an actual timer fire, to stay robust.
/// </summary>
public class AvaloniaUiTimerTests
{
    [AvaloniaFact]
    public void Factory_CreatesTimer_ThatStartsAndStopsWithoutThrowing()
    {
        var factory = new AvaloniaUiTimerFactory();

        IUiTimer timer = factory.Create(TimeSpan.FromMilliseconds(20), () => { });

        Assert.NotNull(timer);
        timer.Start();
        timer.Stop();
        timer.Stop(); // Stop is idempotent / safe when already stopped.
    }

    [Fact]
    public void Constructor_NullOnTick_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new AvaloniaUiTimer(TimeSpan.FromSeconds(1), null!));
}
