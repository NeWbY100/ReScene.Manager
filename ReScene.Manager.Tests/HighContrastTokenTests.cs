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
    /// <summary>
    /// Every app-owned brush, across BOTH dictionaries App.axaml merges. Reading Tokens.axaml alone
    /// gave the closure claim the wrong denominator: Density.axaml carries 12 tab-strip brushes that
    /// were outside this census and outside the swap, so "every token has a counterpart" was true of
    /// a population that was not the whole population.
    /// </summary>
    private const int ExpectedTokenBrushes = 60;

    private static readonly string[] TokenFiles = ["Tokens.axaml", "Density.axaml"];

    private static readonly Regex BrushKey =
        new(@"<SolidColorBrush x:Key=""(?<key>[A-Za-z0-9]+)"" Color=""(?<color>[^""]+)""", RegexOptions.Compiled);

    /// <summary>The 12 Density.axaml brushes, named so the exemption is enumerated, not a wildcard.</summary>
    private static readonly string[] TabStrip =
    [
        "TabItemHeaderBackgroundUnselected", "TabItemHeaderBackgroundUnselectedPointerOver",
        "TabItemHeaderBackgroundUnselectedPressed", "TabItemHeaderBackgroundSelected",
        "TabItemHeaderBackgroundSelectedPointerOver", "TabItemHeaderBackgroundSelectedPressed",
        "TabItemHeaderForegroundUnselected", "TabItemHeaderForegroundUnselectedPointerOver",
        "TabItemHeaderForegroundUnselectedPressed", "TabItemHeaderForegroundSelected",
        "TabItemHeaderForegroundSelectedPointerOver", "TabItemHeaderForegroundSelectedPressed",
    ];

    /// <summary>
    /// Token brushes that intentionally have NO high-contrast override, each with the reason. Empty
    /// today: every one of the 46 is overridden. Kept as the place an exemption must be argued in
    /// writing rather than achieved by deletion.
    /// </summary>
    private static readonly (string Key, string Reason)[] DeliberatelyNotOverridden =
    [
        .. TabStrip.Select(k => (k,
            "Density.axaml's tab strip: measured against the high-contrast window background its text " +
            "already clears AA unchanged (asserted by TheTabStrip_StillClearsAaUnderHighContrast), so an " +
            "override would change appearance without changing reachability. The tab strip's SELECTION " +
            "idiom is a separate open question - see the report: the selected chip is #FF1E1E1E, which is " +
            "1.26:1 against a black HC strip and 1.09:1 against the default one, so the chip fill has " +
            "never been the signal in either theme; the underline is. Overriding these needs that idiom " +
            "decided, not a colour picked.")),
    ];


    /// <summary>
    /// Colours written as literals in view markup instead of tokens, so the high-contrast swap
    /// cannot reach them. MEASURED, with the pattern and scope stated, because the first version of
    /// this list said "3, in InspectorView" — which was the files I happened to have open, not the
    /// app. The real population, counted as
    /// <c>grep -rnoE '(Background|Foreground|BorderBrush)="(#[0-9A-Fa-f]+|[A-Za-z]+)"'</c> over
    /// <c>ReScene.Manager/{Views,Controls}/**/*.axaml</c> excluding <c>Transparent</c> (a no-paint,
    /// not a colour): <b>8 attributes across 1 file</b>, now that all three copies of the warning
    /// bar have been tokenized.
    /// <para>
    /// The warning bar was the sharp one: it existed in THREE byte-identical copies while only
    /// InspectorView had been pointed at the new tokens, so the same warning swapped on one surface
    /// and stayed amber-on-dark on the other two. ReconstructorView and ReconstructWizardBody now
    /// use the tokens too and have left this list.
    /// </para>
    /// <para>
    /// This table is COMPARED AGAINST THE MARKUP rather than against a remembered total. It used to
    /// assert only that its own rows summed to a constant, which cannot notice a file being
    /// tokenized — and did not: the two warning-bar rows survived their own fix and described a
    /// tree that no longer existed. Counting from source is the shape every other census in this
    /// workstream converged on, and the reason is exactly this.
    /// </para>
    /// </summary>
    private static readonly (string File, int Attributes, string Reason)[] LiteralColoursOutsideTheTokenSystem =
    [
        ("FileCompareView.axaml", 8,
            "drop-zone overlays and the busy scrim: translucent accent fills plus three Foreground=\"White\" " +
            "labels drawn over them. A hex-only grep misses the named colours, which is why this count " +
            "states its pattern. NOT yet tokenized, and the honest reason is that it needs a decision " +
            "rather than a mechanical swap: under high contrast the 50%-alpha border composites to " +
            "near-black against a black pane, so the drop target dims — but these are transient " +
            "drag-and-drop and busy affordances, and choosing what they should become is design work " +
            "this round did not do"),
    ];

    /// <summary>
    /// The pattern the count above states, as code. Anchored on a word boundary because an
    /// unanchored version matched <c>LastChildFill="False"</c> and reported 23 across 9 — a count
    /// that was wrong in the direction that looks like diligence.
    /// </summary>
    private static readonly Regex LiteralColourAttribute = new(
        @"(?<=^|\s)(?:Background|Foreground|BorderBrush|Fill|Stroke)=""(?<value>#[0-9A-Fa-f]{3,8}|[A-Z][A-Za-z]+)""",
        RegexOptions.Compiled | RegexOptions.Multiline);

    [AvaloniaFact]
    public void EveryDesignTokenBrush_HasAHighContrastCounterpart()
    {
        IReadOnlyDictionary<string, Color> tokens = ReadAllTokenBrushes();
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

        // Not asserted to be empty — it is not, and pretending otherwise was the defect. What is
        // asserted is that the table still DESCRIBES THE MARKUP: a file that gets tokenized has to
        // leave the list, a file that gains a literal has to join it, and a stale row naming a file
        // that no longer has literals fails rather than sitting there as fiction.
        IReadOnlyDictionary<string, int> measured = MeasureLiteralColours();
        var recorded = LiteralColoursOutsideTheTokenSystem.ToDictionary(l => l.File, l => l.Attributes, StringComparer.Ordinal);

        List<string> drift =
        [
            .. measured.Where(m => !recorded.ContainsKey(m.Key))
                .Select(m => $"{m.Key} has {m.Value} literal colour attributes and is not recorded"),
            .. recorded.Where(r => !measured.ContainsKey(r.Key))
                .Select(r => $"{r.Key} is recorded with {r.Value} but has no literal colours left — delete the row"),
            .. measured.Where(m => recorded.TryGetValue(m.Key, out int n) && n != m.Value)
                .Select(m => $"{m.Key} has {m.Value} literal colour attributes, recorded as {recorded[m.Key]}"),
        ];

        Assert.True(drift.Count == 0,
            $"the literal-colour census no longer describes the markup:{Environment.NewLine}" +
            string.Join(Environment.NewLine, drift));
    }

    /// <summary>
    /// Counts literal colour attributes per view file, from source. <c>Transparent</c> is excluded
    /// deliberately: it paints nothing, so there is no colour for the high-contrast swap to miss.
    /// </summary>
    private static IReadOnlyDictionary<string, int> MeasureLiteralColours()
    {
        string root = Path.GetDirectoryName(ResourcesRoot())!;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (string dir in new[] { "Views", "Controls" })
        {
            string path = Path.Combine(root, dir);
            if (!Directory.Exists(path)) { continue; }

            foreach (string file in Directory.EnumerateFiles(path, "*.axaml", SearchOption.AllDirectories))
            {
                int n = LiteralColourAttribute.Matches(File.ReadAllText(file))
                    .Count(m => m.Groups["value"].Value != "Transparent");
                if (n > 0) { counts[Path.GetFileName(file)] = n; }
            }
        }

        Assert.NotEmpty(counts);
        return counts;
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

    /// <summary>
    /// The twelve tab-strip brushes carry no high-contrast override, so their exemption has to be
    /// backed by measurement rather than by assertion: under the high-contrast window background,
    /// the strip's text must still clear AA on its own.
    /// </summary>
    [AvaloniaFact]
    public void TheTabStrip_StillClearsAaUnderHighContrast()
    {
        IReadOnlyDictionary<string, Color> density = ReadBrushes("Density.axaml");
        Color hcWindow = ReadBrushes("HighContrast.axaml")["WindowBackground"];
        Color selectedChip = density["TabItemHeaderBackgroundSelected"];

        foreach (string key in TabStrip.Where(k => k.Contains("Foreground", StringComparison.Ordinal)))
        {
            Color text = density[key];
            Color behind = key.Contains("Selected", StringComparison.Ordinal) ? selectedChip : hcWindow;
            double ratio = ContrastRatio(text, behind);
            Assert.True(ratio >= 4.5,
                $"{key} is {ratio:F2}:1 against the surface behind it under high contrast, below AA — the " +
                $"exemption in {nameof(DeliberatelyNotOverridden)} claims it needs no override, and that claim " +
                "no longer holds");
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
        "WarningBannerBackground",
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

    private static IReadOnlyDictionary<string, Color> ReadAllTokenBrushes()
    {
        var all = new Dictionary<string, Color>(StringComparer.Ordinal);
        foreach (string file in TokenFiles)
        {
            foreach ((string key, Color colour) in ReadBrushes(file)) { all[key] = colour; }
        }

        return all;
    }

    private static IReadOnlyDictionary<string, Color> ReadBrushes(string fileName)
    {
        string path = Path.Combine(ResourcesRoot(), fileName);
        var brushes = new Dictionary<string, Color>(StringComparer.Ordinal);
        foreach (Match m in BrushKey.Matches(File.ReadAllText(path)))
        {
            // Density's unselected tab background is the named literal "Transparent" rather than
            // hex — a no-paint, which Color.Parse handles and which counts in the population even
            // though it has no ratio worth measuring. The alpha filter in the contrast test skips it.
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
