using System.Buffers.Binary;
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

    // excerpt: PROOF_IMAGE_EXTS L75 — each entry is used as `"*" + ext` (an fnmatch SUFFIX match
    // on the whole lowered filename); since every one of these 5 strings is exactly 4 characters,
    // this collapses to "the file's last 4 characters equal one of these 5 strings" — including
    // the ".jpg"-vs-bare-"jpeg" asymmetry (a name ending in the bare letters "jpeg" with NO
    // preceding dot would also match). Same quirk already shipped in RarProofInspector's
    // image-extension check (ReScene.Lib/ReScene/RAR/RarProofInspector.cs) — kept consistent here.
    private static readonly string[] _proofImageLast4 = [".jpg", "jpeg", ".png", ".bmp", ".gif"];

    // excerpt: generate_srr L629 (log blacklist, case-insensitive exact basename match)
    private static readonly HashSet<string> _logBlacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "rushchk.log", ".upchk.log", "ufxpcrc.log",
    };

    private static readonly byte[] _pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // excerpt: rar_file_blacklist L862-892 — release names whose main RAR is never storable (e.g.
    // it contains cracked .exe content), even when is_storable_fix's gate would otherwise pass.
    // Exact-case membership test, matching Python's `in` on a list of str.
    private static readonly HashSet<string> _fixRarBlacklist = new(StringComparer.Ordinal)
    {
        "BEYOND.THE.FUTURE.FIX.THE.TIME.ARROWS.EBOOT.PATCH.100.JPN.PS3-N0DRM",
        "The.Raven.Legacy.of.a.Master.Thief.FIX-RELOADED",
        "CHAMPIONSHIP.MANAGER.2003.2004.UPDATE.V4.1.3.PATCH.FIX.CRACKED-DEViANCE",
        "CHAMPIONSHIP.MANAGER.2003.2004.UPDATE.V4.1.4.TIMER.FIX.CRACKED-DEViANCE",
        "CHROME.CRACK.FIX-DEViANCE",
        "F1.Racing.Championship.FIX.READ.NFO-HOTDOX",
        "Hunting_Unlimited_3_V1.1_NOCD_CRACK_NFOFIX-RVL",
        "LMA.Manager.2007.FiX-RELOADED",
        "MSC.PATRAN.V2001.R2A.FIX.FOR.RISE-TFL",
        "RUNAWAY.A.ROAD.ADVENTURE.FIX-DEViANCE",
        "Company.of.Heroes.Tales.of.Valor.FIX.GERMAN-0x0007",
        "Dishonored.GERMAN.FIX-0x0007",
        "OPERATION.FLASHPOINT.RESISTANCE.ADDON.FIX-DEViANCE",
        "Deus.Ex.Mankind.Divided.A.Criminal.Past.DLC.FIX-SKIDROW",
        "Bubble.Boy.DVDRip.DiVX.FIX-FIXRUS",
        "Herbie.Fully.Loaded.SUB.FIX-DiAMOND",
        "Super.Streetfighter.IV.SSFIV.Arcade.Edition.DLC.FIX.READNFO.XBOX360-MoNGoLS",
        "Arrested.Development.S02E07.FiX.DVDRip.XviD-SAPHiRE",
        "Friends.Trivia.Game.GERMAN.LAME.SITE.SCRIPTS.FIX-SiLENTGATE",
        "Warhammer.40000.Dawn.Of.War.Winter.Assault.GERMAN.CD2.LAME.SITE.SCRIPTS.FIX-SiLENTGATE",
        "Broken.Oath.1977.DVDRiP.XviD.DiRFiX-GREiD",
        "Abraham.Lincoln.Vs.Zombies.3D.2012.1080p.BluRay.RAR.FiX.x264-LiViDiTY",
        "Game.Of.Thrones.S3.D4.RAR.FIX.MULTiSUBS.COMPLETE.BLURAY-CLASSiC",
        "OPUS.Rocket.of.Whispers.RAR.FIX-TiNYiSO",
    };

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

        // ---- Task 7: stored-file chain (design spec §2d, category order nfo -> m3u -> proof
        // images -> proof RARs -> log -> cue -> pre-existing srs -> fix RAR -> input SFVs). Rule 4
        // (above) already added a proof-linked SFV+RAR success case to `stored` — every pass below
        // only APPENDS, deduping against it where the same extension could otherwise collide.
        // Task 9 splices in the GENERATED-artifact categories (6/9) and the full pass-10 reorder;
        // this is the base skeleton only.
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<string> nfoFiles = ReleaseTraversal.FilterByExtension(all, ".nfo");
        IReadOnlyList<string> m3uFiles = ReleaseTraversal.FilterByExtension(all, ".m3u");
        IReadOnlyList<string> logFiles = ReleaseTraversal.FilterByExtension(all, ".log");
        IReadOnlyList<string> cueFiles = ReleaseTraversal.FilterByExtension(all, ".cue");
        IReadOnlyList<string> srsFiles = ReleaseTraversal.FilterByExtension(all, ".srs");
        IReadOnlyList<string> rarFiles = ReleaseTraversal.FilterByExtension(all, ".rar");
        List<string> knownGoodStems = CollectKnownGoodStems(sfvs, nfoFiles, m3uFiles, rarFiles);

        stored.AddRange(NfoPass(nfoFiles));
        stored.AddRange(m3uFiles);
        stored.AddRange(GetProofImages(all, releaseName, knownGoodStems, warnings));
        stored.AddRange(GetProofRars(rarFiles, stored, warnings, ct));
        stored.AddRange(LogPass(logFiles));
        stored.AddRange(cueFiles);
        stored.AddRange(srsFiles);

        string? fixRar = TryGetFixRar(releaseName, main, stored, warnings);
        if (fixRar is not null)
        {
            stored.Add(fixRar);
        }

        // pass-10 skeleton: every input SFV, unconditionally (matching generate_srr L1190-1192's
        // `for sfv in sfvs`), deduped by resolved path against what an earlier pass already stored
        // (a proof-linked SFV from rule 4, most commonly).
        var alreadyStored = new HashSet<string>(stored.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        foreach (string sfv in sfvs)
        {
            if (alreadyStored.Add(Path.GetFullPath(sfv)))
            {
                stored.Add(sfv);
            }
        }

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

    // ============================================================================================
    // §2d — stored-file chain (Task 7)
    // ============================================================================================

    /// <summary>
    /// excerpt: generate_srr L608-617 (nfo filter) — every <c>*.nfo</c> except (case-insensitive
    /// basename) <c>imdb.nfo</c>/<c>tvmaze.nfo</c>, and except a <c>no.nfo</c> that is EXACTLY 8
    /// bytes (the excerpt's own comment: "contains the text 'no.nfo'") or that can't be sized at
    /// all — both silently skipped, matching pyrescene's own silent <c>except OSError: continue</c>
    /// (not a "[DIVERGENCE: hardening]" case; the excerpt already handles this gracefully).
    /// </summary>
    private static List<string> NfoPass(IReadOnlyList<string> nfoFiles)
    {
        var result = new List<string>();
        foreach (string nfo in nfoFiles)
        {
            string baseName = Path.GetFileName(nfo).ToLowerInvariant();
            if (baseName is "imdb.nfo" or "tvmaze.nfo")
            {
                continue;
            }

            if (baseName == "no.nfo")
            {
                try
                {
                    if (new FileInfo(nfo).Length == 8)
                    {
                        continue;
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
            }

            result.Add(nfo);
        }

        return result;
    }

    /// <summary>
    /// excerpt: generate_srr L626-631 (log filter) — every <c>*.log</c> except the exact
    /// (case-insensitive) blacklist and any name starting with a literal dot.
    /// </summary>
    private static List<string> LogPass(IReadOnlyList<string> logFiles)
    {
        var result = new List<string>();
        foreach (string log in logFiles)
        {
            string baseName = Path.GetFileName(log);
            if (_logBlacklist.Contains(baseName) || baseName.StartsWith('.'))
            {
                continue;
            }

            result.Add(log);
        }

        return result;
    }

    /// <summary>
    /// excerpt: collect_known_good_filenames L198-202 — basenames of every sfv/nfo/m3u/rar found
    /// under the root, each with its LAST FOUR CHARACTERS dropped (a literal slice, not an
    /// extension-aware strip — equivalent for these particular 4-char extensions).
    /// </summary>
    private static List<string> CollectKnownGoodStems(
        IReadOnlyList<string> sfvFiles, IReadOnlyList<string> nfoFiles, IReadOnlyList<string> m3uFiles, IReadOnlyList<string> rarFiles)
    {
        var result = new List<string>();
        foreach (string path in sfvFiles.Concat(nfoFiles).Concat(m3uFiles).Concat(rarFiles))
        {
            string baseName = Path.GetFileName(path);
            result.Add(baseName.Length > 4 ? baseName[..^4] : string.Empty);
        }

        return result;
    }

    /// <summary>
    /// excerpt: filter_proof_image_files L95-112 (keyword bypass, precedes <see cref="AlwaysSkip"/>)
    /// + <c>store_rls_root</c>'s callers — the full per-image decision chain for every proof-image
    /// candidate found under the root, in traversal order.
    /// </summary>
    private static List<string> GetProofImages(IReadOnlyList<string> all, string releaseName, List<string> knownGoodStems, List<string> warnings)
    {
        var result = new List<string>();
        foreach (string file in all)
        {
            if (!IsProofImageFile(Path.GetFileName(file)))
            {
                continue;
            }

            string lower = file.ToLowerInvariant();

            // excerpt L100-106 — keyword-path bypass runs BEFORE always_skip.
            if (IsKeywordProofPath(lower))
            {
                result.Add(file);
                continue;
            }

            if (AlwaysSkip(file, lower))
            {
                continue;
            }

            if (StoreRlsRoot(file, releaseName, knownGoodStems, warnings))
            {
                result.Add(file);
            }
        }

        return result;
    }

    // excerpt: PROOF_IMAGE_EXTS L75 (see the _proofImageLast4 field remarks for the "*"+ext ==
    // "last 4 characters" equivalence).
    private static bool IsProofImageFile(string fileName)
    {
        if (fileName.Length < 4)
        {
            return false;
        }

        return _proofImageLast4.Contains(fileName[^4..], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// excerpt: filter_proof_image_files L100-106 — "proof"/"sample" match ANYWHERE in the lowered
    /// full path; "cover"/"screenshots"/"compare" require an immediately preceding path separator
    /// (<c>os.sep + "keyword"</c>), i.e. they must start a path component. Preserved verbatim — the
    /// design spec's prose shorthand ("keyword substring") elides this distinction, but the
    /// excerpt's separator anchor is what's normative.
    /// </summary>
    private static bool IsKeywordProofPath(string lowerFullPath)
    {
        if (lowerFullPath.Contains("proof", StringComparison.Ordinal) || lowerFullPath.Contains("sample", StringComparison.Ordinal))
        {
            return true;
        }

        char sep = Path.DirectorySeparatorChar;
        return lowerFullPath.Contains(sep + "cover", StringComparison.Ordinal)
            || lowerFullPath.Contains(sep + "screenshots", StringComparison.Ordinal)
            || lowerFullPath.Contains(sep + "compare", StringComparison.Ordinal);
    }

    /// <summary>
    /// excerpt: always_skip L114-127 — space in the (original-case) basename, OR the (lowered)
    /// path-minus-extension ends in "folder", OR the (lowered) basename contains "albumartsmall",
    /// OR the (lowered) basename starts with "albumart_{".
    /// </summary>
    private static bool AlwaysSkip(string fullPath, string lowerFullPath)
    {
        string baseName = Path.GetFileName(fullPath);
        string lowerBaseName = Path.GetFileName(lowerFullPath);
        return baseName.Contains(' ', StringComparison.Ordinal)
            || SplitextStem(lowerFullPath).EndsWith("folder", StringComparison.Ordinal)
            || lowerBaseName.Contains("albumartsmall", StringComparison.Ordinal)
            || lowerBaseName.StartsWith("albumart_{", StringComparison.Ordinal);
    }

    /// <summary>
    /// excerpt: store_rls_root L128-170. Basename starting with "00"/"01" (subsumes "001") stores
    /// unconditionally; otherwise a size strictly greater than 100000 bytes AND a similar known-good
    /// name AND NOT a fixed-resolution cover stores; every other outcome is a skip + warning (both
    /// the size<=100000 and the size-ok-but-rejected branches share the same message format).
    /// </summary>
    private static bool StoreRlsRoot(string proofPath, string releaseName, List<string> knownGoodStems, List<string> warnings)
    {
        string baseName = Path.GetFileName(proofPath);

        // excerpt L135-136 — startswith(("00","01","001")): "001" is already subsumed by "00".
        if (baseName.StartsWith("00", StringComparison.Ordinal) || baseName.StartsWith("01", StringComparison.Ordinal))
        {
            return true;
        }

        long size;
        try
        {
            size = new FileInfo(proofPath).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // [DIVERGENCE: hardening] pyrescene's os.path.getsize call here is unguarded.
            warnings.Add($"Cannot read proof image size: {proofPath} ({e.Message})");
            return false;
        }

        // excerpt L140 — strictly greater than 100000.
        if (size > 100_000 && SimilarToGoodName(proofPath, knownGoodStems) && !FixedResolutionCover(proofPath))
        {
            return true;
        }

        // excerpt L147-152/163-169 — same skip_tpl message for both the size<=100000 branch and
        // the size-ok-but-rejected branch.
        warnings.Add($"'{baseName}' ({size} B) not added to SRR for release {releaseName}");
        return false;
    }

    /// <summary>
    /// excerpt: similar_to_good_name L172-196 (L918-942 duplicate) + strip_zeros L225-237.
    /// </summary>
    private static bool SimilarToGoodName(string proofPath, List<string> knownGoodStems)
    {
        const int s = 10;
        string p = Path.GetFileName(proofPath);

        foreach (string bn in knownGoodStems)
        {
            // NOTE (preserved quirk): the excerpt lowercases only the known-good-name side of each
            // compare (`bn[:s].lower()`) and leaves `p`/`strip_zeros(p)` in their original case —
            // an asymmetric comparison inherited verbatim (the excerpt's parameter is misleadingly
            // named `lproof`, but its one caller never lowercases the argument it passes).
            if (string.Equals(Slice(bn, s).ToLowerInvariant(), Slice(p, s), StringComparison.Ordinal)
                || string.Equals(Slice(StripZeros(bn), s).ToLowerInvariant(), Slice(StripZeros(p), s), StringComparison.Ordinal))
            {
                return true;
            }

            // excerpt L184-195 — possible group name before the extension; the image side is
            // split on the FULL input path (not just its basename), matching the excerpt exactly.
            string grprls = LastDashSegment(bn.ToLowerInvariant());
            string grpimg = LastDashSegment(SplitextStem(proofPath));
            if (string.Equals(grprls, grpimg, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string Slice(string s, int length) => s.Length <= length ? s : s[..length];

    private static string LastDashSegment(string s)
    {
        int idx = s.LastIndexOf('-');
        return idx < 0 ? s : s[(idx + 1)..];
    }

    /// <summary>
    /// Mirrors <c>os.path.splitext(path)[0]</c> closely enough for the proof-image paths this
    /// sees (a dot after the last path separator, with at least one basename character before
    /// it) — full dotfile-edge-case parity doesn't matter for <c>*.jpg</c>/<c>*.png</c>/etc. names.
    /// </summary>
    private static string SplitextStem(string path)
    {
        int sep = path.LastIndexOf(Path.DirectorySeparatorChar);
        int dot = path.LastIndexOf('.');
        return dot > sep + 1 ? path[..dot] : path;
    }

    // excerpt: strip_zeros L225-237 (L971-983 duplicate)
    private static string StripZeros(string fileName)
    {
        if (fileName.StartsWith("00-", StringComparison.Ordinal) || fileName.StartsWith("00_", StringComparison.Ordinal)
            || fileName.StartsWith("01-", StringComparison.Ordinal) || fileName.StartsWith("01_", StringComparison.Ordinal))
        {
            return fileName[3..];
        }

        if (fileName.StartsWith("000-", StringComparison.Ordinal) || fileName.StartsWith("000_", StringComparison.Ordinal)
            || fileName.StartsWith("001-", StringComparison.Ordinal) || fileName.StartsWith("001_", StringComparison.Ordinal))
        {
            return fileName[4..];
        }

        if (fileName.StartsWith("0000-", StringComparison.Ordinal) || fileName.StartsWith("0000_", StringComparison.Ordinal)
            || fileName.StartsWith("0001-", StringComparison.Ordinal) || fileName.StartsWith("0001_", StringComparison.Ordinal))
        {
            return fileName[5..];
        }

        return fileName;
    }

    /// <summary>
    /// excerpt: fixed_resolution_cover L238-244 — true only when the image's pixel dimensions are
    /// exactly 630x1200 (a movie-poster cover most likely added by a site script).
    /// </summary>
    private static bool FixedResolutionCover(string imagePath)
    {
        (int Width, int Height)? size = TryGetImageSize(imagePath);
        return size is { Width: 630, Height: 1200 };
    }

    /// <summary>
    /// excerpt: get_image_size L259-292 — sniffs PNG/GIF/JPEG from the first 24 header bytes (by
    /// content, not by file extension, exactly like pyrescene's imghdr). BMP (and anything else)
    /// falls to the excerpt's own `else: return`, so this always returns <see langword="null"/> for
    /// it, matching pyrescene's behavior of never treating a BMP as a fixed-resolution cover.
    /// </summary>
    private static (int Width, int Height)? TryGetImageSize(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> head = stackalloc byte[24];
            if (fs.Read(head) != 24)
            {
                return null;
            }

            // excerpt L266-270 — PNG: 8-byte signature, IHDR width/height at offset 16 (BE uint32).
            if (head[..8].SequenceEqual(_pngSignature))
            {
                uint check = BinaryPrimitives.ReadUInt32BigEndian(head[4..8]);
                if (check != 0x0d0a1a0a)
                {
                    return null;
                }

                int width = (int)BinaryPrimitives.ReadUInt32BigEndian(head[16..20]);
                int height = (int)BinaryPrimitives.ReadUInt32BigEndian(head[20..24]);
                return (width, height);
            }

            // excerpt L271-272 — GIF: 6-byte signature, LE uint16 width/height immediately after.
            if (head[..6].SequenceEqual("GIF87a"u8) || head[..6].SequenceEqual("GIF89a"u8))
            {
                int width = BinaryPrimitives.ReadUInt16LittleEndian(head[6..8]);
                int height = BinaryPrimitives.ReadUInt16LittleEndian(head[8..10]);
                return (width, height);
            }

            // [DIVERGENCE: simplified] excerpt L247-257's custom test_jpeg (appended to imghdr's
            // own JFIF/Exif sniff) requires an ICC_PROFILE/Adobe marker, and its third fallback
            // branch (`h[0:4] == "\xff\xd8\xff\xdb"`) compares bytes to a Python str literal — dead
            // code under Python 3, never true. Rather than inherit that bug, any SOI-starting
            // (FF D8) file is probed as a JPEG here, which is more permissive but preserves the
            // intent (detect JPEG dimensions).
            if (head[0] == 0xFF && head[1] == 0xD8)
            {
                return TryReadJpegSize(fs);
            }

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// excerpt: get_image_size L273-287 — walks JPEG segments until a SOFn marker (0xC0-0xCF,
    /// excluding 0xC4/0xC8/0xCC which share the range but aren't SOF) is found, then reads
    /// height/width immediately after it.
    /// </summary>
    private static (int Width, int Height)? TryReadJpegSize(Stream fs)
    {
        fs.Position = 0;
        int size = 2;
        int ftype = 0;
        int guard = 0;
        Span<byte> lenBuf = stackalloc byte[2];
        while (!IsSofMarker(ftype))
        {
            // [DIVERGENCE: hardening] a malformed/hostile segment-length field could otherwise
            // seek backward and loop forever; pyrescene has no equivalent guard (it would hang).
            if (++guard > 200)
            {
                return null;
            }

            fs.Seek(size, SeekOrigin.Current);
            int b = fs.ReadByte();
            if (b < 0)
            {
                return null;
            }

            while (b == 0xff)
            {
                b = fs.ReadByte();
                if (b < 0)
                {
                    return null;
                }
            }

            ftype = b;
            if (fs.Read(lenBuf) != 2)
            {
                return null;
            }

            size = BinaryPrimitives.ReadUInt16BigEndian(lenBuf) - 2;
        }

        fs.Seek(1, SeekOrigin.Current);
        Span<byte> dims = stackalloc byte[4];
        if (fs.Read(dims) != 4)
        {
            return null;
        }

        int height = BinaryPrimitives.ReadUInt16BigEndian(dims[..2]);
        int width = BinaryPrimitives.ReadUInt16BigEndian(dims[2..]);
        return (width, height);
    }

    private static bool IsSofMarker(int ftype) => ftype is >= 0xc0 and <= 0xcf && ftype is not (0xc4 or 0xc8 or 0xcc);

    /// <summary>
    /// excerpt: filter_proof_rar_files L204-211 (independent pass — unlike proof images, gated
    /// only by "proof" appearing anywhere in the lowered path, no keyword-vs-always_skip split).
    /// [Task-5 forward note] rule 4 (above, in <see cref="ClassifyProof"/>) may already have stored
    /// this exact RAR as the success case of a proof-linked singleton SFV — deduped by resolved
    /// path against <paramref name="stored"/> so it is never added twice.
    /// </summary>
    private List<string> GetProofRars(IReadOnlyList<string> rarFiles, List<string> stored, List<string> warnings, CancellationToken ct)
    {
        var result = new List<string>();
        var alreadyStored = new HashSet<string>(stored.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);

        foreach (string rar in rarFiles)
        {
            ct.ThrowIfCancellationRequested();
            if (!rar.Contains("proof", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ProofRarFacts facts = _proofRarReader(rar, ct);
            if (!facts.Readable)
            {
                warnings.Add($"Cannot read proof RAR (unsupported or corrupt): {rar}");
                continue;
            }

            if (!facts.AnyImage)
            {
                continue;
            }

            if (alreadyStored.Add(Path.GetFullPath(rar)))
            {
                result.Add(rar);
            }
        }

        return result;
    }

    /// <summary>
    /// excerpt: generate_srr L784-798 (conditional fix RAR) + is_storable_fix L516-524 — stores the
    /// single main RAR only when: the release name matches <see cref="IsStorableFix"/>; there is
    /// exactly one main SFV, listing exactly one entry that is itself a first-volume <c>.rar</c>;
    /// the release isn't in the hardcoded blacklist; and the resolved RAR isn't already queued for
    /// storage (excerpt: "prevent duplicate file add").
    /// </summary>
    private string? TryGetFixRar(string releaseName, List<string> mainSfvs, List<string> stored, List<string> warnings)
    {
        if (mainSfvs.Count != 1 || !IsStorableFix(releaseName) || _fixRarBlacklist.Contains(releaseName))
        {
            return null;
        }

        IReadOnlyList<string>? entries = TryReadSfvEntries(mainSfvs[0], warnings);
        if (entries is null || entries.Count != 1)
        {
            return null;
        }

        string entryName = entries[0];
        if (!string.Equals(Path.GetExtension(entryName), ".rar", StringComparison.OrdinalIgnoreCase)
            || !RARVolumeIdentifier.IsRARVolume(entryName))
        {
            // get_start_rar_files only ever yields a chain's TRUE FIRST volume — a single
            // non-".rar" or continuation-volume (.r00/.part02.rar) entry can never be "the" main
            // RAR here.
            return null;
        }

        string rarPath = Path.Combine(Path.GetDirectoryName(mainSfvs[0]) ?? string.Empty, entryName);
        if (!File.Exists(rarPath))
        {
            // [DIVERGENCE: hardening] the excerpt's gate has no explicit existence check here.
            warnings.Add($"Fix RAR cannot be found: {rarPath}");
            return null;
        }

        // excerpt L793-794 — "prevent duplicate file add".
        string resolved = Path.GetFullPath(rarPath);
        bool alreadyStored = stored.Any(s => string.Equals(Path.GetFullPath(s), resolved, StringComparison.OrdinalIgnoreCase));
        return alreadyStored ? null : rarPath;
    }

    // excerpt: is_storable_fix L516-524 (L1060-1071 duplicate) — four alternatives OR'd together;
    // only the FIRST is case-insensitive (re.IGNORECASE passed in the excerpt); the remaining three
    // are case-sensitive (no flag) — preserved exactly, not unified into one IgnoreCase regex.
    private static bool IsStorableFix(string releaseName) =>
        FixNameRegex1().IsMatch(releaseName) || FixNameRegex2().IsMatch(releaseName)
        || FixNameRegex3().IsMatch(releaseName) || FixNameRegex4().IsMatch(releaseName);

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

    // excerpt: is_storable_fix L521-523 — the only case-insensitive alternative. "proof?" makes
    // the trailing 'f' optional (matches "pro" or "proof"); `.?` allows 0 or 1 arbitrary character
    // between the keyword and "Fix"/"Patch". .NET's `^` (no Multiline option) mirrors re.match's
    // start anchor, same rationale as SubsFalsePositiveRegex above.
    [GeneratedRegex(@"^.*(SFV|PPF|sync|proof?|dir|nfo|Interleaving|Trackorder).?(Fix|Patch).*", RegexOptions.IgnoreCase)]
    private static partial Regex FixNameRegex1();

    // excerpt: is_storable_fix L1068 — case-sensitive (no re.IGNORECASE in the excerpt).
    [GeneratedRegex(@"^.*\.(FiX|FIX)(\.|-).*")]
    private static partial Regex FixNameRegex2();

    // excerpt: is_storable_fix L1069 — case-sensitive; the `.` between "DVDR" and "Fix-" is an
    // UNESCAPED regex metacharacter (any single character), not a literal dot, preserved verbatim.
    [GeneratedRegex(@"^.*\.DVDR.Fix-.*")]
    private static partial Regex FixNameRegex3();

    // excerpt: is_storable_fix L1070 — same unescaped-dot quirk, twice.
    [GeneratedRegex(@"^.*\.DVDR.REPACK.Fix-.*")]
    private static partial Regex FixNameRegex4();

    private enum SfvClass
    {
        Main,
        Excluded,
        Proof,
        Skipped,
    }
}
