using ReScene.App.Core.Models;

using ReScene.App.Core.Services;
namespace ReScene.App.Core.Tests;

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
        string dir = Path.Combine(Path.GetTempPath(), "ReScene.App.Core.Tests", Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void PathComparison_MatchesCurrentOsFilesystemRules()
    {
        StringComparison expected = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        Assert.Equal(expected, RecentFilesService.PathComparison);
    }

    [Fact]
    public void AddEntry_DedupesByOsCasingRules()
    {
        // Two paths differing only in case: on Windows they are the SAME file and must collapse to a
        // single entry (regression guard for the existing behavior); on Linux/macOS they are distinct
        // files and must both survive.
        string tempFile = NewTempFile();
        try
        {
            var svc = new RecentFilesService(new FixedLimitSettingsService(50), tempFile);

            svc.AddEntry(@"C:\Sets\Movie.srr");
            svc.AddEntry(@"c:\sets\movie.srr");

            List<RecentFileEntry> entries = svc.LoadEntries();
            if (OperatingSystem.IsWindows())
            {
                RecentFileEntry only = Assert.Single(entries);
                Assert.Equal(@"c:\sets\movie.srr", only.FilePath); // newest of the two wins
            }
            else
            {
                Assert.Equal(2, entries.Count);
            }
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(tempFile)!, recursive: true);
        }
    }
}
