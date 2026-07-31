using System.Reflection;
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

    /// <summary>
    /// Task 2 brief, Step 1: the HelpExpander per-mode/donation contract, exercised as its own
    /// round-trip (compact entry resets to collapsed, user-opened HelpOpen tracks it, restore
    /// re-flattens and turns donation off, re-entering compact resets again — durability is
    /// compact-session scoped, not permanent). RED-FIRST as given: this test attaches the
    /// expander AFTER the control has already been through its first Evaluate() (Host() shows
    /// the window before SetHelpExpander runs) — a real gap Task 1's own tests never exercised,
    /// since all of them attach the expander BEFORE first attachment. Fixed by
    /// OnHelpExpanderChanged additionally synchronizing a just-attached expander to the CURRENT
    /// mode (see CompactHeightBehavior.cs and the task report for the full analysis) — a narrow,
    /// additive fix; no existing test's behavior changed.
    /// </summary>
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
    /// Fix round 3, item #1 (regression in round 2's ResolveRecoveryTarget refactor): the
    /// ENTRY-time current-focus check (before BringIntoView) was restored correctly in round 2,
    /// but the POST-BringIntoView recheck regressed to generation/mode only, dropping the
    /// "did focus move to something else valid in the meantime" half of the fix-round-1
    /// guarantee. Here, captured is permanently clipped (nothing answers BringIntoView), so the
    /// obscured branch runs; a handler on captured's OWN RequestBringIntoViewEvent — which
    /// fires synchronously, DURING the BringIntoView() call itself — moves focus to a valid,
    /// unrelated element, simulating a user action racing the recovery attempt. The fallback
    /// chain must yield to it rather than overwrite it — proven discriminating by placing an
    /// EARLIER-in-tree-order, otherwise-eligible fallback candidate that the chain would have
    /// landed on instead, had it run at all.
    /// </summary>
    [AvaloniaFact]
    public void PostBringIntoView_FocusMovedToValidElement_FallbackChainYields()
    {
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            var otherFallbackTarget = new Button { Content = "otherFallback", [Grid.RowProperty] = 0 };
            var validElsewhere = new Button { Content = "validElsewhere", [Grid.RowProperty] = 1 };
            var clipper = new Border { [Grid.RowProperty] = 2, Height = 20, ClipToBounds = true };
            var innerStack = new StackPanel();
            var captured = new Button { Content = "captured", Height = 30, Margin = new Thickness(0, 50, 0, 0) };
            innerStack.Children.Add(captured);
            clipper.Child = innerStack;
            root.Children.Add(otherFallbackTarget);
            root.Children.Add(validElsewhere);
            root.Children.Add(clipper);

            // Fires synchronously inside captured.BringIntoView(), simulating focus moving to
            // a valid element WHILE the staged recovery is in progress.
            captured.AddHandler(Control.RequestBringIntoViewEvent, (_, _) => validElsewhere.Focus());
            Dispatcher.UIThread.RunJobs();

            captured.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(captured.IsFocused);

            w.Height = Threshold - 1;   // no HelpExpander set: resolved target is null, so the
                                          // fallback chain (if it ran) would try otherFallbackTarget first
            Dispatcher.UIThread.RunJobs();

            Assert.True(validElsewhere.IsFocused,
                "focus that moved to a valid element during BringIntoView must not be overwritten");
            Assert.False(otherFallbackTarget.IsFocused,
                "the fallback chain must never even run once focus has already moved to something valid");
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// Finding #3 (fix round 2 refinement — the original version of this test was not
    /// discriminating: target sat wholly outside the outer viewport, which even the OLD,
    /// per-clipper-independent check already caught via its "vs outer" test alone, since that
    /// uses the fully-composed transform). THIS geometry is genuinely discriminating: target
    /// straddles the GAP between the two clippers' own ranges. Concretely, in inner-rendered
    /// coordinates: target spans 95..115; inner's own viewport is [0,100] (independently
    /// overlaps target at 95..100); outer's raw window, mapped into inner-rendered space, is
    /// [110,210] (independently overlaps target at 110..115). Each clipper independently finds
    /// SOME overlap with target — but in DISJOINT sub-ranges that share no point (95..100 vs
    /// 110..115, with a 100..110 gap between them), so no single point of target is ever
    /// actually visible through both at once: the true combined region (their intersection,
    /// empty here) excludes it entirely.
    /// It remains true that a SINGLE BringIntoView call cannot recover this shape: for the
    /// discriminating case to exist at all, target must extend beyond the INNER scroller's own
    /// range (if target were wholly within inner's own range, "vs outer independently passes"
    /// would force the combined intersection to include it too — algebraically, X⊆A and X∩B≠∅
    /// together imply X∩(A∩B)≠∅), so inner — always the first ancestor in the bubble path — is
    /// the one that adjusts, and having adjusted it sets e.Handled and the outer never sees
    /// request 1.
    /// FIX-ROUND-5 CORRECTION: rounds 3 and 4 stopped there and concluded relocation was the
    /// only available end-state. That was an artifact of the implementation's one-attempt-per-
    /// target rule, not of Avalonia. A SECOND request finds inner already satisfied (it returns
    /// false, leaving e.Handled false) and therefore reaches the outer, which completes the
    /// recovery — so the correct end-state is that target KEEPS focus. The retry-on-progress
    /// rule is covered directly by
    /// <see cref="PartialInnerProgress_SecondRequestReachesOuter_TargetRecovered"/>; what THIS
    /// test still owns, and asserts below, is the DETECTION half.
    /// RED/GREEN proof (re-verified for this exact test): with IsObscured temporarily reverted
    /// to the pre-fix, per-clipper-independent implementation, this test FAILS (both
    /// independent checks pass, so IsObscured never even calls BringIntoView and neither offset
    /// nor focus ever changes) — RED. With the cumulative-intersection implementation restored,
    /// this test PASSES — GREEN.
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
            innerStack.Children.Add(new Border { Height = 95 });   // pushes target to inner-content-Y 95
            var target = new Button { Content = "target", Height = 20 };
            innerStack.Children.Add(target);
            inner.Content = innerStack;
            var outerStack = new StackPanel();
            outerStack.Children.Add(inner);                        // inner is outer's FIRST content: P=0
            outerStack.Children.Add(new Border { Height = 200 });   // gives outer room to scroll to 110
            outer.Content = outerStack;
            var fallbackTarget = new Button { Content = "fallback", [Grid.RowProperty] = 1 };
            root.Children.Add(fallbackTarget);
            root.Children.Add(outer);
            Dispatcher.UIThread.RunJobs();

            target.Focus();
            inner.Offset = default;                 // inner unscrolled: shows inner-rendered [0,100]
            outer.Offset = new Vector(0, 110);       // outer's raw window becomes inner-rendered
                                                      // [110,210] — independently overlaps target's
                                                      // [95,115] at [110,115], disjoint from inner's
                                                      // own overlap at [95,100] (a 100..110 gap
                                                      // separates the two independent overlaps)
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, inner.Offset.Y);
            Assert.Equal(110, outer.Offset.Y);

            w.Height = Threshold - 1;   // any transition runs the post-layout obscurement check
            Dispatcher.UIThread.RunJobs();

            Assert.True(inner.Offset.Y != 0,
                "inner attempted to bring target into its OWN view — proves BringIntoView ran, " +
                "which only happens if IsObscured's initial verdict was true (the old per-clipper " +
                "check would see no obscurement and never call it at all)");
            Assert.True(target.IsFocused,
                "detection triggered recovery, and recovery completes here (fix round 5): inner " +
                "consumed request 1, the retry reached outer, and target is visible through both");
            Assert.False(fallbackTarget.IsFocused,
                "recoverable focus is never relocated");
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

    /// <summary>
    /// Fix round 3, item #3: <c>Generation++</c>'s placement (before the captured-null return)
    /// is correct, but the deferred job's lambda originally read <c>state.Generation</c> LIVE
    /// at run time instead of a value captured at post time — always comparing the live field
    /// to itself, never detecting staleness. Fixed by freezing it into a local before posting.
    /// A genuine two-real-transitions ABA race — where a second transition bumps the
    /// generation strictly BETWEEN the first transition's job being posted and that job
    /// running — is not constructible through the public API. Proven three independent ways:
    /// (1) QueueEvaluate's own coalescing (the updateQueued guard) allows at most one pending
    /// Evaluate at a time, so a second transition's Evaluate cannot even be queued while the
    /// first transition's Evaluate has not yet run.
    /// (2) Once posted, the deferred recovery job runs at Loaded priority (1) — HIGHER than
    /// the Default priority (0) that Evaluate itself (and thus any subsequent transition's
    /// Evaluate) runs at — so within one dispatcher drain, transition A's OWN recovery job is
    /// always serviced before a newly-queued transition B's Evaluate could run.
    /// (3) <c>Dispatcher.RunJobs(priority)</c> is an INCLUSIVE (>=) threshold over discrete,
    /// adjacent priority values (confirmed empirically in fix round 2: Default=0, Loaded=1,
    /// nothing between them), so there is no partial-drain call that lets Default-priority
    /// work run while withholding Loaded-priority work newly posted as a result of it.
    /// This test instead verifies the guarantee the fix actually provides, directly: the
    /// (reflection-reached) private <c>RelocateFocusIfNeeded</c> is invoked with a generation
    /// value that deliberately does not match the live <c>state.Generation</c> — exactly what
    /// a stale, frozen-at-post-time local would look like after a later transition bumped the
    /// live field — and must no-op, never reaching the fallback chain.
    /// </summary>
    [AvaloniaFact]
    public void StaleGeneration_DirectlyInjected_CausesTheDeferredJobToNoOp()
    {
        (Window w, Grid root) = Host(Threshold - 1);
        try
        {
            var collapsing = new Button { Content = "link", [Grid.RowProperty] = 0 };
            var restoreTarget = new Button { Content = "target", [Grid.RowProperty] = 1 };
            root.Children.Add(collapsing);
            root.Children.Add(restoreTarget);
            CompactHeightBehavior.SetRestoreFocusTarget(root, restoreTarget);
            root.Classes.CollectionChanged += (_, _) =>
                collapsing.IsVisible = !root.Classes.Contains("compactHeight");
            Dispatcher.UIThread.RunJobs();

            collapsing.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(collapsing.IsFocused);

            // Hide collapsing directly (what a real restore transition would do to it),
            // without going through a real transition, so RelocateFocusIfNeeded can be
            // invoked afterward with full control over its generation argument.
            collapsing.IsVisible = false;
            Dispatcher.UIThread.RunJobs();

            object state = GetPrivateState(root);
            int liveGeneration = GetGeneration(state);
            // enteringCompact must MATCH state.IsCompact (the root was hosted below the
            // threshold, so it is compact): fix round 4 found this argument was `false` here,
            // which tripped IsSuperseded's MODE check first and made the generation argument
            // irrelevant — the test no-opped for the wrong reason. Matching the mode leaves the
            // deliberately-mismatched generation as the only thing that can reject the callback.
            InvokeRelocateFocusIfNeeded(root, collapsing, enteringCompact: true, liveGeneration + 1, state);
            Dispatcher.UIThread.RunJobs();

            Assert.False(restoreTarget.IsFocused,
                "a generation that does not match state.Generation must reject the callback " +
                "outright — the fallback chain must never run, so it must never reach the direction target");
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// Fix round 4, item #1: after the BringIntoView attempt, the recovery must re-run the
    /// FULL resolution — re-resolve what is focused NOW (yielding to a newer VALID focus,
    /// RETARGETING a newer in-scope-but-unusable one) and only THEN evaluate settledness.
    /// Round 3 checked settledness FIRST, so the one case where BringIntoView actually
    /// succeeds — the captured element ends up perfectly visible — returned before any
    /// re-resolution, stranding a control that the very same recovery attempt had just
    /// left focused and unusable. Here <c>captured</c> sits in a real ScrollViewer (so
    /// BringIntoView genuinely recovers it, asserted below) and its own
    /// RequestBringIntoView handler focuses <c>strandedNew</c>, permanently clipped by a
    /// plain ClipToBounds Border. Settled-first sees "captured is fine" and returns;
    /// resolve-first sees that focus now sits on a broken element and recovers THAT.
    /// </summary>
    [AvaloniaFact]
    public void PostBringIntoView_FocusMovedToUnusableElement_IsRecovered_NotStranded()
    {
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            // First in tree order, so the fallback walk has a deterministic landing spot.
            var fallbackTarget = new Button { Content = "fallback", [Grid.RowProperty] = 0 };

            var clipper = new Border { [Grid.RowProperty] = 1, Height = 20, ClipToBounds = true };
            var clippedHost = new StackPanel();
            var strandedNew = new Button { Content = "strandedNew", Height = 30, Margin = new Thickness(0, 50, 0, 0) };
            clippedHost.Children.Add(strandedNew);
            clipper.Child = clippedHost;

            var scroller = new ScrollViewer { [Grid.RowProperty] = 2, Height = 60 };
            var stack = new StackPanel();
            for (int i = 0; i < 10; i++) stack.Children.Add(new Button { Content = $"b{i}", Height = 30 });
            scroller.Content = stack;

            root.Children.Add(fallbackTarget);
            root.Children.Add(clipper);
            root.Children.Add(scroller);
            Dispatcher.UIThread.RunJobs();

            var captured = (Button)stack.Children[^1];
            captured.Focus();
            scroller.Offset = default;             // scroll captured out of view
            Dispatcher.UIThread.RunJobs();
            Assert.True(captured.IsFocused);
            Assert.Equal(0, scroller.Offset.Y);

            // Registered only now: Focus() itself raises a bring-into-view request
            // (ScrollViewer.BringIntoViewOnFocusChange), which would fire this during setup.
            // Fires synchronously inside captured.BringIntoView(), BEFORE the scroller
            // handles the bubbling request and recovers captured.
            captured.AddHandler(Control.RequestBringIntoViewEvent, (_, _) => strandedNew.Focus());

            w.Height = Threshold - 1;              // transition runs the staged recovery
            Dispatcher.UIThread.RunJobs();

            Assert.True(scroller.Offset.Y > 0,
                "setup precondition: BringIntoView really did recover `captured`, so a " +
                "settledness-first ordering short-circuits right here");
            Assert.False(strandedNew.IsFocused,
                "the element focused DURING the recovery attempt is itself unusable — it must " +
                "not be left stranded just because the originally-captured element got settled");
            Assert.True(fallbackTarget.IsFocused,
                "re-resolution must retarget onto the newly-focused unusable element and relocate it");
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// Fix round 4, item #2: the OUTER-scroller recovery guarantee. Rounds 1-3 kept
    /// dropping it, the last round claiming it was impossible for nested clippers; it is
    /// not. The geometry that makes it real is an OVERSIZED inner viewport: the inner
    /// scroller already shows the target in full, so it cannot improve anything —
    /// <c>ScrollContentPresenter.BringIntoViewRequested</c> sets
    /// <c>e.Handled = BringDescendantIntoView(...)</c>, and that returns false when no
    /// offset change is needed, so the request bubbles ON to the outer scroller, which is
    /// the only clipper that can clear the cumulative obscurity.
    /// Numbers (root space, after layout; row 2 starts at y=190): outer viewport
    /// [190,290]; outer scrolled to 160 puts inner's 200-tall viewport at [30,230] and the
    /// target at [80,180]. Cumulative visible region = [190,290] ∩ [30,230] = [190,230],
    /// which the target misses entirely → obscured. BringIntoView: inner sees the target
    /// at inner-content [50,150] inside its own [0,200] viewport → no change, unhandled;
    /// outer sees it at outer-content [50,150] against a [160,260] window → scrolls to 50.
    /// The target then lands at root [190,290], fully inside the cumulative region, so it
    /// is recovered and KEEPS focus rather than being relocated.
    /// </summary>
    [AvaloniaFact]
    public void NestedClippers_OnlyOuterCanRecover_BringIntoViewMovesOuter_TargetKeepsFocus()
    {
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            var inner = new ScrollViewer { Height = 200 };
            var innerStack = new StackPanel();
            innerStack.Children.Add(new Border { Height = 50 });
            var target = new Button { Content = "target", Height = 100 };
            innerStack.Children.Add(target);
            inner.Content = innerStack;            // extent 150 < viewport 200: inner CANNOT scroll

            var outer = new ScrollViewer { [Grid.RowProperty] = 2, Height = 100 };
            var outerStack = new StackPanel();
            outerStack.Children.Add(inner);        // inner is outer's first content: outer-content Y 0
            outerStack.Children.Add(new Border { Height = 300 });   // scroll room for outer
            outer.Content = outerStack;
            root.Children.Add(outer);
            Dispatcher.UIThread.RunJobs();

            target.Focus();
            inner.Offset = default;
            outer.Offset = new Vector(0, 160);     // pushes target above outer's viewport
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, inner.Offset.Y);
            Assert.Equal(160, outer.Offset.Y);

            w.Height = Threshold - 1;              // any transition runs the obscurement check
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, inner.Offset.Y);
            Assert.True(outer.Offset.Y < 160,
                "only the OUTER clipper can clear the cumulative obscurity here, so BringIntoView " +
                "must have moved the OUTER offset (the inner one already showed the target in full)");
            Assert.True(target.IsFocused,
                "outer-scroller recovery succeeded, so the target keeps focus and is never relocated");
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// Fix round 4, item #3: <see cref="StaleGeneration_DirectlyInjected_CausesTheDeferredJobToNoOp"/>
    /// only exercises the mismatch guard itself — it passes just as well against the live-capture
    /// form that round 3 replaced, so it never discriminated frozen from live lambda capture.
    /// This one does. It reaches the private callback FACTORY (which freezes state.Generation
    /// into a local at creation time), builds a callback, THEN bumps state.Generation behind its
    /// back — the "later transitions landed between post time and run time" window the freeze
    /// exists for — and only then runs it:
    /// <list type="bullet">
    /// <item>frozen capture: the callback still holds the pre-bump generation, sees the
    /// mismatch, and no-ops (GREEN);</item>
    /// <item>live capture (<c>() =&gt; Relocate(..., state.Generation, state)</c>): the field is
    /// read at RUN time, so it equals itself no matter how many transitions intervened, the
    /// guard can never fire, and the callback relocates focus (RED).</item>
    /// </list>
    /// The positive control at the end — the same scenario with a freshly built callback, whose
    /// frozen generation IS current, relocating exactly as expected — proves the no-op above
    /// came from the guard and not from a scenario that could never have relocated anything.
    /// </summary>
    [AvaloniaFact]
    public void FrozenGeneration_CallbackBuiltBeforeLaterTransitions_NoOps()
    {
        (Window w, Grid root) = Host(Threshold - 1);
        try
        {
            var collapsing = new Button { Content = "link", [Grid.RowProperty] = 0 };
            var fallbackCandidate = new Button { Content = "fallback", [Grid.RowProperty] = 1 };
            root.Children.Add(collapsing);
            root.Children.Add(fallbackCandidate);
            Dispatcher.UIThread.RunJobs();

            collapsing.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.True(collapsing.IsFocused);

            object state = GetPrivateState(root);
            // Built BEFORE the generation moves on — exactly what Evaluate does at post time.
            // enteringCompact matches state.IsCompact (hosted below the threshold), so the mode
            // half of IsSuperseded can never be what rejects this: only the generation can.
            Action callback = InvokeCreateRecoveryCallback(root, collapsing, enteringCompact: true, state);

            // Three later transitions' worth of bumps, landing strictly between the callback's
            // creation and its execution.
            SetGeneration(state, GetGeneration(state) + 3);

            collapsing.IsVisible = false;   // what such a later transition would do to it
            Dispatcher.UIThread.RunJobs();

            callback();
            Dispatcher.UIThread.RunJobs();
            Assert.False(fallbackCandidate.IsFocused,
                "the callback froze the pre-bump generation, so it must reject itself; a LIVE " +
                "read of state.Generation would equal itself here and relocate focus instead");

            InvokeCreateRecoveryCallback(root, collapsing, enteringCompact: true, state)();
            Dispatcher.UIThread.RunJobs();
            Assert.True(fallbackCandidate.IsFocused,
                "positive control: with a matching generation the very same scenario DOES " +
                "relocate — so the no-op above came from the guard, not from an inert scenario");
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// Fix round 5, item #1: one BringIntoView request per target is not enough. A scroller
    /// that PARTIALLY satisfies a request still consumes it — <c>ScrollContentPresenter</c>
    /// sets <c>e.Handled = BringDescendantIntoView(...)</c>, true whenever it moved — so the
    /// next scroller outward never sees request 1. Round 4's one-attempt-per-target rule
    /// therefore relocated focus that a second request would have recovered.
    /// Geometry (the disjoint-overlap shape, whose target straddles the two clippers' gap):
    /// target at inner-content [95,115], inner viewport 100 at offset 0, outer viewport 100 at
    /// offset 110. Request 1: inner scrolls to 15 (bringing target into its OWN view) and
    /// consumes it — target is still cumulatively obscured, since outer's clip excludes it.
    /// Request 2: inner is now satisfied and returns false, so the request bubbles ON and outer
    /// scrolls 110 -> 80, putting target at root [190,210] inside the cumulative region
    /// [190,290] ∩ [110,210]. The loop must issue BOTH — asserted by counting the requests the
    /// target actually receives — and target must keep focus.
    /// </summary>
    [AvaloniaFact]
    public void PartialInnerProgress_SecondRequestReachesOuter_TargetRecovered()
    {
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            var inner = new ScrollViewer { Height = 100 };
            var innerStack = new StackPanel();
            innerStack.Children.Add(new Border { Height = 95 });
            var target = new Button { Content = "target", Height = 20 };
            innerStack.Children.Add(target);
            inner.Content = innerStack;

            var outer = new ScrollViewer { [Grid.RowProperty] = 2, Height = 100 };
            var outerStack = new StackPanel();
            outerStack.Children.Add(inner);
            outerStack.Children.Add(new Border { Height = 200 });
            outer.Content = outerStack;

            var fallbackTarget = new Button { Content = "fallback", [Grid.RowProperty] = 1 };
            root.Children.Add(fallbackTarget);
            root.Children.Add(outer);
            Dispatcher.UIThread.RunJobs();

            target.Focus();
            inner.Offset = default;
            outer.Offset = new Vector(0, 110);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, inner.Offset.Y);
            Assert.Equal(110, outer.Offset.Y);

            // Attached only after the setup Focus(), which raises a request of its own.
            int requests = 0;
            target.AddHandler(Control.RequestBringIntoViewEvent, (_, _) => requests++);

            w.Height = Threshold - 1;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, requests);
            Assert.True(inner.Offset.Y != 0, "request 1 was partially consumed by inner");
            Assert.True(outer.Offset.Y < 110, "request 2 reached outer, which completed the recovery");
            Assert.True(target.IsFocused, "recoverable focus is brought into view across BOTH clippers, never relocated");
            Assert.False(fallbackTarget.IsFocused);
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// Fix round 5, item #2: the boundary of <c>MaxBringIntoViewAttempts</c>. With retry gated
    /// on progress, a well-behaved tree terminates on its own — every request either moves a
    /// scroller (and the next one starts from a strictly better position) or moves nothing and
    /// exhausts that target. The cap exists only for the pathological case this rig builds: a
    /// handler that fakes progress forever, nudging an ancestor scroller on every request while
    /// the target stays permanently obscured. Target sits at [25,55] inside a 20-tall
    /// ClipToBounds Border, so it is clipped away no matter what — yet it is within the outer
    /// ScrollViewer's own viewport, so the real BringIntoView never moves that scroller and the
    /// handler's 1px nudge is the sole (and monotone) source of "progress". The loop must stop
    /// at exactly the cap and fall through to relocation.
    /// </summary>
    [AvaloniaFact]
    public void FakedProgressForever_StopsAtTheCap_AndRelocates()
    {
        (Window w, Grid root) = Host(Threshold + 50);
        try
        {
            var clipper = new Border { Height = 20, ClipToBounds = true };
            var clippedHost = new StackPanel();
            var target = new Button { Content = "target", Height = 30, Margin = new Thickness(0, 25, 0, 0) };
            clippedHost.Children.Add(target);
            clipper.Child = clippedHost;

            var scroller = new ScrollViewer { [Grid.RowProperty] = 2, Height = 100 };
            var scrollerStack = new StackPanel();
            scrollerStack.Children.Add(clipper);
            scrollerStack.Children.Add(new Border { Height = 500 });   // genuine scroll room
            scroller.Content = scrollerStack;

            var fallbackTarget = new Button { Content = "fallback", [Grid.RowProperty] = 1 };
            root.Children.Add(fallbackTarget);
            root.Children.Add(scroller);
            Dispatcher.UIThread.RunJobs();

            target.Focus();
            scroller.Offset = default;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, scroller.Offset.Y);

            int requests = 0;
            target.AddHandler(Control.RequestBringIntoViewEvent, (_, _) =>
            {
                requests++;
                scroller.Offset = new Vector(0, scroller.Offset.Y + 1);   // faked progress
            });

            w.Height = Threshold - 1;
            Dispatcher.UIThread.RunJobs();

            int cap = GetMaxBringIntoViewAttempts();
            Assert.Equal(8, cap);
            Assert.Equal(cap, requests);
            Assert.False(target.IsFocused, "the target never becomes visible, so it cannot keep focus");
            Assert.True(fallbackTarget.IsFocused, "hitting the cap falls through to relocation, never to a silent stop");
        }
        finally { w.Close(); }
    }

    /// <summary>
    /// Fix round 5, item #3: a synchronous BringIntoView handler can CLEAR focus outright.
    /// The captured element is then recovered and looks perfectly settled — attached, visible,
    /// focusable — while NOTHING at all is focused, and the recovery would return leaving the
    /// window with empty focus (keyboard and screen-reader users stranded with no focus ring
    /// and no reachable starting point). A relocation this behavior initiated must never end
    /// that way: settled-but-nothing-focused hands off through the fallback chain, so the
    /// direction target — here the RestoreFocusTarget — ends focused.
    /// </summary>
    [AvaloniaFact]
    public void BringIntoViewHandlerClearedFocus_SettledButEmpty_HandsOffToDirectionTarget()
    {
        (Window w, Grid root) = Host(Threshold - 1);
        try
        {
            var restoreTarget = new Button { Content = "restoreTarget", [Grid.RowProperty] = 1 };
            var scroller = new ScrollViewer { [Grid.RowProperty] = 2, Height = 60 };
            var stack = new StackPanel();
            for (int i = 0; i < 10; i++) stack.Children.Add(new Button { Content = $"b{i}", Height = 30 });
            scroller.Content = stack;
            root.Children.Add(restoreTarget);
            root.Children.Add(scroller);
            CompactHeightBehavior.SetRestoreFocusTarget(root, restoreTarget);
            Dispatcher.UIThread.RunJobs();

            var captured = (Button)stack.Children[^1];
            captured.Focus();
            scroller.Offset = default;             // scroll captured out of view
            Dispatcher.UIThread.RunJobs();
            Assert.True(captured.IsFocused);

            // Attached after the setup Focus() (which raises a request of its own). Fires
            // synchronously inside captured.BringIntoView(), before the scroller recovers it.
            captured.AddHandler(Control.RequestBringIntoViewEvent,
                (_, _) => TopLevel.GetTopLevel(root)!.FocusManager!.ClearFocus());

            w.Height = Threshold + 40;             // -> restore; runs the staged recovery
            Dispatcher.UIThread.RunJobs();

            Assert.True(scroller.Offset.Y > 0,
                "setup precondition: BringIntoView DID recover captured, so it reads as settled");
            Assert.True(restoreTarget.IsFocused,
                "settled with nothing focused must hand off through the chain to the direction target");
        }
        finally { w.Close(); }
    }

    private static object GetPrivateState(Control control)
    {
        FieldInfo statesField = typeof(CompactHeightBehavior).GetField("_states", BindingFlags.NonPublic | BindingFlags.Static)!;
        object statesTable = statesField.GetValue(null)!;
        MethodInfo tryGetValue = statesTable.GetType().GetMethod("TryGetValue")!;
        object?[] args = [control, null];
        bool found = (bool)tryGetValue.Invoke(statesTable, args)!;
        Assert.True(found, "state must already exist for a control with Threshold set");
        return args[1]!;
    }

    private static int GetGeneration(object state) =>
        (int)state.GetType().GetProperty("Generation")!.GetValue(state)!;

    private static int GetMaxBringIntoViewAttempts() =>
        (int)typeof(CompactHeightBehavior)
            .GetField("MaxBringIntoViewAttempts", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;

    private static void SetGeneration(object state, int value) =>
        state.GetType().GetProperty("Generation")!.SetValue(state, value);

    private static Action InvokeCreateRecoveryCallback(Control root, Control captured, bool enteringCompact, object state)
    {
        MethodInfo method = typeof(CompactHeightBehavior).GetMethod("CreateRecoveryCallback", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Action)method.Invoke(null, [root, captured, enteringCompact, state])!;
    }

    private static void InvokeRelocateFocusIfNeeded(Control root, Control captured, bool enteringCompact, int generation, object state)
    {
        MethodInfo method = typeof(CompactHeightBehavior).GetMethod("RelocateFocusIfNeeded", BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Invoke(null, [root, captured, enteringCompact, generation, state]);
    }
}
