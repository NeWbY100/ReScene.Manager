using System.Text.RegularExpressions;
using ReScene.Core.IO;
using ReScene.RAR;
using ReScene.SRR;

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

    // excerpt: get_sample_files L42-43 (FileType.VideoExtensions, referenced not itself excerpted
    // verbatim — the list mirrors pyrescene's rescene/utility.py)
    private static readonly string[] _videoExtensions =
    [
        ".mp4", ".m4v", ".avi", ".mkv", ".wmv", ".vob", ".m2ts", ".ts", ".mpeg", ".mpg", ".m2v", ".m2p",
    ];

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

        List<string> sampleFiles = FindSamples(all, sfvs, warnings, ct);

        var sets = main.Select(sfv => new ReleaseSetInput(sfv, RelativeName(releaseRoot, sfv))).ToList();

        // excerpt: get_start_rar_files L441-455 (design spec §2e). [DIVERGENCE: extension] the
        // excerpt derives main_rars ONLY from selected SFVs' entries and never discovers loose RAR
        // sets on its own — this port adds that discovery, gated to when zero SFVs exist anywhere
        // under the root (an SFV of any kind, even one rules 1-7 exclude, disables it entirely).
        if (sfvs.Count == 0)
        {
            sets.AddRange(DiscoverLooseRarSets(releaseRoot, all, lcRelease, ct));
        }

        // I4 fix: re-check cancellation immediately before returning — a long final SFV/RAR read
        // that got cancelled mid-call must not silently produce a successful result.
        ct.ThrowIfCancellationRequested();

        return new ReleaseScanResult(sets, sampleFiles, subs, stored, musicSfvs, warnings);
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

    /// <summary>
    /// §2c samples (excerpt: <c>get_sample_files</c> L42-68). Phase 1 flags every video-extension
    /// file (in traversal order) whose path contains "sample" or whose literal sibling
    /// <c>sample[:-4] + ".sfv"</c> exists; whatever's left falls through to phase 2, which
    /// cross-references every SFV's entries — read once, regardless of each SFV's rules-1-7
    /// classification (the excerpt reads ALL sfvs here, not just <c>main_sfvs</c>) — by exact
    /// basename.
    /// </summary>
    private List<string> FindSamples(IReadOnlyList<string> all, IReadOnlyList<string> sfvs, List<string> warnings, CancellationToken ct)
    {
        var result = new List<string>();
        var notSamples = new List<string>();

        foreach (string file in all)
        {
            if (!IsVideoExtension(Path.GetExtension(file)))
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();

            // excerpt: get_sample_files L48-52 — "sample" anywhere in the path (case-insensitive)
            // OR a sibling literally named `sample[:-4] + ".sfv"`. The Python slice always drops
            // exactly 4 characters regardless of the real extension's length — for a 3-char ext
            // (".avi") that strips the extension cleanly ("clip.avi" -> "clip.sfv"); for a 4-char
            // ext (".m2ts") it strips one character short of the extension, producing a
            // double-dot name ("clip.m2ts" -> "clip." + ".sfv" = "clip..sfv"). Preserved verbatim
            // — this quirk is intentional pyrescene behavior, not a bug to "fix". [DIVERGENCE:
            // hardening] the length guard below has no Python equivalent — `sample[:-4]` degrades
            // gracefully on a too-short string, while C#'s range operator would throw; every real
            // candidate path is far longer than 4 chars (it always carries the release root), so
            // this is unreachable in practice but kept for defensive consistency with the rest of
            // this file's hardening posture.
            string siblingSfv = (file.Length > 4 ? file[..^4] : string.Empty) + ".sfv";
            if (file.Contains("sample", StringComparison.OrdinalIgnoreCase) || File.Exists(siblingSfv))
            {
                result.Add(file);
            }
            else
            {
                notSamples.Add(file);
            }
        }

        // excerpt: get_sample_files L56-66 (phase 2 — musicvideo/multi-part MKV cross-reference).
        // Entries are read once, only when a candidate remains, matching the excerpt's "this way
        // so we don't always have to read in the SFV files unnecessarily".
        if (notSamples.Count > 0)
        {
            // excerpt: get_sample_files L59-65 — `sfv_stored_files` holds the RAW entry names (not
            // basenames); the membership test then compares the candidate's BASENAME against those
            // raw entries (`os.path.basename(nsample) in sfv_stored_files`). Storing basenames here
            // instead would be MORE permissive than pyrescene (matching subpath-qualified entries
            // too) and diverge from the golden — basename-vs-raw is intentional parity, not a bug.
            var sfvStoredFiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (string sfv in sfvs)
            {
                ct.ThrowIfCancellationRequested();
                IReadOnlyList<string>? entries = TryReadSfvEntries(sfv, warnings);
                if (entries is null)
                {
                    continue;
                }

                foreach (string entry in entries)
                {
                    sfvStoredFiles.Add(entry);
                }
            }

            foreach (string candidate in notSamples)
            {
                if (sfvStoredFiles.Contains(Path.GetFileName(candidate)))
                {
                    result.Add(candidate);
                }
            }
        }

        return result;
    }

    private static bool IsVideoExtension(string extension) =>
        Array.Exists(_videoExtensions, ext => ext.Equals(extension, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// §2e loose-RAR discovery (excerpt: <c>get_start_rar_files</c> L441-455 derives its RAR sets
    /// ONLY from SFV entries and never discovers loose RARs itself).
    /// [DIVERGENCE: extension] the caller invokes this only when zero SFVs exist anywhere under
    /// the root. Every RAR-volume file found is grouped into its archive-set chain (lib
    /// <see cref="RARVolumeIdentifier.GetArchiveSetKey"/>), sorted within the chain (lib
    /// <see cref="RARVolumeNameComparer"/>), and the chain contributes a set only when its true
    /// first volume is literally named ".rar" — a lone .r00/.001 continuation can never open a
    /// set, matching <c>SRRWriter.ResolveVolumesAsync</c>'s equivalent rule for explicit RAR
    /// inputs.
    /// </summary>
    private static List<ReleaseSetInput> DiscoverLooseRarSets(string releaseRoot, IReadOnlyList<string> all, string lcRelease, CancellationToken ct)
    {
        // Case-insensitive chain grouping matches SRRWriter.ResolveVolumesAsync's equivalent
        // dictionary (ReScene.Lib/ReScene/SRR/SRRWriter.cs ~L511) — the default Ordinal comparer
        // would otherwise split e.g. "a.part01.rar" and "A.part02.rar" into two singleton chains,
        // each independently passing the first-volume ".rar" check below and wrongly emitting the
        // continuation volume as its own set.
        var chains = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var volumeIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < all.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            string file = all[i];
            if (!RARVolumeIdentifier.IsRARVolume(Path.GetFileName(file)))
            {
                continue;
            }

            string dir = Path.GetDirectoryName(file) ?? string.Empty;
            if (IsLooseRarDirExcluded(dir, lcRelease))
            {
                continue;
            }

            string key = RARVolumeIdentifier.GetArchiveSetKey(file);
            if (!chains.TryGetValue(key, out List<string>? volumes))
            {
                volumes = [];
                chains[key] = volumes;
            }

            volumes.Add(file);
            volumeIndex[file] = i;
        }

        // Order the emitted sets by their chain's TRUE FIRST VOLUME's traversal position, not by
        // whichever volume of the chain happened to be encountered first above — a continuation
        // volume can sort earlier in traversal than its own chain's first volume (e.g. "a.r00"
        // ordinally precedes "a.rar"). Loose-RAR discovery is a [DIVERGENCE: extension] with no
        // pyrescene ordering target, so this is purely our own canonical-order correctness.
        var candidates = new List<(string First, int Index)>();
        foreach (List<string> volumes in chains.Values)
        {
            volumes.Sort(RARVolumeNameComparer.Instance);
            string first = volumes[0];
            if (string.Equals(Path.GetExtension(first), ".rar", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add((first, volumeIndex[first]));
            }
        }

        return [.. candidates
            .OrderBy(c => c.Index)
            .Select(c => new ReleaseSetInput(c.First, RelativeName(releaseRoot, c.First)))];
    }

    /// <summary>
    /// Directory-only mirror of <see cref="ClassifySfv"/>'s rules 3, 4 (pardir check only), 5, and
    /// 6 (excerpt L342-355, L357, L387-394, L396-405) for loose-RAR discovery — a bare RAR file has
    /// no SFV name or entries to run rule 4's full proof state machine, rule 1, or rule 2 against,
    /// so only the parent-directory exclusions apply.
    /// </summary>
    private static bool IsLooseRarDirExcluded(string dir, string lcRelease)
    {
        string pardir = Path.GetFileName(dir).ToLowerInvariant();

        // excerpt: remove_unwanted_sfvs L342-355 (rule 3)
        if (_exactExcludedDirs.Contains(pardir))
        {
            return true;
        }

        // design spec §2e L186-188 ("rules 3-6" includes rule 4) + excerpt L357 (proof pardir
        // check). Loose-RAR discovery has no SFV to run rule 4's full state machine against, but
        // the directory-name exclusion still applies: a proof RAR is never a release set.
        if (pardir == "proof" || pardir == "proofs")
        {
            return true;
        }

        // excerpt: remove_unwanted_sfvs L387-394 (rule 5)
        if (SubsCdDirRegex().IsMatch(dir))
        {
            return true;
        }

        // excerpt: remove_unwanted_sfvs L396-400 (rule 6a/6b)
        if (pardir.Contains("subpack", StringComparison.Ordinal) && !lcRelease.Contains("subpack", StringComparison.Ordinal))
        {
            return true;
        }

        if (pardir.Contains("subfix", StringComparison.Ordinal) && !lcRelease.Contains("subfix", StringComparison.Ordinal))
        {
            return true;
        }

        // excerpt: remove_unwanted_sfvs L402-405 (rule 6c)
        if (pardir.Contains("fix", StringComparison.Ordinal) && !lcRelease.Contains("fix", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

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
