using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
            Dispatcher.UIThread.RunJobs();
            // The app-level styles hide row-0 content in compact AND the expander header
            // at normal (flat mode); the unit test simulates BOTH with the class
            // (codex round-3: without the header simulation the restore leg never
            // strands focus and the assertion is vacuous):
            var toggle = expander.GetVisualDescendants().OfType<ToggleButton>().First();
            root.Classes.CollectionChanged += (_, _) =>
            {
                bool compact = root.Classes.Contains("compactHeight");
                collapsing.IsVisible = !compact;
                toggle.IsVisible = compact;        // flat normal mode hides the header
            };
            Dispatcher.UIThread.RunJobs();

            collapsing.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(collapsing.IsFocused);

            w.Height = Threshold - 1;              // → compact; collapsing hides
            Dispatcher.UIThread.RunJobs();
            Assert.True(toggle.IsFocused,
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
        // root must not move it. The shell is a DockPanel with the root as FILL so the
        // root's height stays window-driven and the transitions genuinely fire
        // (codex round-3: a StackPanel rehost left the root content-sized and the test
        // could pass without any mode change ever happening).
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            var outside = new Button { Content = "shell" };
            DockPanel.SetDock(outside, Dock.Top);
            var shell = new DockPanel();
            w.Content = null;
            shell.Children.Add(outside);
            shell.Children.Add(root);               // fill child: window-driven height
            w.Content = shell;
            Dispatcher.UIThread.RunJobs();

            outside.Focus();
            Dispatcher.UIThread.RunJobs();

            w.Height = Threshold - 1;              // → compact
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("compactHeight", root.Classes);   // the transition REALLY ran
            Assert.True(outside.IsFocused, "transitions must never steal focus from outside the view");

            w.Height = Threshold + 40;             // → restore (past hysteresis)
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("compactHeight", root.Classes);
            Assert.True(outside.IsFocused);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void ChainTerminal_RootGetsTransientFocusability()
    {
        // Spec rev 11: a view with NO focusable descendants forces the chain to its
        // terminal — the root itself, made focusable only for the hand-off.
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            var collapsing = new Button { Content = "only", [Grid.RowProperty] = 0 };
            root.Children.Add(collapsing);
            root.Classes.CollectionChanged += (_, _) =>
                collapsing.IsVisible = !root.Classes.Contains("compactHeight");
            Dispatcher.UIThread.RunJobs();
            collapsing.Focus();
            Dispatcher.UIThread.RunJobs();

            w.Height = Threshold - 1;              // → compact; the ONLY focusable hides
            Dispatcher.UIThread.RunJobs();
            Assert.True(root.IsFocused, "the chain must terminate at the root");
            Assert.True(root.Focusable, "behavior grants transient focusability");

            var other = new Button { Content = "x", [Grid.RowProperty] = 2 };
            root.Children.Add(other);
            Dispatcher.UIThread.RunJobs();
            other.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.False(root.Focusable, "focusability is reset when the root loses focus");
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void UnfocusableAfterRestore_Relocates_EvenThoughVisible()
    {
        // Spec rev 11 trigger: restore leaves a compact-only-focusable element visible
        // but unfocusable — focus must move to the RestoreFocusTarget.
        (Window w, Grid root) = Host(Threshold - 1);
        try
        {
            var scroller = new ScrollViewer { [Grid.RowProperty] = 2, Focusable = true };
            var restoreTarget = new Button { Content = "input", [Grid.RowProperty] = 1 };
            root.Children.Add(scroller);
            root.Children.Add(restoreTarget);
            CompactHeightBehavior.SetRestoreFocusTarget(root, restoreTarget);
            root.Classes.CollectionChanged += (_, _) =>
                scroller.Focusable = root.Classes.Contains("compactHeight");
            Dispatcher.UIThread.RunJobs();

            scroller.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(scroller.IsFocused);

            w.Height = Threshold + 40;             // → restore; scroller stays visible
            Dispatcher.UIThread.RunJobs();
            Assert.True(restoreTarget.IsFocused,
                "an unfocusable focus-holder is stranding even when fully visible");
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

    // ── Fix round 1 (code review): covering tests for the four blocking findings ─────

    /// <summary>
    /// Finding #1: a fresh instance that starts (and stays) at normal height must still
    /// synchronize its Help expander to the "flat mode, force-expanded" state on its very
    /// first evaluation — even though that evaluation crosses no threshold (state.IsCompact's
    /// false default already matches the computed mode, so nothing "transitions"). Before the
    /// fix, Evaluate's early-return for "no mode change" fired before ApplyHelpExpanderDirection
    /// ever ran, leaving a fresh Expander at its own IsExpanded=false default — hiding the
    /// content in both modes (header hidden by normal-mode styles, body collapsed by default).
    /// </summary>
    [AvaloniaFact]
    public void FreshNormalInstance_SynchronizesExpanderToFlatMode()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,150,*") };
        var expander = new Expander { [Grid.RowProperty] = 0 };
        root.Children.Add(expander);
        root.Children.Add(new Border { [Grid.RowProperty] = 1 });
        root.Children.Add(new Border { [Grid.RowProperty] = 2 });
        CompactHeightBehavior.SetThreshold(root, Threshold);
        CompactHeightBehavior.SetHelpExpander(root, expander);

        var window = new Window { Width = 700, Height = Threshold + 50, Content = root };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.DoesNotContain("compactHeight", root.Classes);   // confirms this never transitions
            Assert.True(expander.IsExpanded,
                "flat/normal mode must force the Help body expanded, even on a fresh instance that never crosses the threshold");
            Assert.False(CompactHeightBehavior.GetHelpOpen(root), "HelpOpen only applies while compact");
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Finding #2: the deferred (Loaded-priority) recovery job must reject itself once its
    /// premise is stale. This exercises the current-focus guard the most naturally
    /// constructible way: a "user" focus move happens SYNCHRONOUSLY within the very same
    /// transition that captured focus (via the compactHeight class-changed side effect, before
    /// the deferred job is even posted) — simulating focus moving on before the staged recovery
    /// gets its turn. The deferred job must never overwrite that later choice, and — proving it
    /// backed off before even resolving a target, not just coincidentally agreed with it — the
    /// fallback chain's own candidate must never end up focused either.
    /// </summary>
    [AvaloniaFact]
    public void StaleDeferredRecovery_FocusMovedAwayFromCaptured_IsNeverOverwritten()
    {
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            var collapsing = new Button { Content = "link", [Grid.RowProperty] = 0 };
            var otherFallbackTarget = new Button { Content = "otherFallback", [Grid.RowProperty] = 1 };
            var elsewhere = new Button { Content = "elsewhere", [Grid.RowProperty] = 2 };
            root.Children.Add(collapsing);
            root.Children.Add(otherFallbackTarget);
            root.Children.Add(elsewhere);
            root.Classes.CollectionChanged += (_, _) =>
            {
                bool compact = root.Classes.Contains("compactHeight");
                collapsing.IsVisible = !compact;
                if (compact)
                {
                    elsewhere.Focus();   // simulated user focus move, synchronous with the transition
                }
            };
            Dispatcher.UIThread.RunJobs();

            collapsing.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(collapsing.IsFocused);

            w.Height = Threshold - 1;   // -> compact; collapsing hides AND focus moves to `elsewhere`
                                          // synchronously, before the deferred recovery job is posted
            Dispatcher.UIThread.RunJobs();

            Assert.True(elsewhere.IsFocused,
                "a focus change that happened before the deferred recovery runs must win");
            Assert.False(otherFallbackTarget.IsFocused,
                "the fallback chain must never even run once focus has already moved away from the captured element");
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// Fix round 2, item #1 (regression in the above fix): the current-focus guard must yield
    /// ONLY to a USABLE different focus. Here, focus moves from A to B synchronously within
    /// the same transition that captured A — but B is (already) permanently clipped by a plain
    /// ClipToBounds Border, not a ScrollViewer, so nothing ever answers BringIntoView and B stays
    /// obscured. B does NOT auto-clear from FocusManager the way an IsVisible=false element does
    /// (clipping is purely visual), so it is genuinely still "the current focus" when the
    /// deferred job runs — and unlike <see cref="StaleDeferredRecovery_FocusMovedAwayFromCaptured_IsNeverOverwritten"/>'s
    /// `elsewhere` (fully valid), B is itself broken. The recovery must not yield to it — it
    /// must relocate FROM B (not from the originally-captured A) to the direction target.
    /// </summary>
    [AvaloniaFact]
    public void CurrentFocusGuard_YieldsOnlyToUsableFocus_RecoversWhenNewFocusIsAlsoStranded()
    {
        (Window w, Grid root) = Host(Threshold - 1);
        try
        {
            var a = new Button { Content = "A", [Grid.RowProperty] = 0 };
            var clipper = new Border { [Grid.RowProperty] = 1, Height = 20, ClipToBounds = true };
            var bHost = new StackPanel();
            var b = new Button { Content = "B", Height = 30, Margin = new Thickness(0, 50, 0, 0) }; // permanently clipped
            bHost.Children.Add(b);
            clipper.Child = bHost;
            var restoreTarget = new Button { Content = "direction target", [Grid.RowProperty] = 2 };
            root.Children.Add(a);
            root.Children.Add(clipper);
            root.Children.Add(restoreTarget);
            CompactHeightBehavior.SetRestoreFocusTarget(root, restoreTarget);
            root.Classes.CollectionChanged += (_, _) =>
            {
                if (!root.Classes.Contains("compactHeight"))
                {
                    b.Focus();   // "user"/some code moves focus to the ALREADY-clipped B,
                                  // synchronously, within the same transition that captured A
                }
            };
            Dispatcher.UIThread.RunJobs();

            a.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(a.IsFocused);

            w.Height = Threshold + 40;   // -> restore; captures A; the handler above moves
                                          // focus to the obscured B before the deferred job runs
            Dispatcher.UIThread.RunJobs();

            Assert.True(restoreTarget.IsFocused,
                "B is a DIFFERENT, in-scope focus target, but it is itself obscured — the guard " +
                "must not yield to it, and recovery must still relocate to the direction target");
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// Finding #3 (fix round 2 refinement — the original version of this test was not
    /// discriminating: target sat wholly outside the outer viewport, which even the OLD,
    /// per-clipper-independent check already caught via its "vs outer" test alone, since that
    /// uses the fully-composed transform). THIS geometry is genuinely discriminating: target is
    /// tall enough (150px) to extend past inner's own 100px viewport. Concretely, in
    /// inner-rendered coordinates: target spans 50..200; inner's own viewport is [0,100]
    /// (independently overlaps target at 50..100); outer's raw window, mapped into
    /// inner-rendered space, is [150,250] (independently overlaps target at 150..200). Each
    /// clipper independently finds SOME overlap with target — but in DISJOINT sub-ranges that
    /// share no point (50..100 vs 150..200), so no single point of target is ever actually
    /// visible through both at once: the true combined region (their intersection, empty here)
    /// excludes it entirely. Proven both ways in the fix-round-2 report: the per-clipper
    /// independent implementation was temporarily restored and confirmed to pass this test (a
    /// false negative — a silent, undetected obscurement) before the cumulative-intersection fix
    /// was reapplied and confirmed to fail it correctly (BringIntoView is only ever invoked, and
    /// only ever moves either offset, when IsObscured's initial verdict is true).
    /// </summary>
    [AvaloniaFact]
    public void NestedClippers_DisjointIndependentOverlaps_AreObscuredOnlyByTheCombinedCheck()
    {
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            var outer = new ScrollViewer { [Grid.RowProperty] = 2, Height = 100 };
            var inner = new ScrollViewer { Height = 100 };
            var innerStack = new StackPanel();
            innerStack.Children.Add(new Border { Height = 50 });     // pushes target to inner-content-Y 50
            var target = new Button { Content = "target", Height = 150 };
            innerStack.Children.Add(target);
            inner.Content = innerStack;
            var outerStack = new StackPanel();
            outerStack.Children.Add(inner);                  // inner is outer's FIRST content: P=0
            outerStack.Children.Add(new Border { Height = 200 }); // gives outer room to scroll to 150
            outer.Content = outerStack;
            root.Children.Add(outer);
            Dispatcher.UIThread.RunJobs();

            target.Focus();
            inner.Offset = default;                 // inner unscrolled: shows inner-rendered [0,100]
            outer.Offset = new Vector(0, 150);       // outer's raw window becomes inner-rendered
                                                      // [150,250] — independently overlaps target's
                                                      // [50,200] at [150,200], disjoint from inner's
                                                      // own overlap at [50,100]
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, inner.Offset.Y);
            Assert.Equal(150, outer.Offset.Y);

            double innerBefore = inner.Offset.Y;
            double outerBefore = outer.Offset.Y;

            w.Height = Threshold - 1;   // any transition runs the post-layout obscurement check
            Dispatcher.UIThread.RunJobs();

            Assert.True(inner.Offset.Y != innerBefore || outer.Offset.Y != outerBefore,
                "BringIntoView must have been attempted — proving IsObscured classified target as " +
                "obscured despite both clippers independently showing SOME (disjoint) overlap with it; " +
                "the old per-clipper-independent check would see no obscurement and touch neither offset");
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// Finding #4a (entering direction): fallback candidates must be validated, not merely
    /// assumed usable. A Focusable=false descendant, an IsVisible=false descendant, and the
    /// captured element itself (clipped but otherwise Focusable/Enabled, so ONLY the explicit
    /// exclusion keeps it out) are all in the tree; none may be selected, and the chain must
    /// still reach the guaranteed root terminal rather than silently stopping.
    /// </summary>
    [AvaloniaFact]
    public void FallbackChain_EnteringDirection_NeverReselectsClippedCapture_ReachesRootTerminal()
    {
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            var clipper = new Border { [Grid.RowProperty] = 2, Height = 20, ClipToBounds = true };
            var innerStack = new StackPanel();
            var captured = new Button { Content = "captured", Height = 30, Margin = new Thickness(0, 50, 0, 0) };
            innerStack.Children.Add(captured);
            clipper.Child = innerStack;

            var unfocusableDescendant = new Button { Content = "unfocusable", Focusable = false, [Grid.RowProperty] = 0 };
            var invisibleDescendant = new Button { Content = "invisible", IsVisible = false, [Grid.RowProperty] = 1 };
            root.Children.Add(unfocusableDescendant);
            root.Children.Add(invisibleDescendant);
            root.Children.Add(clipper);
            Dispatcher.UIThread.RunJobs();

            captured.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(captured.IsFocused);

            w.Height = Threshold - 1;   // no HelpExpander set: resolved target is null, walk begins immediately
            Dispatcher.UIThread.RunJobs();

            Assert.False(captured.IsFocused,
                "captured stays clipped (nothing answers BringIntoView here) and must never be reselected");
            Assert.False(unfocusableDescendant.IsFocused);
            Assert.False(invisibleDescendant.IsFocused);
            Assert.True(root.IsFocused, "every real candidate is unusable: the chain must reach the root terminal");
            Assert.True(root.Focusable, "behavior grants transient focusability at the terminal");
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// Finding #4b (restore direction): the resolved direction target itself can be unusable —
    /// here, a RestoreFocusTarget that is referenced but was never attached to any tree at all.
    /// The chain must skip it (not silently end there) and still reach the root terminal, since
    /// every other real candidate is also unusable.
    /// </summary>
    [AvaloniaFact]
    public void FallbackChain_RestoreDirection_SkipsDetachedRestoreTarget_ReachesRootTerminal()
    {
        (Window w, Grid root) = Host(Threshold - 1);
        try
        {
            var scroller = new ScrollViewer { [Grid.RowProperty] = 2, Focusable = true };
            var unfocusableDescendant = new Button { Content = "unfocusable", Focusable = false, [Grid.RowProperty] = 1 };
            var detachedRestoreTarget = new Button { Content = "detached" }; // referenced but never attached
            root.Children.Add(unfocusableDescendant);
            root.Children.Add(scroller);
            CompactHeightBehavior.SetRestoreFocusTarget(root, detachedRestoreTarget);
            root.Classes.CollectionChanged += (_, _) =>
                scroller.Focusable = root.Classes.Contains("compactHeight");
            Dispatcher.UIThread.RunJobs();

            scroller.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(scroller.IsFocused);

            w.Height = Threshold + 40;   // -> restore: scroller becomes unfocusable (compact-only);
                                          // RestoreFocusTarget resolves to a DETACHED control
            Dispatcher.UIThread.RunJobs();

            Assert.False(unfocusableDescendant.IsFocused);
            Assert.True(root.IsFocused,
                "a detached RestoreFocusTarget must be skipped rather than silently ending the chain there");
            Assert.True(root.Focusable);
        }
        finally { w.Close(); }
    }
}
