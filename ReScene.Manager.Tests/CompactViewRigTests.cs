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
}
