using ReScene.App.Core.ViewModels.Reconstruction;
using ReScene.SRR;

namespace ReScene.App.Core.Tests;

/// <summary>
/// Exercises <see cref="ReconstructionPreflight"/> — the plan-before-mutate reject predicate: multi-set
/// custom packer, reserved-root distinctness, live-input overlap, and the no-file-list release/output
/// self-inclusion. Covers Task 10 cases (i, reject side), (k), (m), (o).
/// </summary>
public sealed class ReconstructionPreflightTests : TempDirTestBase
{
    private static SRRArchiveSet MakeSet(string key, string dir)
    {
        var set = new SRRArchiveSet { Key = key, Directory = dir };
        set.VolumeNames.Add(key.Contains('/', StringComparison.Ordinal) ? key.Replace('/', '\\') + ".rar" : key + ".rar");
        return set;
    }

    private ReconstructionPreflight.Inputs Inputs(
        IReadOnlyList<SRRArchiveSet>? sets = null,
        string? output = null,
        string? release = null,
        string? winrar = null,
        string? verification = null,
        string? srr = null,
        IReadOnlyList<string>? releaseInputs = null,
        CustomPackerType customPacker = CustomPackerType.None,
        bool hasArchiveFileList = true)
        => new(
            sets ?? [MakeSet("k", "")],
            output ?? TempDir,
            release ?? Path.Combine(TempDir, "release"),
            winrar ?? Path.Combine(TempDir, "winrar"),
            verification,
            srr,
            releaseInputs ?? [],
            customPacker,
            hasArchiveFileList);

    // ── multi-set custom packer ────────────────────────────────

    [Fact]
    public void Evaluate_MultiSetCustomPacker_Rejected()
    {
        string? reason = ReconstructionPreflight.Evaluate(Inputs(
            sets: [MakeSet("a", ""), MakeSet("b", "")],
            customPacker: CustomPackerType.AllOnesWithLargeFlag));

        Assert.NotNull(reason);
        Assert.Contains("custom packer", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluate_SingleSetCustomPacker_Allowed()
    {
        string? reason = ReconstructionPreflight.Evaluate(Inputs(
            sets: [MakeSet("a", "")],
            customPacker: CustomPackerType.AllOnesWithLargeFlag));

        Assert.Null(reason);
    }

    // ── (k) live-input overlap ─────────────────────────────────

    [Fact]
    public void Evaluate_WinRarInsideReservedOutputSubtree_Rejected()
    {
        string winrar = Path.Combine(TempDir, "output", "winrar");
        Directory.CreateDirectory(winrar);

        string? reason = ReconstructionPreflight.Evaluate(Inputs(winrar: winrar));

        Assert.NotNull(reason);
    }

    [Fact]
    public void Evaluate_ImportedSrrInsideReservedScratchSubtree_Rejected()
    {
        string scratch = Path.Combine(TempDir, ".rescene-work");
        Directory.CreateDirectory(scratch);
        string srr = Path.Combine(scratch, "release.srr");
        File.WriteAllText(srr, "x");

        string? reason = ReconstructionPreflight.Evaluate(Inputs(srr: srr));

        Assert.NotNull(reason);
    }

    [Fact]
    public void Evaluate_VerificationFileInsideReservedOutputSubtree_Rejected()
    {
        string outputSub = Path.Combine(TempDir, "output");
        Directory.CreateDirectory(outputSub);
        string sfv = Path.Combine(outputSub, "release.sfv");
        File.WriteAllText(sfv, "x");

        string? reason = ReconstructionPreflight.Evaluate(Inputs(verification: sfv));

        Assert.NotNull(reason);
    }

    [Fact]
    public void Evaluate_ConcreteReleaseInputFileInsideReservedSubtree_Rejected()
    {
        string outputSub = Path.Combine(TempDir, "output");
        Directory.CreateDirectory(outputSub);
        string input = Path.Combine(outputSub, "movie.iso");
        File.WriteAllText(input, "x");

        string? reason = ReconstructionPreflight.Evaluate(Inputs(releaseInputs: [input]));

        Assert.NotNull(reason);
    }

    [Fact]
    public void Evaluate_InputsAtOutputRootButOutsideReservedSubtrees_Allowed()
    {
        // A WinRAR folder / SRR / verify file directly under OutputPath but NOT inside output/.rescene-work
        // is fine — cleanup only touches the two reserved subtrees.
        string winrar = Path.Combine(TempDir, "winrar-versions");
        string srr = Path.Combine(TempDir, "release.srr");
        string sfv = Path.Combine(TempDir, "release.sfv");
        Directory.CreateDirectory(winrar);
        File.WriteAllText(srr, "x");
        File.WriteAllText(sfv, "x");

        string? reason = ReconstructionPreflight.Evaluate(Inputs(
            winrar: winrar, srr: srr, verification: sfv,
            release: Path.Combine(TempDir, "some-release")));

        Assert.Null(reason);
    }

    // ── (m) distinct reserved roots ────────────────────────────

    [Fact]
    public void Evaluate_JunctionMakesReservedRootsOverlap_Rejected()
    {
        string output = Path.Combine(TempDir, "out");
        Directory.CreateDirectory(Path.Combine(output, "output"));
        // .rescene-work is a junction INTO output — the two reserved roots now resolve nested.
        TestDirLink.Create(Path.Combine(output, ".rescene-work"), Path.Combine(output, "output"));

        string? reason = ReconstructionPreflight.Evaluate(Inputs(output: output));

        Assert.NotNull(reason);
    }

    // ── (o) no-file-list release/output self-inclusion ─────────

    [Fact]
    public void Evaluate_NoFileList_ReleaseEqualsOutput_Rejected()
    {
        string? reason = ReconstructionPreflight.Evaluate(Inputs(
            output: TempDir, release: TempDir, hasArchiveFileList: false));

        Assert.NotNull(reason);
    }

    [Fact]
    public void Evaluate_NoFileList_ReleaseAncestorOfOutput_Rejected()
    {
        string output = Path.Combine(TempDir, "sub", "out");
        Directory.CreateDirectory(output);

        string? reason = ReconstructionPreflight.Evaluate(Inputs(
            output: output, release: TempDir, hasArchiveFileList: false));

        Assert.NotNull(reason);
    }

    [Fact]
    public void Evaluate_WithFileList_ReleaseAncestorOfOutput_NotRejectedForSelfInclusion()
    {
        // With an archive file list only the listed entries are copied, so the recursive self-inclusion
        // concern does not apply; the release/output nesting is permitted.
        string output = Path.Combine(TempDir, "sub", "out");
        Directory.CreateDirectory(output);

        string? reason = ReconstructionPreflight.Evaluate(Inputs(
            output: output, release: TempDir, hasArchiveFileList: true));

        Assert.Null(reason);
    }
}
