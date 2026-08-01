using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ReScene.Manager.Tests;

/// <summary>
/// Shared per-view test rig for the small-window layout degradation feature (produced by Task 2,
/// consumed unchanged by Tasks 3-6). Hosts a view inside a REAL shell (chrome that genuinely
/// consumes the budget the design's numbers are calibrated against — see
/// <see cref="CompactInvariantRig"/>'s 319-DIP figure) and exercises the three physical input
/// routes (wheel, keyboard, scrollbar-thumb drag) plus a real Tab-walk, all via genuine headless
/// input events — never programmatic <c>BringIntoView</c> or direct property pokes, since a
/// user-facing reachability guarantee has to be proven through the same routes a user has.
/// </summary>
internal static class CompactViewRig
{
    /// <summary>
    /// The canonical small-window shell width (spec's own worked example — 700w is what makes the
    /// 8-tab shell strip wrap to two rows, which is exactly the "wrapped shell strip 58" term in
    /// the design's 319-inner-DIP arithmetic). Fixed here so every view task hosts at the same
    /// width the design's numbers were computed against; only the height varies per call.
    /// </summary>
    private const double ShellWidth = 700;

    /// <summary>Every AdvancedShellView tab header, verbatim — see AdvancedShellView.axaml.</summary>
    private static readonly string[] ShellTabHeaders =
    [
        "Home", "Inspector", "SRR Creator", "SRS Creator",
        "RAR Reconstructor", "SRS Reconstructor", "SRS Restorer", "Compare",
    ];

    private const double Slack = 0.5;

    /// <summary>Cached across every call in the process — the shell's chrome geometry at
    /// <see cref="ShellWidth"/> is deterministic, so probing it once is both correct and cheap.</summary>
    private static double? _cachedChromeOverhead;

    /// <summary>
    /// Hosts the view in a real MainWindow shell sized so the view's inner root gets exactly
    /// innerHeight DIPs; returns the window and the inner root grid.
    /// </summary>
    /// <remarks>
    /// The shell replicates MainWindow.axaml's own chrome (Menu top, status bar bottom) plus
    /// AdvancedShellView's 8-tab TabControl (same header text, so the 700w two-row wrap is
    /// reproduced) using real Avalonia controls — not the production <c>MainWindow</c> class,
    /// which requires a full <c>MainWindowViewModel</c> object graph (real services, AppDataConfig
    /// redirection) and binds each TabItem's content to a FIXED view+VM pair in XAML, neither of
    /// which fits a rig that hosts an arbitrary, already-VM'd <paramref name="view"/> instance.
    /// <para>
    /// The real <paramref name="view"/> is shown at its FINAL window height exactly once — never
    /// at a wrong intermediate guess that then gets corrected. <c>CompactHeightBehavior</c> is
    /// stateful and hysteretic (restore-only: once compact, it takes height >= Threshold+12 to
    /// re-expand — see CompactHeightBehavior's own <c>RestoreSlack</c>); showing the view at an
    /// under-shot height first, even briefly, would let a FRESH instance latch into compact mode
    /// during that transient step and then never escape it once corrected back up to a target
    /// that (shown fresh) should have been expanded. So the chrome's overhead is measured ONCE via
    /// a throwaway PLACEHOLDER (a bare, PageMargin-only Grid — the shape every view's own root
    /// shares, with no behavior attached to latch anything) and cached; the real view's window is
    /// then set to <c>innerHeight + overhead</c> directly and shown only that one time.
    /// </para>
    /// </remarks>
    public static (Window Window, Grid InnerRoot) HostAt(Control view, double innerHeight)
    {
        var innerRoot = (Grid)((ContentControl)view).Content!;

        Window window = BuildShell(view);
        window.Width = ShellWidth;
        window.Height = innerHeight + ChromeOverhead();
        window.Show();
        Dispatcher.UIThread.RunJobs();

        double error = Math.Abs(innerHeight - innerRoot.Bounds.Height);
        if (error >= Slack)
        {
            throw new Xunit.Sdk.XunitException(
                $"HostAt's cached chrome overhead no longer matches this view's shape: target " +
                $"{innerHeight}, achieved {innerRoot.Bounds.Height:F2} at window.Height " +
                $"{window.Height:F2} (error {error:F2}). The view's own root may not be a plain " +
                "Margin-only Grid like the probe assumes.");
        }

        return (window, innerRoot);
    }

    private static double ChromeOverhead()
    {
        if (_cachedChromeOverhead is { } cached)
        {
            return cached;
        }

        var placeholderMargin = (Thickness)Application.Current!.FindResource("PageMargin")!;
        var placeholder = new Grid { Margin = placeholderMargin };
        Window probe = BuildShell(placeholder);
        probe.Width = ShellWidth;
        const double ProbeHeight = 700;
        probe.Height = ProbeHeight;
        probe.Show();
        Dispatcher.UIThread.RunJobs();

        double overhead = ProbeHeight - placeholder.Bounds.Height;
        probe.Close();

        _cachedChromeOverhead = overhead;
        return overhead;
    }

    /// <summary>
    /// Builds the rig's shell chrome around <paramref name="view"/>. No ViewModel is involved —
    /// menu/status headers are literal strings (matching MainWindow.axaml), and the tab hosting
    /// <paramref name="view"/> is arbitrarily the 5th ("RAR Reconstructor") slot regardless of
    /// which view is actually under test: only the chrome's aggregate height and the 8-header
    /// wrap behavior matter here, not a truthful reproduction of which tab holds which content.
    /// </summary>
    private static Window BuildShell(Control view)
    {
        var menu = new Menu
        {
            Items =
            {
                new MenuItem { Header = "_File" },
                new MenuItem { Header = "_Mode" },
                new MenuItem { Header = "_Help" },
            },
        };
        DockPanel.SetDock(menu, Dock.Top);

        var statusBar = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new Grid
            {
                Margin = new Thickness(4, 2),
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock { Text = "Ready" },
                    new Button { Content = "v0.0", [Grid.ColumnProperty] = 1 },
                },
            },
        };
        DockPanel.SetDock(statusBar, Dock.Bottom);

        var tabs = new TabControl { Padding = new Thickness(0) };
        for (int i = 0; i < ShellTabHeaders.Length; i++)
        {
            tabs.Items.Add(new TabItem { Header = ShellTabHeaders[i], Content = i == 4 ? view : null });
        }

        tabs.SelectedIndex = 4;

        var root = new DockPanel();
        root.Children.Add(menu);
        root.Children.Add(statusBar);
        root.Children.Add(tabs);

        return new Window
        {
            Content = root,
            FontFamily = Application.Current?.FindResource("UIFontFamily") as FontFamily
                ?? FontFamily.Default,
        };
    }

    /// <summary>
    /// Criterion C: focuses sentinel, sends genuine Tab keystrokes
    /// (<c>window.KeyPressQwerty(PhysicalKey.Tab, ...)</c>) until focus returns to it; after
    /// EVERY step asserts the focused control's rendered bounds lie within the intersection of
    /// every clipping ancestor's viewport and the window; throws with the offending control's
    /// name. Runs the cycle FORWARD (Tab) and then REVERSE (Shift+Tab), asserting at every step
    /// in both passes.
    /// </summary>
    /// <remarks>
    /// Avalonia's default top-level <c>KeyboardNavigation.TabNavigation</c> ("Continue") does
    /// NOT wrap a whole Window back to its first focusable element — confirmed empirically here,
    /// and by this codebase never setting <c>TabNavigation="Cycle"</c> anywhere (production
    /// MainWindow included). Instead, once the reachable tail of the tab order is exhausted, Tab
    /// settles into a small closed loop among the last few controls (e.g. a status-bar button, a
    /// menu item, and a checkbox bouncing between each other) rather than either advancing
    /// further or genuinely wrapping back to the very first control. The walk therefore ends
    /// successfully either on genuinely returning to the sentinel (a true cycle exists — no
    /// further proof needed) OR on revisiting an already-seen control — but a BARE repeat is not
    /// enough evidence on its own: retro-review finding #2 is that an early, erratic trap (a real
    /// bug that happens to bounce back to something already seen after only 2-3 steps) would
    /// look identical to a genuine, stable terminal loop under a "first repeat wins" rule. So a
    /// repeat that ISN'T the sentinel is CONFIRMED, not trusted blindly: one more full lap of the
    /// apparent cycle length must reproduce the IDENTICAL sequence (see
    /// <see cref="ConfirmStableLoop"/>) before the walk is accepted as done. A genuine steady
    /// state reproduces trivially; an early trap diverges and fails loudly instead of silently
    /// passing.
    /// </remarks>
    public static void AssertTabWalkStaysVisible(Window window, Control sentinel)
    {
        RunTabPass(window, sentinel, forward: true);
        RunTabPass(window, sentinel, forward: false);
    }

    private static void RunTabPass(Window window, Control sentinel, bool forward)
    {
        sentinel.Focus();
        Dispatcher.UIThread.RunJobs();
        AssertFullyVisible(sentinel, window, $"{PassName(forward)} pass, start");

        List<Control> order = [sentinel];
        const int MaxSteps = 500;
        for (int step = 1; step <= MaxSteps; step++)
        {
            Control focused = StepFocus(window, forward)
                ?? throw new Xunit.Sdk.XunitException(
                    $"Tab walk ({PassName(forward)}) lost focus entirely at step {step}.");

            AssertFullyVisible(focused, window, $"{PassName(forward)} pass, step {step}");

            if (ReferenceEquals(focused, sentinel))
            {
                return; // a true cycle back to the sentinel is unambiguous — no confirmation needed
            }

            int firstSeenAt = order.FindIndex(c => ReferenceEquals(c, focused));
            if (firstSeenAt >= 0)
            {
                ConfirmStableLoop(window, forward, order, firstSeenAt, order.Count - firstSeenAt);
                return;
            }

            order.Add(focused);
        }

        throw new Xunit.Sdk.XunitException(
            $"Tab walk ({PassName(forward)}) neither cycled back to the sentinel nor reached a " +
            $"stable terminal loop within {MaxSteps} steps.");
    }

    /// <summary>
    /// Steps forward <paramref name="cycleLength"/> more times and requires EVERY step to
    /// reproduce the exact control the first lap saw at that position (asserting visibility at
    /// each, same as the main walk) — the escapable half of the terminal-loop boundary: an early
    /// trap that only coincidentally repeated once diverges here and throws, rather than being
    /// silently accepted as "done".
    /// </summary>
    private static void ConfirmStableLoop(Window window, bool forward, List<Control> order, int cycleStart, int cycleLength)
    {
        for (int i = 0; i < cycleLength; i++)
        {
            Control expected = order[cycleStart + (i + 1) % cycleLength];
            Control focused = StepFocus(window, forward)
                ?? throw new Xunit.Sdk.XunitException(
                    $"Tab walk ({PassName(forward)}) lost focus entirely during terminal-loop confirmation (step {i}).");

            AssertFullyVisible(focused, window, $"{PassName(forward)} pass, terminal-loop confirmation step {i}");

            if (!ReferenceEquals(focused, expected))
            {
                throw new Xunit.Sdk.XunitException(
                    $"Tab walk ({PassName(forward)}) terminal loop did not reproduce: expected " +
                    $"{Describe(expected)} but got {Describe(focused)} at confirmation step {i} — " +
                    "this looks like an early trap, not a genuine stable cycle.");
            }
        }
    }

    private static string PassName(bool forward) => forward ? "forward" : "reverse";

    /// <summary>Ordered (control type, effective automation name) snapshot of the Tab cycle.</summary>
    /// <remarks>
    /// Requires focus to already sit inside <paramref name="root"/> (focus its intended starting
    /// control before calling — mirrors <see cref="AssertTabWalkStaysVisible"/>'s explicit
    /// sentinel). Records forward-Tab steps while focus remains a descendant of
    /// <paramref name="root"/>, stopping the moment a step would leave root's scope (the walk has
    /// reached the surrounding shell chrome — unambiguous, no confirmation needed) or repeats an
    /// already-recorded control — CONFIRMED via the same one-more-lap check
    /// <see cref="RunTabPass"/> uses, for the identical reason (an early trap must not be
    /// mistaken for the view's own genuine internal cycle).
    /// </remarks>
    public static IReadOnlyList<string> SnapshotTabOrder(Window window, Control root)
    {
        Control focused = window.FocusManager?.GetFocusedElement() as Control
            ?? throw new Xunit.Sdk.XunitException(
                "SnapshotTabOrder requires focus already inside root — focus a starting control first.");

        if (!IsWithin(root, focused))
        {
            throw new Xunit.Sdk.XunitException(
                $"SnapshotTabOrder's initial focus ({Describe(focused)}) is not inside root.");
        }

        List<Control> order = [];
        const int MaxSteps = 500;
        for (int step = 0; step < MaxSteps; step++)
        {
            if (!IsWithin(root, focused))
            {
                return [.. order.Select(Describe)];
            }

            int firstSeenAt = order.FindIndex(c => ReferenceEquals(c, focused));
            if (firstSeenAt >= 0)
            {
                ConfirmStableLoopWithinRoot(window, root, order, firstSeenAt, order.Count - firstSeenAt);
                return [.. order.Select(Describe)];
            }

            order.Add(focused);
            focused = StepFocus(window, forward: true)
                ?? throw new Xunit.Sdk.XunitException(
                    $"SnapshotTabOrder lost focus entirely at step {step}.");
        }

        throw new Xunit.Sdk.XunitException(
            $"SnapshotTabOrder did not leave root or loop back within {MaxSteps} steps.");
    }

    /// <summary>See <see cref="ConfirmStableLoop"/> — the same confirmation, scoped to "still inside root" instead of visibility.</summary>
    private static void ConfirmStableLoopWithinRoot(Window window, Control root, List<Control> order, int cycleStart, int cycleLength)
    {
        for (int i = 0; i < cycleLength; i++)
        {
            Control? focused = StepFocus(window, forward: true);
            if (focused is null || !IsWithin(root, focused))
            {
                throw new Xunit.Sdk.XunitException(
                    $"SnapshotTabOrder's terminal loop did not reproduce: step {i} left root's scope " +
                    "or lost focus entirely — this looks like an early trap, not a genuine stable cycle.");
            }

            Control expected = order[cycleStart + (i + 1) % cycleLength];
            if (!ReferenceEquals(focused, expected))
            {
                throw new Xunit.Sdk.XunitException(
                    $"SnapshotTabOrder's terminal loop did not reproduce: expected {Describe(expected)} " +
                    $"but got {Describe(focused)} at confirmation step {i} — this looks like an early trap, not a genuine stable cycle.");
            }
        }
    }

    /// <summary>
    /// Criterion A, INPUT-DRIVEN (codex round-2 #9 — programmatic BringIntoView is not a user
    /// path): three routes, each asserted per target — (a) WHEEL: genuine wheel input over the
    /// scroll region until the target is fully inside the window; (b) KEYBOARD: real Tab/arrow
    /// input until the target is focused and fully visible; (c) THUMB: pointer
    /// press-drag-release on the vertical scrollbar thumb (headless MouseDown/MouseMove/MouseUp
    /// on the thumb's bounds) until visible.
    /// </summary>
    public static void AssertReachableByWheel(Window window, Control target)
    {
        if (IsFullyVisibleWithinWindow(target, window))
        {
            return;
        }

        ScrollViewer scroller = NearestScrollViewer(target);
        Point at = CenterInWindow(scroller, window);

        const int MaxTicks = 400;
        for (int tick = 0; tick < MaxTicks && !IsFullyVisibleWithinWindow(target, window); tick++)
        {
            // Negative delta.Y scrolls DOWN (increases Offset.Y, revealing later content) —
            // confirmed against ScrollContentPresenter.OnPointerWheelChanged: Offset.Y +=
            // (0 - delta.Y) * scrollSize.
            Wheel(window, at, -1);
        }

        if (!IsFullyVisibleWithinWindow(target, window))
        {
            throw new Xunit.Sdk.XunitException($"{Describe(target)} was not reachable by wheel input.");
        }
    }

    public static void AssertReachableByKeyboard(Window window, Control target)
    {
        if (IsFullyVisibleWithinWindow(target, window) &&
            ReferenceEquals(window.FocusManager?.GetFocusedElement(), target))
        {
            return;
        }

        if (window.FocusManager?.GetFocusedElement() is null)
        {
            StepFocus(window, forward: true); // establish a starting point
        }

        const int MaxSteps = 400;
        for (int step = 0; step < MaxSteps; step++)
        {
            Control? focused = window.FocusManager?.GetFocusedElement() as Control;
            if (ReferenceEquals(focused, target))
            {
                if (!IsFullyVisibleWithinWindow(target, window))
                {
                    throw new Xunit.Sdk.XunitException(
                        $"{Describe(target)} gained keyboard focus but is not fully visible.");
                }

                return;
            }

            StepFocus(window, forward: true);
        }

        throw new Xunit.Sdk.XunitException(
            $"{Describe(target)} was not reachable by Tab within {MaxSteps} steps.");
    }

    public static void AssertReachableByThumb(Window window, Control target)
    {
        if (IsFullyVisibleWithinWindow(target, window))
        {
            return;
        }

        ScrollViewer scroller = NearestScrollViewer(target);

        // scroller.GetVisualDescendants() also finds any NESTED control's own internal
        // ScrollViewer/ScrollBar (e.g. a TextBox's stock template wraps its TextPresenter in a
        // ScrollViewer for horizontal text overflow, complete with its own, normally invisible,
        // zero-range ScrollBar pair). TemplatedParent == scroller is what actually scopes the
        // search to THIS ScrollViewer's own declared scrollbar, not some descendant's.
        ScrollBar bar = scroller.GetVisualDescendants().OfType<ScrollBar>()
            .FirstOrDefault(b => b.Orientation == Orientation.Vertical && ReferenceEquals(b.TemplatedParent, scroller))
            ?? throw new Xunit.Sdk.XunitException(
                $"{Describe(target)}'s scroll region has no realized vertical ScrollBar to drag.");
        Thumb thumb = bar.GetVisualDescendants().OfType<Thumb>().FirstOrDefault()
            ?? throw new Xunit.Sdk.XunitException(
                $"{Describe(target)}'s vertical ScrollBar has no realized thumb to drag.");

        Point origin = CenterInWindow(thumb, window);
        window.MouseDown(origin, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        const int MaxSteps = 200;
        const double StepPixels = 15;
        int step = 1;
        Point last = origin;
        for (; step <= MaxSteps && !IsFullyVisibleWithinWindow(target, window); step++)
        {
            last = new Point(origin.X, origin.Y + step * StepPixels);
            window.MouseMove(last);
            Dispatcher.UIThread.RunJobs();
        }

        window.MouseUp(last, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        if (!IsFullyVisibleWithinWindow(target, window))
        {
            throw new Xunit.Sdk.XunitException($"{Describe(target)} was not reachable by thumb drag.");
        }
    }

    /// <summary>Genuine wheel input (headless <c>window.MouseWheel(point, delta)</c>).</summary>
    public static void Wheel(Window window, Point at, double dy)
    {
        window.MouseWheel(at, new Vector(0, dy));
        Dispatcher.UIThread.RunJobs();
    }

    // ── Shared helpers ───────────────────────────────────────────────

    private static Control? StepFocus(Window window, bool forward)
    {
        RawInputModifiers modifiers = forward ? RawInputModifiers.None : RawInputModifiers.Shift;
        window.KeyPressQwerty(PhysicalKey.Tab, modifiers);
        window.KeyReleaseQwerty(PhysicalKey.Tab, modifiers);
        Dispatcher.UIThread.RunJobs();
        return window.FocusManager?.GetFocusedElement() as Control;
    }

    private static bool IsWithin(Control root, Control control) =>
        ReferenceEquals(root, control) || root.IsVisualAncestorOf(control);

    private static ScrollViewer NearestScrollViewer(Control target) =>
        target.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault()
            ?? throw new Xunit.Sdk.XunitException(
                $"{Describe(target)} has no ancestor ScrollViewer to scroll it into view.");

    private static Point CenterInWindow(Visual element, Window window)
    {
        var center = new Point(element.Bounds.Width / 2, element.Bounds.Height / 2);
        return element.TranslatePoint(center, window)
            ?? throw new Xunit.Sdk.XunitException(
                $"{element.GetType().Name} could not be translated into window coordinates.");
    }

    private static void AssertFullyVisible(Control focused, Window window, string context)
    {
        if (!IsFullyVisibleWithinWindow(focused, window))
        {
            throw new Xunit.Sdk.XunitException(
                $"{Describe(focused)} is not fully visible ({context}): bounds {focused.Bounds}.");
        }
    }

    /// <summary>
    /// STRICTER than <c>CompactHeightBehavior.IsObscured</c> by design (spec's deliberate
    /// asymmetry): the behavior's relocation trigger fires on ENTIRELY obscured (bounds not
    /// intersecting the clip intersection), while this — criterion C's own bar — requires the
    /// element's bounds to lie FULLY WITHIN the intersection of every clipping ancestor's
    /// viewport and the window. Mirrors the behavior's own cumulative-intersection algorithm
    /// (progressively transform every ClipToBounds ancestor's bounds into window space and
    /// intersect) since independent per-clipper checks are provably not equivalent — see
    /// CompactHeightBehavior.IsObscured's own XML doc for the counter-example.
    /// </summary>
    private static bool IsFullyVisibleWithinWindow(Control element, Window window)
    {
        if (!element.IsAttachedToVisualTree() || !element.IsEffectivelyVisible)
        {
            return false;
        }

        if (TransformRect(element, new Rect(element.Bounds.Size), window) is not { } elementRect)
        {
            return false;
        }

        Rect combined = new(window.Bounds.Size);
        foreach (Visual ancestor in element.GetVisualAncestors())
        {
            if (ancestor is not Control clipper || !clipper.ClipToBounds)
            {
                continue;
            }

            if (TransformRect(clipper, new Rect(clipper.Bounds.Size), window) is not { } clipperRect)
            {
                return false;
            }

            combined = combined.Intersect(clipperRect);
        }

        // Slack: floating-point rounding, matching CompactInvariantRig's own tolerance.
        return elementRect.X >= combined.X - Slack
            && elementRect.Y >= combined.Y - Slack
            && elementRect.Right <= combined.Right + Slack
            && elementRect.Bottom <= combined.Bottom + Slack;
    }

    private static Rect? TransformRect(Visual from, Rect localRect, Visual to)
    {
        Point? topLeft = from.TranslatePoint(new Point(localRect.X, localRect.Y), to);
        Point? bottomRight = from.TranslatePoint(new Point(localRect.Right, localRect.Bottom), to);
        return topLeft is { } tl && bottomRight is { } br ? new Rect(tl, br) : null;
    }

    /// <summary>
    /// Retro-review finding #2: reading ONLY the attached <c>AutomationProperties.Name</c>
    /// (as before) is empty for most Buttons in this app (none of the link/toolbar buttons carry
    /// an explicit one), so every such stop collapsed to the same bare "Button:" identity — a
    /// fixture built from that cannot tell "Export Config" apart from "Import from SRR", so a
    /// same-type reorder is invisible to it. The REAL automation peer's <c>GetName()</c> is what
    /// AT actually announces: for a ContentControl (Button/ToggleButton/CheckBox/...) with no
    /// explicit Name, <c>ContentControlAutomationPeer.GetNameCore()</c> falls through to the
    /// realized content's own text — see the peer's own decompiled source, confirmed empirically
    /// below. <c>control.Name</c> (the x:Name) is a SECOND, generic fallback for controls whose
    /// peer name is ALSO empty (e.g. this app's path TextBoxes, which carry an x:Name but no
    /// AutomationProperties.Name/LabeledBy) — deliberately NOT VM/command-based (unlike a
    /// per-view local reimplementation would need), so this stays usable by every future view
    /// task without any view-specific knowledge.
    /// </summary>
    private static string Describe(Control control)
    {
        string peerName = ControlAutomationPeer.CreatePeerForElement(control).GetName();
        string identity = !string.IsNullOrEmpty(peerName) ? peerName
            : !string.IsNullOrEmpty(control.Name) ? control.Name!
            : string.Empty;
        return $"{control.GetType().Name}:{identity}";
    }
}
