using Avalonia.Controls;
using Avalonia.Threading;
using ReScene.Manager.Views;

namespace ReScene.Manager.Helpers;

/// <summary>
/// Manages the modal <see cref="ISOProgressWindow"/> shared by the SRS Creator and SRS Reconstructor
/// views. Both open the dialog when ISO processing starts, cancel the underlying operation if the user
/// closes the dialog while still processing, and close the dialog when processing finishes — only the
/// owning control, the "is processing" check, and the cancel action differ between the two views.
/// Avalonia port of the WPF <c>IsoProgressWindowController</c>.
/// </summary>
internal sealed class IsoProgressWindowController(
    Control owner, Func<bool> isProcessing, Action cancel, Func<object?>? windowDataContext = null)
{
    private readonly Control _owner = owner;
    private readonly Func<bool> _isProcessing = isProcessing;
    private readonly Action _cancel = cancel;

    // The ISO window binds the SRS Creator/Reconstructor VM's ISO* properties. For the tab views (and
    // the Create-SRS wizard) that VM is the owner's own DataContext, so this is left null. The beginner
    // Restore wizard owns the modal from a body whose DataContext is the BeginnerRestoreViewModel — there
    // the VM is a child (SingleRebuilder), supplied explicitly so the window binds the right object.
    private readonly Func<object?>? _windowDataContext = windowDataContext;

    private ISOProgressWindow? _isoWindow;
    private bool _desiredProcessing;
    private bool _reconcilePending;
    private int _generation;
    private Action? _requestClose;

    /// <summary>
    /// Notifies the controller that <c>ISOProcessing</c> moved. The dialog is opened or closed to
    /// match, on the dispatcher. Call from the view model's <c>ISOProcessing</c> property-changed
    /// notification.
    /// <para>
    /// The LATEST <paramref name="processing"/> wins, and the work is done once — several changes
    /// before the dispatcher queue drains previously ran in sequence against a single tracked
    /// reference and could leave a dialog nobody was able to close. Same defect and same fix as
    /// <see cref="ModalProgressWindowController{TWindow}"/>, which is why one census covers both.
    /// </para>
    /// </summary>
    public void OnProcessingChanged(bool processing)
    {
        // A false->true transition starts a new request for a dialog, distinct from whatever
        // request is already in flight. Recorded even when Reconcile below cannot act on it yet
        // (it may still be waiting on a previous window's close) — see the Closed handler in
        // Reconcile for why a window needs to know which request it was opened for.
        if (processing && !_desiredProcessing)
        {
            _generation++;
        }

        _desiredProcessing = processing;
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
        if (_desiredProcessing)
        {
            if (_isoWindow is not null)
            {
                return;
            }

            // WPF resolved the owner via Window.GetWindow(_owner); Avalonia's equivalent is the
            // owning control's attached TopLevel, cast down to a Window. Require a VISIBLE owner:
            // if the control isn't attached to a shown window yet (running headless, or the
            // DataContext wired before the view is placed in a shown visual tree), ShowDialog over
            // it throws, so skip rather than crash.
            if (TopLevel.GetTopLevel(_owner) is not Window { IsVisible: true } ownerWindow)
            {
                return;
            }

            var window = new ISOProgressWindow
            {
                DataContext = _windowDataContext is not null ? _windowDataContext() : _owner.DataContext,
            };
            _isoWindow = window;

            // Captured HERE, per window, rather than read from the controller's current fields when
            // Closed eventually fires. Closing a real window is not necessarily synchronous with the
            // Close() call that starts it, so a processing=true for a fresh request can arrive — and
            // even run its own no-op reconcile, since this window is still the tracked one — before
            // this window's Closed is delivered. By then the controller may be serving a request
            // this window has nothing to do with; its Closed handler must judge it by what was true
            // when IT opened, never by whatever the controller's live fields say once it actually
            // closes.
            int myGeneration = _generation;
            bool closingProgrammatically = false;
            _requestClose = () =>
            {
                closingProgrammatically = true;
                window.Close();
            };

            window.Closed += (sender, _) =>
            {
                // Cancel only if the user closed this window (not us, via _requestClose below) AND
                // no newer request has since superseded the one this window was opened for. Both
                // conjuncts come from what THIS window's own open captured — never from
                // _isProcessing()/_generation read fresh against whatever is current by the time
                // this fires, which may already belong to a request this window never showed
                // progress for.
                if (!closingProgrammatically && myGeneration == _generation && _isProcessing())
                {
                    _cancel();
                }

                // Only clear if THIS window is still the tracked one: Closed can be raised after a
                // newer window has been opened, and clearing unconditionally nulled the newer
                // window's reference, after which nothing could close it.
                if (ReferenceEquals(_isoWindow, sender))
                {
                    _isoWindow = null;
                    _requestClose = null;

                    // A processing=true that arrived while this window's close was still pending
                    // saw a non-null reference and deferred to us, believing the request already
                    // satisfied. Catch up now that the window is actually gone, or that reopen
                    // never happens.
                    if (_desiredProcessing)
                    {
                        ScheduleReconcile();
                    }
                }
            };

            // Avalonia's ShowDialog is async (returns a Task) unlike WPF's synchronous
            // ShowDialog(); fire-and-forget it here — the window shows modally over ownerWindow
            // and is closed programmatically by the not-processing path (or by the user, which the
            // Closed handler above turns into a cancel).
            _ = window.ShowDialog(ownerWindow);
        }
        else
        {
            // Routed through the window's own captured close request rather than closing _isoWindow
            // directly, so the programmatic-close flag the Closed handler above reads lands on the
            // SAME per-window state — a controller-wide flag could be overwritten by a later
            // request before this window's delayed Closed had a chance to read it.
            _requestClose?.Invoke();
        }
    }
}
