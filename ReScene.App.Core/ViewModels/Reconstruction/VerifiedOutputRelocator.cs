using ReScene.SRR;

namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>
/// Transactionally moves a set's verified volumes out of the guarded scratch work-root into the
/// user's real output tree (<c>OutputPath\output\…</c>), which fixes the headline bug (#3) where a
/// verified single-set reconstruction reported success while its output stayed stranded in the hidden
/// scratch dir. Every reported committed path is canonicalised and guarded before it is moved
/// (branch-specific containment, no reparse-point leaf, no duplicate); the placed set must satisfy the
/// set's expected volume identity; and any move failure rolls back best-effort, preserving the scratch
/// tree when a rollback cannot itself complete so nothing recoverable is deleted (#1, #4, #5).
/// </summary>
internal static class VerifiedOutputRelocator
{
    /// <summary>Which reconstruction path produced the committed files — it fixes the source-guard band.</summary>
    internal enum Branch
    {
        /// <summary>rar.exe brute force: committed files land strictly under <c>&lt;workRoot&gt;\output</c>.</summary>
        BruteForce,

        /// <summary>Direct SRR (custom packer) reconstruction: committed files land under <c>&lt;workRoot&gt;</c>.</summary>
        CustomPacker,
    }

    /// <summary>The outcome of a relocation attempt.</summary>
    /// <param name="Success">True when every committed file reached its final destination.</param>
    /// <param name="ScratchPreserved">
    /// True when a failed move could not be fully rolled back, so the caller must NOT delete the work
    /// root (recoverable output still lives there). Only meaningful when <see cref="Success"/> is false.
    /// </param>
    internal readonly record struct RelocationOutcome(bool Success, bool ScratchPreserved);

    /// <summary>Case-insensitive on Windows/macOS (their default filesystem behavior), case-sensitive elsewhere.</summary>
    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    /// <summary>
    /// Relocates <paramref name="committedFiles"/> from the guarded scratch work-root into the output
    /// tree. See the type summary for the guarantees. <paramref name="log"/> receives human-readable
    /// diagnostics for every rejection/rollback.
    /// </summary>
    public static RelocationOutcome Relocate(
        string outputPath,
        string workRoot,
        SRRArchiveSet set,
        int setCount,
        Branch branch,
        bool completeAllVolumes,
        IReadOnlyList<string> committedFiles,
        IFileMover mover,
        Action<string> log)
    {
        string label = string.IsNullOrEmpty(set.Key) ? "(release)" : set.Key;

        // The band a committed path must canonically fall within: brute-force volumes land strictly
        // under <workRoot>\output; custom-packer volumes land anywhere under <workRoot>.
        string guardRoot = branch == Branch.BruteForce
            ? Path.Combine(workRoot, ReconstructionPathGuard.OutputDirName)
            : workRoot;

        // 1. Canonicalise + guard every committed path BEFORE the uniqueness check (a path could alias
        //    another only after ancestor links resolve). Reject the whole set on any violation.
        var resolved = new List<(string Source, string Name)>(committedFiles.Count);
        var seenSources = new HashSet<string>(PathComparer);
        foreach (string name in committedFiles)
        {
            if (!TryResolveRegularFileLeaf(name, out string canonical))
            {
                log($"Set {label}: refusing to relocate '{name}' — not an existing regular file, or its final component is a reparse point.");
                return new RelocationOutcome(false, false);
            }

            if (!IsWithin(guardRoot, canonical))
            {
                log($"Set {label}: refusing to relocate '{name}' — it resolves outside the guarded work root.");
                return new RelocationOutcome(false, false);
            }

            if (!seenSources.Add(canonical))
            {
                log($"Set {label}: refusing to relocate '{name}' — duplicate committed path.");
                return new RelocationOutcome(false, false);
            }

            resolved.Add((canonical, name));
        }

        // 2. Completeness gate: the placed set must match the set's expected volume identity.
        if (!IsComplete(set, branch, completeAllVolumes, resolved, label, log))
        {
            return new RelocationOutcome(false, false);
        }

        // 3. Compute every destination; reject if the relative path escapes output or the destination
        //    already exists (no-overwrite preflight) — before any file is moved.
        var plan = new List<(string Source, string Dest)>(resolved.Count);
        foreach ((string source, string name) in resolved)
        {
            string leaf = VerificationSnapshot.LastSegment(name);
            string rel = setCount > 1 && !string.IsNullOrEmpty(set.Directory)
                ? Path.Combine(set.Directory.Replace('/', Path.DirectorySeparatorChar), leaf)
                : leaf;

            string dest;
            try
            {
                dest = ReconstructionPathGuard.ResolveOutputChild(outputPath, rel);
            }
            catch (ArgumentException ex)
            {
                log($"Set {label}: refusing to relocate '{name}' — {ex.Message}");
                return new RelocationOutcome(false, false);
            }

            if (File.Exists(dest) || Directory.Exists(dest))
            {
                log($"Set {label}: refusing to relocate '{name}' — destination already exists: {dest}");
                return new RelocationOutcome(false, false);
            }

            plan.Add((source, dest));
        }

        // 4. Execute the move plan transactionally; on any failure roll back best-effort.
        var moved = new List<(string Source, string Dest)>(plan.Count);
        try
        {
            foreach ((string source, string dest) in plan)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                mover.Move(source, dest);
                moved.Add((source, dest));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log($"Set {label}: relocation failed ({ex.Message}); rolling back {moved.Count} move(s).");
            bool fullyRolledBack = RollBack(moved, mover, label, log);
            return new RelocationOutcome(false, ScratchPreserved: !fullyRolledBack);
        }

        log($"Set {label}: relocated {moved.Count} volume(s) to the output folder.");
        return new RelocationOutcome(true, false);
    }

    /// <summary>
    /// Resolves <paramref name="path"/> to a canonical, existing, regular-file leaf: ancestor
    /// junctions/symlinks are followed, but the FINAL component must itself be a regular file that is
    /// NOT a reparse point (moving a link would leave a dangling target once the scratch is deleted).
    /// </summary>
    private static bool TryResolveRegularFileLeaf(string path, out string canonical)
    {
        canonical = string.Empty;

        string? parent = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(parent))
        {
            return false;
        }

        string candidate;
        try
        {
            candidate = Path.Combine(ReconstructionPathGuard.ResolveReal(parent), Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }

        FileAttributes attrs;
        try
        {
            attrs = File.GetAttributes(candidate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false; // absent or not inspectable — fail closed
        }

        if (attrs.HasFlag(FileAttributes.ReparsePoint) || attrs.HasFlag(FileAttributes.Directory))
        {
            return false; // a link leaf, or a directory — never a regular committed volume
        }

        canonical = candidate;
        return true;
    }

    /// <summary>True when <paramref name="candidate"/> canonically falls strictly under <paramref name="root"/>.</summary>
    private static bool IsWithin(string root, string candidate)
    {
        try
        {
            return ReconstructionPathGuard.IsStrictDescendant(root, candidate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when the placed set matches the set's expected volume identity: distinct output names and
    /// the expected count. That expected count mirrors <c>Manager</c>'s placement — every release
    /// volume when the whole set was produced (custom packer, or brute force with CompleteAllVolumes),
    /// else the single first volume; when the set carries no volume names there is nothing to check.
    /// </summary>
    private static bool IsComplete(
        SRRArchiveSet set, Branch branch, bool completeAllVolumes,
        IReadOnlyList<(string Source, string Name)> resolved, string label, Action<string> log)
    {
        if (resolved.Count == 0)
        {
            log($"Set {label}: no verified volumes to place.");
            return false;
        }

        var names = new HashSet<string>(PathComparer);
        foreach ((_, string name) in resolved)
        {
            string leaf = VerificationSnapshot.LastSegment(name);
            if (!names.Add(leaf))
            {
                log($"Set {label}: duplicate output volume name '{leaf}'.");
                return false;
            }
        }

        int expectedVolumes = set.VolumeNames.Count;
        if (expectedVolumes == 0)
        {
            return true; // no expected identity to validate against — trust the verified result
        }

        int expectedCount = branch == Branch.CustomPacker || completeAllVolumes ? expectedVolumes : 1;
        if (resolved.Count != expectedCount)
        {
            log($"Set {label}: placed {resolved.Count} volume(s) but expected {expectedCount}.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Moves every <paramref name="moved"/> entry back to its source (reverse order). Returns false if
    /// any restore move itself fails — the caller must then preserve the scratch tree.
    /// </summary>
    private static bool RollBack(List<(string Source, string Dest)> moved, IFileMover mover, string label, Action<string> log)
    {
        bool complete = true;
        for (int i = moved.Count - 1; i >= 0; i--)
        {
            (string source, string dest) = moved[i];
            try
            {
                mover.Move(dest, source);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                complete = false;
                log($"Set {label}: rollback could not restore '{dest}' -> '{source}' ({ex.Message}); preserving the scratch work root.");
            }
        }

        return complete;
    }
}
