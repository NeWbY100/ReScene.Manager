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
        AppBuilder builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
#if AGENT_BRIDGE
        builder = builder.WithAgentBridge(o => o.EnableMutations = true); // avalonia-agent-mcp: local Debug only, writes enabled
#endif
        return builder;
    }
}
