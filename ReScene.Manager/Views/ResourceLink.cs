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
    /// <summary>
    /// Test seam: swap for a fake <see cref="ILauncherService"/> so a
    /// test can raise a REAL Click/UIA Invoke on a link button and assert the invocation actually
    /// fired, without a real OS browser launch as a side effect. Defaults to the real,
    /// production launcher; every one of this class's 3 callers is unaffected in production,
    /// since none of them ever touch this property.
    /// </summary>
    internal static ILauncherService Launcher { get; set; } = new SystemLauncherService();

    internal static void OpenFromTag(object? sender)
    {
        if (sender is Button { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            // OpenUrl already swallows launch failures; the try/catch is belt-and-braces so a
            // click can never surface an unhandled exception.
            try
            {
                Launcher.OpenUrl(url);
            }
            catch
            {
                // Best-effort: opening a link should never crash the app.
            }
        }
    }
}
