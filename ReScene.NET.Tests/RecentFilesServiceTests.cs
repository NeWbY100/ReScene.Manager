using ReScene.App.Core.Models;
using ReScene.NET.Services;

namespace ReScene.NET.Tests;

/// <summary>
/// Tests that RecentFilesService clamps a hand-edited RecentFilesLimit so a 0 or negative value
/// can't wipe the list or throw (finding: unvalidated RecentFilesLimit).
/// </summary>
public class RecentFilesServiceTests
{
    private sealed class FixedLimitSettingsService(int limit) : NoOpAppSettingsService
    {
        public override AppSettings Load() => new() { RecentFilesLimit = limit };
    }

    private static string NewTempFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ReScene.NET.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "recent.json");
    }

    [Theory]
    [InlineData(0)]   // would RemoveRange(0, Count) → wipe the list
    [InlineData(-5)]  // would RemoveRange(negative, …) → throw ArgumentOutOfRangeException
    public void AddEntry_NonPositiveLimit_IsClampedToKeepAtLeastOne(int limit)
    {
        string tempFile = NewTempFile();
        try
        {
            var svc = new RecentFilesService(new FixedLimitSettingsService(limit), tempFile);

            svc.AddEntry(@"C:\a.srr");
            svc.AddEntry(@"C:\b.srr");

            List<RecentFileEntry> entries = svc.LoadEntries();
            RecentFileEntry only = Assert.Single(entries);
            Assert.Equal(@"C:\b.srr", only.FilePath);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(tempFile)!, recursive: true);
        }
    }
}
