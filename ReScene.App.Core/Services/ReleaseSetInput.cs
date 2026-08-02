namespace ReScene.App.Core.Services;

/// <summary>
/// One main RAR set discovered by <see cref="IReleaseScanner"/> — the SFV (or, for the loose-RAR
/// discovery divergence, first-volume RAR) that anchors the set, plus a display/logical name for
/// UI and wizard use.
/// </summary>
/// <param name="SfvOrRarPath">Full path to the set's SFV, or its first-volume RAR when no SFV exists.</param>
/// <param name="RelativeName">
/// Root-relative path with <c>/</c> separators (plain <see cref="Path.GetRelativePath(string, string)"/>) —
/// a display/logical hint only. The writer re-canonicalizes the name against OS final paths
/// (containment and collision checks); this hint is not fed to it directly.
/// </param>
public sealed record ReleaseSetInput(string SfvOrRarPath, string RelativeName);
