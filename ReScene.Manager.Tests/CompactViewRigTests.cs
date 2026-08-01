using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace ReScene.Manager.Tests;

/// <summary>
/// Tests of <see cref="CompactViewRig"/>'s OWN correctness (as opposed to any specific view's
/// behavior) — a separate file from any per-view test class since the rig is shared across
/// Tasks 2-6 and its own guarantees deserve independent coverage.
/// </summary>
public class CompactViewRigTests
{
    /// <summary>
    /// Round-2 retro-review: lap-reproduction alone (the round-1 fix for finding #2) proves a
    /// terminal loop is STABLE, but not that it is COMPLETE — a genuinely stable early A→B→A trap
    /// reproduces perfectly on the extra confirmation lap and would otherwise be accepted as a
    /// legitimate end of the walk, even though later, real focusable controls were never reached.
    /// This is exactly the class of bug a hijacked/misconfigured Tab handler could cause in a real
    /// view. Builds a synthetic two-element trap (a plain Bubble-routed KeyDown handler on the
    /// shared parent, added BEFORE Avalonia's own <c>KeyboardNavigationHandler</c> ever sees the
    /// key — confirmed via decompile: <c>KeyboardNavigationHandler.SetOwner</c> subscribes to
    /// <c>InputElement.KeyDownEvent</c> with a plain, default-Bubble <c>AddHandler</c> call at the
    /// WINDOW itself, so a handler added on a closer ancestor runs first and, by setting
    /// <c>Handled</c>, prevents the window's own handler from ever advancing focus normally) ahead
    /// of two further, perfectly ordinary focusable controls, and asserts the walk fails loudly
    /// naming exactly the controls the trap prevented it from ever reaching.
    /// </summary>
    [AvaloniaFact]
    public void AssertTabWalkStaysVisible_StableEarlyTrap_WithExpectedStops_FailsNamingUnvisitedEntries()
    {
        var a = new Button { Content = "TrapA" };
        var b = new Button { Content = "TrapB" };
        var c = new Button { Content = "Real1" };
        var d = new Button { Content = "Real2" };

        var panel = new StackPanel();
        panel.Children.Add(a);
        panel.Children.Add(b);
        panel.Children.Add(c);
        panel.Children.Add(d);

        var window = new Window { Content = panel };

        // The deliberate trap: hijacks Tab between a/b only. Registered with the framework's
        // default (Bubble) routing, same as KeyboardNavigationHandler's own subscription, but on
        // an element closer to the focused control — so this runs FIRST and, by marking the event
        // Handled, the window's own Tab-advance logic never runs at all for these two controls.
        panel.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Tab)
            {
                return;
            }

            Control? focused = window.FocusManager?.GetFocusedElement() as Control;
            if (ReferenceEquals(focused, a))
            {
                b.Focus();
                e.Handled = true;
            }
            else if (ReferenceEquals(focused, b))
            {
                a.Focus();
                e.Handled = true;
            }
        });

        window.Show();
        Dispatcher.UIThread.RunJobs();

        Xunit.Sdk.XunitException ex = Assert.Throws<Xunit.Sdk.XunitException>(() =>
            CompactViewRig.AssertTabWalkStaysVisible(window, a, expectedForwardStops: [a, b, c, d], expectedReverseStops: [a, b, c, d]));

        Assert.Contains("unvisited", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Real1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Real2", ex.Message, StringComparison.Ordinal);

        window.Close();
    }

    /// <summary>
    /// Round-4 retro-review finding #1: the previous covering test's trap hijacks BOTH directions
    /// (its handler never checks <see cref="KeyEventArgs.KeyModifiers"/>), so it cannot prove a
    /// trap that affects ONLY Shift+Tab is actually caught — the forward pass would already have
    /// failed first, masking whether the reverse-specific logic ever even ran. This test's trap
    /// checks <c>KeyModifiers == KeyModifiers.Shift</c> explicitly, hijacking only the LAST TWO
    /// elements (c/d) under Shift+Tab and leaving plain Tab completely untouched.
    /// <para>
    /// Round-4 finding #2 (the ordering concern): calls the forward and reverse passes
    /// INDEPENDENTLY via the now-<c>internal</c> <see cref="CompactViewRig.RunTabPass"/> — first
    /// asserting the FORWARD pass completes with no exception at all (proving the trap really is
    /// reverse-only, not a blanket hijack), THEN separately asserting the REVERSE pass (started
    /// from d, the far end — mirroring how a real "walk from last" reverse check would anchor
    /// itself) throws, naming exactly the two stops (a, b) the c/d hijack never let it reach. This
    /// removes any ambiguity a single combined <see cref="CompactViewRig.AssertTabWalkStaysVisible"/>
    /// call would have left about whether forward ever actually succeeded before reverse failed.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void ReverseOnlyTrap_ForwardPassSucceeds_ReversePassFailsNamingUnreachedEntries()
    {
        var a = new Button { Content = "Real1" };
        var b = new Button { Content = "Real2" };
        var c = new Button { Content = "TrapC" };
        var d = new Button { Content = "TrapD" };

        var panel = new StackPanel();
        panel.Children.Add(a);
        panel.Children.Add(b);
        panel.Children.Add(c);
        panel.Children.Add(d);

        var window = new Window { Content = panel };

        // The deliberate trap: hijacks Shift+Tab between c/d ONLY — plain Tab (KeyModifiers.None)
        // is untouched, so the forward pass below walks completely normally.
        panel.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Tab || e.KeyModifiers != KeyModifiers.Shift)
            {
                return;
            }

            Control? focused = window.FocusManager?.GetFocusedElement() as Control;
            if (ReferenceEquals(focused, c))
            {
                d.Focus();
                e.Handled = true;
            }
            else if (ReferenceEquals(focused, d))
            {
                c.Focus();
                e.Handled = true;
            }
        });

        window.Show();
        Dispatcher.UIThread.RunJobs();

        // FORWARD must succeed cleanly — proves the hijack really is reverse-only.
        CompactViewRig.RunTabPass(window, a, forward: true, expectedStops: [a, b, c, d]);

        // REVERSE, anchored at the far end (d) rather than the forward sentinel (a) — exactly
        // the "walk from last" shape a real per-view test would use — must fail, hijacked into
        // a c/d bounce that never reaches a or b.
        Xunit.Sdk.XunitException ex = Assert.Throws<Xunit.Sdk.XunitException>(() =>
            CompactViewRig.RunTabPass(window, d, forward: false, expectedStops: [a, b, c, d]));

        Assert.Contains("unvisited", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Real1", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Real2", ex.Message, StringComparison.Ordinal);

        window.Close();
    }
}
