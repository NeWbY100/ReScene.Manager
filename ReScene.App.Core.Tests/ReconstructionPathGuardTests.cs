using System.Diagnostics;
using ReScene.App.Core.ViewModels.Reconstruction;

namespace ReScene.App.Core.Tests;

public class ReconstructionPathGuardTests : TempDirTestBase
{
    [Fact]
    public void ResolveReal_JunctionAncestor_ResolvesToRealTarget()
    {
        string root = Path.Combine(TempDir, "root_basic");
        Directory.CreateDirectory(root);
        string realTarget = Path.Combine(TempDir, "target_basic");
        Directory.CreateDirectory(Path.Combine(realTarget, "leaf"));

        string junction = Path.Combine(root, "junction");
        CreateDirLink(junction, realTarget);

        string resolved = ReconstructionPathGuard.ResolveReal(Path.Combine(junction, "leaf"));

        Assert.Equal(Path.Combine(realTarget, "leaf"), resolved);
    }

    [Fact]
    public void ResolveReal_NonexistentSuffix_AppendsLiterally()
    {
        string root = Path.Combine(TempDir, "root_absent");
        Directory.CreateDirectory(root);

        string resolved = ReconstructionPathGuard.ResolveReal(Path.Combine(root, "does", "not", "exist.txt"));

        Assert.Equal(Path.Combine(root, "does", "not", "exist.txt"), resolved);
    }

    [Fact]
    public void ResolveOutputChild_ReturnsPathUnderOutputRoot()
    {
        string outputPath = Path.Combine(TempDir, "run_child_out");
        Directory.CreateDirectory(outputPath);

        string child = ReconstructionPathGuard.ResolveOutputChild(outputPath, Path.Combine("DVD1", "aln-re4a.rar"));

        Assert.Equal(
            Path.Combine(outputPath, ReconstructionPathGuard.OutputDirName, "DVD1", "aln-re4a.rar"), child);
    }

    [Fact]
    public void ResolveScratchChild_ReturnsPathUnderScratchRoot()
    {
        string outputPath = Path.Combine(TempDir, "run_child_scratch");
        Directory.CreateDirectory(outputPath);

        string child = ReconstructionPathGuard.ResolveScratchChild(outputPath, "DVD1/aln-re4a");
        string scratchRoot = ReconstructionPathGuard.ResolveScratchRoot(outputPath);

        Assert.True(ReconstructionPathGuard.IsStrictDescendant(scratchRoot, child));
        Assert.StartsWith("DVD1_aln-re4a_", Path.GetFileName(child), StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveOutputChild_RootedRelative_Throws()
    {
        string outputPath = Path.Combine(TempDir, "run_rooted");
        Directory.CreateDirectory(outputPath);
        string rooted = Path.Combine(TempDir, "elsewhere.rar");

        Assert.Throws<ArgumentException>(() => ReconstructionPathGuard.ResolveOutputChild(outputPath, rooted));
    }

    [Fact]
    public void ResolveOutputChild_DotDotAlone_Throws()
    {
        string outputPath = Path.Combine(TempDir, "run_dotdot_alone");
        Directory.CreateDirectory(outputPath);

        Assert.Throws<ArgumentException>(() => ReconstructionPathGuard.ResolveOutputChild(outputPath, ".."));
    }

    [Fact]
    public void ResolveOutputChild_DotDotEscape_Throws()
    {
        string outputPath = Path.Combine(TempDir, "run_dotdot_escape");
        Directory.CreateDirectory(outputPath);
        string relative = Path.Combine("..", "escape");

        Assert.Throws<ArgumentException>(() => ReconstructionPathGuard.ResolveOutputChild(outputPath, relative));
    }

    [Fact]
    public void ResolveScratchChild_KeysThatSanitizeAlike_ProduceDistinctDirs()
    {
        string outputPath = Path.Combine(TempDir, "run_collide");
        Directory.CreateDirectory(outputPath);

        // "/" is always sanitized to "_" (on every platform), so these two raw keys sanitize to the
        // same "DVD1_x" — only the appended hash of the raw key keeps their scratch dirs distinct.
        string a = ReconstructionPathGuard.ResolveScratchChild(outputPath, "DVD1/x");
        string b = ReconstructionPathGuard.ResolveScratchChild(outputPath, "DVD1_x");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ResolveScratchChild_IsDistinctFromScratchRoot()
    {
        string outputPath = Path.Combine(TempDir, "run_child_vs_root");
        Directory.CreateDirectory(outputPath);

        string child = ReconstructionPathGuard.ResolveScratchChild(outputPath, "x");
        string root = ReconstructionPathGuard.ResolveScratchRoot(outputPath);

        Assert.NotEqual(root, child);
    }

    [Fact]
    public void IsStrictDescendant_JunctionAncestorEscapes_ReturnsFalse()
    {
        // A junction *above* the leaf redirects real resolution outside `root`, even though the
        // leaf itself ("normalChild") is an ordinary directory and the lexical path looks contained.
        string root = Path.Combine(TempDir, "root_escape");
        Directory.CreateDirectory(root);
        string escapeTarget = Path.Combine(TempDir, "escape_target");
        Directory.CreateDirectory(Path.Combine(escapeTarget, "normalChild"));

        string junctionAncestor = Path.Combine(root, "junctionAncestor");
        CreateDirLink(junctionAncestor, escapeTarget);

        string normalChild = Path.Combine(junctionAncestor, "normalChild");

        Assert.False(ReconstructionPathGuard.IsStrictDescendant(root, normalChild));
    }

    [Fact]
    public void ResolveOutputRoot_JunctionEscapesOutputPath_Throws()
    {
        string outputPath = Path.Combine(TempDir, "run_root_escape");
        Directory.CreateDirectory(outputPath);
        string escapeTarget = Path.Combine(TempDir, "escape_root_target");
        Directory.CreateDirectory(escapeTarget);

        string outputJunction = Path.Combine(outputPath, ReconstructionPathGuard.OutputDirName);
        CreateDirLink(outputJunction, escapeTarget);

        Assert.Throws<IOException>(() => ReconstructionPathGuard.ResolveOutputRoot(outputPath));
    }

    [Fact]
    public void ResolveReal_AccessDeniedAncestor_Throws()
    {
        string root = Path.Combine(TempDir, "root_denied");
        Directory.CreateDirectory(root);
        string denied = Path.Combine(root, "denied");
        Directory.CreateDirectory(denied);
        string child = Path.Combine(denied, "child");

        DenyAccess(denied);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => ReconstructionPathGuard.ResolveReal(child));
        }
        finally
        {
            RestoreAccess(denied);
        }
    }

    [Fact]
    public void Overlaps_CaseComparisonMatchesPlatformDefault()
    {
        string dir = Path.Combine(TempDir, "CaseDir");
        Directory.CreateDirectory(dir);
        string differentCase = Path.Combine(TempDir, "casedir");

        bool overlaps = ReconstructionPathGuard.Overlaps(dir, differentCase);

        // Windows/macOS default filesystems are case-insensitive; everywhere else, a different
        // case is a genuinely different (here, nonexistent) path.
        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), overlaps);
    }

    [Fact]
    public void IsSameOrDescendant_SamePath_IsTrue()
    {
        string dir = Path.Combine(TempDir, "self_same");
        Directory.CreateDirectory(dir);

        Assert.True(ReconstructionPathGuard.IsSameOrDescendant(dir, dir));
    }

    [Fact]
    public void Overlaps_IsSymmetric_ForNestedAndUnrelatedPaths()
    {
        string parent = Path.Combine(TempDir, "parent_sym");
        string child = Path.Combine(parent, "child_sym");
        Directory.CreateDirectory(child);
        string unrelated = Path.Combine(TempDir, "unrelated_sym");
        Directory.CreateDirectory(unrelated);

        Assert.True(ReconstructionPathGuard.Overlaps(parent, child));
        Assert.True(ReconstructionPathGuard.Overlaps(child, parent));
        Assert.False(ReconstructionPathGuard.Overlaps(parent, unrelated));
        Assert.False(ReconstructionPathGuard.Overlaps(unrelated, parent));
    }

    [Fact]
    public void ResolveReservedRoots_DistinctRoots_ReturnsBoth()
    {
        string outputPath = Path.Combine(TempDir, "run_reserved_ok");
        Directory.CreateDirectory(outputPath);

        (string outputRoot, string scratchRoot) = ReconstructionPathGuard.ResolveReservedRoots(outputPath);

        Assert.NotEqual(outputRoot, scratchRoot);
        Assert.True(ReconstructionPathGuard.IsStrictDescendant(outputPath, outputRoot));
        Assert.True(ReconstructionPathGuard.IsStrictDescendant(outputPath, scratchRoot));
    }

    [Fact]
    public void ResolveReservedRoots_ThrowsWhenJunctionCollapsesRoots()
    {
        string outputPath = Path.Combine(TempDir, "run_reserved_collapse");
        Directory.CreateDirectory(outputPath);
        string realOutputDir = Path.Combine(outputPath, ReconstructionPathGuard.OutputDirName);
        Directory.CreateDirectory(realOutputDir);

        // ".rescene-work" is a junction landing on the exact same real directory as "output".
        string scratchLink = Path.Combine(outputPath, ReconstructionPathGuard.ScratchDirName);
        CreateDirLink(scratchLink, realOutputDir);

        Assert.Throws<IOException>(() => ReconstructionPathGuard.ResolveReservedRoots(outputPath));
    }

    /// <summary>
    /// Creates a directory reparse point: a junction via <c>mklink /J</c> on Windows (no elevation
    /// required), or a symlink via <see cref="Directory.CreateSymbolicLink"/> elsewhere. Fails the
    /// test loudly (rather than skipping) if creation genuinely does not succeed.
    /// </summary>
    private static void CreateDirLink(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using Process proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start 'mklink /J' — cannot create the junction test fixture.");
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"'mklink /J \"{link}\" \"{target}\"' failed (exit {proc.ExitCode}): {proc.StandardError.ReadToEnd()}");
            }
        }
        else
        {
            Directory.CreateSymbolicLink(link, target);
        }
    }

    /// <summary>
    /// Denies the current user access to <paramref name="path"/> itself (via <c>icacls</c> on
    /// Windows, or clearing the Unix mode bits elsewhere) so a walk through it must fail closed.
    /// Callers must pair this with <see cref="RestoreAccess"/> so cleanup can proceed.
    /// </summary>
    private static void DenyAccess(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            RunIcacls(path, "/deny", $"{Environment.UserName}:(OI)(CI)F");
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.None);
        }
    }

    private static void RestoreAccess(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            RunIcacls(path, "/remove:d", Environment.UserName);
        }
        else
        {
            File.SetUnixFileMode(
                path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void RunIcacls(string path, params string[] args)
    {
        var psi = new ProcessStartInfo("icacls.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(path);
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using Process proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start 'icacls' — cannot set up the access-denied test fixture.");
        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"icacls {path} {string.Join(' ', args)} failed (exit {proc.ExitCode}): {proc.StandardError.ReadToEnd()}");
        }
    }
}
