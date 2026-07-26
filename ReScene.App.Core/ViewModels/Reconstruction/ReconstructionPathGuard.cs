using System.Security.Cryptography;
using System.Text;

namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>
/// Fail-closed path safety for the two reserved directories reconstruction destructively clears
/// and relocates into: the final <see cref="OutputDirName"/> tree and the <see cref="ScratchDirName"/>
/// scratch tree, both directly under a run's <c>OutputPath</c>. Every predicate/resolver here
/// real-resolves every path component through the filesystem — junctions and symlinks at any
/// ancestor, not just the leaf — before comparing, so a redirecting ancestor cannot make a
/// destructive operation land somewhere it should not (#1). Resolution never falls back to a
/// lexical result: a component that cannot be inspected (e.g. access denied) throws instead of
/// silently being treated as "not a link" or "absent".
/// </summary>
internal static class ReconstructionPathGuard
{
    /// <summary>The final-output reserved subdirectory name, directly under <c>OutputPath</c>.</summary>
    public const string OutputDirName = "output";

    /// <summary>The scratch-work reserved subdirectory name, directly under <c>OutputPath</c>.</summary>
    public const string ScratchDirName = ".rescene-work";

    /// <summary>Case-insensitive on Windows/macOS (their default filesystem behavior), case-sensitive elsewhere.</summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>
    /// Resolves every component of <paramref name="path"/> from root to leaf through the real
    /// filesystem — following junctions/symlinks at any ancestor, not just the leaf — and
    /// re-appends any trailing components that genuinely do not exist yet, literally. Not
    /// <see cref="Directory.Exists"/>-gated: an existing component that cannot be inspected
    /// (e.g. access denied) throws rather than being silently treated as absent.
    /// </summary>
    /// <exception cref="IOException">An existing component could not be resolved.</exception>
    /// <exception cref="UnauthorizedAccessException">An existing component could not be inspected.</exception>
    public static string ResolveReal(string path) => ResolveReal(path, depth: 0);

    // depth guards the recursion introduced by link-target adoption below (a link's stored target
    // can itself route through links; pathological target cycles would otherwise recurse forever
    // — the BCL's returnFinalTarget loop detection only catches pure link-to-link cycles). Same
    // guard as SrrNameCanonicalizer.ResolveAncestorChain.
    private static string ResolveReal(string path, int depth)
    {
        if (depth > 40)
        {
            throw new IOException($"Too many nested links while resolving '{path}'.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string full = Path.GetFullPath(path);
        string root = Path.GetPathRoot(full) is { Length: > 0 } r
            ? r
            : throw new ArgumentException($"'{path}' has no root.", nameof(path));

        string[] parts = full[root.Length..].Split(
            Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        string resolved = root;
        bool pastExisting = false;

        foreach (string part in parts)
        {
            string candidate = Path.Combine(resolved, part);

            if (pastExisting)
            {
                resolved = candidate;
                continue;
            }

            FileSystemInfo? target;
            try
            {
                // returnFinalTarget follows the whole chain (junction-of-junction included) in
                // one call, so a single component-level check is enough per level of the walk.
                target = Directory.ResolveLinkTarget(candidate, returnFinalTarget: true);
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
            {
                // Genuinely absent from here on — re-append the remaining components literally.
                pastExisting = true;
                resolved = candidate;
                continue;
            }

            // Null means candidate exists but is not a link. An adopted target's STRING can
            // itself route through unresolved ancestor links (macOS /var -> /private/var: links
            // created toward temp paths store the /var spelling) — re-walk it so every arm leaves
            // `resolved` fully canonical, or a linked path and a directly-walked path to the same
            // file compare unequal and containment falsely rejects. Same fix as
            // SrrNameCanonicalizer.ApplyComponent.
            resolved = target is null ? candidate : ResolveReal(target.FullName, depth + 1);
        }

        return resolved;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> real-resolves to a location strictly beneath
    /// <paramref name="root"/> (not equal to it).
    /// </summary>
    public static bool IsStrictDescendant(string root, string candidate)
    {
        string realRoot = ResolveReal(root);
        string realCandidate = ResolveReal(candidate);

        if (string.Equals(realRoot, realCandidate, PathComparison))
        {
            return false;
        }

        return realCandidate.StartsWith(AppendSeparator(realRoot), PathComparison);
    }

    /// <summary>
    /// True when <paramref name="candidate"/> real-resolves to <paramref name="root"/> itself or
    /// a strict descendant of it.
    /// </summary>
    public static bool IsSameOrDescendant(string root, string candidate) =>
        string.Equals(ResolveReal(root), ResolveReal(candidate), PathComparison) || IsStrictDescendant(root, candidate);

    /// <summary>
    /// True when <paramref name="a"/> and <paramref name="b"/> real-resolve to the same location,
    /// or either real-resolves to a strict descendant of the other. Used both for live-input
    /// rejection (an input must not overlap a reserved subtree) and reserved-root distinctness.
    /// </summary>
    public static bool Overlaps(string a, string b) =>
        string.Equals(ResolveReal(a), ResolveReal(b), PathComparison)
        || IsStrictDescendant(a, b)
        || IsStrictDescendant(b, a);

    /// <summary>
    /// Resolves both reserved roots under <paramref name="outputPath"/>, verifies each real-resolves
    /// under the real <paramref name="outputPath"/>, and asserts they are distinct and mutually
    /// non-overlapping. Every destructive operation must resolve the pair through this first.
    /// </summary>
    /// <exception cref="IOException">
    /// Either root does not resolve under the real output path, or a junction makes them equal or nested.
    /// </exception>
    public static (string OutputRoot, string ScratchRoot) ResolveReservedRoots(string outputPath)
    {
        string outputRoot = ResolveOutputRoot(outputPath);
        string scratchRoot = ResolveScratchRoot(outputPath);

        if (Overlaps(outputRoot, scratchRoot))
        {
            throw new IOException(
                $"The reserved '{OutputDirName}' and '{ScratchDirName}' roots under '{outputPath}' " +
                "resolve to the same or a nested location — refusing to treat them as safe.");
        }

        return (outputRoot, scratchRoot);
    }

    /// <summary>Resolves the final-output reserved root, verifying it real-resolves under the real <paramref name="outputPath"/>.</summary>
    public static string ResolveOutputRoot(string outputPath) => ResolveReservedRoot(outputPath, OutputDirName);

    /// <summary>Resolves the scratch-work reserved root, verifying it real-resolves under the real <paramref name="outputPath"/>.</summary>
    public static string ResolveScratchRoot(string outputPath) => ResolveReservedRoot(outputPath, ScratchDirName);

    private static string ResolveReservedRoot(string outputPath, string dirName)
    {
        string realOutputPath = ResolveReal(outputPath);
        string realRoot = ResolveReal(Path.Combine(outputPath, dirName));

        if (!IsStrictDescendant(realOutputPath, realRoot))
        {
            throw new IOException(
                $"The '{dirName}' reserved root under '{outputPath}' does not resolve under the real " +
                "output path — a junction may redirect it elsewhere.");
        }

        return realRoot;
    }

    /// <summary>
    /// Resolves a strict descendant of the output root for <paramref name="relative"/> (e.g. a
    /// set's dir-qualified relative output path, such as <c>"DVD1/aln-re4a.rar"</c>). Throws when
    /// <paramref name="relative"/> is rooted or otherwise escapes the output root (e.g. via <c>..</c>).
    /// </summary>
    public static string ResolveOutputChild(string outputPath, string relative)
    {
        if (Path.IsPathRooted(relative))
        {
            throw new ArgumentException($"'{relative}' must be a relative path.", nameof(relative));
        }

        string outputRoot = ResolveOutputRoot(outputPath);
        string realChild = ResolveReal(Path.Combine(outputRoot, relative));

        if (!IsStrictDescendant(outputRoot, realChild))
        {
            throw new ArgumentException($"'{relative}' escapes the reserved output root.", nameof(relative));
        }

        return realChild;
    }

    /// <summary>
    /// Resolves a strict descendant of the scratch root for <paramref name="setKey"/>: the key
    /// sanitized for filesystem use, with a short stable hash of the raw key appended so two raw
    /// keys that sanitize alike still get distinct scratch directories.
    /// </summary>
    public static string ResolveScratchChild(string outputPath, string setKey)
    {
        string scratchRoot = ResolveScratchRoot(outputPath);
        string dirName = $"{Sanitize(setKey)}_{ShortHash(setKey)}";
        string realChild = ResolveReal(Path.Combine(scratchRoot, dirName));

        if (!IsStrictDescendant(scratchRoot, realChild))
        {
            throw new ArgumentException($"Scratch child for '{setKey}' escapes the reserved scratch root.", nameof(setKey));
        }

        return realChild;
    }

    private static string Sanitize(string key)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            key = key.Replace(c, '_');
        }

        return key.Replace('/', '_');
    }

    /// <summary>
    /// An 8-hex-char SHA-256 prefix of <paramref name="key"/> — stable across processes (unlike
    /// <see cref="string.GetHashCode()"/>, which is randomized per-run), for collision resistance only.
    /// </summary>
    private static string ShortHash(string key) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8];

    private static string AppendSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
