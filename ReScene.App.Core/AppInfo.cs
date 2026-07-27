namespace ReScene.App.Core;

/// <summary>
/// The user-facing application name. A head may override it once at startup (like
/// AppDataConfig); the default matches the sole head, ReScene.Manager. (The WPF-era
/// "ReScene.NET" default died with the deleted WPF head.)
/// </summary>
public static class AppInfo
{
    public static string DisplayName { get; set; } = "ReScene Manager";
}
