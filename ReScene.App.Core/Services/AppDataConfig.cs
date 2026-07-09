namespace ReScene.App.Core.Services;

/// <summary>
/// The %LOCALAPPDATA% subfolder used for all persisted JSON. Each app head sets this once at
/// startup before any settings access: the WPF app keeps the default; ReScene.Manager uses its
/// own folder so the two apps' settings never collide (fresh start, no migration).
/// </summary>
public static class AppDataConfig
{
    public static string FolderName { get; set; } = "ReScene.NET";
}
