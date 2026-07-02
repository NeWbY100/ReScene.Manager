using ReScene.Core;

namespace ReScene.NET.ViewModels.Reconstruction;

/// <summary>
/// One installed WinRAR version folder that the brute-force engine would accept. <paramref name="Tag"/>
/// is the variant part of the folder name after the version digits (e.g. "beta1"; empty when none) —
/// it distinguishes folders that parse to the same version.
/// </summary>
public sealed record InstalledRarVersion(int Version, string FolderName, string Path, string Tag = "");

/// <summary>
/// Enumerates the installed WinRAR sub-versions in the WinRAR versions folder, applying the same
/// rules the engine uses (GetValidRarDirectories): an immediate subfolder
/// counts only if it contains <c>rar.exe</c> and its name parses to a version. Pure and
/// I/O-only; the view-model calls it off the UI thread.
/// </summary>
public static class WinRarVersionScanner
{
    public static IReadOnlyList<InstalledRarVersion> Scan(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return [];
        }

        List<InstalledRarVersion> found = [];
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

            found.Add(new InstalledRarVersion(version, name, dir, variantTag));
        }

        return found.OrderBy(v => v.Version).ToList();
    }
}
