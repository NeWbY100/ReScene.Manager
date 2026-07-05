namespace ReScene.NET.ViewModels.Reconstruction;

/// <summary>
/// One installed WinRAR version folder that the brute-force engine would accept. <paramref name="Tag"/>
/// is the variant part of the folder name after the version digits (e.g. "beta1"; empty when none) —
/// it distinguishes folders that parse to the same version.
/// </summary>
public sealed record InstalledRarVersion(int Version, string FolderName, string Path, string Tag = "");
