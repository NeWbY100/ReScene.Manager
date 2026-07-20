namespace ReScene.App.Core.Services;

/// <summary>
/// A single directory that <see cref="ReleaseTraversal.EnumerateFiles"/> could not fully read.
/// </summary>
/// <param name="Path">The directory that failed to enumerate.</param>
/// <param name="Message">The underlying I/O exception's message.</param>
public sealed record TraversalIssue(string Path, string Message);
