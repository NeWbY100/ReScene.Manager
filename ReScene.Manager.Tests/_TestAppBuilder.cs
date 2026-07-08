using Avalonia;
using Avalonia.Headless;
using ReScene.Manager;

[assembly: AvaloniaTestApplication(typeof(ReScene.Manager.Tests.TestAppBuilder))]

namespace ReScene.Manager.Tests;

/// <summary>
/// Configures the headless Avalonia application used to run <c>[AvaloniaFact]</c> tests.
/// Boots the real <see cref="App"/> (not a bare <see cref="Application"/>) so Tokens.axaml and the
/// Fluent theme are merged into <c>Application.Current.Resources</c> exactly as in production —
/// required for tests that assert on <c>DynamicResource</c>-backed brushes.
/// </summary>
public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
