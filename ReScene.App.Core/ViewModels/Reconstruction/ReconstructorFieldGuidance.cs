using ReScene.App.Core.Models;

namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>
/// Pure field-validation helpers for the RAR Reconstructor's path inputs. Each method takes a
/// raw path string and returns the <see cref="FieldStatus"/> the view should show, preserving the
/// exact severities and message text the view-model previously computed inline.
/// </summary>
internal static class ReconstructorFieldGuidance
{
    /// <summary>
    /// Status for the WinRAR installations directory: the folder containing per-version WinRAR
    /// subfolders (a directory, not a path to rar.exe).
    /// </summary>
    public static FieldStatus EvaluateWinRARPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FieldStatus.Warning("Required — choose the WinRAR installations folder.");
        }

        if (!Directory.Exists(value))
        {
            return FieldStatus.Error("This WinRAR directory does not exist.");
        }

        return FieldStatus.Ok("WinRAR installations directory selected.");
    }

    /// <summary>Status for the release/source path (a directory or single file of unpacked contents).</summary>
    public static FieldStatus EvaluateReleasePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FieldStatus.Warning("Required — choose the release folder.");
        }

        if (!Directory.Exists(value) && !File.Exists(value))
        {
            return FieldStatus.Error("This path does not exist.");
        }

        return FieldStatus.Ok("Source files selected.");
    }

    /// <summary>
    /// Overlap-aware release status: a red error when Release and Output overlap (same folder or one
    /// nested in the other), otherwise the single-path result.
    /// </summary>
    public static FieldStatus EvaluateReleasePath(string releasePath, string outputPath)
        => PathsOverlap(releasePath, outputPath)
            ? FieldStatus.Error("Release and Output must be different folders.")
            : EvaluateReleasePath(releasePath);

    /// <summary>Status for the verification (.sfv/.sha1) file path.</summary>
    public static FieldStatus EvaluateVerificationPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FieldStatus.Warning("Required — choose the .sfv or .sha1 to verify against.");
        }

        if (!File.Exists(value))
        {
            return FieldStatus.Error("This .sfv/.sha1 file does not exist.");
        }

        return FieldStatus.Info("Reconstructed archives will be verified against this file.");
    }

    /// <summary>
    /// Status for the output directory (where rebuilt archives are written). It is created at
    /// Start if missing, so only emptiness is flagged here.
    /// </summary>
    public static FieldStatus EvaluateOutputPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FieldStatus.Warning("Required — choose the output folder.");
        }

        return FieldStatus.Ok("Output folder set.");
    }

    /// <summary>
    /// Overlap-aware output status: a red error when Release and Output overlap (same folder or one
    /// nested in the other), otherwise the single-path result.
    /// </summary>
    public static FieldStatus EvaluateOutputPath(string outputPath, string releasePath)
        => PathsOverlap(releasePath, outputPath)
            ? FieldStatus.Error("Release and Output must be different folders.")
            : EvaluateOutputPath(outputPath);

    /// <summary>
    /// Whether the Paths tab still needs attention: any of the four required paths (WinRAR,
    /// Release, Verify, Output) is empty or invalid, so the run could not start. Drives the
    /// warning glyph on the Paths sub-tab header.
    /// </summary>
    public static bool PathsNeedAttention(
        string winRARPath, string releasePath, string verificationPath, string outputPath)
    {
        return NeedsAttention(EvaluateWinRARPath(winRARPath))
            || NeedsAttention(EvaluateReleasePath(releasePath))
            || NeedsAttention(EvaluateVerificationPath(verificationPath))
            || NeedsAttention(EvaluateOutputPath(outputPath))
            || PathsOverlap(releasePath, outputPath)
            || PathsOverlap(verificationPath, outputPath);
    }

    /// <summary>A field needs attention unless its value is accepted (Ok) or merely informational (Info).</summary>
    private static bool NeedsAttention(FieldStatus status) =>
        status.State is not (FieldState.Ok or FieldState.Info);

    /// <summary>
    /// Whether Start would show the subdirectory modified-date warning: the release directory has
    /// subdirectories but the imported SRR carried no directory timestamps to restore.
    /// </summary>
    public static bool NeedsSubdirTimestampWarning(string releasePath, int importedDirTimestampCount)
    {
        try
        {
            return Directory.Exists(releasePath)
                && Directory.EnumerateDirectories(releasePath).Any()
                && importedDirTimestampCount == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable directory — let Start surface the real error.
            return false;
        }
    }

    /// <summary>
    /// True when <paramref name="candidatePath"/> real-resolves to the same location as, a
    /// descendant of, or an ancestor of either reserved subtree reconstruction destructively
    /// clears under <paramref name="outputPath"/> — <c>output</c> or <c>.rescene-work</c> —
    /// following junctions/symlinks at any ancestor and comparing case-correctly for the current
    /// filesystem (#2, #26). Deliberately does <em>not</em> flag a candidate that merely sits
    /// elsewhere under the bare <paramref name="outputPath"/> root: multi-set runs legitimately
    /// share that root, and cleanup only ever touches the two reserved subtrees. Fails closed
    /// (returns true) when an existing path component cannot be resolved (e.g. access denied) —
    /// never silently treated as "no overlap". Returns false for empty or unparseable paths (the
    /// per-path validation handles those).
    /// </summary>
    public static bool PathsOverlap(string candidatePath, string outputPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(outputPath))
        {
            return false;
        }

        try
        {
            string reservedOutputRoot = ReconstructionPathGuard.ResolveOutputRoot(outputPath);
            string reservedScratchRoot = ReconstructionPathGuard.ResolveScratchRoot(outputPath);

            return ReconstructionPathGuard.Overlaps(candidatePath, reservedOutputRoot)
                || ReconstructionPathGuard.Overlaps(candidatePath, reservedScratchRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            // Malformed path — not a real overlap; per-path validation reports the format error.
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An existing path component could not be resolved (access denied, or a junction that
            // escapes the output path) — fail closed rather than silently reporting no overlap.
            return true;
        }
    }
}
