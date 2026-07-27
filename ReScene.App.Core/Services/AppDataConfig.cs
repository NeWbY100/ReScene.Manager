namespace ReScene.App.Core.Services;

/// <summary>
/// The %LOCALAPPDATA% subfolder used for all persisted JSON. A head may override this once at
/// startup before any settings access; the default matches the sole head, ReScene.Manager.
/// (Deliberately NOT the WPF era's "ReScene.NET" folder — the rebrand was a fresh start with
/// no settings migration, and the two folders must never collide.)
/// </summary>
public static class AppDataConfig
{
    public static string FolderName { get; set; } = "ReScene.Manager";
}
