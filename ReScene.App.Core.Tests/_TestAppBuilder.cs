using Avalonia;
using Avalonia.Headless;

[assembly: Avalonia.Headless.AvaloniaTestApplication(typeof(ReScene.App.Core.Tests.TestAppBuilder))]

namespace ReScene.App.Core.Tests;

public sealed class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Avalonia.Application>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
