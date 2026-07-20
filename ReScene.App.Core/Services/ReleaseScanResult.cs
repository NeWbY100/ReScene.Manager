namespace ReScene.App.Core.Services;

/// <summary>
/// The outcome of <see cref="IReleaseScanner.Scan"/> — pyrescene's release-folder classification
/// (design spec §2), split into the six destinations <c>generate_srr</c> ultimately files a
/// release's files into. Task 5 (§2a, the main-set decision tree) populates
/// <see cref="MainSets"/>, <see cref="SubtitleSfvs"/>, the proof share of <see cref="StoredFiles"/>,
/// and <see cref="Warnings"/>; <see cref="SampleFiles"/> and <see cref="MusicSfvs"/> are left empty
/// except for rescue-fallback music re-admission until Tasks 6-7 (§2b-§2e) fill in the rest.
/// </summary>
/// <param name="MainSets">The release's main RAR sets, in traversal order.</param>
/// <param name="SampleFiles">Sample media files (§2c — populated by Task 6).</param>
/// <param name="SubtitleSfvs">
/// Excluded SFVs queued as nested-SRR candidates (pyrescene's <c>extra_sfvs</c>) — every SFV
/// excluded by rules 1-6 except proof-linked ones (rule 4) and SFVs skipped for living in a
/// <c>dirfix</c> subdirectory. Also carries every main SFV when the release name itself is a
/// subpack/subfix release (§2a "Excluded-SFV destinations").
/// </param>
/// <param name="StoredFiles">
/// Files whose raw bytes get embedded verbatim in the SRR — proof SFV/RAR pairs from rule 4 in
/// this task; NFOs, logs, cues, etc. from §2d (Task 7).
/// </param>
/// <param name="MusicSfvs">
/// SFVs detected as music sets — only reachable via the §2a rescue fallback in this task
/// (<c>[DIVERGENCE]</c> pyrescene admits them as ordinary main sets; this port routes them here
/// instead per Spec 2's music-set handling). Otherwise populated by §2b (Task 6).
/// </param>
/// <param name="Warnings">
/// Human-readable warnings accumulated during the scan (unreadable directories, proof RARs that
/// could not be read or found, etc.), in the order they were produced.
/// </param>
public sealed record ReleaseScanResult(
    IReadOnlyList<ReleaseSetInput> MainSets,
    IReadOnlyList<string> SampleFiles,
    IReadOnlyList<string> SubtitleSfvs,
    IReadOnlyList<string> StoredFiles,
    IReadOnlyList<string> MusicSfvs,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Builds a warnings-only result for a release root that could not be enumerated at all
    /// (design spec §2 "Error contract", L204-209) — every collection empty except
    /// <see cref="Warnings"/>, which names the root and the underlying failure.
    /// </summary>
    public static ReleaseScanResult RootError(string root, string message) =>
        new([], [], [], [], [], [$"Cannot scan '{root}': {message}"]);
}
