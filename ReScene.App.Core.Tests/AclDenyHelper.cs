using System.Diagnostics;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Forces a real path-resolution throw by making a directory present-but-un-inspectable: on Windows an
/// <c>icacls</c> deny of the current user, elsewhere by clearing the Unix mode bits. This is the same
/// seam the reconstruction path-guard tests use to drive <see cref="UnauthorizedAccessException"/> out
/// of <c>ReconstructionPathGuard.ResolveReal</c>; it lives here so the relocator and run-loop tests can
/// share one copy rather than each carrying their own. (Three other test classes predate this helper and
/// still hold private copies; they were left untouched to keep this change minimal.)
/// </summary>
internal static class AclDenyHelper
{
    /// <summary>
    /// Denies the current user access to <paramref name="path"/> itself so a walk through it fails
    /// closed. Callers MUST pair this with <see cref="RestoreAccess"/> in a <c>finally</c> so temp-dir
    /// cleanup can proceed.
    /// </summary>
    public static void DenyAccess(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            RunIcacls(path, "/deny", $"{Environment.UserName}:(OI)(CI)F");
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.None);
        }
    }

    public static void RestoreAccess(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            RunIcacls(path, "/remove:d", Environment.UserName);
        }
        else
        {
            File.SetUnixFileMode(
                path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void RunIcacls(string path, params string[] args)
    {
        var psi = new ProcessStartInfo("icacls.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(path);
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using Process proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start 'icacls' — cannot set up the access-denied test fixture.");
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"icacls {path} {string.Join(' ', args)} failed (exit {proc.ExitCode}): {proc.StandardError.ReadToEnd()}");
        }
    }
}
