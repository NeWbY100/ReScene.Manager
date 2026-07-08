namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>
/// Pure decision of which installed versions to tick, from folder contents and current intent.
/// An explicit (config) selection wins when present; otherwise the enabled major versions decide.
/// </summary>
internal static class VersionSelectionReconciler
{
    public static HashSet<int> ComputeTicked(
        IReadOnlyList<InstalledRARVersion> installed,
        IReadOnlyList<int>? pendingExplicit,
        IReadOnlySet<int> enabledMajors)
    {
        if (pendingExplicit is not null)
        {
            HashSet<int> wanted = [.. pendingExplicit];
            return [.. installed.Where(v => wanted.Contains(v.Version)).Select(v => v.Version)];
        }

        return [.. installed.Where(v => enabledMajors.Contains(v.Version / 100)).Select(v => v.Version)];
    }
}
