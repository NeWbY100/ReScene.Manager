using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ReScene.Manager.Tests;

/// <summary>
/// Proves the semantic style classes in <c>Resources/Styles.axaml</c> (merged into
/// <c>App.axaml</c> after <c>FluentTheme</c>) actually resolve — not just that the file parses.
/// Each case renders a control carrying one class into a headless <see cref="Window"/>, pumps
/// layout via <see cref="Dispatcher.UIThread.RunJobs"/>, and asserts a representative property
/// took the token-driven value the class sets, plus zero Avalonia binding errors overall.
/// Expected colors are hardcoded from <c>Resources/Tokens.axaml</c> (same convention as
/// <see cref="FieldStatusLineTests"/>) rather than re-resolved via <c>FindResource</c>, so a test
/// failure can't be masked by both the style and the assertion drifting together.
/// </summary>
public class StylesTests
{
    private static Color AccentPrimary => Color.Parse("#FF0078D4");
    private static Color AccentError => Color.Parse("#FFF44747");
    private static Color BorderSubtle => Color.Parse("#FF3C3C3C");
    private static Color BorderMedium => Color.Parse("#FF4D4D4D");
    private static Color PanelBackground => Color.Parse("#FF252526");
    private static Color SurfaceBackground => Color.Parse("#FF2D2D30");
    private static Color HeaderForeground => Color.Parse("#FFE0E0E0");
    private static Color PanelHeaderSeparator => Color.Parse("#FF333333");
    private static Color LogTerminalForeground => Color.Parse("#FF4EC9B0");
    private static Color StatusVersionForeground => Color.Parse("#FFAAAAAA");

    private static Color Solid(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    [AvaloniaFact]
    public void AllSemanticClasses_ResolveTheirTokenDrivenProperties_WithNoBindingErrors()
    {
        var primary = new Button { Content = "Primary", Classes = { "primary" } };
        var cancel = new Button { Content = "Cancel", Classes = { "cancel" } };
        var ghost = new Button { Content = "Ghost", Classes = { "ghost" } };
        var recentItem = new Button { Content = "Recent", Classes = { "recentItem" } };
        var toolbarToggle = new ToggleButton { Content = "Hex", Classes = { "toolbar" } };
        var statusLink = new Button { Content = "v1.0", Classes = { "link", "statusVersion" } };
        var mono = new TextBlock { Text = "DEADBEEF", Classes = { "mono" } };
        var panelHeader = new TextBlock { Text = "Section", Classes = { "panelHeader" } };
        var panelHeaderBar = new Border { Classes = { "panelHeaderBar" }, Child = new TextBlock { Text = "Header" } };
        var section = new Border { Classes = { "section" }, Child = new TextBlock { Text = "Body" } };
        var logList = new ListBox { Classes = { "logList" }, ItemsSource = new[] { "line one", "line two" } };

        var root = new StackPanel
        {
            Children =
            {
                primary, cancel, ghost, recentItem, toolbarToggle, statusLink,
                mono, panelHeader, panelHeaderBar, section, logList,
            },
        };

        using var sink = new BindingErrorSink();
        var window = new Window { Content = root };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Button.primary: accent-filled, white text, MediumRadius.
        Assert.Equal(AccentPrimary, Solid(primary.Background));
        Assert.Equal(Colors.White, Solid(primary.Foreground));
        Assert.Equal(new CornerRadius(3), primary.CornerRadius);

        // Button.cancel: transparent bg, AccentError foreground/border.
        Assert.Equal(Colors.Transparent, Solid(cancel.Background));
        Assert.Equal(AccentError, Solid(cancel.Foreground));
        Assert.Equal(AccentError, Solid(cancel.BorderBrush));

        // Button.ghost: transparent bg, subtle border.
        Assert.Equal(Colors.Transparent, Solid(ghost.Background));
        Assert.Equal(BorderSubtle, Solid(ghost.BorderBrush));

        // Button.recentItem: flat (no border), left-aligned content.
        Assert.Equal(Colors.Transparent, Solid(recentItem.Background));
        Assert.Equal(new Thickness(0), recentItem.BorderThickness);
        Assert.Equal(HorizontalAlignment.Left, recentItem.HorizontalContentAlignment);

        // ToggleButton.toolbar: compact ghost-style toggle (base/unchecked state) + WPF-matching FontSize.
        Assert.Equal(Colors.Transparent, Solid(toolbarToggle.Background));
        Assert.Equal(BorderMedium, Solid(toolbarToggle.BorderBrush));
        Assert.Equal(11, toolbarToggle.FontSize);

        // Button.statusVersion: muted rest foreground set via a STYLE setter (so it wins over
        // Button.link's HyperlinkForeground and lets :pointerover brighten it — hover is launch-smoke).
        Assert.Equal(StatusVersionForeground, Solid(statusLink.Foreground));

        // TextBlock.mono: monospaced family + size.
        Assert.Contains("Cascadia Mono", mono.FontFamily.Name, StringComparison.Ordinal);
        Assert.Equal(14, mono.FontSize);

        // TextBlock.panelHeader: semibold header foreground.
        Assert.Equal(FontWeight.SemiBold, panelHeader.FontWeight);
        Assert.Equal(HeaderForeground, Solid(panelHeader.Foreground));

        // Border.panelHeaderBar: surface background, bottom separator.
        Assert.Equal(SurfaceBackground, Solid(panelHeaderBar.Background));
        Assert.Equal(PanelHeaderSeparator, Solid(panelHeaderBar.BorderBrush));
        Assert.Equal(new Thickness(0, 0, 0, 1), panelHeaderBar.BorderThickness);

        // Border.section: panel background, subtle border, LargeRadius, card margin.
        Assert.Equal(PanelBackground, Solid(section.Background));
        Assert.Equal(BorderSubtle, Solid(section.BorderBrush));
        Assert.Equal(new CornerRadius(4), section.CornerRadius);
        Assert.Equal(new Thickness(0, 0, 0, 4), section.Margin);

        // ListBox.logList: terminal-style monospaced foreground on panel background.
        Assert.Equal(PanelBackground, Solid(logList.Background));
        Assert.Equal(LogTerminalForeground, Solid(logList.Foreground));
        Assert.Contains("Cascadia Mono", logList.FontFamily.Name, StringComparison.Ordinal);

        Assert.Empty(sink.Messages);
    }

    /// <summary>
    /// Final review, MAJOR: the re-templated <c>Expander.helpDisclosure</c> shows its header
    /// ToggleButton only in compact mode (<c>IsVisible=False</c> at normal, re-enabled by the
    /// <c>Grid.compactHeight</c> selector), and the review asked whether that leaves the UIA tree
    /// exposing an ACTIONABLE-but-invisible peer at normal size, or splits the Expander's semantics
    /// incoherently across peers. VERIFIED, not assumed — this walks the real automation tree the
    /// shared template produces in each mode, rather than trusting that
    /// <c>IsVisible=False</c> prunes a peer.
    /// <para>
    /// What the walk established (MEASURED against this template, both modes):
    /// at NORMAL the ToggleButton peer is ABSENT from the tree ENTIRELY — not merely reported
    /// offscreen — while the toggle CONTROL is still present in the visual tree (asserted below, so
    /// the absence is provably UIA pruning and not the control being missing), and the body's
    /// content stays reachable; at COMPACT the toggle appears as a keyboard-focusable Button peer
    /// carrying the Toggle pattern, with its accessible name mirrored from the Expander's own. The
    /// Expander's stock <c>ExpanderAutomationPeer</c> carries ExpandCollapse in BOTH modes and is
    /// itself not keyboard focusable — so the two peers are complementary exactly as spec §1
    /// describes ("the toggle announces its expanded/collapsed state through its own Toggle
    /// pattern, while the Expander's stock ExpanderAutomationPeer continues to expose
    /// ExpandCollapse"), never two competing actionable peers for one control.
    /// </para>
    /// <para>
    /// The name is deliberately NOT the "Help" every other caller uses: mirroring is a
    /// TemplateBinding, and a distinctive value proves the binding rather than a coincidence with a
    /// hardcoded string. Tested through the shared style directly rather than through any one view,
    /// because the template and the compact selector are what the finding is about.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void HelpDisclosure_ExposesCoherentAutomationPeers_InBothModes()
    {
        const string HelpName = "Help & links";

        var expander = new Expander { Classes = { "helpDisclosure" }, IsExpanded = true };
        expander.Content = new TextBlock { Text = "help body" };
        AutomationProperties.SetName(expander, HelpName);

        var host = new Grid();
        host.Children.Add(expander);
        var window = new Window { Width = 700, Height = 400, Content = host };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            // ── NORMAL: the header toggle is hidden by the base style ──
            ToggleButton toggle = expander.GetVisualDescendants().OfType<ToggleButton>().Single();
            Assert.False(toggle.IsVisible,
                "precondition: the base style hides the header toggle at normal size");

            AutomationPeer expanderPeer = ControlAutomationPeer.CreatePeerForElement(expander);
            Assert.Equal(HelpName, expanderPeer.GetName());
            var expandCollapse = Assert.IsAssignableFrom<IExpandCollapseProvider>(expanderPeer);
            Assert.Equal(ExpandCollapseState.Expanded, expandCollapse.ExpandCollapseState);
            Assert.False(expanderPeer.IsKeyboardFocusable(),
                "the Expander itself is not a Tab stop — the toggle is, when it exists");

            Assert.DoesNotContain(DescendantPeers(expanderPeer), p => OwnerOf(p) is ToggleButton);
            Assert.True(toggle.IsVisible == false && expander.GetVisualDescendants().Contains(toggle),
                "the toggle CONTROL is still in the visual tree, so its peer's absence above is " +
                "genuine UIA pruning of an invisible control rather than the control not existing");

            // ── COMPACT: the compactHeight selector reveals it ──
            host.Classes.Add("compactHeight");
            expander.IsExpanded = false;   // what a real compact entry leaves behind (condition 5)
            Dispatcher.UIThread.RunJobs();
            Assert.True(toggle.IsVisible);

            AutomationPeer compactExpanderPeer = ControlAutomationPeer.CreatePeerForElement(expander);
            var compactExpandCollapse = Assert.IsAssignableFrom<IExpandCollapseProvider>(compactExpanderPeer);
            Assert.Equal(ExpandCollapseState.Collapsed, compactExpandCollapse.ExpandCollapseState);

            AutomationPeer togglePeer = Assert.Single(
                DescendantPeers(compactExpanderPeer), p => OwnerOf(p) is ToggleButton);
            Assert.Equal(HelpName, togglePeer.GetName());
            Assert.Equal(AutomationControlType.Button, togglePeer.GetAutomationControlType());
            Assert.True(togglePeer.IsKeyboardFocusable());
            Assert.True(togglePeer.IsEnabled());
            Assert.False(togglePeer.IsOffscreen());
            Assert.IsAssignableFrom<IToggleProvider>(togglePeer);

            // ExpandCollapse tracks the real state, so an AT reading the region is never told
            // something different from what the toggle reports.
            expander.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ExpandCollapseState.Expanded,
                Assert.IsAssignableFrom<IExpandCollapseProvider>(
                    ControlAutomationPeer.CreatePeerForElement(expander)).ExpandCollapseState);
        }
        finally { window.Close(); }
    }

    private static Control? OwnerOf(AutomationPeer peer) =>
        peer is ControlAutomationPeer control ? control.Owner : null;

    private static IEnumerable<AutomationPeer> DescendantPeers(AutomationPeer peer)
    {
        foreach (AutomationPeer child in peer.GetChildren())
        {
            yield return child;
            foreach (AutomationPeer grandchild in DescendantPeers(child))
            {
                yield return grandchild;
            }
        }
    }
}
