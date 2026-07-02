using CommunityToolkit.Mvvm.ComponentModel;

namespace ReScene.NET.ViewModels.Reconstruction;

/// <summary>
/// A single installed WinRAR sub-version leaf in the version tree. <paramref name="tag"/> is the
/// variant part of the folder name (e.g. "beta1"; empty when none) so same-version folders stay
/// visually distinct.
/// </summary>
public sealed partial class RarVersionLeaf(int version, string folderName, string tag = "") : ObservableObject
{
    public int Version { get; } = version;
    public string FolderName { get; } = folderName;
    public string Tag { get; } = tag;
    public string Label { get; } = $"{version / 100}.{version % 100:D2}";

    /// <summary>Version plus variant tag when present — "2.50", "2.50 beta1".</summary>
    public string LabelWithTag { get; } = tag.Length == 0
        ? $"{version / 100}.{version % 100:D2}"
        : $"{version / 100}.{version % 100:D2} {tag}";

    /// <summary>The originating folder name, parenthesised for the muted ground-truth suffix.</summary>
    public string FolderDisplay { get; } = $"({folderName})";

    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}
