using Avalonia;
#if AGENT_BRIDGE
using AvaDevBridge;
#endif

namespace ReScene.Manager;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
#if AGENT_BRIDGE
        builder = builder.WithAgentBridge(); // avalonia-agent-mcp: local Debug only
#endif
        return builder;
    }
}
