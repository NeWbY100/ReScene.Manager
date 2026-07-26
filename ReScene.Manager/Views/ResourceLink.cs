using Avalonia.Controls;
using ReScene.App.Core.Services;

namespace ReScene.Manager.Views;

/// <summary>
/// Shared behavior for the WinRAR-pack download links shown on three surfaces (the RAR Reconstructor
/// tab header, the Beginner Reconstruct wizard's step 1, and the Settings window's RAR Reconstruction
/// tab): opens the URL riding on the link Button's Tag in the OS default browser. One implementation
/// so the surfaces cannot drift behaviorally — their Click handlers all delegate here.
/// </summary>
internal static class ResourceLink
{
    internal static void OpenFromTag(object? sender)
    {
        if (sender is Button { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            // OpenUrl already swallows launch failures; the try/catch is belt-and-braces so a
            // click can never surface an unhandled exception.
            try
            {
                new SystemLauncherService().OpenUrl(url);
            }
            catch
            {
                // Best-effort: opening a link should never crash the app.
            }
        }
    }
}
