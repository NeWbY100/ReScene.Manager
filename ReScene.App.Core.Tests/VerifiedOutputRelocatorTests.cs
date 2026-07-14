using System.Diagnostics;
using ReScene.App.Core.ViewModels.Reconstruction;
using ReScene.SRR;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Exercises <see cref="VerifiedOutputRelocator"/> with real files on disk: the branch-specific source
/// guard, subfolder-preserving destination layout, the completeness gate, and the transactional
/// move/rollback (with a fake mover that fails on a chosen move). Covers Task 10 cases (a)-(f), (h),
/// (l), (n).
/// </summary>
public sealed class VerifiedOutputRelocatorTests : TempDirTestBase
{
    private string OutputPath => TempDir;

    // ── Fakes / helpers ──────────────────────────────────────

    /// <summary>
    /// A mover that performs a real <see cref="File.Move(string,string)"/> for every call EXCEPT those
    /// whose 1-based call index is in <see cref="FailOnCalls"/>, where it throws before touching disk.
    /// Records every attempted move so a test can assert the rollback moved files back.
    /// </summary>
    private sealed class ScriptedFileMover : IFileMover
    {
        public HashSet<int> FailOnCalls { get; } = [];
        public List<(string Source, string Destination)> Moves { get; } = [];
        private int _calls;

        public void Move(string source, string destination)
        {
            _calls++;
            Moves.Add((source, destination));
            if (FailOnCalls.Contains(_calls))
            {
                throw new IOException($"forced failure on move #{_calls}");
            }

            File.Move(source, destination, overwrite: false);
        }
    }

    private static readonly IFileMover Real = new SystemFileMover();

    private static SRRArchiveSet MakeSet(string key, string dir, params string[] volumes)
    {
        var set = new SRRArchiveSet { Key = key, Directory = dir };
        foreach (string v in volumes)
        {
            set.VolumeNames.Add(v);
        }

        return set;
    }

    private string WorkRoot(string key) => Path.Combine(OutputPath, ".rescene-work", key.Replace('/', '_'));

    /// <summary>Writes a brute-force committed volume under <c>&lt;workRoot&gt;\output\{rel}</c>; returns its path.</summary>
    private static string WriteBruteVolume(string workRoot, string rel, string content = "vol")
    {
        string path = Path.Combine(workRoot, "output", rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Writes a custom-packer committed volume under <c>&lt;workRoot&gt;\{rel}</c>; returns its path.</summary>
    private static string WriteCustomVolume(string workRoot, string rel, string content = "vol")
    {
        string path = Path.Combine(workRoot, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static void CreateJunction(string link, string target)
    {
        Directory.CreateDirectory(target);
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using Process p = Process.Start(psi)!;
        p.WaitForExit();
        Assert.True(p.ExitCode == 0 && Directory.Exists(link),
            $"Could not create junction '{link}' -> '{target}': {p.StandardError.ReadToEnd()}");
    }

    private VerifiedOutputRelocator.RelocationOutcome Relocate(
        string workRoot, SRRArchiveSet set, int setCount, VerifiedOutputRelocator.Branch branch,
        bool completeAllVolumes, IReadOnlyList<string> committed, IFileMover mover)
        => VerifiedOutputRelocator.Relocate(
            OutputPath, workRoot, set, setCount, branch, completeAllVolumes, committed, mover, _ => { });

    // ── (a) single keyed set: lands at output\<name>, NOT output\<Directory>\ ─────────────

    [Fact]
    public void Relocate_SingleKeyedSet_LandsAtOutputRoot_NotUnderSetDirectory()
    {
        SRRArchiveSet set = MakeSet("DVD1/aln-re4a", "DVD1", "DVD1\\aln-re4a.rar");
        string workRoot = WorkRoot(set.Key);
        string source = WriteBruteVolume(workRoot, "aln-re4a.rar", "payload");

        VerifiedOutputRelocator.RelocationOutcome outcome = Relocate(
            workRoot, set, setCount: 1, VerifiedOutputRelocator.Branch.BruteForce,
            completeAllVolumes: true, [source], Real);

        Assert.True(outcome.Success);
        Assert.True(File.Exists(Path.Combine(OutputPath, "output", "aln-re4a.rar")));
        Assert.False(File.Exists(Path.Combine(OutputPath, "output", "DVD1", "aln-re4a.rar")));
        Assert.False(File.Exists(source)); // moved, not copied
    }

    // ── (b) two multi-set members sharing Directory="DVD1": both survive under output\DVD1 ──

    [Fact]
    public void Relocate_TwoSetsSharingDirectory_BothLandUnderThatDirectory()
    {
        SRRArchiveSet a = MakeSet("DVD1/a", "DVD1", "DVD1\\a.rar");
        SRRArchiveSet b = MakeSet("DVD1/b", "DVD1", "DVD1\\b.rar");
        string workA = WorkRoot(a.Key);
        string workB = WorkRoot(b.Key);
        string srcA = WriteBruteVolume(workA, "a.rar");
        string srcB = WriteBruteVolume(workB, "b.rar");

        Assert.True(Relocate(workA, a, 2, VerifiedOutputRelocator.Branch.BruteForce, true, [srcA], Real).Success);
        Assert.True(Relocate(workB, b, 2, VerifiedOutputRelocator.Branch.BruteForce, true, [srcB], Real).Success);

        Assert.True(File.Exists(Path.Combine(OutputPath, "output", "DVD1", "a.rar")));
        Assert.True(File.Exists(Path.Combine(OutputPath, "output", "DVD1", "b.rar")));
    }

    // ── (c) malicious Directory escaping output: rejected, nothing moved outside output ──

    [Fact]
    public void Relocate_MultiSetDirectoryEscapesOutputRoot_Rejected_NothingMoved()
    {
        SRRArchiveSet set = MakeSet("x", "../../x", "x.rar");
        string workRoot = WorkRoot(set.Key);
        string source = WriteBruteVolume(workRoot, "x.rar");

        VerifiedOutputRelocator.RelocationOutcome outcome = Relocate(
            workRoot, set, setCount: 2, VerifiedOutputRelocator.Branch.BruteForce, true, [source], Real);

        Assert.False(outcome.Success);
        Assert.True(File.Exists(source)); // untouched
        Assert.False(File.Exists(Path.Combine(TempDir, "..", "..", "x", "x.rar")));
    }

    // ── (d) brute source guard band ──────────────────────────────────────────────────────

    [Fact]
    public void Relocate_Brute_CommittedPathOutsideWorkRootOutput_Rejected()
    {
        SRRArchiveSet set = MakeSet("k", "", "x.rar");
        string workRoot = WorkRoot(set.Key);
        // A file the lib would never place there: directly under <workRoot>\input, falsely "committed".
        string strayDir = Path.Combine(workRoot, "input");
        Directory.CreateDirectory(strayDir);
        string stray = Path.Combine(strayDir, "x.rar");
        File.WriteAllText(stray, "stray");

        VerifiedOutputRelocator.RelocationOutcome outcome = Relocate(
            workRoot, set, 1, VerifiedOutputRelocator.Branch.BruteForce, false, [stray], Real);

        Assert.False(outcome.Success);
        Assert.True(File.Exists(stray)); // never moved
    }

    [Fact]
    public void Relocate_Brute_DuplicateCommittedPath_Rejected()
    {
        SRRArchiveSet set = MakeSet("k", "", "x.rar");
        string workRoot = WorkRoot(set.Key);
        string source = WriteBruteVolume(workRoot, "x.rar");

        VerifiedOutputRelocator.RelocationOutcome outcome = Relocate(
            workRoot, set, 1, VerifiedOutputRelocator.Branch.BruteForce, false, [source, source], Real);

        Assert.False(outcome.Success);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Relocate_Brute_NonExistentCommittedPath_Rejected()
    {
        SRRArchiveSet set = MakeSet("k", "", "x.rar");
        string workRoot = WorkRoot(set.Key);
        Directory.CreateDirectory(Path.Combine(workRoot, "output"));
        string ghost = Path.Combine(workRoot, "output", "does-not-exist.rar");

        VerifiedOutputRelocator.RelocationOutcome outcome = Relocate(
            workRoot, set, 1, VerifiedOutputRelocator.Branch.BruteForce, false, [ghost], Real);

        Assert.False(outcome.Success);
    }

    // ── (e) partial (CompleteAllVolumes off) and generated names both relocate ────────────

    [Fact]
    public void Relocate_CompleteAllVolumesOff_SingleGeneratedName_Relocates()
    {
        // Identity for the not-complete-all-volumes mode is a single volume; a generated name (that
        // does not match the release VolumeName) must still relocate — completeness comes from the result.
        SRRArchiveSet set = MakeSet("k", "", "aln-re4a.rar");
        string workRoot = WorkRoot(set.Key);
        string source = WriteBruteVolume(workRoot, "reconstructed-000.rar");

        VerifiedOutputRelocator.RelocationOutcome outcome = Relocate(
            workRoot, set, 1, VerifiedOutputRelocator.Branch.BruteForce,
            completeAllVolumes: false, [source], Real);

        Assert.True(outcome.Success);
        Assert.True(File.Exists(Path.Combine(OutputPath, "output", "reconstructed-000.rar")));
    }

    // ── (f) destination-exists preflight, mover-failure rollback, incomplete-rollback preserve ──

    [Fact]
    public void Relocate_DestinationAlreadyExists_Rejected_NothingMoved()
    {
        SRRArchiveSet set = MakeSet("k", "", "x.rar");
        string workRoot = WorkRoot(set.Key);
        string source = WriteBruteVolume(workRoot, "x.rar");
        string dest = Path.Combine(OutputPath, "output", "x.rar");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllText(dest, "pre-existing");

        VerifiedOutputRelocator.RelocationOutcome outcome = Relocate(
            workRoot, set, 1, VerifiedOutputRelocator.Branch.BruteForce, false, [source], Real);

        Assert.False(outcome.Success);
        Assert.True(File.Exists(source));
        Assert.Equal("pre-existing", File.ReadAllText(dest)); // untouched
    }

    [Fact]
    public void Relocate_MoverFailsOnSecondMove_RollsBackFirst_ScratchNotPreserved()
    {
        SRRArchiveSet set = MakeSet("k", "", "a.rar", "b.rar");
        string workRoot = WorkRoot(set.Key);
        string a = WriteBruteVolume(workRoot, "a.rar");
        string b = WriteBruteVolume(workRoot, "b.rar");
        var mover = new ScriptedFileMover();
        mover.FailOnCalls.Add(2); // forward move of b fails

        VerifiedOutputRelocator.RelocationOutcome outcome = Relocate(
            workRoot, set, 1, VerifiedOutputRelocator.Branch.BruteForce,
            completeAllVolumes: true, [a, b], mover);

        Assert.False(outcome.Success);
        Assert.False(outcome.ScratchPreserved);           // rollback fully restored the moved file
        Assert.True(File.Exists(a));                       // a moved back to source
        Assert.True(File.Exists(b));                       // b never moved
        Assert.False(File.Exists(Path.Combine(OutputPath, "output", "a.rar")));
        Assert.False(File.Exists(Path.Combine(OutputPath, "output", "b.rar")));
    }

    [Fact]
    public void Relocate_RollbackMoveAlsoFails_ScratchPreserved()
    {
        SRRArchiveSet set = MakeSet("k", "", "a.rar", "b.rar");
        string workRoot = WorkRoot(set.Key);
        string a = WriteBruteVolume(workRoot, "a.rar");
        string b = WriteBruteVolume(workRoot, "b.rar");
        var mover = new ScriptedFileMover();
        mover.FailOnCalls.Add(2); // forward move of b fails
        mover.FailOnCalls.Add(3); // the rollback move of a also fails

        VerifiedOutputRelocator.RelocationOutcome outcome = Relocate(
            workRoot, set, 1, VerifiedOutputRelocator.Branch.BruteForce,
            completeAllVolumes: true, [a, b], mover);

        Assert.False(outcome.Success);
        Assert.True(outcome.ScratchPreserved); // rollback could not complete: caller must keep the scratch
    }

    // ── (h) single-set custom packer: files under the custom work-root relocate ───────────

    [Fact]
    public void Relocate_CustomPacker_FilesUnderWorkRoot_Relocate()
    {
        SRRArchiveSet set = MakeSet("k", "", "test.rar", "DVD1\\y.rar");
        string workRoot = WorkRoot(set.Key);
        string top = WriteCustomVolume(workRoot, "test.rar");     // <workRoot>\test.rar
        string nested = WriteCustomVolume(workRoot, Path.Combine("DVD1", "y.rar")); // <workRoot>\DVD1\y.rar

        VerifiedOutputRelocator.RelocationOutcome outcome = Relocate(
            workRoot, set, 1, VerifiedOutputRelocator.Branch.CustomPacker,
            completeAllVolumes: false, [top, nested], Real);

        Assert.True(outcome.Success);
        Assert.True(File.Exists(Path.Combine(OutputPath, "output", "test.rar")));
        Assert.True(File.Exists(Path.Combine(OutputPath, "output", "y.rar"))); // single set → flattened
    }

    // ── (l) empty Directory on a multi-set member → output\<name> (no throw) ───────────────

    [Fact]
    public void Relocate_MultiSetEmptyDirectory_LandsAtOutputRoot()
    {
        SRRArchiveSet set = MakeSet("cd1", "", "cd1.rar");
        string workRoot = WorkRoot(set.Key);
        string source = WriteBruteVolume(workRoot, "cd1.rar");

        VerifiedOutputRelocator.RelocationOutcome outcome = Relocate(
            workRoot, set, setCount: 2, VerifiedOutputRelocator.Branch.BruteForce, true, [source], Real);

        Assert.True(outcome.Success);
        Assert.True(File.Exists(Path.Combine(OutputPath, "output", "cd1.rar")));
    }

    // ── (n) reparse-point source leaf rejected; ancestor junction still passes ─────────────

    [Fact]
    public void Relocate_CommittedLeafIsReparsePoint_Rejected()
    {
        SRRArchiveSet set = MakeSet("k", "", "x.rar");
        string workRoot = WorkRoot(set.Key);
        Directory.CreateDirectory(Path.Combine(workRoot, "output"));
        // The committed "file" is actually a junction (reparse point) — must never be moved as a link.
        string linkLeaf = Path.Combine(workRoot, "output", "x.rar");
        CreateJunction(linkLeaf, Path.Combine(TempDir, "junction-target"));

        VerifiedOutputRelocator.RelocationOutcome outcome = Relocate(
            workRoot, set, 1, VerifiedOutputRelocator.Branch.BruteForce, false, [linkLeaf], Real);

        Assert.False(outcome.Success);
        Assert.True(Directory.Exists(linkLeaf)); // not moved
    }

    [Fact]
    public void Relocate_FileUnderJunctionBackedAncestor_Passes()
    {
        SRRArchiveSet set = MakeSet("k", "", "x.rar");
        // workRoot is a junction to a real directory elsewhere — a legitimate ancestor reparse point.
        string realBacking = Path.Combine(TempDir, "real-backing");
        string workRoot = Path.Combine(OutputPath, ".rescene-work", "k");
        Directory.CreateDirectory(Path.Combine(OutputPath, ".rescene-work"));
        CreateJunction(workRoot, realBacking);
        string source = WriteBruteVolume(workRoot, "x.rar"); // resolves into realBacking\output\x.rar

        VerifiedOutputRelocator.RelocationOutcome outcome = Relocate(
            workRoot, set, 1, VerifiedOutputRelocator.Branch.BruteForce, false, [source], Real);

        Assert.True(outcome.Success);
        Assert.True(File.Exists(Path.Combine(OutputPath, "output", "x.rar")));
    }

    // ── destination-resolution path IO error is caught, not rethrown (final-review Important) ─────
    // ResolveOutputChild (step 3) can throw IOException / UnauthorizedAccessException — not just the
    // ArgumentException the step used to catch — via ResolveOutputRoot -> ResolveReal. Both must be
    // reported as a failed relocation for THIS set (nothing has moved yet), never escape Relocate and
    // abort the whole run.

    [Fact]
    public void Relocate_OutputRootResolutionThrowsIOException_ReturnsFailure_DoesNotThrow()
    {
        // A dedicated output root (a subdir of TempDir) whose reserved 'output' child is a junction that
        // escapes it, so ResolveOutputRoot throws IOException when step 3 resolves the destination.
        string outputPath = Path.Combine(TempDir, "run-io");
        Directory.CreateDirectory(outputPath);
        string escapeTarget = Path.Combine(TempDir, "escape-io"); // sibling of outputPath — outside it
        CreateJunction(Path.Combine(outputPath, "output"), escapeTarget);

        SRRArchiveSet set = MakeSet("k", "", "x.rar");
        string workRoot = Path.Combine(outputPath, ".rescene-work", "k");
        string source = WriteBruteVolume(workRoot, "x.rar");

        VerifiedOutputRelocator.RelocationOutcome outcome = VerifiedOutputRelocator.Relocate(
            outputPath, workRoot, set, setCount: 1, VerifiedOutputRelocator.Branch.BruteForce,
            completeAllVolumes: true, [source], Real, _ => { });

        Assert.False(outcome.Success);
        Assert.False(outcome.ScratchPreserved); // nothing moved at step 3 — no scratch to preserve
        Assert.True(File.Exists(source));       // untouched
    }

    [Fact]
    public void Relocate_OutputRootResolutionThrowsUnauthorizedAccess_ReturnsFailure_DoesNotThrow()
    {
        // The reserved 'output' root exists but is denied inspection, so ResolveOutputRoot -> ResolveReal
        // throws UnauthorizedAccessException (not ArgumentException) when step 3 resolves the destination.
        string outputPath = Path.Combine(TempDir, "run-acl");
        Directory.CreateDirectory(outputPath);
        string outputDir = Path.Combine(outputPath, ReconstructionPathGuard.OutputDirName);
        Directory.CreateDirectory(outputDir);

        SRRArchiveSet set = MakeSet("k", "", "x.rar");
        string workRoot = Path.Combine(outputPath, ".rescene-work", "k");
        string source = WriteBruteVolume(workRoot, "x.rar");

        AclDenyHelper.DenyAccess(outputDir);
        try
        {
            VerifiedOutputRelocator.RelocationOutcome outcome = VerifiedOutputRelocator.Relocate(
                outputPath, workRoot, set, setCount: 1, VerifiedOutputRelocator.Branch.BruteForce,
                completeAllVolumes: true, [source], Real, _ => { });

            Assert.False(outcome.Success);
            Assert.False(outcome.ScratchPreserved);
            Assert.True(File.Exists(source));
        }
        finally
        {
            AclDenyHelper.RestoreAccess(outputDir); // restore BEFORE temp-dir cleanup
        }
    }
}
