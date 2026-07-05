using ReScene.Core;

namespace ReScene.NET.ViewModels.Reconstruction;

/// <summary>
/// Enumerates the installed WinRAR sub-versions in the WinRAR versions folder, applying the same
/// rules the engine uses (GetValidRARDirectories): an immediate subfolder
/// counts only if it contains <c>rar.exe</c> and its name parses to a version. Pure and
/// I/O-only; the view-model calls it off the UI thread.
/// </summary>
public static class WinRARVersionScanner
{
    public static IReadOnlyList<InstalledRARVersion> Scan(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        List<InstalledRARVersion> found = [];
        foreach (string dir in Directory.GetDirectories(folder))
        {
            if (!File.Exists(Path.Combine(dir, "rar.exe")))
            {
                continue;
            }

            string name = Path.GetFileName(dir);
            if (!Manager.TryParseRARVersion(name, out int version, out string variantTag))
            {
                continue;
            }

            found.Add(new InstalledRARVersion(version, name, dir, variantTag));
        }

        return found.OrderBy(v => v.Version).ToList();
    }
}
