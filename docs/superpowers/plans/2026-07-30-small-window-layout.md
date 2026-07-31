# Small-Window Layout Degradation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps
> use checkbox (`- [ ]`) syntax for tracking.

**Status: rev 9 — codex rounds 1-2 + a11y riders folded. PENDING CODEX ROUND 3 — do
not execute until codex approves (user directive: codex gates every step).**

(The formerly-pending SRSCreator inventory nit is folded — spec rev 7 corrected it.)

**Goal:** Below each task view's fit floor, panes shrink and scroll instead of clipping,
header chrome collapses behind a Help disclosure, and keyboard focus can never land
outside the window — per spec `docs/superpowers/specs/2026-07-30-small-window-layout-design.md`
(rev 6, a11y-APPROVED with conditions folded; codex gate moves to this plan).

**Architecture:** One attached behavior (`CompactHeightBehavior`) toggles a `compactHeight`
style class on each view's inner layout root from its measured height, applies per-view
`RowSizes` (RowDefinitions are not styleable), and stages focus across transitions. All
structures are always present — mode changes only sizing constraints and visibility.
An executable per-view invariant test pins the height arithmetic (thresholds, compact
floors ≤ 307, help-donation sum).

**Tech Stack:** Avalonia 11.3.18, .NET 10, xUnit + Avalonia.Headless (existing
ReScene.Manager.Tests harness), frame rig + ava-desktop for visual verification.

## Global Constraints

- Spec rev 7 is normative; its §1 numbers are design targets — the invariant test measures
  rendered truth. All heights in inner-content DIPs (view's inner layout root).
- 307 = the hard CI bound (319 available at 700×450 minus 12 jitter slack).
- Pixel-identical at normal sizes (criterion F: five-view frame-rig parity + both-mode
  tab-order snapshots).
- No reparenting; single-instance content; styles for what selectors reach, behavior for
  RowDefinitions. Any label gaining `TextWrapping` takes `Classes="wrapLabel"`.
- Style-priority rule (Styles.axaml glyph comment): template-inline and local values beat
  unconditional styles; class/pseudo-token selectors (`.compactHeight …`) run at
  StyleTrigger and win — locals that must yield (splitter Background, chrome IsVisible)
  move into styles first.
- Forced rebuilds before trusting any XAML-behavior probe or red/green claim
  (stale-XAML hazard; marker-scan or `-t:Rebuild`).
- One top-level type per file; internal types; commit trailers per house rules; never
  `git add -A`.

---

### Task 1: CompactHeightBehavior + threshold-invariant rig

**Files:**
- Create: `ReScene.Manager/Behaviors/CompactHeightBehavior.cs`
- Create: `ReScene.Manager/Behaviors/CompactRowSize.cs`
- Create: `ReScene.Manager/Behaviors/CompactRowMode.cs`
- Create: `ReScene.Manager.Tests/CompactHeightBehaviorTests.cs`
- Create: `ReScene.Manager.Tests/CompactInvariantRig.cs` (shared helper, used by every
  view task's invariant test)

**Interfaces (later tasks consume):**
- `CompactHeightBehavior.ThresholdProperty` (attached `double`, inner DIPs; NaN = off).
- `CompactHeightBehavior.RowSizesProperty` (attached `IReadOnlyList<CompactRowSize>`).
- `CompactHeightBehavior.HelpOpenProperty` (attached `bool`; managed by the HelpExpander
  wiring; while compact AND open, donation values apply).
- `CompactHeightBehavior.RestoreFocusTargetProperty` (attached `Control?`; the per-view
  control focused when leaving compact strands focus — spec rev 7). The COMPACT-direction
  target is derived: the realized header ToggleButton of the attached HelpExpander (the
  Expander control itself is not focusable).
- `CompactHeightBehavior.HelpBodyMaxHeightProperty` (attached `double`; the behavior
  applies it as MaxHeight on the expander body's internal ScrollViewer — the
  invariant-verified donated budget, per view).
- `CompactRowSize` record + `CompactRowMode` enum (one type per file):
  `internal sealed record CompactRowSize(int RowIndex, double NormalHeight, double CompactMinHeight, double HelpOpenMinHeight, CompactRowMode Mode);`
  `internal enum CompactRowMode { MinOnly, PixelRestore, AutoToStar }`
  — `MinOnly`: Height untouched, MinHeight swaps per mode (Reconstructor's star rows).
  — `PixelRestore`: compact sets `Height = CompactMinHeight` px; expand restores
    `Height = NormalHeight` px unless a splitter drag was captured (CreatorView's
    stored-files row).
  — `AutoToStar`: compact sets `Height = 1*` + `MinHeight = CompactMinHeight`; expand
    restores `Height = Auto`, `MinHeight = 0` (the three-band config rows: natural at
    normal size, squeezed star in compact).
- Style class contract: `compactHeight` present on the attached control while compact.
- **Descendant application (CreatorView needs it):** `RowSizesProperty` may also be
  attached to DESCENDANT grids of the threshold-bearing root (e.g. a grid inside the
  config band's ScrollViewer, whose own bounds never shrink and so cannot carry a
  threshold). On mode/help changes the owning behavior applies its root's RowSizes AND
  every descendant-attached list; descendants are COLLECTED AT EACH APPLY (a cheap
  visual-tree walk on mode/help changes only) — no cached discovery, so attachment order
  and late tree construction cannot produce stale row sets.
- `CompactInvariantRig.MeasureFloor(Control innerRoot)` → double (sum of the root's
  children's desired heights + spacing at width 676, conditionals forced by the caller).

- [ ] **Step 1: Write the failing behavior tests** (`CompactHeightBehaviorTests.cs`).
  Test view: a `Grid` with three rows and a `Border` filler, behavior attached, hosted in
  a `Window` whose `Height` the test drives; `Dispatcher.UIThread.RunJobs()` after sizing.

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ReScene.Manager.Behaviors;

namespace ReScene.Manager.Tests;

/// <summary>
/// Contract tests for <see cref="CompactHeightBehavior"/> (spec §1): threshold semantics
/// with restore-only hysteresis, ignored zero bounds, RowSizes application with
/// splitter-capture, help-open donation, class preservation, and staged focus.
/// </summary>
public class CompactHeightBehaviorTests
{
    private const double Threshold = 300;

    private static (Window Window, Grid Root) Host(double height, IReadOnlyList<CompactRowSize>? rows = null)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,150,*"),
        };
        root.Children.Add(new Border { Height = 40, [Grid.RowProperty] = 0 });
        root.Children.Add(new Border { [Grid.RowProperty] = 1 });
        root.Children.Add(new Border { [Grid.RowProperty] = 2 });
        CompactHeightBehavior.SetThreshold(root, Threshold);
        if (rows is not null)
        {
            CompactHeightBehavior.SetRowSizes(root, rows);
        }

        var window = new Window { Width = 700, Height = height, Content = root };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, root);
    }

    [AvaloniaFact]
    public void FreshInstance_AtThresholdPlusOne_IsExpanded()
    {
        (Window w, Grid root) = Host(Threshold + 1);
        try
        {
            Assert.DoesNotContain("compactHeight", root.Classes);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void FreshInstance_BelowThreshold_IsCompact()
    {
        (Window w, Grid root) = Host(Threshold - 1);
        try
        {
            Assert.Contains("compactHeight", root.Classes);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Hysteresis_RestoreOnlyAtThresholdPlus12()
    {
        (Window w, Grid root) = Host(Threshold - 1);
        try
        {
            Assert.Contains("compactHeight", root.Classes);

            w.Height = Threshold + 6;              // inside the hysteresis band
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("compactHeight", root.Classes);

            w.Height = Threshold + 12;             // restore boundary
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("compactHeight", root.Classes);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void RapidCrossings_EndStateWins_NoClassChurnLeftovers()
    {
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            root.Classes.Add("keepMe");
            for (int i = 0; i < 6; i++)
            {
                w.Height = (i % 2 == 0) ? Threshold - 40 : Threshold + 40;
                Dispatcher.UIThread.RunJobs();
            }
            // Ended high (i=5 odd → +40, above restore boundary).
            Assert.DoesNotContain("compactHeight", root.Classes);
            Assert.Contains("keepMe", root.Classes);   // other classes never touched
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void RowSizes_ApplyOnCompact_RestorePreservingSplitterDrag()
    {
        CompactRowSize[] rows = [new(RowIndex: 1, NormalHeight: 150, CompactMinHeight: 80, HelpOpenMinHeight: 60, Mode: CompactRowMode.PixelRestore)];
        (Window w, Grid root) = Host(Threshold + 50, rows);
        try
        {
            // Simulate a user splitter drag at normal size.
            root.RowDefinitions[1].Height = new GridLength(190);
            Dispatcher.UIThread.RunJobs();

            w.Height = Threshold - 1;              // → compact
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(80, root.RowDefinitions[1].MinHeight);

            w.Height = Threshold + 12;             // → restore
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(190, root.RowDefinitions[1].Height.Value); // drag survives round-trip
            Assert.Equal(150, CompactHeightBehavior.GetRowSizes(root)![0].NormalHeight);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void AutoToStar_SwapsRowHeightKind_PerMode()
    {
        CompactRowSize[] rows = [new(1, double.NaN, 110, 80, CompactRowMode.AutoToStar)];
        (Window w, Grid root) = Host(Threshold + 50, rows);
        try
        {
            root.RowDefinitions[1].Height = GridLength.Auto;   // three-band normal shape
            Dispatcher.UIThread.RunJobs();

            w.Height = Threshold - 1;                          // → compact
            Dispatcher.UIThread.RunJobs();
            Assert.True(root.RowDefinitions[1].Height.IsStar);
            Assert.Equal(110, root.RowDefinitions[1].MinHeight);

            w.Height = Threshold + 12;                         // → restore
            Dispatcher.UIThread.RunJobs();
            Assert.True(root.RowDefinitions[1].Height.IsAuto);
            Assert.Equal(0, root.RowDefinitions[1].MinHeight);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void DescendantGridRowSizes_FollowTheRootsMode()
    {
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            var inner = new Grid { RowDefinitions = new RowDefinitions("150,Auto"), [Grid.RowProperty] = 2 };
            inner.Children.Add(new Border());
            CompactHeightBehavior.SetRowSizes(inner,
                [new CompactRowSize(0, 150, 80, 80, CompactRowMode.PixelRestore)]);
            root.Children.Add(inner);
            Dispatcher.UIThread.RunJobs();

            w.Height = Threshold - 1;                          // root goes compact
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(80, inner.RowDefinitions[0].Height.Value);

            w.Height = Threshold + 12;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(150, inner.RowDefinitions[0].Height.Value);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void HelpOpen_WhileCompact_AppliesDonationMinimums()
    {
        CompactRowSize[] rows = [new(1, 150, 80, 60, CompactRowMode.MinOnly)];
        (Window w, Grid root) = Host(Threshold - 1, rows);
        try
        {
            Assert.Equal(80, root.RowDefinitions[1].MinHeight);
            CompactHeightBehavior.SetHelpOpen(root, true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(60, root.RowDefinitions[1].MinHeight);
            CompactHeightBehavior.SetHelpOpen(root, false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(80, root.RowDefinitions[1].MinHeight);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void FocusInsideCollapsingRegion_MovesToDesignatedTarget_OnCompactOnly()
    {
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            // Direction-specific targets (spec rev 7): compact target = the expander's
            // realized header toggle; restore target = a named normal-mode control.
            var expander = new Expander { [Grid.RowProperty] = 2 };
            var collapsing = new Button { Content = "link", [Grid.RowProperty] = 0 };
            var restoreTarget = new Button { Content = "firstInput", [Grid.RowProperty] = 1 };
            root.Children.Add(expander);
            root.Children.Add(collapsing);
            root.Children.Add(restoreTarget);
            CompactHeightBehavior.SetHelpExpander(root, expander);
            CompactHeightBehavior.SetRestoreFocusTarget(root, restoreTarget);
            // The app-level styles hide row-0 content in compact and the expander header
            // at normal; the unit test simulates both with the class:
            root.Classes.CollectionChanged += (_, _) =>
                collapsing.IsVisible = !root.Classes.Contains("compactHeight");
            Dispatcher.UIThread.RunJobs();

            collapsing.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(collapsing.IsFocused);

            w.Height = Threshold - 1;              // → compact; collapsing hides
            Dispatcher.UIThread.RunJobs();
            var headerToggle = expander.GetVisualDescendants().OfType<ToggleButton>().First();
            Assert.True(headerToggle.IsFocused,
                "focus must land on the HEADER TOGGLE (the Expander itself is not focusable)");

            w.Height = Threshold + 12;             // → restore; the toggle hides (flat mode)
            Dispatcher.UIThread.RunJobs();
            Assert.True(restoreTarget.IsFocused,
                "restore-direction stranding must land on the RestoreFocusTarget");
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void FocusOutsideTheView_IsNeverStolen_ByTransitions()
    {
        // Spec rev 8 precondition: a transition while focus sits OUTSIDE the behavior's
        // root must not move it.
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            var outside = new Button { Content = "shell" };
            var shell = new StackPanel();
            w.Content = null;
            shell.Children.Add(outside);
            shell.Children.Add(root);
            w.Content = shell;
            Dispatcher.UIThread.RunJobs();

            outside.Focus();
            Dispatcher.UIThread.RunJobs();

            w.Height = Threshold - 1;              // → compact
            Dispatcher.UIThread.RunJobs();
            Assert.True(outside.IsFocused, "transitions must never steal focus from outside the view");

            w.Height = Threshold + 12;             // → restore
            Dispatcher.UIThread.RunJobs();
            Assert.True(outside.IsFocused);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void ClippedButRecoverable_Focus_IsBroughtIntoView_NotRelocated()
    {
        // Spec rev 7 step (5): an element merely scrolled out of a viewport is recovered
        // via BringIntoView, never relocated.
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            var scroller = new ScrollViewer { [Grid.RowProperty] = 2, Height = 60 };
            var stack = new StackPanel();
            for (int i = 0; i < 10; i++) stack.Children.Add(new Button { Content = $"b{i}", Height = 30 });
            scroller.Content = stack;
            root.Children.Add(scroller);
            Dispatcher.UIThread.RunJobs();

            Button last = (Button)stack.Children[^1];
            last.Focus();
            scroller.Offset = default;             // scroll the focused button out of view
            Dispatcher.UIThread.RunJobs();

            w.Height = Threshold - 1;              // transition runs the obscurement check
            Dispatcher.UIThread.RunJobs();
            Assert.True(last.IsFocused, "recoverable focus must be brought into view, not relocated");
            Assert.True(scroller.Offset.Y > 0, "BringIntoView must have scrolled the viewer");
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Reattach_ReevaluatesFromCurrentBounds()
    {
        (Window w, Grid root) = Host(Threshold - 1);
        try
        {
            Assert.Contains("compactHeight", root.Classes);
            w.Content = null;                      // detach
            Dispatcher.UIThread.RunJobs();
            w.Height = Threshold + 50;
            w.Content = root;                      // reattach at a tall height
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("compactHeight", root.Classes);
        }
        finally { w.Close(); }
    }
}
```

- [ ] **Step 2: Run to verify failure** —
  `dotnet build ReScene.Manager.Tests -t:Rebuild` then
  `dotnet test --no-build --filter FullyQualifiedName~CompactHeightBehavior`.
  Expected: compile error (`CompactHeightBehavior` missing) — that is the red.

- [ ] **Step 3: Implement `CompactRowMode.cs` and `CompactRowSize.cs`** (one type per
  file, both verbatim):

```csharp
namespace ReScene.Manager.Behaviors;

/// <summary>
/// How <see cref="CompactHeightBehavior"/> treats one RowDefinition across modes
/// (RowDefinitions are not styleable, so the behavior owns their values — spec §1).
/// </summary>
internal enum CompactRowMode
{
    /// <summary>Height untouched; only MinHeight swaps per mode (star work rows).</summary>
    MinOnly,

    /// <summary>Compact sets Height = CompactMinHeight px; expand restores
    /// Height = NormalHeight px unless a splitter drag was captured (fixed pixel rows
    /// such as CreatorView's stored-files row).</summary>
    PixelRestore,

    /// <summary>Compact sets Height = 1* with MinHeight = CompactMinHeight; expand
    /// restores Height = Auto, MinHeight = 0 (three-band config rows).</summary>
    AutoToStar,
}
```

```csharp
namespace ReScene.Manager.Behaviors;

/// <summary>
/// One RowDefinition's per-mode sizing for <see cref="CompactHeightBehavior"/> (spec §1).
/// While compact AND the Help body is open, <see cref="HelpOpenMinHeight"/> replaces
/// <see cref="CompactMinHeight"/> (the donation rule).
/// </summary>
internal sealed record CompactRowSize(
    int RowIndex,
    double NormalHeight,
    double CompactMinHeight,
    double HelpOpenMinHeight,
    CompactRowMode Mode);
```

- [ ] **Step 4: Implement `CompactHeightBehavior.cs`** — attached-property static class in
  the `ListBoxAutoScroll` idiom. Normative contract (spec §1); shape:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ReScene.Manager.Behaviors;

/// <summary>
/// Toggles the <c>compactHeight</c> style class on a view's inner layout root from its own
/// bounds height (spec §1): compact when height &lt; Threshold, restore at ≥ Threshold+12
/// (restore-only hysteresis — a fresh instance at Threshold+1 starts expanded). Applies
/// per-view <see cref="CompactRowSize"/> values on the root AND on descendant grids
/// carrying their own RowSizes attachment (collected at each apply), applies help-open
/// donation, manages the Help expander's per-mode state, and runs the spec rev-7 staged
/// focus algorithm across transitions.
/// </summary>
internal static class CompactHeightBehavior
{
    private const string ClassName = "compactHeight";
    private const double RestoreSlack = 12;

    public static readonly AttachedProperty<double> ThresholdProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("Threshold", typeof(CompactHeightBehavior), double.NaN);
    public static readonly AttachedProperty<IReadOnlyList<CompactRowSize>?> RowSizesProperty =
        AvaloniaProperty.RegisterAttached<Control, IReadOnlyList<CompactRowSize>?>("RowSizes", typeof(CompactHeightBehavior));
    public static readonly AttachedProperty<bool> HelpOpenProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("HelpOpen", typeof(CompactHeightBehavior));
    public static readonly AttachedProperty<Expander?> HelpExpanderProperty =
        AvaloniaProperty.RegisterAttached<Control, Expander?>("HelpExpander", typeof(CompactHeightBehavior));
    public static readonly AttachedProperty<Control?> RestoreFocusTargetProperty =
        AvaloniaProperty.RegisterAttached<Control, Control?>("RestoreFocusTarget", typeof(CompactHeightBehavior));
    public static readonly AttachedProperty<double> HelpBodyMaxHeightProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("HelpBodyMaxHeight", typeof(CompactHeightBehavior), double.NaN);
    // + Get/Set statics for each, per the ListBoxAutoScroll house pattern.

    // Per-instance state via ConditionalWeakTable<Control, State>:
    //   bool isCompact; bool updateQueued; double? capturedDragHeight[row].
    //
    // Wiring (static ctor): ThresholdProperty.Changed → hook/unhook. On hook:
    //   control.AttachedToVisualTree / DetachedFromVisualTree manage a subscription to
    //   BoundsProperty changes; each change posts ONE coalesced dispatcher callback
    //   (updateQueued guard) that runs Evaluate(control).
    //
    // Evaluate(control):
    //   double h = control.Bounds.Height; if (h <= 0 || double.IsNaN(threshold)) return;
    //   bool wantCompact = state.isCompact ? h < threshold + RestoreSlack : h < threshold;
    //   if (wantCompact == state.isCompact) return;
    //   CaptureFocusedElement();   // BOTH directions (spec rev 7/8 — restore can
    //                              // strand focus on the hiding header toggle)
    //   state.isCompact = wantCompact; ApplyRows(control, state); ToggleClass(control);
    //   Dispatcher.UIThread.Post(() => RelocateFocusIfHidden(control), DispatcherPriority.Loaded);
    //     // staged: class/rows applied → layout runs → focus checked (spec §1)
    //
    // ApplyRows: for each CompactRowSize r on a Grid root, per r.Mode:
    //   MinOnly     — compact: MinHeight = HelpOpen ? HelpOpenMinHeight : CompactMinHeight;
    //                 expand: MinHeight = the XAML value captured at first hook. Height
    //                 never touched.
    //   PixelRestore — compact: capture row.Height (if Absolute) as drag height once,
    //                 then Height = CompactMinHeight px, MinHeight likewise (HelpOpen
    //                 variant applies); expand: Height = captured drag ?? NormalHeight px,
    //                 MinHeight = captured XAML minimum.
    //   AutoToStar  — compact: Height = new GridLength(1, Star), MinHeight =
    //                 CompactMinHeight (HelpOpen variant applies); expand: Height =
    //                 GridLength.Auto, MinHeight = 0.
    //
    // HelpOpenProperty.Changed → if compact, re-run ApplyRows (donation swap only).
    //
    // Staged focus (spec rev 7, both directions):
    //   captured = focused element, taken BEFORE styles/rows apply;
    //   after the post-apply layout pass:
    //   IsObscured(el) = el is detached, OR any ancestor IsVisible==false, OR the
    //     element's rendered bounds do not intersect the INTERSECTION of every clipping
    //     ancestor's viewport (IsEffectivelyVisible alone is insufficient — it ignores
    //     clipping);
    //   PRECONDITION (spec rev 8): the captured element was focused AND is a
    //     descendant of this root — otherwise do NOTHING (no focus theft from the
    //     shell menu/tab strip/other windows/empty focus).
    //   if IsObscured(captured): captured.BringIntoView(); re-run layout; re-check;
    //   if STILL obscured: focus the direction's target through the FALLBACK CHAIN
    //     (spec rev 8): resolved target (entering compact → the HelpExpander's realized
    //     header ToggleButton, which is TEMPLATED and may be null on an early pass;
    //     leaving → GetRestoreFocusTarget(root)) → first focusable descendant of the
    //     root → the root itself. Never a silent no-op; the tests assert non-null
    //     resolution.
    //
    // HelpExpander wiring: entering compact → IsExpanded = false (condition-5 reset);
    //   entering normal → IsExpanded = true (flat mode). Subscribe IsExpanded →
    //   SetHelpOpen(root, isCompact && IsExpanded). While compact && open, apply
    //   HelpBodyMaxHeight to the body ScrollViewer (found in the expander content).
}
```

  The implementer writes the full method bodies to this contract; every behavior test in
  Step 1 must pass without modification (tests are the contract's fixed points).

- [ ] **Step 5: Implement `CompactInvariantRig.cs`** — the shared measurement helper:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ReScene.Manager.Tests;

/// <summary>
/// Shared floor-measurement for the per-view threshold-invariant tests (spec §1's four
/// one-sum checks). Measures in inner-content DIPs at width 676 (the 700×450 inner width).
/// </summary>
internal static class CompactInvariantRig
{
    public const double InnerBudget = 319;   // measured: 450 − 26 − 58 − 23 − 24
    public const double CiBound = 307;       // InnerBudget − 12 jitter slack (spec §1)
    public const double InnerWidth = 676;

    /// <summary>
    /// The ROW-AWARE floor of an inner Grid (codex round-1 #5: a naive
    /// Measure(∞) reports CONTENT height for star and scrolling rows, not their
    /// minimums): Σ per RowDefinition — star rows contribute MinHeight; pixel rows their
    /// Height; Auto rows the max desired height of their children measured at
    /// InnerWidth×∞ — plus inter-row margins. Callers force conditional rows visible and
    /// set the mode class BEFORE calling.
    /// </summary>
    public static double MeasureFloor(Grid innerRoot)
    {
        innerRoot.Measure(new Size(InnerWidth, double.PositiveInfinity));
        double total = 0;
        for (int i = 0; i < innerRoot.RowDefinitions.Count; i++)
        {
            RowDefinition row = innerRoot.RowDefinitions[i];
            if (row.Height.IsAbsolute) { total += row.Height.Value; continue; }
            if (row.Height.IsStar) { total += row.MinHeight; continue; }
            double rowDesired = 0;
            foreach (Control child in innerRoot.Children.OfType<Control>())
            {
                if (Grid.GetRow(child) != i) continue;
                rowDesired = Math.Max(rowDesired,
                    child.DesiredSize.Height + child.Margin.Top + child.Margin.Bottom);
            }
            total += rowDesired;
        }
        return total;
    }

    /// <summary>
    /// Arrangement assertion: arrange the root at InnerWidth × the given height and
    /// verify NO child's rendered bounds extend past the bottom edge (the rendered form
    /// of "the floor fits"). Complements MeasureFloor — the invariant tests run both.
    /// </summary>
    public static void AssertArrangesWithin(Grid innerRoot, double height)
    {
        innerRoot.Measure(new Size(InnerWidth, height));
        innerRoot.Arrange(new Rect(0, 0, InnerWidth, height));
        foreach (Control child in innerRoot.Children.OfType<Control>())
        {
            if (!child.IsVisible) continue;
            double bottom = child.Bounds.Y + child.Bounds.Height;
            if (bottom > height + 0.5)
                throw new Xunit.Sdk.XunitException(
                    $"{child.GetType().Name} bottom {bottom:F1} exceeds {height}");
        }
    }
}
```

- [ ] **Step 6: Green + gate** — forced rebuild both projects; behavior tests green; full
  Manager suite green; solution rebuild 0W/0E.
- [ ] **Step 7: Commit** `feat(ui): CompactHeightBehavior + compact invariant rig`
  (stage the four new files explicitly).

---

### Task 2: ReconstructorView conversion (template view)

Numbers are spec rev 6: TabControl minimums 130 normal / 96 compact / 60 help-open;
log 80; threshold 421; compact worst floor ≤ 305; Help body MaxHeight ≈ 38
(test-computed); tip always-visible, compact-trimmed under the five a11y conditions.

**Files:**
- Modify: `ReScene.Manager/Views/ReconstructorView.axaml`
- Modify: `ReScene.Manager/Views/ReconstructorView.axaml.cs` (behavior wiring in ctor)
- Modify: `ReScene.Manager/Behaviors/CompactHeightBehavior.cs` (add HelpExpander wiring)
- Modify: `ReScene.Manager/Resources/Styles.axaml` (helpDisclosure + tipLine + splitter styles)
- Test: `ReScene.Manager.Tests/ReconstructorCompactTests.cs` (new)
- Test: `ReScene.Manager.Tests/CompactViewRig.cs` (new — the shared per-view test rig,
  produced here, consumed by Tasks 3–6)
- Test: extend `CompactHeightBehaviorTests.cs` (HelpExpander wiring cases)

**Interfaces:**
- Consumes Task 1: Threshold/RowSizes/HelpOpen/HelpExpander/RestoreFocusTarget/
  HelpBodyMaxHeight, `compactHeight` class, `CompactInvariantRig`.
- Produces (Tasks 3-6 reuse): `HelpExpanderProperty` on the behavior; style classes
  `helpDisclosure` and `tipLine`; the splitter base/focus styles; and `CompactViewRig`
  (the shared test rig) with these exact members (bodies authored in this task — the
  executable forms of criteria A/C; per-view tests of Tasks 2-6 call ONLY these members
  plus per-view VM property setters, no other undefined helpers):

```csharp
internal static class CompactViewRig
{
    /// Hosts the view in a real MainWindow shell sized so the view's inner root gets
    /// exactly innerHeight DIPs; returns the window and the inner root grid.
    public static (Window Window, Grid InnerRoot) HostAt(Control view, double innerHeight);

    /// Criterion C: focuses sentinel, sends genuine Tab keystrokes
    /// (window.KeyPressQwerty(PhysicalKey.Tab, ...)) until focus returns to it; after
    /// EVERY step asserts the focused control's rendered bounds lie within the
    /// intersection of every clipping ancestor's viewport and the window; throws with
    /// the offending control's name.
    public static void AssertTabWalkStaysVisible(Window window, Control sentinel);

    /// Ordered (control type, automation name) snapshot of the Tab cycle.
    public static IReadOnlyList<string> SnapshotTabOrder(Window window, Control root);

    /// Criterion A, INPUT-DRIVEN (codex round-2 #9 — programmatic BringIntoView is not
    /// a user path): three routes, each asserted per target — (a) WHEEL: genuine wheel
    /// input over the scroll region until the target is fully inside the window;
    /// (b) KEYBOARD: real Tab/arrow input until the target is focused and fully
    /// visible; (c) THUMB: pointer press-drag-release on the vertical scrollbar thumb
    /// (headless MouseDown/MouseMove/MouseUp on the thumb's bounds) until visible.
    public static void AssertReachableByWheel(Window window, Control target);
    public static void AssertReachableByKeyboard(Window window, Control target);
    public static void AssertReachableByThumb(Window window, Control target);

    /// Genuine wheel input (headless window.MouseWheel(point, delta)).
    public static void Wheel(Window window, Avalonia.Point at, double dy);
}
```

  `AssertTabWalkStaysVisible` runs the cycle FORWARD (Tab) and then REVERSE
  (Shift+Tab), asserting at every step in both passes (codex round-2 #9). The rig's
  method bodies are written in this task and are themselves red-first-exercised by the
  Reconstructor cases; the plan fixes their contracts, the task their code.

- [ ] **Step 1: Extend the behavior — HelpExpander wiring (test-first).**
  Add to `CompactHeightBehaviorTests.cs`:

```csharp
    [AvaloniaFact]
    public void HelpExpander_FlatWhenExpandedMode_ResetOnCompactEntry_TogglesHelpOpen()
    {
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            var expander = new Expander { [Grid.RowProperty] = 0 };
            root.Children.Add(expander);
            CompactHeightBehavior.SetHelpExpander(root, expander);
            Dispatcher.UIThread.RunJobs();

            // Expanded (normal) mode: behavior pins the flat state.
            Assert.True(expander.IsExpanded);

            w.Height = Threshold - 1;                    // enter compact
            Dispatcher.UIThread.RunJobs();
            Assert.False(expander.IsExpanded);           // condition 5: starts collapsed
            Assert.False(CompactHeightBehavior.GetHelpOpen(root));

            expander.IsExpanded = true;                  // user opens Help
            Dispatcher.UIThread.RunJobs();
            Assert.True(CompactHeightBehavior.GetHelpOpen(root));

            w.Height = Threshold + 12;                   // restore to normal
            Dispatcher.UIThread.RunJobs();
            Assert.True(expander.IsExpanded);            // flat again
            Assert.False(CompactHeightBehavior.GetHelpOpen(root)); // donation off at normal

            w.Height = Threshold - 1;                    // re-enter compact
            Dispatcher.UIThread.RunJobs();
            Assert.False(expander.IsExpanded);           // durability is compact-session scoped
        }
        finally { w.Close(); }
    }
```

  Implement in `CompactHeightBehavior`: attached `HelpExpanderProperty` (`Expander?`).
  On mode transitions: entering compact → `IsExpanded = false`; entering normal →
  `IsExpanded = true`. Subscribe the expander's `IsExpanded` changes → `SetHelpOpen(root,
  isCompact && expander.IsExpanded)` (HelpOpen is never true at normal size). Forced
  rebuild, red → green, full behavior suite green.

- [ ] **Step 2: Author the test suite RED-FIRST** — write `CompactViewRig.cs` (bodies
  included) and `ReconstructorCompactTests.cs` per Step 6's case list, then run them
  against TODAY's view (forced rebuild): the invariant cases are red (the old 220/140
  minimums exceed every bound) and the reachability cases are red at 700×450. Capture
  the red output. Only then proceed.

- [ ] **Step 3: XAML restructure.** In `ReconstructorView.axaml`:

  (a) Row 0's `StackPanel` is wrapped in the disclosure (single instance — the intro
  TextBlock and links WrapPanel MOVE inside; authoring-time move, not runtime). The FIRST
  link Button gains `x:Name="WindowsPackLink"` (the RestoreFocusTarget). The expander
  CONTENT is wrapped in a `ScrollViewer` (inset on the content panel) so the behavior's
  `HelpBodyMaxHeight` bounds the BODY alone — never the header:

```xml
    <Expander Grid.Row="0" x:Name="HelpDisclosure" Classes="helpDisclosure"
              Margin="0,0,0,6"
              AutomationProperties.Name="Help &amp; links">
      <Expander.Header>
        <TextBlock Text="Help &amp; links" FontSize="{DynamicResource FontSizeCaption}" />
      </Expander.Header>
      <!-- The BODY ScrollViewer is the HelpBodyMaxHeight target and is Focusable so
           keyboard users can scroll a capped body (spec §2); inset on the content
           panel per the house rule. -->
      <ScrollViewer Focusable="True" VerticalScrollBarVisibility="Auto"
                    ScrollViewer.AllowAutoHide="False">
        <StackPanel Margin="0,0,4,0">
          <!-- existing intro TextBlock (verbatim) -->
          <!-- existing links WrapPanel (verbatim, Click+Tag untouched) -->
        </StackPanel>
      </ScrollViewer>
    </Expander>
```

  The old `Margin="0,0,0,6"` moves from the StackPanel to the Expander; the inner
  StackPanel loses it.

  (b) Tip row (conditions 1/2 — full text stays the single bound source):

```xml
    <TextBlock Grid.Row="2" Classes="tipLine"
               Text="Tip: click &#x201C;Import from SRR&#x201D; to auto-configure versions, compression, dictionary, timestamps and Host OS from the release's SRR."
               ToolTip.Tip="{Binding $self.Text}"
               AutomationProperties.HelpText="{Binding $self.Text}"
               Foreground="{DynamicResource ForegroundSecondary}" FontSize="{DynamicResource FontSizeCaption}"
               TextWrapping="Wrap" Margin="0,0,0,4" />
```

  (c) Row definitions: row 4 `MinHeight="220"` → `MinHeight="130"`; row 6
  `MinHeight="140"` → `MinHeight="80"`. TabControl `MinHeight="220"` → `MinHeight="130"`.

  (d) Splitter: remove `Background="Transparent"` (moves to the style);
  add `AutomationProperties.Name="Resize options and log"`.

- [ ] **Step 3: Code-behind wiring** (`ReconstructorView.axaml.cs` ctor, after
  `InitializeComponent`):

```csharp
        Grid root = (Grid)Content!;
        Behaviors.CompactHeightBehavior.SetThreshold(root, 421);
        Behaviors.CompactHeightBehavior.SetRowSizes(root,
            [new Behaviors.CompactRowSize(RowIndex: 4, NormalHeight: double.NaN,
                CompactMinHeight: 96, HelpOpenMinHeight: 60, Mode: Behaviors.CompactRowMode.MinOnly)]);
        Behaviors.CompactHeightBehavior.SetHelpExpander(root, HelpDisclosure);
        Behaviors.CompactHeightBehavior.SetHelpBodyMaxHeight(root, 38);
        Behaviors.CompactHeightBehavior.SetRestoreFocusTarget(root, WindowsPackLink);
```

- [ ] **Step 4: Styles** (`Styles.axaml`, after the checkbox-glyph block):

```xml
  <!-- Small-window chrome (spec rev 6 §2): the Help disclosure renders FLAT at normal
       size — header hidden, body pinned expanded by CompactHeightBehavior — so the page
       is pixel-identical to the pre-disclosure layout. Under .compactHeight the header
       shows and the body obeys the behavior (collapsed on entry, donation while open).
       Class tokens keep every rule at StyleTrigger priority (see the glyph comment). -->
  <Style Selector="Expander.helpDisclosure /template/ ToggleButton">
    <Setter Property="IsVisible" Value="False" />
  </Style>
  <Style Selector="Grid.compactHeight Expander.helpDisclosure /template/ ToggleButton">
    <Setter Property="IsVisible" Value="True" />
  </Style>
  <!-- No Expander-level MaxHeight: the behavior applies HelpBodyMaxHeight to the BODY's
       internal ScrollViewer only (a whole-control cap would squeeze the header). -->

  <!-- Compact tip (a11y conditions 1/2: trimming is VISUAL-ONLY over the full bound
       text; HelpText carries it for AT; the tip is never a budget donor). -->
  <Style Selector="Grid.compactHeight TextBlock.tipLine">
    <Setter Property="TextWrapping" Value="NoWrap" />
    <Setter Property="TextTrimming" Value="CharacterEllipsis" />
  </Style>

  <!-- Splitter base + focus (base moves out of local values so :focus can win; ≥3:1 vs
       both panes, asserted by test). -->
  <Style Selector="GridSplitter">
    <Setter Property="Background" Value="Transparent" />
  </Style>
  <Style Selector="GridSplitter:focus">
    <Setter Property="Background" Value="{DynamicResource AccentPrimary}" />
  </Style>
```

  (`AccentPrimary` is the existing Tokens.axaml brush; the test asserts rendered
  contrast, not the resource choice.)

- [ ] **Step 6 (authored in Step 2, verified GREEN here): the view test suite**
  (`ReconstructorCompactTests.cs`) — the per-view shape every later view task copies.
  Inert VM construction per `ReconstructorViewTests.CreateVm`; CompactViewRig members +
  VM setters only. Cases:

```csharp
    // 1. Invariant (spec §1's four checks; CompactInvariantRig):
    //    - render view at width 676, force HasCustomPackerWarning=true, expanded mode:
    //      MeasureFloor < 421 (threshold)
    //    - compact class applied, Help closed: MeasureFloor <= 307
    //    - compact + HelpOpen (donation rows applied, body expanded to MaxHeight):
    //      MeasureFloor <= 307
    //    - pinned/action rows within the same sums (Reconstructor: toolbar row measured)
    // 2. Rendered matrix: MainWindow-hosted view at 700×450 (compact active), at inner
    //    height 421 fresh (== Threshold exactly → EXPANDED: compact iff h < T), and at
    //    inner height 422 fresh (Threshold+1, EXPANDED — hysteresis is restore-only):
    //    criterion A reachability for the last Options control and last Output control
    //    (scroll to end, translated bounds inside viewport), criterion B no-clip with the
    //    warning forced, criterion C real Tab walk (sentinel → full cycle → every focused
    //    bounds within all clipping ancestors ∩ window).
    // 3. Tab-order snapshots: ordered (type, automation name) at normal — equals the
    //    pre-change snapshot committed as a fixture; compact — equals the spec §2 order.
    // 4. Chrome: compact tip UIA Name == the FULL tip text (condition 1) and
    //    HelpText == full text (condition 2); exactly three link Buttons in the tree in
    //    BOTH modes (single instance); links invocable in compact (expander open →
    //    Click raises); expander reset on compact re-entry; Help-open DONATION — with
    //    Help open at inner height 319, the tab row's MinHeight is 60, the body
    //    ScrollViewer's MaxHeight is 38, and the body's LAST link is keyboard-reachable
    //    (Tab + BringIntoView inside the body scroller); the TIP row never donates
    //    (identical height with Help open and closed — condition 4).
    // 5. Splitter: focusable via Tab, Up/Down moves the split, bounds clamp at 96/80,
    //    focus visual rendered (background brush changes on focus) with ≥3:1 contrast
    //    computed against both pane backgrounds.
```

  Each comment line becomes a real `[AvaloniaFact]`/`[AvaloniaTheory]`; the implementer
  writes bodies to these contracts. Red-first where behavior exists to break (invariant
  numbers against the OLD minimums are naturally red before Step 2's XAML lands — run
  Step 5's invariant test before Step 2 to capture the red, then land Steps 2-4).

- [ ] **Step 6: Frame-rig parity** — normal-size before/after captures of the full view
  (the rig pattern from the versions-tree work: ForceRenderTimerTick + RunJobs before
  every capture); pixel-compare. Any diff in the header region fails the task unless the
  documented fallback (custom two-slot header) is invoked — record the decision.

- [ ] **Step 7: Suites + gate** — forced rebuilds; full Manager suite; solution rebuild
  0W/0E; runtime ava-desktop smoke at 700×450 (visual: header disclosure present, tabs
  scroll, log ≥2 rows, no clipping).

- [ ] **Step 8: Commit** `feat(ui): Reconstructor small-window degradation (template)`
  — explicit paths only.

---

### Task 3: SRSCreatorView three-band conversion

Spec rev 6 numbers: threshold 520; compact config min 110 (help-open 80); log 80; pinned
band ≤ 75; compact worst floor ≤ 307. **Corrected feedback inventory** (measured against
the markup — the spec's "result banner" does not exist in this view; outcome lands in the
log): pinned band = separator + action DockPanel (Create SRS / Cancel(conditional) /
ProgressMessage(conditional)) + ProgressBar(conditional).

**Files:**
- Modify: `ReScene.Manager/Views/SRSCreatorView.axaml`
- Modify: `ReScene.Manager/Views/SRSCreatorView.axaml.cs` (behavior wiring)
- Test: `ReScene.Manager.Tests/SRSCreatorCompactTests.cs` (new; copies Task 2's shape)

**Interfaces:**
- Consumes: Task 1 behavior (Threshold/RowSizes/HelpExpander/RestoreFocusTarget,
  `CompactRowMode.AutoToStar`), Task 2's `helpDisclosure` styles, CompactViewRig, and
  test shape.
- Produces: the three-band XAML pattern Tasks 4–5 replicate.

- [ ] **Step 1: XAML restructure.** The root DockPanel becomes a 4-row Grid; every
  existing element moves VERBATIM (bindings, names, margins untouched) into its band:

```xml
  <Grid Margin="{DynamicResource PageMargin}" x:Name="RootGrid">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto" />                 <!-- 0: Help chrome -->
      <RowDefinition Height="Auto" />                 <!-- 1: config (AutoToStar) -->
      <RowDefinition Height="Auto" />                 <!-- 2: pinned action band -->
      <RowDefinition Height="*" MinHeight="80" />     <!-- 3: log band -->
    </Grid.RowDefinitions>

    <!-- 0: chrome — intro moves inside the disclosure (single instance; this view has
         no links, header text "Help" per spec §2). -->
    <Expander Grid.Row="0" x:Name="HelpDisclosure" Classes="helpDisclosure"
              Margin="0,0,0,6" AutomationProperties.Name="Help">
      <Expander.Header>
        <TextBlock Text="Help" FontSize="{DynamicResource FontSizeCaption}" />
      </Expander.Header>
      <!-- Focusable body scroller (keyboard route for the text-only body — spec §2)
           hosting the existing intro TextBlock verbatim, minus its old
           DockPanel.Dock/margin (margin moves to the Expander). -->
      <ScrollViewer Focusable="True" VerticalScrollBarVisibility="Auto"
                    ScrollViewer.AllowAutoHide="False">
        <!-- intro TextBlock -->
      </ScrollViewer>
    </Expander>

    <!-- 1: config band — always-present ScrollViewer; content renders at natural height
         at normal size (row is Auto), squeezes and scrolls in compact (AutoToStar). -->
    <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto"
                  ScrollViewer.AllowAutoHide="False">
      <StackPanel Margin="0,0,4,0">
        <!-- VERBATIM moves, in today's order, DockPanel.Dock attributes dropped:
             Sample File label TextBlock
             Sample File picker DockPanel (Browse + InputTextBox)
             FieldStatusLine SampleStatus
             ISO DockPanel (IsVisible=ShowISOSelection)
             separator Border
             Main file label / picker DockPanel / FieldStatusLine MainFileStatus
             separator Border
             Output label / picker DockPanel / FieldStatusLine OutputStatus
             separator Border
             Options header TextBlock + App name DockPanel -->
      </StackPanel>
    </ScrollViewer>

    <!-- 2: pinned action band — ALWAYS visible (the a11y survey's core defect: this row
         clipped off-screen mid-run). Overlay/adorner implementations forbidden. -->
    <StackPanel Grid.Row="2">
      <Border Height="1" Background="{DynamicResource BorderSeparator}" Margin="0,4" />
      <!-- action DockPanel (Create SRS / Cancel / ProgressMessage) — verbatim -->
      <!-- ProgressBar — verbatim -->
      <Border Height="1" Background="{DynamicResource BorderSeparator}" Margin="0,4" />
    </StackPanel>

    <!-- 3: log band — verbatim log DockPanel (header + logList); the "Log" header
         TextBlock gains x:Name="LogHeader" and the logList ListBox gains
         AutomationProperties.LabeledBy="{Binding #LogHeader}" (the Reconstructor already
         has this pairing — the audit extends it to every touched log). -->
  </Grid>
```

  Inset rule: the ScrollViewer carries no Padding — the content StackPanel carries the
  right-margin for the scrollbar gutter (house rule from the scroll-extent fix).

- [ ] **Step 2: Code-behind wiring** (ctor):

```csharp
        Grid root = RootGrid;
        Behaviors.CompactHeightBehavior.SetThreshold(root, 520);
        Behaviors.CompactHeightBehavior.SetRowSizes(root,
            [new Behaviors.CompactRowSize(RowIndex: 1, NormalHeight: double.NaN,
                CompactMinHeight: 110, HelpOpenMinHeight: 80, Mode: Behaviors.CompactRowMode.AutoToStar)]);
        Behaviors.CompactHeightBehavior.SetHelpExpander(root, HelpDisclosure);
        Behaviors.CompactHeightBehavior.SetHelpBodyMaxHeight(root, 40);
        Behaviors.CompactHeightBehavior.SetRestoreFocusTarget(root, InputTextBox);
```

- [ ] **Step 3: Tests** (`SRSCreatorCompactTests.cs`, Task 2's five-part shape adapted —
  CompactViewRig members + VM setters only):

```csharp
    // 1. Invariant: expanded worst floor < 520 (force ShowISOSelection, all three
    //    FieldStatusLines set, ShowProgress+IsCreating true); compact floor (Help
    //    closed) <= 307; compact + HelpOpen + body MaxHeight <= 307 one-sum; pinned
    //    band worst (Cancel visible + ProgressMessage + ProgressBar) <= 75.
    // 2. Rendered matrix at 700×450 and fresh at threshold+1 (=521, expanded):
    //    criterion A for the LAST config control (App name TextBox) and the primary
    //    action; B no-clip with all conditionals forced; C real Tab walk.
    // 3. Tab-order snapshots both modes (normal == pre-change fixture).
    // 4. Chrome: single-instance intro; expander reset on compact re-entry; focus guard.
    // 5. Pinned band: with band-1 scrolled to TOP and to BOTTOM, the Create SRS button's
    //    translated bounds stay fully inside the window while ProgressBar+Cancel are
    //    forced visible (the defect this task exists to fix, asserted directly).
```

  Red-first: run the invariant + pinned-band tests before Step 1's XAML lands — the
  pinned-band case is red against the DockPanel layout (measured: action row at 0px at
  700×450 with conditionals). Capture the red, land Steps 1–2, re-run green.

- [ ] **Step 4: Frame-rig parity** (normal size before/after), suites + gate + runtime
  smoke at 700×450, per Task 2's steps 6–7.
- [ ] **Step 5: Commit** `feat(ui): SRSCreator three-band small-window degradation`.

---

### Task 4: SRSReconstructorView three-band conversion

Spec rev 6 numbers: threshold 450; compact config min 110 (help-open 80); log 80; pinned
band ≤ 75; compact worst floor ≤ 307. Feedback inventory (spec-correct for this view):
pinned band = separator + action DockPanel (Rebuild Sample) + result Border (conditional,
`ShowResult`) + separator. This view has NO progress controls.

**Files:**
- Modify: `ReScene.Manager/Views/SRSReconstructorView.axaml`
- Modify: `ReScene.Manager/Views/SRSReconstructorView.axaml.cs` (behavior wiring)
- Test: `ReScene.Manager.Tests/SRSReconstructorCompactTests.cs` (new)

**Interfaces:**
- Consumes: Task 1 behavior (`CompactRowMode.AutoToStar`), Task 2's `helpDisclosure`
  styles, Task 3's band pattern.

- [ ] **Step 1: XAML restructure** — root DockPanel → the 4-row Grid, elements verbatim:

```xml
  <Grid Margin="{DynamicResource PageMargin}" x:Name="RootGrid">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto" />                 <!-- 0: Help chrome -->
      <RowDefinition Height="Auto" />                 <!-- 1: config (AutoToStar) -->
      <RowDefinition Height="Auto" />                 <!-- 2: pinned action band -->
      <RowDefinition Height="*" MinHeight="80" />     <!-- 3: log band -->
    </Grid.RowDefinitions>

    <!-- 0: chrome — the intro TextBlock moves inside a Focusable body scroller
         (keyboard route, spec §2); header "Help". Same shape as Task 3's snippet. -->
    <Expander Grid.Row="0" x:Name="HelpDisclosure" Classes="helpDisclosure"
              Margin="0,0,0,6" AutomationProperties.Name="Help">
      <Expander.Header>
        <TextBlock Text="Help" FontSize="{DynamicResource FontSizeCaption}" />
      </Expander.Header>
      <ScrollViewer Focusable="True" VerticalScrollBarVisibility="Auto"
                    ScrollViewer.AllowAutoHide="False">
        <!-- intro TextBlock -->
      </ScrollViewer>
    </Expander>

    <!-- 1: config band — ScrollViewer + StackPanel (inset on content panel), hosting in
         today's order: SRS File label / picker DockPanel (Browse + SRSFileTextBox) /
         FieldStatusLine SRSStatus / separator / Media File label / picker DockPanel
         (MediaFileTextBox, IsReadOnly binding intact) / FieldStatusLine MediaStatus /
         separator / Output label / picker DockPanel (OutputTextBox) / FieldStatusLine
         OutputStatus — all verbatim, Dock attributes dropped. -->

    <!-- 2: pinned band (StackPanel): separator / action DockPanel (Rebuild Sample,
         verbatim incl. its 0,0,0,4 margin) / result Border (verbatim — IsVisible=
         ShowResult, ResultSuccessToBrush background; ADD TextTrimming=
         "CharacterEllipsis" + MaxLines=2 on its TextBlock so a long ResultSummary is
         bounded — a11y rev-3 NEW-4's banner cap; ToolTip.Tip AND
         AutomationProperties.HelpText both bind the full summary — the same
         visual-only-trim rule as the tip) /
         separator. -->

    <!-- 3: log band — verbatim log DockPanel + the LogHeader/LabeledBy pairing (as in
         Task 3) — codex round-2 #10: Task 4 was the one view the audit missed. -->
  </Grid>
```

- [ ] **Step 2: Code-behind wiring** (ctor):

```csharp
        Grid root = RootGrid;
        Behaviors.CompactHeightBehavior.SetThreshold(root, 450);
        Behaviors.CompactHeightBehavior.SetRowSizes(root,
            [new Behaviors.CompactRowSize(RowIndex: 1, NormalHeight: double.NaN,
                CompactMinHeight: 110, HelpOpenMinHeight: 80, Mode: Behaviors.CompactRowMode.AutoToStar)]);
        Behaviors.CompactHeightBehavior.SetHelpExpander(root, HelpDisclosure);
        Behaviors.CompactHeightBehavior.SetHelpBodyMaxHeight(root, 40);
        Behaviors.CompactHeightBehavior.SetRestoreFocusTarget(root, SRSFileTextBox);
```

- [ ] **Step 3: Tests** (`SRSReconstructorCompactTests.cs`, the established five-part
  shape — CompactViewRig members + VM setters only):

```csharp
    // 1. Invariant: expanded worst floor < 450 (force all three FieldStatusLines set +
    //    ShowResult with a two-line ResultSummary); compact floor <= 307; compact +
    //    HelpOpen one-sum <= 307; pinned band worst (result visible, 2-line summary
    //    capped) <= 75.
    // 2. Rendered matrix at 700×450 and fresh at 451 (expanded): A for the last config
    //    control (Output Browse) and Rebuild Sample; B no-clip all conditionals; C real
    //    Tab walk.
    // 3. Tab-order snapshots both modes.
    // 4. Chrome: single-instance intro; reset on compact re-entry; focus guard.
    // 5. Pinned band: Rebuild Sample + result Border fully inside the window with band 1
    //    scrolled to both extremes and ShowResult forced (this view's measured defect:
    //    log at 74px base shrinking to 0 under conditionals).
    // 6. Result cap: with a 300-char ResultSummary, the Border's height stays <= its
    //    cap and the FULL text is exposed (UIA Name/ToolTip) — trimming is visual-only,
    //    same rule as the tip (a11y conditions 1/2 applied to the banner).
```

  Red-first: invariant + pinned-band cases against the DockPanel layout.

- [ ] **Step 4: Frame-rig parity, suites + gate, runtime smoke** (Task 2 steps 6–7).
- [ ] **Step 5: Commit** `feat(ui): SRSReconstructor three-band small-window degradation`.

---

### Task 5: SampleRestorerView three-band conversion + DataGrid handoff

Spec rev 6 numbers: threshold 535; compact config min 110 (help-open 80); log 80; pinned
band ≤ 75; compact worst floor ≤ 307. Feedback inventory (measured): pinned band =
separator + action DockPanel (Restore All / Cancel(conditional, `IsRestoring`) /
OverallProgressText(**always visible**) / ProgressMessage(conditional, `ShowProgress`)) +
ProgressBar(conditional) + separator. This is the view whose action row and log measure
0px at 700×450 BASE state — the headline defect.

**Files:**
- Modify: `ReScene.Manager/Views/SampleRestorerView.axaml`
- Modify: `ReScene.Manager/Views/SampleRestorerView.axaml.cs` (behavior wiring)
- Create: `ReScene.Manager/Behaviors/ScrollHandoffBehavior.cs`
- Test: `ReScene.Manager.Tests/ScrollHandoffBehaviorTests.cs` (new)
- Test: `ReScene.Manager.Tests/SampleRestorerCompactTests.cs` (new)

**Interfaces:**
- Consumes: Task 1 behavior (`AutoToStar`), Task 2 styles, Task 3's band pattern.
- Produces: `ScrollHandoffBehavior.HandoffProperty` (attached bool) — inner scrollable
  hands wheel input to the outer ScrollViewer at its extents. Task 6 reuses it if
  CreatorView's grid needs it.

- [ ] **Step 1: `ScrollHandoffBehavior` (test-first).** TWO mechanisms (codex round-1
  #8):
  (a) WHEEL — a gesture that would scroll past the internal extent (top at offset 0
  scrolling up; bottom at max scrolling down) must reach the ancestor: handle the grid's
  `PointerWheelChanged`; when at-extent in the wheel direction, adjust the OUTER
  ScrollViewer's `Offset` directly and mark the event handled (no synthetic re-raise —
  Avalonia wheel args are not re-raisable).
  (b) KEYBOARD/FOCUS — handle `RequestBringIntoView` bubbling from cells: when the
  requested rect lies outside the OUTER viewer's viewport, scroll the outer viewer to
  reveal it (the DataGrid brings the cell into ITS viewport; the behavior chains the
  remainder).
  Tests (`ScrollHandoffBehaviorTests.cs`) — GENUINE INPUT ONLY: DataGrid with 20 rows
  inside an outer ScrollViewer sized to clip both; wheel via the Avalonia.Headless
  `window.MouseWheel(point, delta)` API over the grid (down at grid-bottom moves the
  OUTER offset; up at grid-top likewise; mid-grid moves only the INNER offset);
  keyboard via real Down-arrow presses walking cell focus past the outer viewport — the
  focused cell must end fully visible (the chained BringIntoView).

- [ ] **Step 2: XAML restructure** — root DockPanel → 4-row Grid (Task 3's shape),
  elements verbatim:

```xml
    <!-- 0: chrome — intro inside the disclosure; header "Help". -->

    <!-- 1: config band ScrollViewer + StackPanel hosting, in today's order:
         SRR File label / picker (SRRFileTextBox) / FieldStatusLine SRRStatus / sep /
         Media Directory label / picker (MediaDirTextBox) / FieldStatusLine MatchStatus /
         sep / Output Directory label / picker (OutputDirTextBox — NO status line today,
         none added) / sep / "Embedded SRS Files" header TextBlock (gains
         x:Name="SRSEntriesHeader") / SRSEntriesGrid (verbatim incl. MinHeight 100 /
         MaxHeight 250 and the fullSizeGlyph template column; gains
         AutomationProperties.LabeledBy="{Binding #SRSEntriesHeader}" — the spec's
         LabeledBy audit — and behaviors:ScrollHandoffBehavior.Handoff="True"). -->

    <!-- 2: pinned band (StackPanel): separator / action DockPanel (Restore All, Cancel,
         OverallProgressText, ProgressMessage — verbatim) / ProgressBar (verbatim) /
         separator. -->

    <!-- 3: log band — verbatim log DockPanel + the LogHeader/LabeledBy pairing (as in
         Task 3). -->
```

- [ ] **Step 3: Code-behind wiring** (ctor): threshold **535**, RowSizes
  `[new(1, double.NaN, 110, 80, AutoToStar)]`, HelpExpander = HelpDisclosure,
  HelpBodyMaxHeight = 40, RestoreFocusTarget = SRRFileTextBox — Task 3's snippet with
  this view's numbers.

- [ ] **Step 4: Tests** (`SampleRestorerCompactTests.cs`):

```csharp
    // 1. Invariant: expanded worst floor < 535 (FieldStatusLines set, IsRestoring +
    //    ShowProgress true, grid populated to MaxHeight with 12 rows); compact floor
    //    <= 307; compact + HelpOpen one-sum <= 307; pinned band worst <= 75.
    // 2. Rendered matrix at 700×450 and fresh at 536 (expanded): A for the grid's last
    //    row's checkbox and Restore All; B no-clip with all conditionals + populated
    //    grid; C real Tab walk INCLUDING through the grid (outer Tab enters/leaves it).
    // 3. Tab-order snapshots both modes.
    // 4. Chrome: single-instance intro; reset on compact re-entry; focus guard.
    // 5. Pinned band: Restore All + ProgressBar fully inside the window with band 1
    //    scrolled to both extremes and IsRestoring+ShowProgress forced — THE base-state
    //    defect assertion (red against today's layout at 700×450 with zero conditionals).
    // 6. Handoff: wheel at grid extents moves the config band's scroller; cell focus
    //    via keyboard navigation chains BringIntoView to the outer viewer (focus a
    //    bottom-row cell while the grid is half-clipped by the band → the cell's bounds
    //    end fully visible); inner arrow-key navigation stays inside the grid.
    // 7. LabeledBy: the grid's UIA name resolves to "Embedded SRS Files".
```

  Red-first: cases 1 and 5 against today's DockPanel layout (5 is red at BASE state —
  the strongest red in the feature).

- [ ] **Step 5: Frame-rig parity, suites + gate, runtime smoke** (Task 2 steps 6–7).
- [ ] **Step 6: Commit** `feat(ui): SampleRestorer three-band degradation + scroll handoff`.

---

### Task 6: CreatorView three-band generalization

Spec rev 6 numbers: threshold 720 (Creator is compact in most real windows — expected);
compact config min 110 (help-open 80); log band 80; pinned ≤ 75; compact worst floor
≤ 307. Facts from the markup: the detected-sets region ALREADY carries MaxHeight=96 with
its own ScrollViewer (spec's bounding exists — verify, don't add); the action StackPanel
already unites the button row + ProgressBar (+ always-visible ActionHint, conditional
ProgressMessage); the in-scroller splitter is `Height="5"` with a local
`Background="Transparent"` (local moves to the Task 2 style).

**Files:**
- Modify: `ReScene.Manager/Views/CreatorView.axaml`
- Modify: `ReScene.Manager/Views/CreatorView.axaml.cs` (behavior wiring)
- Test: `ReScene.Manager.Tests/CreatorCompactTests.cs` (new)

**Interfaces:**
- Consumes: Task 1 behavior incl. DESCENDANT RowSizes application and
  `CompactRowMode.PixelRestore`; Task 2 styles; Task 3's band pattern;
  Task 5's `ScrollHandoffBehavior` (applied to StoredFilesGrid — it sits inside the
  band-1 scroller and has its own internal scrolling).

- [ ] **Step 1: XAML restructure.** Outer grid becomes the 4-band shape; the OLD outer
  rows 1–5 and the old bottom-grid rows 0–3 all move VERBATIM into band 1's inner grid;
  the old bottom-grid action/log rows become bands 2/3:

```xml
  <Grid Margin="{DynamicResource PageMargin}" x:Name="RootGrid">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto" />                 <!-- 0: Help chrome -->
      <RowDefinition Height="Auto" />                 <!-- 1: config (AutoToStar) -->
      <RowDefinition Height="Auto" />                 <!-- 2: pinned action band -->
      <RowDefinition Height="*" MinHeight="80" />     <!-- 3: log band -->
    </Grid.RowDefinitions>

    <!-- 0: chrome — the description TextBlock moves inside; header "Help". -->
    <Expander Grid.Row="0" x:Name="HelpDisclosure" Classes="helpDisclosure"
              Margin="0,0,0,6" AutomationProperties.Name="Help" />
    <!-- (header/body per Task 3's snippet; body = the existing description TextBlock) -->

    <!-- 1: config band — ScrollViewer hosting an INNER GRID (not a StackPanel: the
         stored-files splitter needs grid rows), x:Name="ConfigGrid":
           row 0 Auto : Input section StackPanel (verbatim — incl. the scanning
                        ProgressBar and the detected-sets MaxHeight=96 scroller)
           row 1 Auto : separator
           row 2 Auto : Stored Files header + buttons StackPanel (verbatim)
           row 3      : Height="150" MinHeight="150" — IDENTICAL minimums to today
                        (normal-mode parity incl. the splitter's drag floor — codex
                        round-1 #10); the compact 80 arrives ONLY via the descendant
                        PixelRestore entry, which lowers Height AND MinHeight together.
                        StoredFilesGrid verbatim, plus
                        behaviors:ScrollHandoffBehavior.Handoff="True" and a
                        "Stored Files" LabeledBy pairing (header gains
                        x:Name="StoredFilesHeader"; grid gains
                        AutomationProperties.LabeledBy="{Binding #StoredFilesHeader}")
           row 4 Auto : the GridSplitter (verbatim minus its local Background, plus
                        AutomationProperties.Name="Resize stored files and output" —
                        spec §5;
                        criterion E scoped to NORMAL size for this in-scroller splitter —
                        it stays focusable/operable in both modes)
           row 5 Auto : Output section StackPanel (verbatim)
           row 6 Auto : separator
           row 7 Auto : Options section StackPanel (verbatim — 7 checkboxes + app name)
         NOTE row 3 keeps a pixel Height so the splitter drags exactly as today; the
         compact 80 arrives via the DESCENDANT RowSizes application (PixelRestore),
         which also preserves a user drag across the round-trip. -->

    <!-- 2: pinned band (StackPanel): separator / the existing action StackPanel
         (DockPanel with Create SRR / Cancel / ActionHint / ProgressMessage + the
         ProgressBar) verbatim / separator. -->

    <!-- 3: log band — DockPanel: the old log-header DockPanel (Dock=Top, verbatim incl.
         SaveLogStatus, its "Log" TextBlock gaining x:Name="LogHeader") + the logList
         ListBox as fill with AutomationProperties.LabeledBy="{Binding #LogHeader}". The
         old row-7 MinHeight=40 disappears with the old bottom grid; the band minimum 80
         supersedes it (header ~28 + ≥2 rows — the spec's log rule now uniform). -->
  </Grid>
```

- [ ] **Step 2: Code-behind wiring** (ctor):

```csharp
        Behaviors.CompactHeightBehavior.SetThreshold(RootGrid, 720);
        Behaviors.CompactHeightBehavior.SetRowSizes(RootGrid,
            [new Behaviors.CompactRowSize(RowIndex: 1, NormalHeight: double.NaN,
                CompactMinHeight: 110, HelpOpenMinHeight: 80, Mode: Behaviors.CompactRowMode.AutoToStar)]);
        Behaviors.CompactHeightBehavior.SetRowSizes(ConfigGrid,
            [new Behaviors.CompactRowSize(RowIndex: 3, NormalHeight: 150,
                CompactMinHeight: 80, HelpOpenMinHeight: 80, Mode: Behaviors.CompactRowMode.PixelRestore)]);
        Behaviors.CompactHeightBehavior.SetHelpExpander(RootGrid, HelpDisclosure);
        Behaviors.CompactHeightBehavior.SetHelpBodyMaxHeight(RootGrid, 40);
        Behaviors.CompactHeightBehavior.SetRestoreFocusTarget(RootGrid, InputTextBox);
```

- [ ] **Step 3: Tests** (`CreatorCompactTests.cs`):

```csharp
    // 1. Invariant: expanded worst floor < 720 (force IsScanning, HasDetectedSets with
    //    12 sets — capped at 96 —, all statuses, IsCreating + ShowProgress, grid with
    //    8 stored files); compact floor <= 307; compact + HelpOpen one-sum <= 307;
    //    pinned band worst <= 75.
    // 2. Rendered matrix at 700×450 and fresh at 721 (expanded): A for the LAST option
    //    control (App name TextBox) and Create SRR; B no-clip with all conditionals;
    //    C real Tab walk (through both DataGrid and the option stack).
    // 3. Tab-order snapshots both modes.
    // 4. Chrome: single-instance description; reset on compact re-entry; focus guard.
    // 5. Pinned band: Create SRR + ProgressBar inside the window with band 1 scrolled
    //    to both extremes and conditionals forced (red today: the bottom half is
    //    crushed AND clipped at 700×450).
    // 6. Stored-files row: splitter drag at NORMAL size still resizes row 3 and the
    //    drag survives a compact round-trip (descendant PixelRestore capture);
    //    in compact the row is 80 and the grid scrolls internally; wheel handoff at
    //    the grid's extents reaches the band-1 scroller.
    // 7. Detected-sets bounding: with 12 sets the region's height stays <= 96 in both
    //    modes (verifying the EXISTING cap holds inside the new structure).
```

  Red-first: cases 1 and 5 against today's layout.

- [ ] **Step 4: Frame-rig parity** — extra care here (largest structural move): the
  before/after normal-size captures must be pixel-identical INCLUDING splitter drag
  behavior; any diff fails the task. The in-scroller splitter ALSO runs the Task-2
  focus-visual assertions (rendered :focus brush, ≥3:1 against both neighbours, and the
  high-contrast smoke) — criterion E's pane-minimum bound is normal-scoped, its focus
  VISUAL is not (codex round-2 new).
- [ ] **Step 5: Suites + gate + runtime smoke; Commit**
  `feat(ui): Creator three-band small-window degradation`.

---

### Task 7: Settings audit + whole-board close

**Files:**
- Modify: `ReScene.Manager.Tests/ScrollReachabilityTests.cs` (extend the Settings fact
  with a 700×450-era assertion if missing — audit only)
- Create: `ReScene.Manager.Tests/SmallWindowBoardTests.cs`
- Modify: `CHANGELOG.md` (outer repo, Unreleased)
- Modify: `docs/superpowers/specs/2026-07-30-small-window-layout-design.md` (status →
  implemented; fold the recorded pending nit: SRSCreator inventory has no result banner)

- [ ] **Step 1: Settings audit.** SettingsWindow owns MinWidth 560 / MinHeight 360 and
  its pages scroll (stage-1 fix). Assert (extend `ScrollReachabilityTests`): at exactly
  560×360, every page's last control is reachable and a real Tab walk stays inside the
  window (criterion C applied to Settings). No compact machinery is added — the audit
  proves none is needed; if it FAILS, stop and report (spec change required).
- [ ] **Step 2: Board tests** (`SmallWindowBoardTests.cs`):

```csharp
    // 1. Font-enlargement (spec Testing): override EVERY inherited font source at once
    //    (codex round-1 #11) — ControlContentThemeFontSize 12→16, the :is(Window)
    //    style's 12px via a higher-priority class-token style on the hosting window
    //    (StyleTrigger rule), FontSizeCaption 13→17, MonoFontSize (the log lists) and
    //    FontSizeBody (the warning row) each +4 — every named font source the five
    //    views consume — then at 700×450 each view's pinned/action band and log header
    //    remain unclipped AND the per-view tip and reachability assertions still hold
    //    (growth absorbed by scrolling regions).
    // 2. RenderScaling sweep: the five compact floors hold at 1.25/1.5 scaling
    //    (invariant rig re-measured under RenderScaling overrides) — distinct from 1.
    // 3. Cross-view: every task view's threshold invariant test type exists and runs
    //    (guard against a view task silently dropping its invariant — reflection over
    //    the test assembly for the *CompactTests naming pattern, count == 5).
```

- [ ] **Step 3: Full board** — forced rebuilds; Manager suite; App.Core suite; solution
  rebuild gate 0W/0E; counts recorded from actual output.
- [ ] **Step 4: Runtime pass** — ava-desktop at 700×450 and at the user's VM size:
  every view visited, compact chrome present, Help open/close exercised, screenshots
  captured for the report.
- [ ] **Step 5: Docs** — CHANGELOG Unreleased: "Task pages now adapt to small windows:
  panes shrink and scroll instead of clipping, header help collapses behind a disclosure,
  and every control stays reachable by keyboard at the minimum window size (700×450)."
  Spec status → "rev 6 — implemented <commit>", folding the SRSCreator-inventory nit.
- [ ] **Step 6: Commit** `docs: small-window layout complete` + report the acceptance
  smoke for the user: VM visual pass over all five tabs at the VM's native size.
