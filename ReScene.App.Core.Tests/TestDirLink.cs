using System.Diagnostics;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Creates the directory reparse points that the path-guard fixtures need. Windows and POSIX have
/// no common API for this, so every test that needs one goes through here rather than keeping its
/// own copy — a copy that calls <c>cmd.exe</c> unconditionally simply cannot run off Windows.
/// </summary>
internal static class TestDirLink
{
    /// <summary>
    /// Creates a directory reparse point at <paramref name="link"/> aimed at <paramref name="target"/>:
    /// a junction via <c>mklink /J</c> on Windows (no elevation required), or a symlink via
    /// <see cref="Directory.CreateSymbolicLink"/> elsewhere. The target directory is created first so
    /// callers may name a location that does not exist yet. Fails the test loudly (rather than
    /// skipping) if creation genuinely does not succeed.
    /// </summary>
    public static void Create(string link, string target)
    {
        Directory.CreateDirectory(target);

        if (OperatingSystem.IsWindows())
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using Process proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start 'mklink /J' — cannot create the junction test fixture.");
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"'mklink /J \"{link}\" \"{target}\"' failed (exit {proc.ExitCode}): {proc.StandardError.ReadToEnd()}");
            }
        }
        else
        {
            Directory.CreateSymbolicLink(link, target);
        }
    }
}
