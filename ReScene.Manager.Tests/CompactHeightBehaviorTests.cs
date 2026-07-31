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
}
