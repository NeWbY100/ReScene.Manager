using ReScene.App.Core.Services;

namespace ReScene.App.Core.Tests;

public class ReleaseTraversalTests : TempDirTestBase
{
    private string Make(params string[] rel)
    {
        string p = Path.Combine([TempDir, .. rel]);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void EnumerateFiles_TopDown_OrdinalPerLevel_FilesBeforeSubdirs()
    {
        Make("b.txt");
        Make("A.txt");            // ordinal: 'A' (65) < 'b' (98)
        Make("CD2", "y.sfv");
        Make("CD10", "z.sfv");    // ordinal: "CD10" < "CD2" (char '1' < '2')
        Make("CD2", "sub", "q.txt");

        var files = ReleaseTraversal.EnumerateFiles(TempDir).Files
            .Select(f => Path.GetRelativePath(TempDir, f).Replace('\\', '/'))
            .ToList();

        Assert.Equal(["A.txt", "b.txt", "CD10/z.sfv", "CD2/y.sfv", "CD2/sub/q.txt"], files);
    }

    [Fact]
    public void EnumerateFiles_CaseOnlyNames_TotallyOrdered()
    {
        Make("a.nfo");
        Make("A.nfo1");           // distinct names differing in case sort deterministically
        var files = ReleaseTraversal.EnumerateFiles(TempDir).Files.Select(Path.GetFileName).ToList();
        Assert.Equal(["A.nfo1", "a.nfo"], files);
    }

    [Fact]
    public void FilterByExtension_PreservesOrder_IgnoresCase()
    {
        Make("CD2", "b.SFV");
        Make("CD1", "a.sfv");
        Make("CD1", "x.nfo");
        var all = ReleaseTraversal.EnumerateFiles(TempDir).Files;
        var sfvs = ReleaseTraversal.FilterByExtension(all, ".sfv")
            .Select(f => Path.GetFileName(f)).ToList();
        Assert.Equal(["a.sfv", "b.SFV"], sfvs);
    }

    [Fact]
    public void EnumerateFiles_AbsoluteRoot_ResultsAreAllRooted()
    {
        Make("a.txt");
        Make("CD1", "b.sfv");

        TraversalResult result = ReleaseTraversal.EnumerateFiles(TempDir);

        Assert.All(result.Files, f => Assert.True(Path.IsPathRooted(f)));
    }

    [Fact]
    public void EnumerateFiles_RelativeRoot_ResolvesToFullPaths_MatchingAbsoluteRootResult()
    {
        // The candidate directory is created directly under the process's CURRENT working
        // directory (not under TempDir, which typically lives on a different drive from the test
        // binary's output folder) so a genuine relative root can be passed WITHOUT mutating the
        // process-wide CurrentDirectory — Directory.SetCurrentDirectory would be unsafe here since
        // xUnit runs test classes in separate collections concurrently by default.
        string cwd = Directory.GetCurrentDirectory();
        string relativeName = $"relroot_test_{Guid.NewGuid():N}";
        string localDir = Path.Combine(cwd, relativeName);
        Directory.CreateDirectory(localDir);
        string filePath = Path.Combine(localDir, "a.txt");
        File.WriteAllText(filePath, "x");

        try
        {
            TraversalResult result = ReleaseTraversal.EnumerateFiles(relativeName);

            Assert.All(result.Files, f => Assert.True(Path.IsPathRooted(f)));
            Assert.Equal([Path.GetFullPath(filePath)], result.Files);
        }
        finally
        {
            Directory.Delete(localDir, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_RootAccessDenied_SetsRootFailedAndReturnsNoFiles()
    {
        AclDenyHelper.DenyAccess(TempDir);
        try
        {
            if (!DenyTookEffect(TempDir))
            {
                return; // host does not enforce the deny ACE; nothing to assert
            }

            TraversalResult result = ReleaseTraversal.EnumerateFiles(TempDir);

            Assert.True(result.RootFailed);
            Assert.Empty(result.Files);
            TraversalIssue issue = Assert.Single(result.Issues);
            Assert.Equal(TempDir, issue.Path);
        }
        finally
        {
            AclDenyHelper.RestoreAccess(TempDir);
        }
    }

    [Fact]
    public void EnumerateFiles_DescendantAccessDenied_RecordsIssueAndKeepsRemainingFiles()
    {
        Make("visible1.txt");
        string denied = Path.Combine(TempDir, "denied");
        Directory.CreateDirectory(denied);
        File.WriteAllText(Path.Combine(denied, "secret.txt"), "x");
        Make("visible2.txt");

        AclDenyHelper.DenyAccess(denied);
        try
        {
            if (!DenyTookEffect(denied))
            {
                return; // host does not enforce the deny ACE; nothing to assert
            }

            TraversalResult result = ReleaseTraversal.EnumerateFiles(TempDir);

            Assert.False(result.RootFailed);
            var relative = result.Files
                .Select(f => Path.GetRelativePath(TempDir, f).Replace('\\', '/'))
                .ToList();
            Assert.Equal(["visible1.txt", "visible2.txt"], relative);
            TraversalIssue issue = Assert.Single(result.Issues);
            Assert.Equal(denied, issue.Path);
        }
        finally
        {
            AclDenyHelper.RestoreAccess(denied);
        }
    }

    [Fact]
    public void EnumerateFiles_ReparsePointDirectory_NotDescended()
    {
        // The link's target lives OUTSIDE TempDir so the linked file is reachable only through
        // the reparse point itself — unlike a plain subdirectory, which would still be walked.
        string outside = Path.Combine(Path.GetTempPath(), $"rescene_test_outside_{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "hidden.txt"), "x");

        string link = Path.Combine(TempDir, "link");
        TestDirLink.Create(link, outside);
        Make("visible.txt");

        try
        {
            var files = ReleaseTraversal.EnumerateFiles(TempDir).Files
                .Select(f => Path.GetRelativePath(TempDir, f).Replace('\\', '/'))
                .ToList();

            Assert.Equal(["visible.txt"], files);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void EnumerateFiles_PreCancelledToken_ThrowsOperationCanceled()
    {
        Make("a.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => ReleaseTraversal.EnumerateFiles(TempDir, cts.Token));
    }

    /// <summary>
    /// Some hosts (e.g. a process holding backup/restore privileges) don't actually enforce an
    /// <c>icacls</c> deny ACE. Confirms the deny is real before an assertion depends on it, so the
    /// ACL-deny tests fail closed (skip the assertion) instead of flaking red on such hosts.
    /// </summary>
    private static bool DenyTookEffect(string path)
    {
        try
        {
            Directory.GetFiles(path);
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

}
