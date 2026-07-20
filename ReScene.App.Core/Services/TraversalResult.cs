namespace ReScene.App.Core.Services;

/// <summary>
/// The outcome of <see cref="ReleaseTraversal.EnumerateFiles"/>.
/// </summary>
/// <param name="Files">
/// Every file found under the traversed root, in the deterministic top-down order documented on
/// <see cref="ReleaseTraversal"/>.
/// </param>
/// <param name="Issues">
/// One entry per directory that failed to enumerate (permission denied, I/O error), in traversal
/// order. Files under a failed directory are simply absent from <see cref="Files"/> — the failure
/// does not abort the rest of the traversal.
/// </param>
/// <param name="RootFailed">
/// <see langword="true"/> when the traversal root itself could not be enumerated: <see cref="Files"/>
/// is empty and <see cref="Issues"/> holds exactly one entry for the root. The scanner (Tasks 5-7)
/// maps <see cref="Issues"/> to warnings and a <see langword="true"/> root failure to a
/// warnings-only scan result.
/// </param>
public sealed record TraversalResult(IReadOnlyList<string> Files, IReadOnlyList<TraversalIssue> Issues, bool RootFailed);
