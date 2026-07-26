using System.Diagnostics;
using ReScene.App.Core.Models;
using ReScene.App.Core.ViewModels.Reconstruction;

namespace ReScene.App.Core.Tests;

public class ReconstructorFieldGuidanceTests : TempDirTestBase
{
    [Fact]
    public void PathsNeedAttention_AllEmpty_IsTrue() => Assert.True(ReconstructorFieldGuidance.PathsNeedAttention("", "", "", ""));

    [Fact]
    public void PathsNeedAttention_OutputEmpty_IsTrue()
    {
        string verify = Path.Combine(TempDir, "verify.sfv");
        File.WriteAllText(verify, "");
        // WinRAR/Release/Verify all valid, Output empty → still needs attention.
        Assert.True(ReconstructorFieldGuidance.PathsNeedAttention(TempDir, TempDir, verify, ""));
    }

    [Fact]
    public void PathsNeedAttention_OutputWhitespace_IsTrue()
    {
        string verify = Path.Combine(TempDir, "verify.sfv");
        File.WriteAllText(verify, "");
        // Output that is only whitespace counts as unset (IsNullOrWhiteSpace).
        Assert.True(ReconstructorFieldGuidance.PathsNeedAttention(TempDir, TempDir, verify, "   "));
    }

    [Fact]
    public void PathsNeedAttention_NonexistentWinRAR_IsTrue()
    {
        string verify = Path.Combine(TempDir, "verify.sfv");
        File.WriteAllText(verify, "");
        string missing = Path.Combine(TempDir, "does-not-exist");
        Assert.True(ReconstructorFieldGuidance.PathsNeedAttention(missing, TempDir, verify, TempDir));
    }

    [Fact]
    public void PathsNeedAttention_AllValid_IsFalse()
    {
        string release = Path.Combine(TempDir, "release");
        string output = Path.Combine(TempDir, "output");
        Directory.CreateDirectory(release);
        Directory.CreateDirectory(output);
        string verify = Path.Combine(TempDir, "verify.sfv");
        File.WriteAllText(verify, "");
        // WinRAR + Release = existing dirs, Verify = existing file, Output = separate non-empty dir.
        Assert.False(ReconstructorFieldGuidance.PathsNeedAttention(TempDir, release, verify, output));
    }

    [Fact]
    public void EvaluateWinRARPath_Empty_IsWarning() => Assert.Equal(FieldState.Warning, ReconstructorFieldGuidance.EvaluateWinRARPath("").State);

    [Fact]
    public void EvaluateReleasePath_Empty_IsWarning() => Assert.Equal(FieldState.Warning, ReconstructorFieldGuidance.EvaluateReleasePath("").State);

    [Fact]
    public void EvaluateVerificationPath_Empty_IsWarning() => Assert.Equal(FieldState.Warning, ReconstructorFieldGuidance.EvaluateVerificationPath("").State);

    [Fact]
    public void EvaluateOutputPath_Empty_IsWarning() => Assert.Equal(FieldState.Warning, ReconstructorFieldGuidance.EvaluateOutputPath("").State);

    [Fact]
    public void EvaluateOutputPath_Whitespace_IsWarning() => Assert.Equal(FieldState.Warning, ReconstructorFieldGuidance.EvaluateOutputPath("   ").State);

    [Fact]
    public void EvaluateOutputPath_Set_IsOk() => Assert.Equal(FieldState.Ok, ReconstructorFieldGuidance.EvaluateOutputPath(TempDir).State);

    [Fact]
    public void PathsOverlap_SamePath_IsTrue() => Assert.True(ReconstructorFieldGuidance.PathsOverlap(TempDir, TempDir));

    [Fact]
    public void PathsOverlap_OutputNestedInRelease_IsTrue()
    {
        string output = Path.Combine(TempDir, "output");
        Assert.True(ReconstructorFieldGuidance.PathsOverlap(TempDir, output));
    }

    [Fact]
    public void PathsOverlap_CandidateInOutputPathRootOnly_IsFalse()
    {
        // "release" sits directly under the bare OutputPath root, but not under the "output" or
        // ".rescene-work" reserved subtrees that reconstruction destructively clears — multi-set
        // runs legitimately share the OutputPath root, so this must not be flagged.
        string release = Path.Combine(TempDir, "release");
        Assert.False(ReconstructorFieldGuidance.PathsOverlap(release, TempDir));
    }

    [Fact]
    public void PathsOverlap_VerificationUnderOutputReservedRoot_IsTrue()
    {
        string outputPath = Path.Combine(TempDir, "run_verify_under_output");
        Directory.CreateDirectory(outputPath);
        string verify = Path.Combine(outputPath, ReconstructionPathGuard.OutputDirName, "verify.sfv");

        Assert.True(ReconstructorFieldGuidance.PathsOverlap(verify, outputPath));
    }

    [Fact]
    public void PathsOverlap_VerificationUnderScratchReservedRoot_IsTrue()
    {
        string outputPath = Path.Combine(TempDir, "run_verify_under_scratch");
        Directory.CreateDirectory(outputPath);
        string verify = Path.Combine(outputPath, ReconstructionPathGuard.ScratchDirName, "verify.sfv");

        Assert.True(ReconstructorFieldGuidance.PathsOverlap(verify, outputPath));
    }

    [Fact]
    public void PathsOverlap_VerificationInOutputPathRootOnly_IsFalse()
    {
        // Same OutputPath root as above, but the verification file sits beside — not under —
        // the reserved "output"/".rescene-work" subtrees: not flagged.
        string outputPath = Path.Combine(TempDir, "run_verify_root_only");
        Directory.CreateDirectory(outputPath);
        string verify = Path.Combine(outputPath, "verify.sfv");

        Assert.False(ReconstructorFieldGuidance.PathsOverlap(verify, outputPath));
    }

    [Fact]
    public void PathsOverlap_JunctionAncestorResolvesUnderOutputRoot_IsTrue()
    {
        // (#2) Lexically, "junctionRoot/junction/leaf" looks nothing like the output path, but the
        // junction real-resolves straight into the reserved "output" root — must still be caught.
        string outputPath = Path.Combine(TempDir, "run_junction_target");
        Directory.CreateDirectory(outputPath);
        string outputRoot = Path.Combine(outputPath, ReconstructionPathGuard.OutputDirName);
        Directory.CreateDirectory(Path.Combine(outputRoot, "leaf"));

        string junctionRoot = Path.Combine(TempDir, "junction_root");
        Directory.CreateDirectory(junctionRoot);
        string junction = Path.Combine(junctionRoot, "junction");
        TestDirLink.Create(junction, outputRoot);

        string candidate = Path.Combine(junction, "leaf");

        Assert.True(ReconstructorFieldGuidance.PathsOverlap(candidate, outputPath));
    }

    [Fact]
    public void PathsOverlap_ResolutionFailureOnExistingPath_FailsClosed()
    {
        string outputPath = Path.Combine(TempDir, "run_denied_output");
        Directory.CreateDirectory(outputPath);

        string denied = Path.Combine(TempDir, "denied_release");
        Directory.CreateDirectory(denied);
        string candidate = Path.Combine(denied, "child");

        DenyAccess(denied);
        try
        {
            // An unresolvable existing path must be treated as attention-needed, never silently
            // passed through as "no overlap".
            Assert.True(ReconstructorFieldGuidance.PathsOverlap(candidate, outputPath));
        }
        finally
        {
            RestoreAccess(denied);
        }
    }

    [Fact]
    public void PathsOverlap_Siblings_IsFalse()
    {
        string a = Path.Combine(TempDir, "release");
        string b = Path.Combine(TempDir, "output");
        Assert.False(ReconstructorFieldGuidance.PathsOverlap(a, b));
    }

    [Fact]
    public void PathsOverlap_SimilarPrefixButNotNested_IsFalse()
    {
        // "rel" must not be considered nested in "release".
        string a = Path.Combine(TempDir, "rel");
        string b = Path.Combine(TempDir, "release");
        Assert.False(ReconstructorFieldGuidance.PathsOverlap(a, b));
    }

    [Fact]
    public void PathsOverlap_EmptyPathA_IsFalse() => Assert.False(ReconstructorFieldGuidance.PathsOverlap("", TempDir));

    [Fact]
    public void PathsOverlap_EmptyPathB_IsFalse() => Assert.False(ReconstructorFieldGuidance.PathsOverlap(TempDir, ""));

    [Fact]
    public void PathsOverlap_DiffersOnlyByCase_MatchesPlatformDefault()
    {
        // (#26) Case comparison must follow the current filesystem's default, not be hardcoded
        // case-insensitive: candidate == outputPath (an ancestor of the reserved subtrees) only on
        // Windows/macOS; elsewhere a different case is a distinct, nonexistent path.
        string dir = Path.Combine(TempDir, "CaseFold");
        Directory.CreateDirectory(dir);
        string differentCase = Path.Combine(TempDir, "casefold");

        bool overlaps = ReconstructorFieldGuidance.PathsOverlap(differentCase, dir);

        Assert.Equal(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS(), overlaps);
    }

    [Fact]
    public void EvaluateReleasePath_OverlapsOutput_IsError()
    {
        FieldStatus s = ReconstructorFieldGuidance.EvaluateReleasePath(TempDir, TempDir);
        Assert.Equal(FieldState.Error, s.State);
        Assert.Contains("different folders", s.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateOutputPath_OverlapsRelease_IsError()
    {
        FieldStatus s = ReconstructorFieldGuidance.EvaluateOutputPath(TempDir, TempDir);
        Assert.Equal(FieldState.Error, s.State);
        Assert.Contains("different folders", s.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateReleasePath_NoOverlap_FallsThroughToSinglePath()
    {
        string release = Path.Combine(TempDir, "release");
        string output = Path.Combine(TempDir, "output");
        Directory.CreateDirectory(release);
        FieldStatus s = ReconstructorFieldGuidance.EvaluateReleasePath(release, output);
        Assert.Equal(FieldState.Ok, s.State); // existing release dir -> "Source files selected."
    }

    [Fact]
    public void EvaluateOutputPath_NoOverlap_FallsThroughToSinglePath()
    {
        string release = Path.Combine(TempDir, "release");
        string output = Path.Combine(TempDir, "output");
        FieldStatus s = ReconstructorFieldGuidance.EvaluateOutputPath(output, release);
        Assert.Equal(FieldState.Ok, s.State); // non-empty output -> "Output folder set."
    }

    [Fact]
    public void EvaluateReleasePath_EmptyOutput_NoFalseOverlap()
    {
        // Output empty -> not an overlap; release falls through to its single-path result.
        FieldStatus s = ReconstructorFieldGuidance.EvaluateReleasePath(TempDir, "");
        Assert.Equal(FieldState.Ok, s.State);
    }

    [Fact]
    public void PathsNeedAttention_Overlap_IsTrue()
    {
        string verify = Path.Combine(TempDir, "verify.sfv");
        File.WriteAllText(verify, "");
        // WinRAR/Release/Verify/Output all otherwise valid, but Release == Output.
        Assert.True(ReconstructorFieldGuidance.PathsNeedAttention(TempDir, TempDir, verify, TempDir));
    }

    [Fact]
    public void PathsNeedAttention_VerificationOverlapsReservedSubtree_IsTrue()
    {
        string release = Path.Combine(TempDir, "release2");
        string output = Path.Combine(TempDir, "output2");
        Directory.CreateDirectory(release);
        string outputRoot = Path.Combine(output, ReconstructionPathGuard.OutputDirName);
        Directory.CreateDirectory(outputRoot);
        string verify = Path.Combine(outputRoot, "verify.sfv");
        File.WriteAllText(verify, "");

        // WinRAR/Release/Output all otherwise valid and non-overlapping, but Verify sits under the
        // reserved "output" subtree beneath Output — reconstruction would overwrite it.
        Assert.True(ReconstructorFieldGuidance.PathsNeedAttention(TempDir, release, verify, output));
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
