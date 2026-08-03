using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using ReScene.Manager.Services;

namespace ReScene.Manager.Tests;

/// <summary>
/// The high-contrast dictionary must cover the SAME population as the design tokens it overrides.
/// A brush added to Tokens.axaml without a high-contrast counterpart is invisible to review and
/// silently keeps its normal-theme colour under HC, so it fails here instead.
/// <para>
/// POPULATION is read from the token source at test time, not from a list kept alongside it — the
/// shape every census in this workstream converged on after three rounds of counting the wrong
/// thing.
/// </para>
/// <para>
/// WHAT THIS DOES NOT CATCH: brushes that Fluent itself supplies and this app never redefines are
/// outside the token file and outside this census; under HC they take Fluent's own values, which no
/// test here verifies. Colours set as literals in view markup rather than as tokens are likewise
/// invisible — measured, 3 exist (InspectorView's warning bar), and they are listed by
/// <see cref="LiteralColoursOutsideTheTokenSystem"/> so the exemption is enumerated rather than
/// assumed.
/// </para>
/// </summary>
public class HighContrastTokenTests
{
    private const int ExpectedTokenBrushes = 46;

    private static readonly Regex BrushKey =
        new(@"<SolidColorBrush x:Key=""(?<key>[A-Za-z0-9]+)"" Color=""(?<color>#[0-9A-Fa-f]+)""", RegexOptions.Compiled);

    /// <summary>
    /// Token brushes that intentionally have NO high-contrast override, each with the reason. Empty
    /// today: every one of the 46 is overridden. Kept as the place an exemption must be argued in
    /// writing rather than achieved by deletion.
    /// </summary>
    private static readonly (string Key, string Reason)[] DeliberatelyNotOverridden = [];

    /// <summary>
    /// Colours written as literals in view markup instead of tokens, so the HC dictionary cannot
    /// reach them. Enumerated so the hole is a known size.
    /// </summary>
    private static readonly (string File, string Reason)[] LiteralColoursOutsideTheTokenSystem =
    [
        ("InspectorView.axaml", "the custom-packer warning bar's amber fill, border and text are literal hex"),
    ];

    [AvaloniaFact]
    public void EveryDesignTokenBrush_HasAHighContrastCounterpart()
    {
        IReadOnlyDictionary<string, Color> tokens = ReadBrushes("Tokens.axaml");
        IReadOnlyDictionary<string, Color> highContrast = ReadBrushes("HighContrast.axaml");

        Assert.True(tokens.Count == ExpectedTokenBrushes,
            $"Tokens.axaml now defines {tokens.Count} brushes, not {ExpectedTokenBrushes}. Every one needs a " +
            "high-contrast counterpart, or a written exemption in " +
            $"{nameof(DeliberatelyNotOverridden)}; then update this number.");

        var exempt = DeliberatelyNotOverridden.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        List<string> missing = [.. tokens.Keys.Where(k => !highContrast.ContainsKey(k) && !exempt.Contains(k))];

        Assert.True(missing.Count == 0,
            $"{missing.Count} design-token brushes keep their normal-theme colour under high contrast: " +
            $"{string.Join(", ", missing)}. Add each to HighContrast.axaml, or record why it needs no override.");

        List<string> stray = [.. highContrast.Keys.Where(k => !tokens.ContainsKey(k))];
        Assert.True(stray.Count == 0,
            $"{stray.Count} high-contrast overrides target brushes the token file no longer defines: " +
            $"{string.Join(", ", stray)}. They override nothing; delete them.");

        Assert.NotEmpty(LiteralColoursOutsideTheTokenSystem);
    }

    /// <summary>
    /// The overrides must actually be high contrast. Every one is checked against black, which is
    /// what every surface in the dictionary is — so this is the ratio a user really sees, not a
    /// notional one. Text clears WCAG AA for normal text (4.5:1); everything else clears the 3:1 the
    /// standard sets for UI components and graphical objects (SC 1.4.11).
    /// </summary>
    [AvaloniaFact]
    public void EveryHighContrastForeground_ClearsWcagAaAgainstTheSurfacesItSitsOn()
    {
        IReadOnlyDictionary<string, Color> hc = ReadBrushes("HighContrast.axaml");
        Color background = hc["WindowBackground"];

        var failures = new List<string>();
        foreach ((string key, Color colour) in hc)
        {
            if (IsSurface(key) || colour.A < 255) { continue; }

            double required = IsText(key) ? 4.5 : 3.0;
            double ratio = ContrastRatio(colour, background);
            if (ratio < required)
            {
                failures.Add($"{key} {Describe(colour)} on {Describe(background)} is {ratio:F2}:1, needs {required:F1}:1");
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} high-contrast tokens do not meet WCAG AA against the surface they sit on." +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    /// <summary>
    /// The three semantic signals must stay apart by HUE, not only by luminance — a user who cannot
    /// separate them cannot tell an error from a success, and both are "bright on black".
    /// </summary>
    [AvaloniaFact]
    public void TheSemanticSignals_StayDistinguishableFromOneAnother()
    {
        IReadOnlyDictionary<string, Color> hc = ReadBrushes("HighContrast.axaml");

        (string A, string B)[] mustDiffer =
        [
            ("AccentSuccess", "AccentError"),
            ("AccentSuccess", "AccentWarning"),
            ("AccentWarning", "AccentError"),
        ];

        foreach ((string a, string b) in mustDiffer)
        {
            Assert.True(hc[a] != hc[b], $"{a} and {b} are the same colour under high contrast");
            Assert.True(ContrastRatio(hc[a], hc[b]) > 1.0 || Hue(hc[a]) != Hue(hc[b]),
                $"{a} and {b} are indistinguishable under high contrast");
        }
    }

    [AvaloniaFact]
    public void TheServiceApplies_AndRemoves_TheRealShippedDictionary()
    {
        Application app = Application.Current!;
        int before = app.Resources.MergedDictionaries.Count;
        var service = new HighContrastThemeService(app, app.PlatformSettings);
        try
        {
            Assert.False(service.IsHighContrastApplied);

            service.Apply(ColorContrastPreference.High);
            Assert.True(service.IsHighContrastApplied);
            Assert.Equal(before + 1, app.Resources.MergedDictionaries.Count);

            // The merged overrides must actually win the lookup, which is the whole mechanism.
            Assert.True(app.Resources.TryGetResource("WindowBackground", null, out object? swapped));
            Assert.Equal(Colors.Black, ((ISolidColorBrush)swapped!).Color);

            // Idempotent: a ColorValuesChanged for an unrelated reason must not stack a second copy.
            service.Apply(ColorContrastPreference.High);
            Assert.Equal(before + 1, app.Resources.MergedDictionaries.Count);

            service.Apply(ColorContrastPreference.NoPreference);
            Assert.False(service.IsHighContrastApplied);
            Assert.Equal(before, app.Resources.MergedDictionaries.Count);

            Assert.True(app.Resources.TryGetResource("WindowBackground", null, out object? restored));
            Assert.Equal(Color.Parse("#FF1E1E1E"), ((ISolidColorBrush)restored!).Color);
        }
        finally { service.Dispose(); }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Which tokens are painted BEHIND content rather than on it. Enumerated rather than matched by
    /// name: the first version of this test asked whether the key contained "Background", and
    /// <c>HexHeaderBrush</c> — a header fill — failed against itself at 1.00:1 because its name says
    /// nothing about what it is. A name is not a classification.
    /// </summary>
    private static readonly HashSet<string> Surfaces = new(StringComparer.Ordinal)
    {
        "WindowBackground", "PanelBackground", "SurfaceBackground", "InputBackground",
        "HexHeaderBrush", "SystemControlBackgroundListLowBrush", "SystemControlBackgroundAltHighBrush",
        "HoverBackground", "ActiveBackground", "SelectedItemBackground",
        "SystemControlHighlightListLowBrush", "PropertyHighlightBrush",
        "HexSelectionBrush", "HexMatchHighlightBrush", "HexDiffHighlightBrush", "DiffRowBackground",
    };

    private static bool IsSurface(string key) => Surfaces.Contains(key);

    private static bool IsText(string key) =>
        key.Contains("Foreground", StringComparison.Ordinal);

    private static string Describe(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static double Hue(Color c) => c.ToHsl().H;

    private static double ContrastRatio(Color a, Color b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double RelativeLuminance(Color c) =>
        (0.2126 * Linear(c.R)) + (0.7152 * Linear(c.G)) + (0.0722 * Linear(c.B));

    private static double Linear(byte channel)
    {
        double v = channel / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    private static IReadOnlyDictionary<string, Color> ReadBrushes(string fileName)
    {
        string path = Path.Combine(ResourcesRoot(), fileName);
        var brushes = new Dictionary<string, Color>(StringComparer.Ordinal);
        foreach (Match m in BrushKey.Matches(File.ReadAllText(path)))
        {
            brushes[m.Groups["key"].Value] = Color.Parse(m.Groups["color"].Value);
        }

        return brushes;
    }

    private static string ResourcesRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "ReScene.Manager", "Resources");
            if (File.Exists(Path.Combine(candidate, "Tokens.axaml"))) { return candidate; }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException($"could not find ReScene.Manager/Resources above {AppContext.BaseDirectory}");
    }
}
