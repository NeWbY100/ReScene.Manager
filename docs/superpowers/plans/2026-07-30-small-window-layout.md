# Small-Window Layout Degradation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps
> use checkbox (`- [ ]`) syntax for tracking.

**Status: DRAFT rev 1 — Task 1 fully authored; Tasks 2–7 pending authorship. DO NOT
EXECUTE until this line says the plan is complete and codex-approved.**

**Goal:** Below each task view's fit floor, panes shrink and scroll instead of clipping,
header chrome collapses behind a Help disclosure, and keyboard focus can never land
outside the window — per spec `docs/superpowers/specs/2026-07-30-small-window-layout-design.md`
(rev 4, a11y-APPROVED; codex gate moves to this plan).

**Architecture:** One attached behavior (`CompactHeightBehavior`) toggles a `compactHeight`
style class on each view's inner layout root from its measured height, applies per-view
`RowSizes` (RowDefinitions are not styleable), and stages focus across transitions. All
structures are always present — mode changes only sizing constraints and visibility.
An executable per-view invariant test pins the height arithmetic (thresholds, compact
floors ≤ 307, help-donation sum).

**Tech Stack:** Avalonia 11.3.18, .NET 10, xUnit + Avalonia.Headless (existing
ReScene.Manager.Tests harness), frame rig + ava-desktop for visual verification.

## Global Constraints

- Spec rev 4 is normative; its §1 numbers are design targets — the invariant test measures
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
- Create: `ReScene.Manager.Tests/CompactHeightBehaviorTests.cs`
- Create: `ReScene.Manager.Tests/CompactInvariantRig.cs` (shared helper, used by every
  view task's invariant test)

**Interfaces (later tasks consume):**
- `CompactHeightBehavior.ThresholdProperty` (attached `double`, inner DIPs; NaN = off).
- `CompactHeightBehavior.RowSizesProperty` (attached `IReadOnlyList<CompactRowSize>`).
- `CompactHeightBehavior.HelpOpenProperty` (attached `bool`; the view binds it to the Help
  expander's `IsExpanded`; while compact AND open, donation values apply).
- `CompactRowSize` record:
  `internal sealed record CompactRowSize(int RowIndex, double NormalHeight, double CompactMinHeight, double HelpOpenMinHeight, bool HeightIsPixel);`
  (`HeightIsPixel=true` restores `Height = NormalHeight` px on expand — CreatorView's
  stored-files row; `false` leaves Height alone and only swaps MinHeight.)
- Style class contract: `compactHeight` present on the attached control while compact.
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
        CompactRowSize[] rows = [new(RowIndex: 1, NormalHeight: 150, CompactMinHeight: 80, HelpOpenMinHeight: 60, HeightIsPixel: true)];
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
    public void HelpOpen_WhileCompact_AppliesDonationMinimums()
    {
        CompactRowSize[] rows = [new(1, 150, 80, 60, HeightIsPixel: false)];
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
            var collapsing = new Button { Content = "link", [Grid.RowProperty] = 0 };
            var target = new Button { Content = "helpHeader", [Grid.RowProperty] = 2 };
            root.Children.Add(collapsing);
            root.Children.Add(target);
            CompactHeightBehavior.SetFocusFallback(root, target);
            // The style that hides row-0 content in compact is app-level; the unit test
            // simulates it: collapsing region hides when the class lands.
            root.Classes.CollectionChanged += (_, _) =>
                collapsing.IsVisible = !root.Classes.Contains("compactHeight");
            Dispatcher.UIThread.RunJobs();

            collapsing.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(collapsing.IsFocused);

            w.Height = Threshold - 1;              // → compact; collapsing hides
            Dispatcher.UIThread.RunJobs();
            Assert.True(target.IsFocused,
                "focus must relocate to the designated fallback when its element collapses");

            w.Height = Threshold + 12;             // → restore; no focus change expected
            Dispatcher.UIThread.RunJobs();
            Assert.True(target.IsFocused);
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

- [ ] **Step 3: Implement `CompactRowSize.cs`** (one type per file):

```csharp
namespace ReScene.Manager.Behaviors;

/// <summary>
/// One RowDefinition's per-mode sizing for <see cref="CompactHeightBehavior"/> (spec §1):
/// RowDefinitions are not styleable (no Classes), so the behavior owns their mode values.
/// <see cref="HeightIsPixel"/> rows restore <c>Height = NormalHeight</c> pixels on expand
/// — unless the user dragged a splitter, in which case the captured drag height wins.
/// </summary>
internal sealed record CompactRowSize(
    int RowIndex,
    double NormalHeight,
    double CompactMinHeight,
    double HelpOpenMinHeight,
    bool HeightIsPixel);
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
/// per-view <see cref="CompactRowSize"/> values (RowDefinitions cannot be styled), applies
/// help-open donation minimums, and relocates focus to <c>FocusFallback</c> when the
/// focused element is inside a region the compact styles collapse.
/// </summary>
internal static class CompactHeightBehavior
{
    private const string ClassName = "compactHeight";
    private const double RestoreSlack = 12;

    public static readonly AttachedProperty<double> ThresholdProperty = /* RegisterAttached, default double.NaN */;
    public static readonly AttachedProperty<IReadOnlyList<CompactRowSize>?> RowSizesProperty = /* … */;
    public static readonly AttachedProperty<bool> HelpOpenProperty = /* … */;
    public static readonly AttachedProperty<Control?> FocusFallbackProperty = /* … */;
    // + Get/Set statics for each, per house pattern.

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
    //   if (entering compact) CaptureFocusedElement();
    //   state.isCompact = wantCompact; ApplyRows(control, state); ToggleClass(control);
    //   Dispatcher.UIThread.Post(() => RelocateFocusIfHidden(control), DispatcherPriority.Loaded);
    //     // staged: class/rows applied → layout runs → focus checked (spec §1)
    //
    // ApplyRows: for each CompactRowSize r on a Grid root:
    //   compact: if (r.HeightIsPixel && row.Height.IsAbsolute) capture row.Height as drag
    //            height once; row.MinHeight = HelpOpen ? r.HelpOpenMinHeight
    //                                                  : r.CompactMinHeight;
    //            if (r.HeightIsPixel) row.Height = new GridLength(row-min, Pixel? →
    //              spec: compact uses star for band rows / pixel for stored-files; the
    //              rule: HeightIsPixel rows get Height = MinHeight px in compact;
    //              star rows keep their star Height and only MinHeight changes)
    //   expand:  row.MinHeight = normal minimum (the XAML value captured at first hook);
    //            if (r.HeightIsPixel) row.Height = captured drag ?? NormalHeight px.
    //
    // HelpOpenProperty.Changed → if compact, re-run ApplyRows (donation swap only).
    //
    // RelocateFocusIfHidden: focused = TopLevel.GetTopLevel(control)?.FocusManager?
    //   .GetFocusedElement() as Visual; if focused is a descendant of control and
    //   !focused.IsEffectivelyVisible → (GetFocusFallback(control) ?? control).Focus().
    //   Runs in BOTH directions (spec §1: expanding can hide the expander header).
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
    /// The rendered floor of an inner root: measure with infinite height at InnerWidth and
    /// return the desired height. Callers force conditional rows visible and set the mode
    /// class BEFORE measuring; this helper only measures.
    /// </summary>
    public static double MeasureFloor(Control innerRoot)
    {
        innerRoot.Measure(new Size(InnerWidth, double.PositiveInfinity));
        return innerRoot.DesiredSize.Height;
    }
}
```

- [ ] **Step 6: Green + gate** — forced rebuild both projects; behavior tests green; full
  Manager suite green; solution rebuild 0W/0E.
- [ ] **Step 7: Commit** `feat(ui): CompactHeightBehavior + compact invariant rig`
  (stage the four new files explicitly).

---

### Tasks 2–7 (pending authorship — DO NOT DISPATCH)

Authored next, one at a time, each against the view's full markup, to the same
no-placeholder standard:

- **Task 2 — Reconstructor** (template view): Help expander (always-present, styled-flat
  normal), chrome styles, TabControl/log minimums via RowSizes (130/110/80 + log 80),
  splitter Background→style + focus style + AutomationProperties.Name, threshold 421,
  invariant test, rendered A/B/C at 700×450 and 433, tab-order snapshots, frame-rig parity.
- **Task 3 — SRSCreatorView** (first three-band conversion): band grid, pinned feedback
  band (Create/Cancel + ProgressMessage + ProgressBar + banner ≤75), config ScrollViewer,
  threshold 520, invariant + rendered matrix + snapshots.
- **Task 4 — SRSReconstructorView** (three-band, threshold 450).
- **Task 5 — SampleRestorerView** (three-band + DataGrid-in-scroller handoff contracts,
  threshold 535).
- **Task 6 — CreatorView** (largest: three-band generalization, stored-files row via
  RowSizes 150/80 pixel mode, detected-sets MaxHeight, in-scroller splitter E-scoping,
  threshold 720).
- **Task 7 — Settings audit + whole board**: Settings compliance check, five-view parity
  evidence, criterion C Tab-walks all views, font-enlargement test, full suites + gate,
  CHANGELOG entry, spec → implemented.
