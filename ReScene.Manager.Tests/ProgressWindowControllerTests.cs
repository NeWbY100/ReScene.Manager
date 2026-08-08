using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ReScene.Manager.Helpers;
using ReScene.Manager.Views;

namespace ReScene.Manager.Tests;

/// <summary>
/// The modal-progress controllers must not leave a dialog on screen that nothing can close.
/// <para>
/// Both controllers post their open and close onto the dispatcher, so a busy flag that moves more
/// than once before the queue drains produces interleavings the straight-line code did not expect.
/// Two of them orphan a window: the <c>Closed</c> handler cleared the tracked reference
/// unconditionally, so a window closing LATE nulled the reference to a NEWER window and the
/// not-busy branch then had nothing to close; and two queued opens each constructed a window while
/// only the last was tracked. Either way a progress modal stays up forever and the application
/// looks hung behind a dialog with no way out.
/// </para>
/// <para>
/// WHY THE COUNT IS TAKEN FROM RENDERED WINDOWS. An earlier version of this rig counted
/// <c>IClassicDesktopStyleApplicationLifetime.Windows</c>, which is null in headless: it reported
/// -1 at every step and measured nothing at all while appearing to pass. Windows are tracked here
/// through <see cref="Window.WindowOpenedEvent"/> and counted live only while their
/// <c>PlatformImpl</c> survives, which is the same liveness signal the diagnosis used.
/// </para>
/// <para>
/// WHAT THIS DOES NOT CATCH: it drives the flag directly rather than through a real run, so an
/// ordering the view models produce but this file does not enumerate is outside it. It asserts a
/// window is gone, never that the operation behind it was cancelled — the cancel path is
/// <see cref="ProgressWindowLifecycle"/>'s and is covered by the per-window tests.
/// </para>
/// </summary>
public class ProgressWindowControllerTests
{
    private static readonly List<Window> Tracked = [];

    static ProgressWindowControllerTests() =>
        Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) =>
        {
            if (!Tracked.Contains(w)) { Tracked.Add(w); }
        });

    private static int Live<TWindow>() where TWindow : Window =>
        Tracked.Count(w => w is TWindow && w.PlatformImpl is not null);

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Hosts a shown owner window, since both controllers deliberately no-op unless their owner is a
    /// visible <see cref="Window"/> (ShowDialog over an unshown window throws).
    /// </summary>
    private static Window ShownOwner()
    {
        var owner = new Window { Width = 600, Height = 400 };
        owner.Show();
        Pump();
        return owner;
    }

    /// <summary>
    /// The two interleavings that orphan a dialog, run against whichever controller is supplied.
    /// <paramref name="drive"/> is the controller's own busy notification, so each controller is
    /// exercised through its real public entry point rather than a copy of its logic.
    /// </summary>
    private static void AssertNoOrphanedDialog<TWindow>(Action<bool> drive, Func<bool> setTrue, Action setFalse)
        where TWindow : Window
    {
        int baseline = Live<TWindow>();

        // Sanity leg: the ordinary open/close must work, or the two legs below prove nothing.
        _ = setTrue();
        drive(true);
        Pump();
        Assert.True(Live<TWindow>() == baseline + 1,
            $"rig validity: a plain busy=true did not open a {typeof(TWindow).Name}, so nothing below is measuring an orphan");

        setFalse();
        drive(false);
        Pump();
        Assert.True(Live<TWindow>() == baseline,
            $"a plain busy=false left {Live<TWindow>() - baseline} {typeof(TWindow).Name}(s) open");

        // Leg 1 — the flicker. true/false/true with no dispatcher turn between them: the first
        // window's late Closed nulls the reference to the THIRD window, which the final false can
        // then never close.
        _ = setTrue();
        drive(true);
        setFalse();
        drive(false);
        _ = setTrue();
        drive(true);
        Pump();

        setFalse();
        drive(false);
        Pump();

        Assert.True(Live<TWindow>() == baseline,
            $"after a true/false/true flicker and a final false, {Live<TWindow>() - baseline} " +
            $"{typeof(TWindow).Name}(s) are still open. The controller lost track of one, so nothing " +
            "can close it and the user is left behind a modal with no way out.");

        // Leg 2 — two queued opens. Each post constructs its own window while only the last is
        // tracked, so the first is unreachable the moment it is shown.
        _ = setTrue();
        drive(true);
        drive(true);
        Pump();

        setFalse();
        drive(false);
        Pump();

        Assert.True(Live<TWindow>() == baseline,
            $"after two queued opens and a close, {Live<TWindow>() - baseline} {typeof(TWindow).Name}(s) " +
            "are still open — a second dialog was constructed and never tracked.");
    }

    [AvaloniaFact]
    public void ModalProgressWindowController_LeavesNoOrphanedDialog()
    {
        Window owner = ShownOwner();
        try
        {
            bool busy = false;
            var controller = new ModalProgressWindowController<FileCopyProgressWindow>(
                owner, () => busy, () => { });

            AssertNoOrphanedDialog<FileCopyProgressWindow>(
                controller.OnBusyChanged, () => busy = true, () => busy = false);
        }
        finally { owner.Close(); Pump(); }
    }

    [AvaloniaFact]
    public void IsoProgressWindowController_LeavesNoOrphanedDialog()
    {
        Window owner = ShownOwner();
        try
        {
            bool busy = false;
            var controller = new IsoProgressWindowController(owner, () => busy, () => { });

            AssertNoOrphanedDialog<ISOProgressWindow>(
                controller.OnProcessingChanged, () => busy = true, () => busy = false);
        }
        finally { owner.Close(); Pump(); }
    }

    /// <summary>
    /// Lets a test hold a window's close genuinely pending and then let the SAME close complete —
    /// standing in for a platform whose native close is not synchronous with the managed
    /// <c>Close()</c> call that starts it — without resorting to a second <c>Close()</c> call to
    /// "release" it, which would be a second, genuinely different close negotiation rather than
    /// the completion of the first.
    /// <para>
    /// <c>Window.Close()</c> raises the public <c>Closing</c> event synchronously while deciding
    /// whether to proceed (<c>ShouldCancelClose</c> calls <c>OnClosing</c>, which raises
    /// <c>Closing</c>, then reads <c>e.Cancel</c>) — cancelling it there is enough to stop
    /// <c>CloseInternal()</c> from ever running, so <c>PlatformImpl</c> is never disposed and
    /// <c>Closed</c> never fires, exactly as a close a real platform has not finished yet would
    /// look from the outside. Releasing calls <c>PlatformImpl.Dispose()</c> directly: that is
    /// exactly what <c>CloseInternal()</c> would have gone on to do had <c>Closing</c> not been
    /// held, so it finishes the SAME close <c>CloseCore</c> already accepted — <c>Closing</c> does
    /// not fire again, and whatever called <c>Close()</c> the one time is never invoked again
    /// either.
    /// </para>
    /// </summary>
    private sealed class PendingClose
    {
        private readonly Window _window;
        private bool _hold = true;

        public PendingClose(Window window)
        {
            _window = window;
            _window.Closing += (_, e) =>
            {
                ClosingCount++;
                if (_hold) { e.Cancel = true; }
            };
        }

        public int ClosingCount { get; private set; }

        public void Release()
        {
            _hold = false;
            _window.PlatformImpl?.Dispose();
        }
    }

    /// <summary>
    /// The race the two legs of <see cref="AssertNoOrphanedDialog{TWindow}"/> do not cover: they
    /// only ever pump after a close has already resolved, because the headless backend finishes
    /// <c>Close()</c> synchronously with nothing held pending. <see cref="PendingClose"/> gives
    /// this leg a close that genuinely stays pending across a pump instead.
    /// <para>
    /// While that close sits pending, a fresh busy=true arrives. The buggy reconcile sees a
    /// non-null tracked reference and returns, believing the request already satisfied. When the
    /// pending close is then allowed to complete, the resulting <c>Closed</c> event must (a) NOT
    /// cancel the new operation — the operation it belonged to is the one that asked for the
    /// close, not whatever is live now — and (b) reopen the dialog, since nothing else will.
    /// </para>
    /// </summary>
    private static void AssertReopensWhileCloseIsPending<TWindow>(
        Action<bool> drive, Func<bool> setTrue, Action setFalse, Func<int> cancelCount)
        where TWindow : Window
    {
        int baseline = Live<TWindow>();
        int cancelsBefore = cancelCount();
        var before = new HashSet<Window>(Tracked);

        _ = setTrue();
        drive(true);
        Pump();
        Assert.True(Live<TWindow>() == baseline + 1,
            $"rig validity: a plain busy=true did not open a {typeof(TWindow).Name}, so nothing " +
            "below is measuring anything");

        var pending = new PendingClose(
            Tracked.OfType<TWindow>().Single(w => !before.Contains(w) && w.PlatformImpl is not null));

        // The controller's own not-busy branch requests the close — held pending, so Closed does
        // not fire, standing in for a platform whose native close has not finished yet.
        setFalse();
        drive(false);
        Pump();
        Assert.True(pending.ClosingCount == 1,
            "rig invalid: the controller's Close() call must have raised Closing exactly once here");
        Assert.True(Live<TWindow>() == baseline + 1,
            "rig invalid: the close must still be pending here (Closed must not have fired), or " +
            "this leg proves nothing");

        // The reopen intent arrives while that close is still in flight.
        _ = setTrue();
        drive(true);
        Pump();
        Assert.True(Live<TWindow>() == baseline + 1,
            "rig invalid: still just the one pending-close window at this point — a second one " +
            "opening here means the coalescing guard broke, not what this leg measures");

        // Now let the SAME close actually complete.
        pending.Release();
        Pump();

        Assert.True(pending.ClosingCount == 1,
            "releasing the pending close must not raise Closing again — it completes the SAME " +
            "close, not a second one");
        Assert.True(Live<TWindow>() == baseline + 1,
            $"expected exactly one live {typeof(TWindow).Name} after the flicker, found " +
            $"{Live<TWindow>() - baseline}. The busy=true that arrived while the previous " +
            "window's close was still pending must reopen once that close is actually " +
            "delivered — nothing else will.");
        Assert.True(cancelCount() == cancelsBefore,
            $"the delayed Closed event cancelled the restarted operation " +
            $"({cancelCount() - cancelsBefore} time(s)) — it must only ever affect the operation " +
            "the closed window belonged to, never a busy=true that arrived after.");
    }

    /// <summary>
    /// The exact failure a controller-wide "did I close this" flag cannot catch: the window
    /// belongs to request A; the USER starts closing it directly (never through <c>drive()</c>, so
    /// the controller's own close-request path — the only place that marks a close as
    /// programmatic — never runs) while A is still desired, and that close is held pending. A then
    /// goes not-desired and a fresh request B goes desired with NO pump in between, so the single
    /// coalesced reconcile that eventually runs sees only B's "desired" — A's not-desired is
    /// invisible to it, and the window is still non-null, so it defers rather than opening a
    /// second one. When the pending close finally lands, the window was never marked programmatic,
    /// so a check that stopped at "was this us" alone would treat it as a live cancel — but it
    /// belongs to request A, not to B, and B may not even have a window yet.
    /// </summary>
    private static void AssertStaleUserCloseDuringRequestSwitchDoesNotCancelNewerRequest<TWindow>(
        Action<bool> drive, Func<bool> setTrue, Action setFalse, Func<int> cancelCount)
        where TWindow : Window
    {
        int baseline = Live<TWindow>();
        int cancelsBefore = cancelCount();
        var before = new HashSet<Window>(Tracked);

        _ = setTrue();
        drive(true);
        Pump();
        Assert.True(Live<TWindow>() == baseline + 1,
            $"rig validity: a plain busy=true did not open a {typeof(TWindow).Name}, so nothing " +
            "below is measuring anything");

        TWindow firstWindow = Tracked.OfType<TWindow>()
            .Single(w => !before.Contains(w) && w.PlatformImpl is not null);
        var pending = new PendingClose(firstWindow);

        // The user closes it directly — never through drive() — while A is still desired.
        firstWindow.Close();
        Assert.True(pending.ClosingCount == 1,
            "rig invalid: the simulated user close must have raised Closing exactly once");
        Assert.True(Live<TWindow>() == baseline + 1,
            "rig invalid: the close must still be pending here, or this leg proves nothing");

        // A goes not-desired and B goes desired with no Pump in between, so only ONE coalesced
        // reconcile runs below, and it sees only the LATEST (B's) desired state.
        setFalse();
        drive(false);
        _ = setTrue();
        drive(true);
        Pump();
        Assert.True(Live<TWindow>() == baseline + 1,
            "rig invalid: the coalesced reconcile must still find the pending window non-null and " +
            "defer, or this leg proves nothing");

        // Now let the stale window's close actually land.
        pending.Release();
        Pump();

        Assert.True(cancelCount() == cancelsBefore,
            $"the stale window's delayed Closed cancelled the newer request " +
            $"({cancelCount() - cancelsBefore} time(s)) — a window must be judged by the request " +
            "it was opened for, never by whatever the controller is doing once it actually closes.");
        Assert.True(Live<TWindow>() == baseline + 1,
            $"expected exactly one live {typeof(TWindow).Name} after the request switch, found " +
            $"{Live<TWindow>() - baseline} — the newer request must still get its own dialog.");
    }

    /// <summary>
    /// The per-window half of the same fix, isolated from the generation check above: window W1's
    /// close is requested programmatically — marking ITS OWN captured "did I request this" flag,
    /// not a controller-wide one — and, once released, correctly cancels nothing. A fresh request
    /// then opens a second window, W2, an entirely new capture starting from scratch. This time
    /// the close is genuinely the user's, driven directly exactly as the leg above does. If the
    /// flag were shared by the controller instead of captured per window, W1 having set it would
    /// leave it set for W2 too, and this cancel would go missing.
    /// </summary>
    private static void AssertEarlierProgrammaticCloseDoesNotTaintALaterWindowsCancel<TWindow>(
        Action<bool> drive, Func<bool> setTrue, Action setFalse, Func<int> cancelCount)
        where TWindow : Window
    {
        int baseline = Live<TWindow>();
        int cancelsBefore = cancelCount();

        // First window: closed programmatically (busy goes false, the controller requests the
        // close), held pending, then released — must not cancel, and must fully close before a
        // second window can open.
        var beforeFirst = new HashSet<Window>(Tracked);
        _ = setTrue();
        drive(true);
        Pump();
        Assert.True(Live<TWindow>() == baseline + 1,
            $"rig validity: a plain busy=true did not open a {typeof(TWindow).Name}, so nothing " +
            "below is measuring anything");

        TWindow firstWindow = Tracked.OfType<TWindow>()
            .Single(w => !beforeFirst.Contains(w) && w.PlatformImpl is not null);
        var firstPending = new PendingClose(firstWindow);

        setFalse();
        drive(false);
        Pump();
        Assert.True(firstPending.ClosingCount == 1,
            "rig invalid: the controller's Close() call on the first window must have raised " +
            "Closing exactly once");

        firstPending.Release();
        Pump();
        Assert.True(cancelCount() == cancelsBefore,
            "rig invalid: releasing the first window's own programmatic close must not cancel " +
            "anything, or this leg is not isolating what it claims to");
        Assert.True(Live<TWindow>() == baseline,
            "rig invalid: the first window must be fully closed before the second opens, or this " +
            "leg is not testing two windows");

        // Second window: a fresh request, a fresh capture — closed by the user this time.
        var beforeSecond = new HashSet<Window>(Tracked);
        _ = setTrue();
        drive(true);
        Pump();
        Assert.True(Live<TWindow>() == baseline + 1,
            $"rig validity: busy=true did not open a second {typeof(TWindow).Name}");

        TWindow secondWindow = Tracked.OfType<TWindow>()
            .Single(w => !beforeSecond.Contains(w) && w.PlatformImpl is not null);
        var secondPending = new PendingClose(secondWindow);

        secondWindow.Close();
        Assert.True(secondPending.ClosingCount == 1,
            "rig invalid: the simulated user close of the second window must have raised Closing " +
            "exactly once");

        secondPending.Release();
        Pump();

        Assert.True(cancelCount() == cancelsBefore + 1,
            $"the second window's genuine user close was not cancelled " +
            $"({cancelCount() - cancelsBefore} cancel(s) total, expected 1) — the first window's " +
            "own programmatic-close flag must not leak into a later window's classification.");
    }

    [AvaloniaFact]
    public void ModalProgressWindowController_ReopensWhenBusyReturnsWhileCloseIsPending()
    {
        Window owner = ShownOwner();
        try
        {
            bool busy = false;
            int cancelCount = 0;
            var controller = new ModalProgressWindowController<FileCopyProgressWindow>(
                owner, () => busy, () => cancelCount++);

            AssertReopensWhileCloseIsPending<FileCopyProgressWindow>(
                controller.OnBusyChanged, () => busy = true, () => busy = false, () => cancelCount);
        }
        finally { owner.Close(); Pump(); }
    }

    [AvaloniaFact]
    public void IsoProgressWindowController_ReopensWhenBusyReturnsWhileCloseIsPending()
    {
        Window owner = ShownOwner();
        try
        {
            bool busy = false;
            int cancelCount = 0;
            var controller = new IsoProgressWindowController(owner, () => busy, () => cancelCount++);

            AssertReopensWhileCloseIsPending<ISOProgressWindow>(
                controller.OnProcessingChanged, () => busy = true, () => busy = false, () => cancelCount);
        }
        finally { owner.Close(); Pump(); }
    }

    [AvaloniaFact]
    public void ModalProgressWindowController_StaleUserCloseDuringRequestSwitch_DoesNotCancelNewerRequest()
    {
        Window owner = ShownOwner();
        try
        {
            bool busy = false;
            int cancelCount = 0;
            var controller = new ModalProgressWindowController<FileCopyProgressWindow>(
                owner, () => busy, () => cancelCount++);

            AssertStaleUserCloseDuringRequestSwitchDoesNotCancelNewerRequest<FileCopyProgressWindow>(
                controller.OnBusyChanged, () => busy = true, () => busy = false, () => cancelCount);
        }
        finally { owner.Close(); Pump(); }
    }

    [AvaloniaFact]
    public void IsoProgressWindowController_StaleUserCloseDuringRequestSwitch_DoesNotCancelNewerRequest()
    {
        Window owner = ShownOwner();
        try
        {
            bool busy = false;
            int cancelCount = 0;
            var controller = new IsoProgressWindowController(owner, () => busy, () => cancelCount++);

            AssertStaleUserCloseDuringRequestSwitchDoesNotCancelNewerRequest<ISOProgressWindow>(
                controller.OnProcessingChanged, () => busy = true, () => busy = false, () => cancelCount);
        }
        finally { owner.Close(); Pump(); }
    }

    [AvaloniaFact]
    public void ModalProgressWindowController_EarlierProgrammaticCloseDoesNotTaintALaterWindowsCancel()
    {
        Window owner = ShownOwner();
        try
        {
            bool busy = false;
            int cancelCount = 0;
            var controller = new ModalProgressWindowController<FileCopyProgressWindow>(
                owner, () => busy, () => cancelCount++);

            AssertEarlierProgrammaticCloseDoesNotTaintALaterWindowsCancel<FileCopyProgressWindow>(
                controller.OnBusyChanged, () => busy = true, () => busy = false, () => cancelCount);
        }
        finally { owner.Close(); Pump(); }
    }

    [AvaloniaFact]
    public void IsoProgressWindowController_EarlierProgrammaticCloseDoesNotTaintALaterWindowsCancel()
    {
        Window owner = ShownOwner();
        try
        {
            bool busy = false;
            int cancelCount = 0;
            var controller = new IsoProgressWindowController(owner, () => busy, () => cancelCount++);

            AssertEarlierProgrammaticCloseDoesNotTaintALaterWindowsCancel<ISOProgressWindow>(
                controller.OnProcessingChanged, () => busy = true, () => busy = false, () => cancelCount);
        }
        finally { owner.Close(); Pump(); }
    }

    /// <summary>
    /// The population, taken from the assembly rather than from a list: every type in
    /// <c>ReScene.Manager.Helpers</c> that HOLDS a window is a lifecycle controller and has to be
    /// covered above. The defect this file guards was identical in both controllers, found only
    /// because the second was read after the first was diagnosed — so the census exists to make the
    /// third one impossible to miss.
    /// </summary>
    [AvaloniaFact]
    public void EveryWindowHoldingHelper_IsCoveredByThisFile()
    {
        Type[] holders =
        [
            .. typeof(IsoProgressWindowController).Assembly.GetTypes()
                .Where(t => t.Namespace == "ReScene.Manager.Helpers")
                // Excludes compiler-generated display classes: both controllers now capture their
                // window in a per-open Closed/close-request closure (so a stale Closed reads what
                // THAT window's own open captured, never the controller's current fields), and the
                // compiler backs each closure with a class holding a Window-typed field of its own.
                // That is an implementation detail of the SAME controller already covered below, not
                // a third window-holding helper.
                .Where(t => !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false))
                .Where(t => t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .Any(f => typeof(Window).IsAssignableFrom(f.FieldType)))
                .OrderBy(t => t.Name, StringComparer.Ordinal),
        ];

        string[] covered =
        [
            typeof(ModalProgressWindowController<>).Name,
            nameof(IsoProgressWindowController),
        ];

        List<string> uncovered = [.. holders.Select(t => t.Name).Where(n => !covered.Contains(n, StringComparer.Ordinal))];

        Assert.True(uncovered.Count == 0,
            $"{uncovered.Count} helper(s) hold a Window and have no orphaned-dialog guard here: " +
            $"{string.Join(", ", uncovered)}. Add a leg for each, or say why it cannot orphan one.");

        // Rig validity: the reflection must actually be finding the known controllers, or an empty
        // result would pass this test while examining nothing.
        Assert.True(holders.Length == 2,
            $"expected the 2 known window-holding helpers, found {holders.Length}: " +
            $"{string.Join(", ", holders.Select(t => t.Name))}");

        // ProgressWindowLifecycle is deliberately absent: it holds no window, only wiring a Cancel
        // button and a Closing guard onto a window somebody else owns, so it has nothing to orphan.
        Assert.DoesNotContain(nameof(ProgressWindowLifecycle), holders.Select(t => t.Name));
    }
}
