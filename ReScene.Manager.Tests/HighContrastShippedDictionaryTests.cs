using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Models;
using ReScene.Manager.Controls;
using ReScene.Manager.Services;

namespace ReScene.Manager.Tests;

/// <summary>
/// The rendered-pixel high-contrast tests, driven through the SHIPPED dictionary applied the way
/// the application applies it.
/// <para>
/// WHY THIS EXISTS SEPARATELY FROM <c>CreatorCompactTests</c>. That file's 46-key fixture predates
/// the real dictionary: it was written when the app had no high-contrast skin at all, and it
/// simulates one by setting 46 keys directly on <c>Application.Resources</c>. That was the honest
/// instrument available at the time and it stays as historical simulation. It cannot, however, tell
/// anyone whether the thing that SHIPS works, for two reasons: its colours are the fixture author's
/// and not <c>HighContrast.axaml</c>'s, and a top-level key set is not the mechanism the app uses.
/// <see cref="HighContrastThemeService"/> merges a <c>ResourceInclude</c> into
/// <c>Application.Resources.MergedDictionaries</c>, which resolves through a different lookup path.
/// Every test here goes through the real service, the real URI and the real values.
/// </para>
/// <para>
/// WHAT THIS DOES NOT CATCH. It renders headlessly with Skia, so it measures what Avalonia draws,
/// not what a compositing window manager finally puts on a screen. Brushes Fluent supplies and this
/// app never redefines are outside the token system and therefore outside the swap — the checkbox
/// glyph test below exists precisely to put a number on that hole rather than leave it stated. It
/// proves colours are distinguishable, never that a design is legible: font weight, glyph size and
/// the meaning a colour carries are all outside a contrast ratio. And it drives the states it names
/// (selected, pointer-over, pressed, focused) one at a time on a control hosted for the purpose, so
/// a composition that only occurs when two states coincide is not reached.
/// </para>
/// <para>
/// POPULATION AND COUNTS. The swap covers the <b>46</b> brushes in <c>Tokens.axaml</c>
/// (<c>grep -c '&lt;SolidColorBrush x:Key' ReScene.Manager/Resources/Tokens.axaml</c>), matched
/// one-for-one by <b>46</b> in <c>HighContrast.axaml</c> and guarded by
/// <see cref="HighContrastTokenTests"/>. It does NOT cover the <b>12</b> further brushes in
/// <c>Density.axaml</c> (same pattern, same scope) — the <c>TabItemHeader*</c> tab-strip keys, which
/// keep their normal-theme values under high contrast. That gap is measured and reported in the
/// round's report rather than asserted here, because widening the census is a change to a file this
/// test does not own.
/// </para>
/// </summary>
public class HighContrastShippedDictionaryTests
{
    /// <summary>WCAG AA for normal text.</summary>
    private const double TextThreshold = 4.5;

    /// <summary>WCAG 2.2 SC 1.4.11, user-interface components and graphical objects.</summary>
    private const double ComponentThreshold = 3.0;

    /// <summary>
    /// Applies the real <c>HighContrast.axaml</c> through the real service, exactly as
    /// <c>App.OnFrameworkInitializationCompleted</c> does, and removes it again on dispose.
    /// <para>
    /// Deliberately NOT a re-implementation: constructing <see cref="HighContrastThemeService"/> and
    /// calling <c>Apply</c> is what the application itself does when the platform reports
    /// <see cref="ColorContrastPreference.High"/>, so a change to the merge mechanism breaks these
    /// tests instead of leaving them quietly measuring a private copy of it.
    /// </para>
    /// </summary>
    private sealed class ShippedHighContrastScope : IDisposable
    {
        private readonly HighContrastThemeService _service;

        public ShippedHighContrastScope()
        {
            _service = new HighContrastThemeService(Application.Current!, Application.Current!.PlatformSettings);
            _service.Apply(ColorContrastPreference.High);
            Dispatcher.UIThread.RunJobs();

            // Rig validity, asserted at the point of application rather than trusted: if the merge
            // silently did nothing, every measurement below would be taken in the DEFAULT theme and
            // would pass while proving nothing about high contrast at all.
            Assert.True(_service.IsHighContrastApplied,
                "rig validity: the shipped high-contrast dictionary did not merge, so nothing below is measuring high contrast");
            Assert.Equal(Colors.Black, Resolve("WindowBackground"));
            Assert.Equal(Colors.White, Resolve("ForegroundPrimary"));
        }

        public void Dispose()
        {
            _service.Dispose();
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// Resolves a token through the app's whole merged-dictionary chain — the same lookup a
    /// <c>DynamicResource</c> performs, so the value seen here is the value a control would paint.
    /// </summary>
    private static Color Resolve(string key)
    {
        Assert.True(Application.Current!.Resources.TryGetResource(key, null, out object? value),
            $"token '{key}' does not resolve at all");
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    /// <summary>
    /// Points a property at a token the way markup does, so a control built in code re-colours on a
    /// dictionary swap exactly as one built from XAML would. Using the real markup extension rather
    /// than a captured brush is the whole reason these controls can be constructed here at all.
    /// </summary>
    private static void BindToToken(AvaloniaObject target, AvaloniaProperty property, string key) =>
        target[!property] = new DynamicResourceExtension(key);

    // ── The mechanism itself ────────────────────────────────────────────────────

    /// <summary>
    /// The scope is measuring the shipped artefact and not a simulation. Pins the dictionary URI to
    /// the service's own constant (so a moved file fails here rather than silently reverting these
    /// tests to the default theme) and checks that removal restores the normal palette, because a
    /// scope that leaked would make every later test in the assembly a high-contrast test.
    /// </summary>
    [AvaloniaFact]
    public void TheScope_DrivesTheShippedDictionary_AndLeavesNothingBehind()
    {
        Assert.Equal("avares://ReScene.Manager/Resources/HighContrast.axaml", HighContrastThemeService.DictionaryUri);

        Color beforeWindow = Resolve("WindowBackground");
        Color beforeForeground = Resolve("ForegroundPrimary");
        Assert.NotEqual(Colors.Black, beforeWindow);

        using (new ShippedHighContrastScope())
        {
            // The constructor already asserted the swap; this pins a value the fixture in
            // CreatorCompactTests happens to agree on, so the two instruments cannot drift apart
            // without someone noticing.
            Assert.Equal(Colors.Black, Resolve("PanelBackground"));
        }

        Assert.Equal(beforeWindow, Resolve("WindowBackground"));
        Assert.Equal(beforeForeground, Resolve("ForegroundPrimary"));
    }

    // ── Splitter focus visual ───────────────────────────────────────────────────

    /// <summary>
    /// The splitter's focus indication under the shipped dictionary, measured from rendered pixels
    /// at the splitter's own centre against both neighbouring panes — the same technique
    /// <c>CreatorCompactTests.MeasureSplitterFocusContrast</c> established, now with the real
    /// palette underneath it instead of a fixture's.
    /// </summary>
    [AvaloniaFact]
    public void SplitterFocusVisual_UnderTheShippedDictionary_StaysDistinctFromBothPanes()
    {
        var splitter = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch };
        var above = new Border();
        var below = new Border();
        BindToToken(above, Border.BackgroundProperty, "PanelBackground");
        BindToToken(below, Border.BackgroundProperty, "SurfaceBackground");

        var grid = new Grid { RowDefinitions = new RowDefinitions("40,8,40") };
        Grid.SetRow(above, 0);
        Grid.SetRow(splitter, 1);
        Grid.SetRow(below, 2);
        grid.Children.Add(above);
        grid.Children.Add(splitter);
        grid.Children.Add(below);

        var window = new Window { Width = 200, Height = 88, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            using (new ShippedHighContrastScope())
            {
                splitter.Focus();
                Pump();

                Assert.True(splitter.IsFocused,
                    "rig validity: the splitter never took focus, so no focus visual was rendered to measure");
                Assert.True(splitter.Bounds.Height > 0,
                    "rig validity: the splitter has no height, so its centre pixel is not its own");

                Point centre = new(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2);
                Color focus = SamplePixel(window, splitter.TranslatePoint(centre, window)!.Value);
                Color paneAbove = SamplePixel(window, splitter.TranslatePoint(new Point(centre.X, -3), window)!.Value);
                Color paneBelow = SamplePixel(window, splitter.TranslatePoint(new Point(centre.X, splitter.Bounds.Height + 3), window)!.Value);

                double vsAbove = ContrastRatio(focus, paneAbove);
                double vsBelow = ContrastRatio(focus, paneBelow);

                Assert.True(vsAbove >= ComponentThreshold,
                    $"focused splitter is {vsAbove:F2}:1 against the pane above under the shipped high-contrast " +
                    $"dictionary ({Describe(focus)} on {Describe(paneAbove)}), needs {ComponentThreshold:F1}:1");
                Assert.True(vsBelow >= ComponentThreshold,
                    $"focused splitter is {vsBelow:F2}:1 against the pane below under the shipped high-contrast " +
                    $"dictionary ({Describe(focus)} on {Describe(paneBelow)}), needs {ComponentThreshold:F1}:1");
            }
        }
        finally { window.Close(); }
    }

    // ── Checkbox glyph ──────────────────────────────────────────────────────────

    /// <summary>
    /// The checkbox glyph under the shipped dictionary. This is the test most likely to report a
    /// genuine hole rather than a pass: Fluent draws the box with its OWN theme brushes, which this
    /// app does not redefine and <c>HighContrast.axaml</c> therefore cannot reach, while the surface
    /// behind it turns black. Whatever the number is, it belongs in the record — the token census
    /// discloses "Fluent's own brushes are outside the swap" as prose, and this puts a measured
    /// ratio on it.
    /// </summary>
    [AvaloniaFact]
    public void CheckBoxGlyph_UnderTheShippedDictionary_StaysVisibleAgainstItsSurface()
    {
        var checkBox = new CheckBox { Content = "Recurse subdirectories" };
        var surface = new Border { Padding = new Thickness(12), Child = checkBox };
        BindToToken(surface, Border.BackgroundProperty, "PanelBackground");

        var window = new Window { Width = 320, Height = 96, Content = surface };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            using (new ShippedHighContrastScope())
            {
                Pump();

                Border box = checkBox.GetVisualDescendants().OfType<Border>().First(b => b.Name == "NormalRectangle");
                Assert.True(box.Bounds is { Width: > 0, Height: > 0 },
                    "rig validity: the checkbox glyph box has no size, so there is nothing rendered to sample");

                // The glyph's own edge, not its centre: an unchecked box is a BORDER on the surface,
                // so the centre would sample the surface itself and report 1.00:1 against it — a
                // measurement of nothing. Sampling half a pixel inside the top edge catches the
                // stroke that actually distinguishes the control.
                Point edge = box.TranslatePoint(new Point(box.Bounds.Width / 2, 0.5), window)!.Value;
                Point away = surface.TranslatePoint(new Point(2, 2), window)!.Value;

                Color glyphEdge = SamplePixel(window, edge);
                Color behind = SamplePixel(window, away);

                double ratio = ContrastRatio(glyphEdge, behind);
                Assert.True(ratio >= ComponentThreshold,
                    $"the checkbox glyph's border is {ratio:F2}:1 against the surface behind it under the shipped " +
                    $"high-contrast dictionary ({Describe(glyphEdge)} on {Describe(behind)}, needs " +
                    $"{ComponentThreshold:F1}:1). Fluent's own control brushes are outside this app's token system, " +
                    "so HighContrast.axaml cannot reach them — fixing this means adding the Fluent checkbox keys to " +
                    "the dictionary, not adjusting a token.");
            }
        }
        finally { window.Close(); }
    }

    // ── Field-status glyphs ─────────────────────────────────────────────────────

    /// <summary>
    /// Each field-status glyph against the surface it sits on, under the shipped dictionary. The
    /// glyph is colour-only decoration (the message beside it carries the meaning), so it is judged
    /// at the 3:1 graphical-object bar rather than the text bar.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(FieldState.Ok)]
    [InlineData(FieldState.Info)]
    [InlineData(FieldState.Warning)]
    [InlineData(FieldState.Error)]
    public void FieldStatusGlyph_UnderTheShippedDictionary_ClearsTheGraphicalObjectBar(FieldState state)
    {
        using var scope = new ShippedHighContrastScope();

        var line = new FieldStatusLine { Status = StatusFor(state) };
        var surface = new Border { Padding = new Thickness(8), Child = line };
        BindToToken(surface, Border.BackgroundProperty, "PanelBackground");

        var window = new Window { Width = 360, Height = 80, Content = surface };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Pump();

            TextBlock glyph = line.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "Glyph");
            Assert.False(string.IsNullOrEmpty(glyph.Text),
                $"rig validity: {state} renders no glyph, so its colour cannot be the signal it is claimed to be");

            Color foreground = Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground).Color;
            Color behind = Resolve("PanelBackground");

            double ratio = ContrastRatio(foreground, behind);
            Assert.True(ratio >= ComponentThreshold,
                $"the {state} status glyph is {ratio:F2}:1 on PanelBackground under the shipped high-contrast " +
                $"dictionary ({Describe(foreground)} on {Describe(behind)}), needs {ComponentThreshold:F1}:1");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// CHARACTERIZATION OF A KNOWN, UNFIXED DEFECT — this test asserts what the app currently does,
    /// not what it should do, and it is written this way on purpose so the gap is recorded in code
    /// rather than only in a report.
    /// <para>
    /// A field-status glyph does NOT follow a contrast change that happens while it is on screen.
    /// <c>FieldStateToBrushConverter</c> resolves its brush by calling <c>TryGetResource</c> inside
    /// <c>Convert</c> and returning the brush INSTANCE it found, rather than handing back a
    /// <c>DynamicResource</c> reference. A converter re-runs when its binding re-evaluates — when
    /// <c>Status.State</c> changes — and merging a dictionary does not change <c>Status.State</c>.
    /// So the glyph keeps the palette it was built with until the field's state next moves.
    /// </para>
    /// <para>
    /// SCOPE, measured rather than guessed. Three converters share the eager-resolution shape —
    /// <c>FieldStateToBrushConverter</c>, <c>BoolToBrushConverter</c> and
    /// <c>IndentDiffBrushConverter</c> (<c>grep -rn 'TryGetResource' --include=*.cs
    /// ReScene.Manager/Converters/</c>, 3 hits). The defect does NOT affect the common path: the app
    /// reads the contrast preference in <c>OnFrameworkInitializationCompleted</c> before any window
    /// exists, so a session that STARTS in high contrast builds every converter-driven brush from
    /// the high-contrast palette and is correct. It bites only on a live toggle, which is also the
    /// one path no headless test can prove the platform even raises.
    /// </para>
    /// <para>
    /// WHY IT IS NOT FIXED HERE: the repair is to drive these brushes from style setters keyed on
    /// state rather than from a converter, which is a change to a control embedded 32 times plus its
    /// existing colour assertions — a round of its own, not a coda to this one. WHEN IT IS FIXED,
    /// THIS TEST MUST FAIL, and the fix is to invert it into the assertion its name describes.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void FieldStatusGlyph_DoesNotYetFollowAContrastChangeThatHappensWhileItIsOnScreen()
    {
        var line = new FieldStatusLine { Status = StatusFor(FieldState.Ok) };
        var window = new Window { Width = 360, Height = 80, Content = line };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            TextBlock glyph = line.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "Glyph");
            Color beforeSwap = Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground).Color;

            // Premise, asserted rather than assumed: the two palettes must actually disagree about
            // this token, or "it followed the swap" and "it ignored the swap" look identical.
            Color defaultSuccess = Resolve("AccentSuccess");
            Assert.Equal(defaultSuccess, beforeSwap);

            using (new ShippedHighContrastScope())
            {
                Pump();

                Color highContrastSuccess = Resolve("AccentSuccess");
                Assert.NotEqual(defaultSuccess, highContrastSuccess);

                Color afterSwap = Assert.IsAssignableFrom<ISolidColorBrush>(glyph.Foreground).Color;
                Assert.True(afterSwap == defaultSuccess,
                    $"the status glyph now paints {Describe(afterSwap)} after a live dictionary swap. If it has " +
                    $"become {Describe(highContrastSuccess)} the converter staleness is FIXED — invert this test " +
                    "into the assertion its name describes and delete the characterization notes above.");

                // The consequence, stated as the number it is: the stale brush is measured against
                // the surface it now sits on, so the cost of not fixing this is on the record.
                double stale = ContrastRatio(afterSwap, Resolve("PanelBackground"));
                Assert.True(stale >= ComponentThreshold,
                    $"the stale glyph colour {Describe(afterSwap)} is {stale:F2}:1 on the now-black panel, below " +
                    $"{ComponentThreshold:F1}:1 — the staleness would have crossed from cosmetic into unreadable " +
                    "and this characterization must be escalated to a fix.");
            }
        }
        finally { window.Close(); }
    }

    // ── Selection and pressed states ────────────────────────────────────────────

    /// <summary>
    /// A SELECTED log row under the shipped dictionary.
    /// <para>
    /// This is the composition the token census cannot see, and the reason this file exists.
    /// <c>HighContrastTokenTests</c> checks every foreground against <c>WindowBackground</c> and
    /// skips surfaces, so it never asks what happens when text lands on a surface that the
    /// dictionary deliberately INVERTS. <c>HighContrast.axaml</c> turns
    /// <c>SelectedItemBackground</c> white on purpose, so selection reads as a change of shape
    /// rather than of shade; the log list paints its rows with <c>LogTerminalForeground</c>, which
    /// the same dictionary turns bright green. Both choices are defensible alone.
    /// </para>
    /// <para>
    /// The row background is sampled from the render and the text colour is read from the resolved
    /// brush, deliberately: sampling a glyph pixel measures antialiasing coverage rather than the
    /// colour the text is drawn in, which would understate a real failure.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void SelectedLogRow_UnderTheShippedDictionary_KeepsItsTextReadable()
    {
        var list = new ListBox
        {
            ItemsSource = new[] { "first log line", "second log line", "third log line" },
            Width = 320,
            Height = 90,
        };
        list.Classes.Add("logList");

        var window = new Window { Width = 360, Height = 120, Content = list };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            using (new ShippedHighContrastScope())
            {
                list.SelectedIndex = 0;
                Pump();

                ListBoxItem row = list.GetVisualDescendants().OfType<ListBoxItem>().First();
                Assert.True(row.IsSelected,
                    "rig validity: the first row never entered the selected state, so the :selected style never applied");
                Assert.True(row.Bounds is { Width: > 8, Height: > 2 },
                    "rig validity: the selected row has no usable area to sample");

                // Near the row's trailing edge, vertically centred: inside the selected fill and
                // clear of the label's glyphs.
                Point fill = row.TranslatePoint(new Point(row.Bounds.Width - 3, row.Bounds.Height / 2), window)!.Value;
                Color background = SamplePixel(window, fill);
                Color foreground = Resolve("LogTerminalForeground");

                double ratio = ContrastRatio(foreground, background);
                Assert.True(ratio >= TextThreshold,
                    $"a selected log row renders its text at {ratio:F2}:1 under the shipped high-contrast " +
                    $"dictionary (needs {TextThreshold:F1}:1): {Describe(foreground)} text on a " +
                    $"{Describe(background)} fill. HighContrast.axaml inverts SelectedItemBackground to white " +
                    "without inverting the foregrounds that land on it.");
            }
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// A PRESSED recent-file button under the shipped dictionary — the same defect class as the
    /// selected row, reached through a different style. <c>Button.recentItem:pressed</c> fills its
    /// content presenter with <c>ActiveBackground</c>, which the dictionary turns white, while the
    /// secondary caption inside it is <c>ForegroundSecondary</c>, which the same dictionary also
    /// turns white.
    /// <para>
    /// The button is built here rather than taken from <c>HomeView</c> on purpose: the composition
    /// under test belongs to the app-wide style in <c>Styles.axaml</c>, so exercising the class
    /// directly tests the rule itself and does not drag a ViewModel into a contrast measurement.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void PressedRecentItem_UnderTheShippedDictionary_KeepsItsTextReadable()
    {
        var caption = new TextBlock { Text = @"C:\releases\Some.Release-GROUP\file.srr" };
        BindToToken(caption, TextBlock.ForegroundProperty, "ForegroundSecondary");

        var button = new Button { Content = caption, Width = 320 };
        button.Classes.Add("recentItem");

        var window = new Window { Width = 360, Height = 120, Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            using (new ShippedHighContrastScope())
            {
                ((IPseudoClasses)button.Classes).Set(":pressed", true);
                Pump();

                // Rig validity in the form this workstream learned to insist on: if the pseudo-class
                // did not actually apply, the style never ran and a pass would mean nothing.
                Assert.Contains(":pressed", button.Classes);

                ContentPresenter presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First();
                Assert.True(presenter.Bounds is { Width: > 8, Height: > 2 },
                    "rig validity: the pressed content presenter has no usable area to sample");

                Point fill = presenter.TranslatePoint(new Point(presenter.Bounds.Width - 3, presenter.Bounds.Height / 2), window)!.Value;
                Color background = SamplePixel(window, fill);
                Color foreground = Assert.IsAssignableFrom<ISolidColorBrush>(caption.Foreground).Color;

                double ratio = ContrastRatio(foreground, background);
                Assert.True(ratio >= TextThreshold,
                    $"a pressed recent-file row renders its caption at {ratio:F2}:1 under the shipped " +
                    $"high-contrast dictionary (needs {TextThreshold:F1}:1): {Describe(foreground)} text on a " +
                    $"{Describe(background)} fill. HighContrast.axaml inverts ActiveBackground to white without " +
                    "inverting the foregrounds that land on it.");
            }
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The HOVER state, which the same dictionary treats differently — <c>HoverBackground</c> stays
    /// a dark grey rather than inverting. Included so the two failures above are shown to be a
    /// property of the INVERSION and not of "this file measures selection states harshly": the same
    /// tokens, one state over, are expected to pass comfortably.
    /// </summary>
    [AvaloniaFact]
    public void HoveredLogRow_UnderTheShippedDictionary_KeepsItsTextReadable()
    {
        using var scope = new ShippedHighContrastScope();

        Color foreground = Resolve("LogTerminalForeground");
        Color background = Resolve("HoverBackground");

        double ratio = ContrastRatio(foreground, background);
        Assert.True(ratio >= TextThreshold,
            $"a hovered log row is {ratio:F2}:1 under the shipped high-contrast dictionary " +
            $"({Describe(foreground)} on {Describe(background)}), needs {TextThreshold:F1}:1");
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static FieldStatus StatusFor(FieldState state) => state switch
    {
        FieldState.Ok => FieldStatus.Ok("Looks right."),
        FieldState.Info => FieldStatus.Info("For your information."),
        FieldState.Warning => FieldStatus.Warning("Worth a look."),
        FieldState.Error => FieldStatus.Error("Cannot continue."),
        _ => FieldStatus.None,
    };

    /// <summary>
    /// Lets layout, styling and the render timer all settle. Both pumps are needed: the first lets
    /// the style change apply, the tick draws it, and the second lets anything the draw invalidated
    /// settle before a pixel is read back.
    /// </summary>
    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Renders the window and reads one pixel back. A third copy of the technique
    /// <c>CreatorCompactTests</c> and <c>ReconstructorCompactTests</c> each carry; promoting it into
    /// the shared rig is still the open question their own comments record, and this file does not
    /// settle it.
    /// </summary>
    private static Color SamplePixel(Window window, Point pointInWindow)
    {
        var size = new PixelSize((int)Math.Ceiling(window.Bounds.Width), (int)Math.Ceiling(window.Bounds.Height));
        using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(window);

        byte[] buffer = new byte[size.Width * size.Height * 4];
        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), buffer.Length, size.Width * 4);
        }
        finally { handle.Free(); }

        int x = Math.Clamp((int)pointInWindow.X, 0, size.Width - 1);
        int y = Math.Clamp((int)pointInWindow.Y, 0, size.Height - 1);
        int offset = (y * size.Width * 4) + (x * 4);
        // Avalonia's RenderTargetBitmap default pixel format is BGRA8888.
        return Color.FromArgb(buffer[offset + 3], buffer[offset + 2], buffer[offset + 1], buffer[offset]);
    }

    private static string Describe(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

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
}
