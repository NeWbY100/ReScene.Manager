namespace ReScene.App.Core.Services;

/// <summary>
/// Deterministic release-tree traversal that the release scanner (Tasks 5-7) walks to classify a
/// release folder. [DIVERGENCE: determinism] — design spec §2 "Ordering" (rev 4): pyrescene's
/// byte order is raw <c>os.walk</c> enumeration (filesystem-dependent, not reproducible in
/// general; pyrescene-rules-excerpt.txt lines 12-24, <c>get_files</c>) — this emulation instead
/// sorts each directory level's subdirectory and file names with <see cref="StringComparer.Ordinal"/>
/// (case-sensitive) and emits a directory's files before descending into its subdirectories,
/// top-down, so identical trees produce identical output regardless of filesystem enumeration
/// order. All scanner category passes consume this order.
/// </summary>
public static class ReleaseTraversal
{
    /// <summary>
    /// Enumerates every file under <paramref name="root"/> in the deterministic order documented
    /// on this class. A directory that fails to enumerate (permission denied, I/O error) is
    /// recorded as a <see cref="TraversalIssue"/> and its subtree is skipped; the traversal
    /// continues with the remaining directories. If <paramref name="root"/> itself fails to
    /// enumerate, the result carries <see cref="TraversalResult.RootFailed"/> = <see langword="true"/>
    /// with no files. <paramref name="ct"/> is checked before any I/O and again per directory.
    /// </summary>
    public static TraversalResult EnumerateFiles(string root, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var files = new List<string>();
        var issues = new List<TraversalIssue>();
        try
        {
            _ = Directory.GetFiles(root); // probe: root failure is fatal, not a per-item issue
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new TraversalResult([], [new TraversalIssue(root, e.Message)], RootFailed: true);
        }

        Walk(root, files, issues, ct);
        return new TraversalResult(files, issues, RootFailed: false);
    }

    /// <summary>
    /// Filters <paramref name="files"/> to those matching <paramref name="extension"/>
    /// (case-insensitive), preserving the input order.
    /// </summary>
    public static IReadOnlyList<string> FilterByExtension(IReadOnlyList<string> files, string extension) =>
        [.. files.Where(f => string.Equals(Path.GetExtension(f), extension, StringComparison.OrdinalIgnoreCase))];

    private static void Walk(string dir, List<string> files, List<TraversalIssue> issues, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string[] dirFiles;
        string[] subdirs;
        try
        {
            dirFiles = Directory.GetFiles(dir);
            subdirs = Directory.GetDirectories(dir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            issues.Add(new TraversalIssue(dir, e.Message)); // scanner maps this to a Warning
            return;
        }

        Array.Sort(dirFiles, StringComparer.Ordinal);
        Array.Sort(subdirs, StringComparer.Ordinal);
        files.AddRange(dirFiles);

        foreach (string sub in subdirs)
        {
            // pyrescene's os.walk does not follow directory reparse points by default.
            if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            Walk(sub, files, issues, ct);
        }
    }
}
