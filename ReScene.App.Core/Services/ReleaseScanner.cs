using System.Text.RegularExpressions;
using ReScene.Core.IO;
using ReScene.RAR;

namespace ReScene.App.Core.Services;

/// <summary>
/// Default <see cref="IReleaseScanner"/> — a line-for-line port of pyrescene's
/// <c>remove_unwanted_sfvs</c> (pyrescene-rules-excerpt.txt L294-436, design spec §2a) plus the
/// rescue fallback and excluded-SFV destination rules that sit alongside it in
/// <c>generate_srr</c>. Sequential, first-match: each <c>*.sfv</c> under the root is classified by
/// walking rules 1-7 in order and stopping at the first one that applies.
/// </summary>
public sealed partial class ReleaseScanner : IReleaseScanner
{
    // excerpt: remove_unwanted_sfvs L344-348 (rule 3 exact parent-directory set)
    private static readonly HashSet<string> _exactExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "subs", "vobsubs", "vobsub", "subtitles", "sub", "czsubs",
        "subpack", "vobsubs-full", "vobsubs-light", "codec", "codecs", "cover", "covers",
    };

    // excerpt: has_music L419-423 (case-SENSITIVE endswith, preserved verbatim — [DIVERGENCE] noted
    // on the rescue fallback that consumes it)
    private static readonly string[] _musicExtensions = [".mp3", ".flac", ".mp2"];

    private readonly Func<string, IReadOnlyList<string>> _sfvEntryReader;
    private readonly Func<string, CancellationToken, ProofRarFacts> _proofRarReader;

    /// <summary>Production scanner: reads real SFV files and real proof RARs from disk.</summary>
    public ReleaseScanner() : this(null, null)
    {
    }

    /// <summary>
    /// Test seam: <paramref name="sfvEntryReader"/> overrides how an SFV's listed file names are
    /// read (default: <see cref="SFVFile.ReadFile"/> against the real file); <paramref name="proofRarReader"/>
    /// overrides how a proof RAR's packed-block facts are read (default:
    /// <see cref="RarProofInspector.Inspect"/> against the real file). Rule 4 consumes
    /// <see cref="ProofRarFacts.LastPackedIsImage"/>; the independent proof-RAR pass (Task 7)
    /// consumes <see cref="ProofRarFacts.AnyImage"/> — one seam serves both.
    /// </summary>
    internal ReleaseScanner(
        Func<string, IReadOnlyList<string>>? sfvEntryReader, Func<string, CancellationToken, ProofRarFacts>? proofRarReader)
    {
        _sfvEntryReader = sfvEntryReader ?? DefaultReadSfvEntries;
        _proofRarReader = proofRarReader ?? RarProofInspector.Inspect;
    }

    /// <inheritdoc/>
    public ReleaseScanResult Scan(string releaseRoot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        TraversalResult traversal = ReleaseTraversal.EnumerateFiles(releaseRoot, ct);
        if (traversal.RootFailed)
        {
            return ReleaseScanResult.RootError(releaseRoot, traversal.Issues[0].Message);
        }

        IReadOnlyList<string> all = traversal.Files;
        var warnings = new List<string>(
            traversal.Issues.Select(i => $"Unreadable: {i.Path} ({i.Message})"));

        string releaseName = Path.GetFileName(Path.TrimEndingDirectorySeparator(releaseRoot));
        string lcRelease = releaseName.ToLowerInvariant();
        IReadOnlyList<string> sfvs = ReleaseTraversal.FilterByExtension(all, ".sfv");

        var main = new List<string>();
        var excludedCandidates = new List<string>();
        var stored = new List<string>();

        foreach (string sfv in sfvs)
        {
            ct.ThrowIfCancellationRequested();
            SfvClass cls = ClassifySfv(sfv, lcRelease, warnings, stored, ct);
            switch (cls)
            {
                case SfvClass.Main:
                    main.Add(sfv);
                    break;
                case SfvClass.Excluded:
                    excludedCandidates.Add(sfv);
                    break;
                case SfvClass.Proof:
                    // The SFV (and, where applicable, its RAR) was already added to `stored`
                    // inside ClassifySfv/ClassifyProof — the two destinations differ per branch.
                    break;
                case SfvClass.Skipped:
                    // I3 hardening: the SFV itself was unreadable — a warning was already added
                    // inside TryReadSfvEntries; it gets no destination at all (spec §2 error
                    // contract's "otherwise skipped" branch, distinct from an actively-excluded
                    // SFV, which still reaches SubtitleSfvs).
                    break;
            }
        }

        // excerpt: remove_unwanted_sfvs L425-434 (rescue fallback: re-admit multi-entry or
        // music-having SFVs when nothing survived rules 1-7 — re-examines every SFV found, not
        // just the ones rules 1-7 excluded, exactly like pyrescene's `for sfv in sfv_list`)
        var musicSfvs = new List<string>();
        if (main.Count == 0)
        {
            foreach (string sfv in sfvs)
            {
                ct.ThrowIfCancellationRequested();
                IReadOnlyList<string>? entries = TryReadSfvEntries(sfv, warnings);
                if (entries is null)
                {
                    // I3 hardening: warning already added — don't let one bad SFV crash rescue.
                    continue;
                }

                if (entries.Count > 1)
                {
                    main.Add(sfv);
                }
                else if (entries.Any(HasMusicExtension))
                {
                    // [DIVERGENCE] pyrescene admits rescued music SFVs as ordinary main sets;
                    // Spec 2 routes them to MusicSfvs instead (design spec §2 L138-141).
                    musicSfvs.Add(sfv);
                    warnings.Add($"Rescued as a music set (unsupported until Spec 2): {sfv}");
                }
            }

            if (main.Count == 0 && musicSfvs.Count == 0)
            {
                // excerpt: remove_unwanted_sfvs L432-434
                warnings.Add($"{releaseName} might be missing an SFV file.");
            }
        }

        // C1 fix: the FINAL wanted set (post-rescue) is main UNION musicSfvs — pyrescene's rescue
        // tail appends BOTH kinds into the SAME wanted_sfvs list (excerpt L425-429), so
        // get_unwanted_sfvs (L438) excludes both from the excluded/extra_sfvs computation.
        // Checking only `main` let a rescue-promoted MUSIC sfv double-list in both MusicSfvs and
        // SubtitleSfvs.
        var wanted = new HashSet<string>(main);
        wanted.UnionWith(musicSfvs);

        // I5 fix: build `subs` in a SINGLE traversal-ordered pass over `sfvs` (rather than
        // concatenating excludedCandidates then main) so a subpack/subfix release's merged
        // excluded + main-queued SubtitleSfvs stays in canonical traversal order instead of two
        // concatenated runs. design spec §2a "Excluded-SFV destinations": pyrescene computes
        // `extra_sfvs` against the FINAL (post-rescue) wanted_sfvs set
        // (`get_unwanted_sfvs(allsfvs, wantedsfvs)`, called after remove_unwanted_sfvs —
        // including its own rescue tail — has already returned) — an SFV the rescue promoted
        // into `main`/`musicSfvs` is no longer excluded, even though the first pass flagged it.
        bool subpackOrSubfixRelease = lcRelease.Contains("subpack", StringComparison.Ordinal)
            || lcRelease.Contains("subfix", StringComparison.Ordinal);
        var excludedSet = new HashSet<string>(excludedCandidates);
        var mainSet = new HashSet<string>(main);
        var subs = new List<string>();
        foreach (string sfv in sfvs)
        {
            if (excludedSet.Contains(sfv) && !wanted.Contains(sfv))
            {
                RouteExcluded(sfv, subs, warnings);
            }
            else if (subpackOrSubfixRelease && mainSet.Contains(sfv))
            {
                // A subpack/subfix release queues every MAIN sfv for nested-SRR processing too,
                // in addition to being a main set (excerpt's final `generate_srr` block).
                subs.Add(sfv);
            }
        }

        // I4 fix: re-check cancellation immediately before returning — a long final SFV/RAR read
        // that got cancelled mid-call must not silently produce a successful result.
        ct.ThrowIfCancellationRequested();

        var sets = main.Select(sfv => new ReleaseSetInput(sfv, RelativeName(releaseRoot, sfv))).ToList();
        return new ReleaseScanResult(sets, [], subs, stored, musicSfvs, warnings);
    }

    /// <summary>
    /// The per-SFV branch of <c>remove_unwanted_sfvs</c> (excerpt L294-407), rules 1-7 in
    /// sequential first-match order. Rule 4 (proof) may add directly to <paramref name="stored"/>
    /// — it is the only rule whose "excluded" outcome carries a second file (the proof RAR)
    /// alongside the SFV.
    /// </summary>
    private SfvClass ClassifySfv(string sfv, string lcRelease, List<string> warnings, List<string> stored, CancellationToken ct)
    {
        string sfvName = Path.GetFileName(sfv);
        string lcSfvName = sfvName.ToLowerInvariant();
        string dir = Path.GetDirectoryName(sfv) ?? string.Empty;
        string pardir = Path.GetFileName(dir).ToLowerInvariant();

        // excerpt: remove_unwanted_sfvs L312-317 (rule 1: vobsub/subtitle name, release lacks the carve-out)
        if ((lcSfvName.Contains("vobsub", StringComparison.Ordinal) || lcSfvName.Contains("subtitle", StringComparison.Ordinal))
            && !lcRelease.Contains("subpack", StringComparison.Ordinal)
            && !lcRelease.Contains("vobsub", StringComparison.Ordinal)
            && !lcRelease.Contains("subtitle", StringComparison.Ordinal)
            && !lcRelease.Contains("sub.pack", StringComparison.Ordinal))
        {
            return SfvClass.Excluded;
        }

        // excerpt: remove_unwanted_sfvs L319-340 (rule 2: "subs" false-positive fall-through — the
        // `pass` branch does NOT accept the SFV, it only skips ahead to rules 3-7)
        if (lcSfvName.Contains("subs", StringComparison.Ordinal))
        {
            bool fallsThrough = SubsFalsePositiveRegex().IsMatch(sfvName)
                || lcRelease.Contains("subs", StringComparison.Ordinal)
                || lcRelease.Contains("subpack", StringComparison.Ordinal)
                || lcRelease.Contains("vobsub", StringComparison.Ordinal)
                || lcRelease.Contains("subtitle", StringComparison.Ordinal)
                || lcRelease.Contains("subfix", StringComparison.Ordinal)
                || lcRelease.Contains("sub.pack", StringComparison.Ordinal);
            if (!fallsThrough)
            {
                return SfvClass.Excluded;
            }
        }

        // excerpt: remove_unwanted_sfvs L342-355 (rule 3: exact subtitle/cover/codec parent dir)
        if (_exactExcludedDirs.Contains(pardir))
        {
            return SfvClass.Excluded;
        }

        // excerpt: remove_unwanted_sfvs L357-385 (rule 4: proof state machine)
        if (pardir == "proof" || pardir == "proofs")
        {
            SfvClass? proofResult = ClassifyProof(sfv, dir, warnings, stored, ct);
            if (proofResult is { } result)
            {
                return result;
            }
            // else: not proof after all (multi-entry SFV, or a readable RAR whose last packed
            // block isn't an image) — falls through to rules 5-7.
        }

        // excerpt: remove_unwanted_sfvs L387-394 (rule 5: `.*Subs.?CD\d$` directory)
        if (SubsCdDirRegex().IsMatch(dir))
        {
            return SfvClass.Excluded;
        }

        // excerpt: remove_unwanted_sfvs L396-400 (rule 6a/6b: subpack/subfix substring parent dir)
        if (pardir.Contains("subpack", StringComparison.Ordinal) && !lcRelease.Contains("subpack", StringComparison.Ordinal))
        {
            return SfvClass.Excluded;
        }

        if (pardir.Contains("subfix", StringComparison.Ordinal) && !lcRelease.Contains("subfix", StringComparison.Ordinal))
        {
            return SfvClass.Excluded;
        }

        // excerpt: remove_unwanted_sfvs L402-405 (rule 6c: generic "fix" substring parent dir)
        if (pardir.Contains("fix", StringComparison.Ordinal) && !lcRelease.Contains("fix", StringComparison.Ordinal))
        {
            return SfvClass.Excluded;
        }

        // excerpt: remove_unwanted_sfvs L407 (rule 7: otherwise, main set)
        return SfvClass.Main;
    }

    /// <summary>
    /// Rule 4's proof state machine (excerpt L357-385). Returns <see cref="SfvClass.Proof"/> when
    /// the SFV is excluded as proof material (adding to <paramref name="stored"/> itself, since the
    /// set of files stored differs per branch); returns <see langword="null"/> to signal "not
    /// proof — fall through to rules 5-7" (a multi-entry SFV, or a readable RAR whose last packed
    /// block is not an image).
    /// </summary>
    private SfvClass? ClassifyProof(string sfv, string dir, List<string> warnings, List<string> stored, CancellationToken ct)
    {
        IReadOnlyList<string>? entries = TryReadSfvEntries(sfv, warnings);
        if (entries is null)
        {
            // I3 hardening: an unreadable SFV can't be verified as either the proof singleton or
            // anything else — warn (already done inside TryReadSfvEntries) and skip it entirely
            // (spec §2 error contract's "otherwise skipped" branch) rather than guessing it into
            // MainSets or SubtitleSfvs.
            return SfvClass.Skipped;
        }

        // excerpt: remove_unwanted_sfvs L360 (exactly one entry required)
        if (entries.Count != 1)
        {
            return null;
        }

        string entryName = entries[0];

        // excerpt: remove_unwanted_sfvs L362-363 ("e.g. .sfv for proof file" — the singleton isn't
        // even RAR-compressed; the RAR path is never checked for existence in this branch)
        if (!entryName.EndsWith(".rar", StringComparison.Ordinal))
        {
            stored.Add(sfv);
            return SfvClass.Proof;
        }

        string rarPath = Path.Combine(dir, entryName);

        // excerpt: remove_unwanted_sfvs L364-379 (readable RAR: last packed block's image-ness wins)
        if (File.Exists(rarPath))
        {
            ProofRarFacts facts = _proofRarReader(rarPath, ct);
            if (!facts.Readable)
            {
                // excerpt: remove_unwanted_sfvs L374-377 ("No RAR5 support yet" / caught
                // ValueError). Only the SFV is stored here — the RAR could not be verified as
                // proof content, so this port does not embed its raw bytes on its behalf.
                warnings.Add($"Cannot read proof RAR (unsupported or corrupt): {rarPath}");
                stored.Add(sfv);
                return SfvClass.Proof;
            }

            if (!facts.LastPackedIsImage)
            {
                return null;
            }

            stored.Add(sfv);
            stored.Add(rarPath);
            return SfvClass.Proof;
        }

        // excerpt: remove_unwanted_sfvs L380-385 (proof RAR missing on disk)
        warnings.Add($"Proof RAR cannot be found: {rarPath}");
        stored.Add(sfv);
        return SfvClass.Proof;
    }

    /// <summary>
    /// Routes a rules-1-6-excluded SFV to its destination (design spec §2a "Excluded-SFV
    /// destinations"): <see cref="ReleaseScanResult.SubtitleSfvs"/>, except an SFV whose immediate
    /// parent directory name contains "dirfix" is skipped entirely with a warning instead
    /// (pyrescene: <c>generate_srr</c>'s <c>extra_sfvs</c> loop, "not for dirfix releases moved to
    /// the main folder" — <c>"dirfix" in subdir.lower()</c>, a substring check on the immediate
    /// parent directory name only).
    /// </summary>
    private static void RouteExcluded(string sfv, List<string> subs, List<string> warnings)
    {
        string pardir = Path.GetFileName(Path.GetDirectoryName(sfv) ?? string.Empty);
        if (pardir.Contains("dirfix", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add($"Excluded SFV skipped (dirfix subdirectory): {sfv}");
            return;
        }

        subs.Add(sfv);
    }

    /// <summary>
    /// Reads an SFV's entries, converting any I/O or parse failure into a per-item warning instead
    /// of letting it crash the whole scan (design spec §2 "Error contract": scanner failures
    /// degrade to warnings, never a hard stop — item classified stored-only when readable metadata
    /// suffices, otherwise skipped). [DIVERGENCE: hardening] pyrescene's <c>parse_sfv_file</c>
    /// would crash or propagate on a malformed/inaccessible SFV.
    /// </summary>
    private IReadOnlyList<string>? TryReadSfvEntries(string sfv, List<string> warnings)
    {
        try
        {
            return _sfvEntryReader(sfv);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            warnings.Add($"Unreadable SFV: {sfv} ({e.Message})");
            return null;
        }
    }

    private static bool HasMusicExtension(string fileName) =>
        Array.Exists(_musicExtensions, ext => fileName.EndsWith(ext, StringComparison.Ordinal));

    private static string RelativeName(string releaseRoot, string fullPath) =>
        Path.GetRelativePath(releaseRoot, fullPath).Replace('\\', '/');

    private static IReadOnlyList<string> DefaultReadSfvEntries(string sfvPath) =>
        [.. SFVFile.ReadFile(sfvPath).Entries.Select(e => e.FileName)];

    // excerpt: remove_unwanted_sfvs L331 — `^000?-|.*(cd\d|flac).*` (IGNORECASE). .NET's `^` (no
    // Multiline option) anchors to the absolute string start exactly like Python's re.match, so
    // this translates directly: the first alternative only matches at position 0, the second is
    // already unanchored via its own `.*` wrapping.
    [GeneratedRegex(@"^000?-|.*(cd\d|flac).*", RegexOptions.IgnoreCase)]
    private static partial Regex SubsFalsePositiveRegex();

    // excerpt: remove_unwanted_sfvs L387
    [GeneratedRegex(@".*Subs.?CD\d$", RegexOptions.IgnoreCase)]
    private static partial Regex SubsCdDirRegex();

    private enum SfvClass
    {
        Main,
        Excluded,
        Proof,
        Skipped,
    }
}
