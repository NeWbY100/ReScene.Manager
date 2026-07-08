namespace ReScene.App.Core.Services;

/// <summary>Opens URLs and reveals files/folders in the OS default handler / file manager.</summary>
public interface ILauncherService
{
    /// <summary>Opens <paramref name="url"/> in the OS default browser/handler.</summary>
    void OpenUrl(string url);

    /// <summary>Opens the folder (or the file's containing folder) at <paramref name="path"/> in the OS file manager.</summary>
    void RevealPath(string path);
}
