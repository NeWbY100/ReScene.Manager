using ReScene.Core.Cryptography;
using ReScene.Core.IO;
using ReScene.SRR;

namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>
/// An immutable, one-time parse of the user's verification file (.sfv or .sha1), taken once at Start
/// <em>before</em> the destructive output-directory cleanup. Every downstream verification read —
/// per-set expected CRCs, the first-volume gate hashes, and the flat set's fallback volume names —
/// draws from this snapshot instead of re-reading <c>VerificationPath</c>, which may no longer exist
/// (or exist with different contents) by the time later code in the run executes (#14).
/// </summary>
internal sealed record VerificationSnapshot(HashType HashType, IReadOnlyList<(string Name, string Hash)> Entries)
{
    /// <summary>A snapshot with no entries — the no-verification-file default.</summary>
    public static readonly VerificationSnapshot Empty = new(HashType.CRC32, []);

    /// <summary>Every hash in file order — CRC32 for a .sfv snapshot, SHA1 for a .sha1 snapshot.</summary>
    public IReadOnlyList<string> AllHashes { get; } = [.. Entries.Select(e => e.Hash)];

    /// <summary>
    /// The RAR-volume entry names, in file order, filtered to <see cref="RARVolumeIdentifier.IsRARVolume"/> —
    /// matches the old <c>ResolveSfvVolumeNames</c> filtering (a stray non-volume entry, e.g. a .nfo, is excluded).
    /// </summary>
    public IReadOnlyList<string> VolumeNames { get; } =
        [.. Entries.Select(e => e.Name).Where(RARVolumeIdentifier.IsRARVolume)];

    /// <summary>
    /// Canonical dir-qualified CRC32 map (<see cref="QualifiedKey"/> -&gt; CRC). Empty for a SHA1
    /// snapshot — SHA1 entries only feed <see cref="AllHashes"/>, never per-volume verification.
    /// </summary>
    public IReadOnlyDictionary<string, string> Crc32ByName { get; } = HashType == HashType.CRC32
        ? BuildCrc32Map(Entries)
        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a CRC32 for each of <paramref name="volumeNames"/>: the canonical dir-qualified key
    /// first (<see cref="QualifiedKey"/>), else an unambiguous basename fallback — two snapshot
    /// entries sharing a basename under different directories never collapse to one match. Returns at
    /// most one entry per input volume, keyed by that volume's OWN qualified key, so merging this
    /// result with another snapshot's (e.g. an embedded-SFV-derived one) can never double-count a
    /// volume (#9). Empty when this snapshot has no CRC32 entries.
    /// </summary>
    public IReadOnlyDictionary<string, string> HashesForVolumes(IEnumerable<string> volumeNames)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Crc32ByName.Count == 0)
        {
            return result;
        }

        Dictionary<string, List<string>>? byBasename = null;
        foreach (string volume in volumeNames)
        {
            string qualified = QualifiedKey(volume);
            if (Crc32ByName.TryGetValue(qualified, out string? crc))
            {
                result[qualified] = crc;
                continue;
            }

            byBasename ??= GroupKeysByBasename();
            if (byBasename.TryGetValue(LastSegment(volume), out List<string>? candidates)
                && candidates.Count == 1
                && Crc32ByName.TryGetValue(candidates[0], out crc))
            {
                result[qualified] = crc;
            }
        }

        return result;
    }

    /// <summary>Groups this snapshot's canonical keys by basename, to detect ambiguous fallback matches.</summary>
    private Dictionary<string, List<string>> GroupKeysByBasename()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in Crc32ByName.Keys)
        {
            string basename = LastSegment(key);
            if (!map.TryGetValue(basename, out List<string>? list))
            {
                list = [];
                map[basename] = list;
            }

            list.Add(key);
        }

        return map;
    }

    private static Dictionary<string, string> BuildCrc32Map(IReadOnlyList<(string Name, string Hash)> entries)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, string hash) in entries)
        {
            map[QualifiedKey(name)] = hash;
        }

        return map;
    }

    /// <summary>
    /// The canonical dir-qualified key: separators normalized to '/', leading/trailing '/' trimmed.
    /// Keeps the extension — deliberately different from <see cref="RARVolumeIdentifier.GetArchiveSetKey"/>,
    /// which strips it. Matches the lib's private canonical key builder (Task 2).
    /// </summary>
    private static string QualifiedKey(string name) => name.Replace('\\', '/').Trim('/');

    private static readonly char[] _pathSegmentSeparators = ['/', '\\'];

    /// <summary>
    /// The last path segment, splitting on both '/' and '\' regardless of platform. App-side mirror of
    /// the lib's private <c>Manager.LastSegment</c> (Task 2) — SRR-internal volume names can carry
    /// either separator, so <see cref="Path.GetFileName(string)"/> (platform-separator-only) is unsafe here.
    /// </summary>
    public static string LastSegment(string name)
    {
        int index = name.LastIndexOfAny(_pathSegmentSeparators);
        return index < 0 ? name : name[(index + 1)..];
    }

    /// <summary>Parses a verification file (.sfv or .sha1, by extension) at <paramref name="path"/> into a snapshot.</summary>
    public static VerificationSnapshot Load(string path)
    {
        if (Path.GetExtension(path).Equals(".sha1", StringComparison.OrdinalIgnoreCase))
        {
            var sha1 = SHA1File.ReadFile(path);
            return new VerificationSnapshot(HashType.SHA1, [.. sha1.Entries.Select(e => (e.FileName, e.SHA1))]);
        }

        var sfv = SFVFile.ReadFile(path);
        return new VerificationSnapshot(HashType.CRC32, [.. sfv.Entries.Select(e => (e.FileName, e.CRC))]);
    }
}
