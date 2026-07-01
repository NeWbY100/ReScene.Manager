using CommunityToolkit.Mvvm.ComponentModel;

namespace ReScene.NET.ViewModels.Reconstruction;

/// <summary>A single installed WinRAR sub-version leaf in the version tree.</summary>
public sealed partial class RarVersionLeaf(int version, string folderName) : ObservableObject
{
    public int Version { get; } = version;
    public string FolderName { get; } = folderName;
    public string Label { get; } = $"{version / 100}.{version % 100:D2}";

    [ObservableProperty]
    public partial bool IsChecked { get; set; }
}
