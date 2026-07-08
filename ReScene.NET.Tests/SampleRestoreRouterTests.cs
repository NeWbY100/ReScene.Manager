using ReScene.NET.Helpers;

using ReScene.App.Core.Helpers;
namespace ReScene.NET.Tests;

public class SampleRestoreRouterTests
{
    [Theory]
    [InlineData(@"C:\rel\movie.srr", SampleRestoreKind.SRR)]
    [InlineData(@"C:\rel\movie.SRR", SampleRestoreKind.SRR)]
    [InlineData(@"C:\rel\movie.sample.srs", SampleRestoreKind.SRS)]
    [InlineData(@"C:\rel\movie.SRS", SampleRestoreKind.SRS)]
    [InlineData(@"C:\rel\movie.mkv", SampleRestoreKind.Unknown)]
    [InlineData("", SampleRestoreKind.Unknown)]
    [InlineData(null, SampleRestoreKind.Unknown)]
    public void Route_ClassifiesByExtension(string? path, SampleRestoreKind expected)
        => Assert.Equal(expected, SampleRestoreRouter.Route(path));
}
