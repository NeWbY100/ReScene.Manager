using ReScene.NET.ViewModels.Reconstruction;

namespace ReScene.NET.Tests;

public sealed class WinRarVersionScannerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "wrvs-" + Guid.NewGuid().ToString("N"));

    public WinRarVersionScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private void MakeVersion(string folderName, bool withRarExe)
    {
        string dir = Path.Combine(_root, folderName);
        Directory.CreateDirectory(dir);
        if (withRarExe)
        {
            File.WriteAllText(Path.Combine(dir, "rar.exe"), "stub");
        }
    }

    [Fact]
    public void Scan_NullOrMissingFolder_ReturnsEmpty()
    {
        Assert.Empty(WinRarVersionScanner.Scan(null));
        Assert.Empty(WinRarVersionScanner.Scan(""));
        Assert.Empty(WinRarVersionScanner.Scan(Path.Combine(_root, "does-not-exist")));
    }

    [Fact]
    public void Scan_IncludesOnlyFoldersWithRarExeAndParseableName_SortedAscending()
    {
        MakeVersion("winrar-624", withRarExe: true);
        MakeVersion("winrar-560", withRarExe: true);
        MakeVersion("winrar-590", withRarExe: false);  // no rar.exe -> excluded
        MakeVersion("winrar-beta", withRarExe: true);  // unparseable -> excluded (no throw)

        IReadOnlyList<InstalledRarVersion> result = WinRarVersionScanner.Scan(_root);

        Assert.Equal(new[] { 560, 624 }, result.Select(r => r.Version).ToArray());
        Assert.Equal("winrar-560", result[0].FolderName);
    }

    [Fact]
    public void Scan_TwoDigitName_NormalisedToThreeDigits()
    {
        MakeVersion("winrar-56", withRarExe: true);

        IReadOnlyList<InstalledRarVersion> result = WinRarVersionScanner.Scan(_root);

        Assert.Single(result);
        Assert.Equal(560, result[0].Version);
    }

    [Fact]
    public void Scan_SameVersionVariants_CarryDistinguishingTags()
    {
        MakeVersion("winrar-250", withRarExe: true);
        MakeVersion("winrar-250-beta1", withRarExe: true);

        IReadOnlyList<InstalledRarVersion> result = WinRarVersionScanner.Scan(_root);

        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal(250, r.Version));
        Assert.Contains(result, r => r.FolderName == "winrar-250" && r.Tag.Length == 0);
        Assert.Contains(result, r => r.FolderName == "winrar-250-beta1" && r.Tag == "beta1");
    }
}
