namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>
/// Thrown by <see cref="RARCommandLineBuilder.BuildCommandLineArguments"/> when the cartesian-product
/// option matrix would exceed <see cref="RARCommandLineBuilder.MaxMatrixCardinality"/> combinations.
/// Thrown before the result list is allocated — a run this large (each combination spawns a WinRAR
/// process) is virtually always an extreme/mistaken switch combination or <c>-mt</c> range, not an
/// intentional brute-force.
/// </summary>
internal sealed class RARCommandLineMatrixTooLargeException(long cardinality, long maxCardinality)
    : Exception($"The option matrix would produce {cardinality:N0} combinations, exceeding the {maxCardinality:N0} limit. Narrow the selected switches or the -mt range.")
{
    /// <summary>The cardinality that was rejected.</summary>
    public long Cardinality { get; } = cardinality;

    /// <summary>The cap that was exceeded.</summary>
    public long MaxCardinality { get; } = maxCardinality;
}
