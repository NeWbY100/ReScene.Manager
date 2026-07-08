using System.Diagnostics;

namespace ReScene.App.Core.Services;

/// <summary>
/// Cross-platform <see cref="ILauncherService"/> backed by <see cref="Process"/>. Mirrors the
/// platform-detection style of <c>RarExecutable</c>: branch on <see cref="OperatingSystem"/>
/// rather than assuming Windows. Launch failures are swallowed — callers treat opening a URL or
/// revealing a folder as best-effort, never a hard failure.
/// </summary>
public sealed class SystemLauncherService : ILauncherService
{
    public void OpenUrl(string url)
    {
        try
        {
            // UseShellExecute=true is already cross-platform for URLs (Windows/Linux/macOS all
            // hand the URL to the default browser this way).
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            // Best-effort: no default browser configured, launch denied, etc.
        }
    }

    public void RevealPath(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Explorer opens folders (and selects files) via ShellExecute.
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", path);
            }
            else
            {
                // Linux (and other Unix-likes): ShellExecute on a directory path does not open a
                // file manager the way it does on Windows — it tries to execute the directory.
                Process.Start("xdg-open", path);
            }
        }
        catch
        {
            // Best-effort: no file manager registered, launch denied, etc.
        }
    }
}
