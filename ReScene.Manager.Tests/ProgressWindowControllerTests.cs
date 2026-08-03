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
