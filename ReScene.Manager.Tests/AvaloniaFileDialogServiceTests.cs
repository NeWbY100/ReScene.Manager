using ReScene.Manager.Services;

namespace ReScene.Manager.Tests;

/// <summary>
/// Unit tests for <see cref="AvaloniaFileDialogService.ResolveStartDirectory"/> — the pure helper
/// that maps a Browse field's current value to the picker's start directory. The contract: an
/// existing directory is itself; anything else falls back to its containing directory when THAT
/// exists (existing file, or a stale leaf that was renamed/deleted); otherwise null (platform
/// default). It must never throw, whatever garbage sits in the field. Real temp dirs keep the
/// cases cross-platform.
/// </summary>
public class AvaloniaFileDialogServiceTests
{
    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "rescene-dlg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void ExistingDirectory_ResolvesToItself()
    {
        string dir = CreateTempDir();
        try
        {
            Assert.Equal(dir, AvaloniaFileDialogService.ResolveStartDirectory(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ExistingFile_ResolvesToContainingDirectory()
    {
        string dir = CreateTempDir();
        try
        {
            string file = Path.Combine(dir, "sample.mkv");
            File.WriteAllText(file, "x");
            Assert.Equal(dir, AvaloniaFileDialogService.ResolveStartDirectory(file));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void StaleLeaf_UnderExistingDirectory_ResolvesToThatDirectory()
    {
        // The field still names a file that was renamed/deleted — the folder remains useful.
        string dir = CreateTempDir();
        try
        {
            string gone = Path.Combine(dir, "no-longer-here.sfv");
            Assert.Equal(dir, AvaloniaFileDialogService.ResolveStartDirectory(gone));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void NonexistentRoot_ResolvesToNull()
    {
        string bogus = Path.Combine(Path.GetTempPath(), "rescene-gone-" + Guid.NewGuid().ToString("N"), "deep", "file.rar");
        Assert.Null(AvaloniaFileDialogService.ResolveStartDirectory(bogus));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankInput_ResolvesToNull(string? value)
        => Assert.Null(AvaloniaFileDialogService.ResolveStartDirectory(value));

    [Fact]
    public void GarbageInput_NeverThrows_ResolvesToNull()
    {
        // Browse must work no matter what sits in the field (WCAG-adjacent failure-mode guarantee).
        Assert.Null(AvaloniaFileDialogService.ResolveStartDirectory("inva|id<>path\twith\0junk"));
        Assert.Null(AvaloniaFileDialogService.ResolveStartDirectory(new string('x', 40_000)));
    }
}
