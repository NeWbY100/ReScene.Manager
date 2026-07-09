namespace ReScene.App.Core;

/// <summary>
/// The user-facing application name. Each head sets it once at startup (like AppDataConfig):
/// the WPF app keeps "ReScene.NET"; ReScene.Manager sets "ReScene Manager" for the rebrand.
/// </summary>
public static class AppInfo
{
    public static string DisplayName { get; set; } = "ReScene.NET";
}
