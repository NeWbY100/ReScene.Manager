using ReScene.SRR;

namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>
/// The plan-before-mutate reject predicate for a reconstruction run: every decision that can refuse a
/// run is made here, from already-resolved inputs, so it can run BEFORE any destructive pre-run
/// cleanup and before any delete-confirmation dialog (#17). Returns the user-facing reason a run must
/// be refused, or <see langword="null"/> when the run may proceed. Pure — it performs only read-only
/// path resolution; it never mutates the filesystem.
/// </summary>
internal static class ReconstructionPreflight
{
    /// <summary>The already-gathered inputs a preflight decision needs (no view-model coupling).</summary>
    /// <param name="Sets">The resolved archive sets (via <see cref="ArchiveSetPlanner.ResolveSets"/>).</param>
    /// <param name="OutputPath">The run's output folder.</param>
    /// <param name="ReleasePath">The release/source folder.</param>
    /// <param name="WinRARPath">The WinRAR installations folder (or a selected rar.exe).</param>
    /// <param name="VerificationPath">The .sfv/.sha1 verification file, if any.</param>
    /// <param name="SrrFilePath">The imported SRR file, if any.</param>
    /// <param name="ReleaseInputFiles">Absolute paths of the concrete release input files that will be copied.</param>
    /// <param name="CustomPackerType">The detected custom packer (multi-set custom packer is unsupported).</param>
    /// <param name="HasArchiveFileList">Whether the SRR carries an archive file list (else a recursive release copy is used).</param>
    internal sealed record Inputs(
        IReadOnlyList<SRRArchiveSet> Sets,
        string OutputPath,
        string ReleasePath,
        string WinRARPath,
        string? VerificationPath,
        string? SrrFilePath,
        IReadOnlyList<string> ReleaseInputFiles,
        CustomPackerType CustomPackerType,
        bool HasArchiveFileList);

    /// <summary>Evaluates every reject-the-run decision. Returns the rejection reason, or null to proceed.</summary>
    public static string? Evaluate(Inputs inputs)
    {
        // 1. The direct-SRR (custom packer) reconstruction path is single-set only; a multi-set custom
        //    packer cannot be reconstructed, so reject it before erasing any existing output.
        if (inputs.CustomPackerType != CustomPackerType.None && inputs.Sets.Count > 1)
        {
            return "This SRR uses a custom packer and contains multiple archive sets, which cannot be reconstructed.";
        }

        // 2. The two reserved subtrees reconstruction destructively clears must be distinct and safely
        //    under the output folder — a junction that makes them equal/nested is unsafe (#1).
        try
        {
            _ = ReconstructionPathGuard.ResolveReservedRoots(inputs.OutputPath);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return $"The output folder's reserved subfolders are not safe to use:\n{ex.Message}";
        }

        // 3. No live input may sit in, under, or above either reserved subtree — the pre-run cleanup
        //    would erase it (or copy it into itself). A sibling under the output ROOT is fine.
        foreach ((string label, string? path) in LiveInputs(inputs))
        {
            if (!string.IsNullOrWhiteSpace(path) && ReconstructorFieldGuidance.PathsOverlap(path, inputs.OutputPath))
            {
                return $"The {label} overlaps the output folder's reserved '{ReconstructionPathGuard.OutputDirName}' / " +
                    $"'{ReconstructionPathGuard.ScratchDirName}' subfolders. Move it outside the output folder.";
            }
        }

        // 4. Without an archive file list the whole release is copied recursively into the scratch
        //    input dir; if the release is the output folder or an ancestor of it, that copy would
        //    self-include the scratch tree. (This also blocks Output == Release, byte-for-byte.)
        if (!inputs.HasArchiveFileList && !string.IsNullOrWhiteSpace(inputs.ReleasePath))
        {
            try
            {
                if (ReconstructionPathGuard.IsSameOrDescendant(inputs.ReleasePath, inputs.OutputPath))
                {
                    return "The Output folder must be different from the Release folder, and not inside it.";
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                return $"The release and output folders could not be compared safely:\n{ex.Message}";
            }
        }

        return null;
    }

    private static IEnumerable<(string Label, string? Path)> LiveInputs(Inputs inputs)
    {
        yield return ("imported SRR file", inputs.SrrFilePath);
        yield return ("verification file", inputs.VerificationPath);
        yield return ("WinRAR folder", inputs.WinRARPath);
        foreach (string file in inputs.ReleaseInputFiles)
        {
            yield return ("release input file", file);
        }
    }
}
