using Avalonia.Controls;
using Avalonia.Threading;

namespace ReScene.Manager.Helpers;

/// <summary>
/// Generic modal-progress-window opener that mirrors <see cref="IsoProgressWindowController"/>: opens a
/// <typeparamref name="TWindow"/> when a "busy" flag turns true, cancels the underlying operation if the
/// user closes the dialog while still busy, and closes the dialog programmatically once the flag
/// clears. Used by <see cref="Views.BruteForceProgressWindow"/> for its two nested
/// <see cref="Views.FileCopyProgressWindow"/>/<see cref="Views.CRCValidationProgressWindow"/> dialogs —
/// one instance per busy flag (<c>IsCopying</c>/<c>IsVerifying</c>) — rather than duplicating the
/// open/close plumbing for each. <see cref="IsoProgressWindowController"/> itself stays a dedicated,
/// non-generic type since it predates this one and is only ever used for one window/VM pairing.
/// </summary>
internal sealed class ModalProgressWindowController<TWindow>(Control owner, Func<bool> isBusy, Action cancel)
    where TWindow : Window, new()
{
    private readonly Control _owner = owner;
    private readonly Func<bool> _isBusy = isBusy;
    private readonly Action _cancel = cancel;

    private TWindow? _window;
    private bool _desiredBusy;
    private bool _reconcilePending;
    private bool _closingProgrammatically;

    /// <summary>
    /// Notifies the controller that the busy flag moved. The dialog is opened or closed to match, on
    /// the dispatcher. Call from the view model's property-changed notification for the busy flag,
    /// and once up front to catch up with state that may already be true when the owner's DataContext
    /// is wired.
    /// <para>
    /// The LATEST <paramref name="busy"/> wins, and the work is done once. Previously each call
    /// posted its own open or close, so several changes before the queue drained ran in sequence
    /// against a single tracked reference: two queued opens each constructed a window while only the
    /// last was tracked, and a stale close could run after a newer open. Recording the desired state
    /// and reconciling once collapses every such interleaving to the outcome the caller last asked
    /// for.
    /// </para>
    /// </summary>
    public void OnBusyChanged(bool busy)
    {
        _desiredBusy = busy;
        ScheduleReconcile();
    }

    private void ScheduleReconcile()
    {
        if (_reconcilePending)
        {
            return;
        }

        _reconcilePending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _reconcilePending = false;
            Reconcile();
        });
    }

    private void Reconcile()
    {
        if (_desiredBusy)
        {
            if (_window is not null)
            {
                return;
            }

            // The owner here is the BruteForceProgressWindow itself, so it is always its own
            // TopLevel — a bare "is Window" check never skips. Require a VISIBLE owner: if this
            // catch-up call runs before the owner is shown (headless, or DataContext wired before
            // the window is displayed), ShowDialog over a not-yet-shown window throws, so skip.
            if (TopLevel.GetTopLevel(_owner) is not Window { IsVisible: true } ownerWindow)
            {
                return;
            }

            var window = new TWindow { DataContext = _owner.DataContext };
            _window = window;
            _closingProgrammatically = false;

            window.Closed += (sender, _) =>
            {
                // If the window was closed by the user (not programmatically, via the not-busy
                // branch below), treat it as a cancel request. Gated on _closingProgrammatically —
                // set right before WE call Close() below, for exactly this window — rather than on
                // _isBusy() alone: closing a real window is not necessarily synchronous with the
                // Close() call that starts it, so a busy=true for a RESTARTED operation can arrive
                // (and its own reconcile run) before this Closed is delivered. _isBusy() would then
                // read the restarted operation's live state and cancel it by mistake; whether WE
                // asked this window to close does not depend on what is busy now.
                if (!_closingProgrammatically && _isBusy())
                {
                    _cancel();
                }

                // Only clear the reference if THIS window is still the tracked one. Closed can be
                // raised after a newer window has been opened and tracked, and clearing
                // unconditionally then nulled the newer window's reference — after which nothing
                // could close it.
                if (ReferenceEquals(_window, sender))
                {
                    _window = null;

                    // A busy=true that arrived while this window's close was still pending saw a
                    // non-null reference and deferred to us, believing the request already
                    // satisfied. Catch up now that the window is actually gone, or that reopen
                    // never happens.
                    if (_desiredBusy)
                    {
                        ScheduleReconcile();
                    }
                }
            };

            // Avalonia's ShowDialog is async (returns a Task) unlike WPF's synchronous
            // ShowDialog(); fire-and-forget it here — the window shows modally over ownerWindow and
            // is closed programmatically by the not-busy path (or by the user, which the Closed
            // handler above turns into a cancel).
            _ = window.ShowDialog(ownerWindow);
        }
        else if (_window is not null)
        {
            // The reference is cleared by the Closed handler, which knows whether the window closing
            // is still the tracked one.
            _closingProgrammatically = true;
            _window.Close();
        }
    }
}
