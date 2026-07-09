using Avalonia.Platform.Storage;

namespace ReScene.Manager.Services;

/// <summary>
/// Converts the app's WPF-style filter strings (<c>"Description|*.ext1;*.ext2"</c>) into Avalonia
/// <see cref="FilePickerFileType"/> instances for <c>StorageProvider</c> pickers. Kept a pure static
/// helper so the conversion can be unit-tested without a window / <c>TopLevel</c>.
/// </summary>
public static class FilePickerFilters
{
    /// <summary>
    /// Converts each <c>"Description|*.ext1;*.ext2"</c> entry into a <see cref="FilePickerFileType"/>
    /// named by the description with the split patterns. An all-files pattern (<c>*.*</c> or <c>*</c>)
    /// is normalized to <c>"*"</c>, which Avalonia understands as "all files". Blank entries are
    /// skipped; an entry with no <c>'|'</c> separator is treated as both name and pattern.
    /// </summary>
    public static FilePickerFileType[] ToFileTypes(IReadOnlyList<string> filters)
    {
        ArgumentNullException.ThrowIfNull(filters);

        var result = new List<FilePickerFileType>(filters.Count);
        foreach (string filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                continue;
            }

            string name;
            string patternSegment;
            int separator = filter.IndexOf('|', StringComparison.Ordinal);
            if (separator >= 0)
            {
                name = filter[..separator].Trim();
                patternSegment = filter[(separator + 1)..];
            }
            else
            {
                // No description — use the whole entry as both the display name and the pattern.
                name = filter.Trim();
                patternSegment = filter;
            }

            string[] patterns = patternSegment
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizePattern)
                .ToArray();

            result.Add(new FilePickerFileType(name) { Patterns = patterns });
        }

        return [.. result];
    }

    private static string NormalizePattern(string pattern) => pattern is "*.*" or "*" ? "*" : pattern;
}
