# Small-Window Layout Degradation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps
> use checkbox (`- [ ]`) syntax for tracking.

**Status: DRAFT rev 4 — Tasks 1–4 fully authored (against spec rev 6); Tasks 5–7 pending
authorship. DO NOT EXECUTE until this line says the plan is complete and codex-approved.**

**Pending spec nits (batch into the next spec touch, do not block):** §4's SRSCreator
feedback inventory wrongly lists a "result banner" — SRSCreatorView has none (the outcome
lands in the log; the result Border belongs to SRSReconstructorView). The plan's Task 3
carries the true inventory.

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
- Create: `ReScene.Manager/Behaviors/CompactRowMode.cs`
- Create: `ReScene.Manager.Tests/CompactHeightBehaviorTests.cs`
- Create: `ReScene.Manager.Tests/CompactInvariantRig.cs` (shared helper, used by every
  view task's invariant test)

**Interfaces (later tasks consume):**
- `CompactHeightBehavior.ThresholdProperty` (attached `double`, inner DIPs; NaN = off).
- `CompactHeightBehavior.RowSizesProperty` (attached `IReadOnlyList<CompactRowSize>`).
- `CompactHeightBehavior.HelpOpenProperty` (attached `bool`; the view binds it to the Help
  expander's `IsExpanded`; while compact AND open, donation values apply).
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
- Test: extend `CompactHeightBehaviorTests.cs` (HelpExpander wiring cases)

**Interfaces:**
- Consumes Task 1: Threshold/RowSizes/HelpOpen/FocusFallback, `compactHeight` class,
  `CompactInvariantRig`.
- Produces (Tasks 3-6 reuse): `HelpExpanderProperty` on the behavior; style classes
  `helpDisclosure` and `tipLine`; the splitter base/focus styles; the per-view test
  shape (invariant + rendered matrix + snapshots + chrome assertions).

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

- [ ] **Step 2: XAML restructure.** In `ReconstructorView.axaml`:

  (a) Row 0's `StackPanel` is wrapped in the disclosure (single instance — the intro
  TextBlock and links WrapPanel MOVE inside; authoring-time move, not runtime):

```xml
    <Expander Grid.Row="0" x:Name="HelpDisclosure" Classes="helpDisclosure"
              Margin="0,0,0,6"
              AutomationProperties.Name="Help &amp; links">
      <Expander.Header>
        <TextBlock Text="Help &amp; links" FontSize="{DynamicResource FontSizeCaption}" />
      </Expander.Header>
      <StackPanel>
        <!-- existing intro TextBlock (verbatim) -->
        <!-- existing links WrapPanel (verbatim, Click+Tag untouched) -->
      </StackPanel>
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
        Behaviors.CompactHeightBehavior.SetFocusFallback(root, HelpDisclosure);
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
  <Style Selector="Grid.compactHeight Expander.helpDisclosure">
    <Setter Property="MaxHeight" Value="200" />
    <!-- Body height is bounded by the behavior-computed budget; 200 is the style-level
         backstop — the invariant test asserts the real bound (~38 body + header). -->
  </Style>

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
    <Setter Property="Background" Value="{DynamicResource AccentBrush}" />
  </Style>
```

  (Exact accent resource name verified against Tokens.axaml at implementation; the test
  asserts rendered contrast, not the resource choice.)

- [ ] **Step 5: The view test suite** (`ReconstructorCompactTests.cs`) — the per-view
  shape every later view task copies. Uses `BeginnerShellTestFactory`-style inert VM
  construction (see `ReconstructorViewTests.CreateVm`). Cases:

```csharp
    // 1. Invariant (spec §1's four checks; CompactInvariantRig):
    //    - render view at width 676, force HasCustomPackerWarning=true, expanded mode:
    //      MeasureFloor < 421 (threshold)
    //    - compact class applied, Help closed: MeasureFloor <= 307
    //    - compact + HelpOpen (donation rows applied, body expanded to MaxHeight):
    //      MeasureFloor <= 307
    //    - pinned/action rows within the same sums (Reconstructor: toolbar row measured)
    // 2. Rendered matrix: MainWindow-hosted view at 700×450 (compact active) and at
    //    inner height 422 fresh (= Threshold+1, EXPANDED — hysteresis is restore-only):
    //    criterion A reachability for the last Options control and last Output control
    //    (scroll to end, translated bounds inside viewport), criterion B no-clip with the
    //    warning forced, criterion C real Tab walk (sentinel → full cycle → every focused
    //    bounds within all clipping ancestors ∩ window).
    // 3. Tab-order snapshots: ordered (type, automation name) at normal — equals the
    //    pre-change snapshot committed as a fixture; compact — equals the spec §2 order.
    // 4. Chrome: compact tip UIA Name == the FULL tip text (condition 1) and
    //    HelpText == full text (condition 2); exactly three link Buttons in the tree in
    //    BOTH modes (single instance); links invocable in compact (expander open →
    //    Click raises); expander reset on compact re-entry.
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
- Consumes: Task 1 behavior (Threshold/RowSizes/HelpExpander/FocusFallback,
  `CompactRowMode.AutoToStar`), Task 2's `helpDisclosure` styles and test shape.
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
      <!-- the existing intro TextBlock, verbatim, minus its old DockPanel.Dock/margin
           (margin moves to the Expander) -->
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

    <!-- 3: log band — verbatim log DockPanel (header + logList). -->
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
        Behaviors.CompactHeightBehavior.SetFocusFallback(root, HelpDisclosure);
```

- [ ] **Step 3: Tests** (`SRSCreatorCompactTests.cs`, Task 2's five-part shape adapted):

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

    <!-- 0: chrome — the intro TextBlock moves inside; header "Help". -->
    <Expander Grid.Row="0" x:Name="HelpDisclosure" Classes="helpDisclosure"
              Margin="0,0,0,6" AutomationProperties.Name="Help">
      <Expander.Header>
        <TextBlock Text="Help" FontSize="{DynamicResource FontSizeCaption}" />
      </Expander.Header>
      <!-- existing intro TextBlock, verbatim -->
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
         bounded — a11y rev-3 NEW-4's banner cap; ToolTip.Tip binds the full summary) /
         separator. -->

    <!-- 3: log band — verbatim log DockPanel. -->
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
        Behaviors.CompactHeightBehavior.SetFocusFallback(root, HelpDisclosure);
```

- [ ] **Step 3: Tests** (`SRSReconstructorCompactTests.cs`, the established five-part
  shape):

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

### Tasks 5–7 (pending authorship — DO NOT DISPATCH)

Authored next, one at a time, each against the view's full markup, to the same
no-placeholder standard:

- **Task 5 — SampleRestorerView** (three-band + DataGrid-in-scroller handoff contracts,
  threshold 535).
- **Task 6 — CreatorView** (largest: three-band generalization, stored-files row via
  RowSizes 150/80 pixel mode, detected-sets MaxHeight, in-scroller splitter E-scoping,
  threshold 720).
- **Task 7 — Settings audit + whole board**: Settings compliance check, five-view parity
  evidence, criterion C Tab-walks all views, font-enlargement test, full suites + gate,
  CHANGELOG entry, spec → implemented.
