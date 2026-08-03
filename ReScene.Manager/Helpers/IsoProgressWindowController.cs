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
        _desiredProcessing = processing;

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

            window.Closed += (sender, _) =>
            {
                // If the window was cancelled (not closed by code), cancel the operation.
                if (_isProcessing())
                {
                    _cancel();
                }

                // Only clear if THIS window is still the tracked one: Closed can be raised after a
                // newer window has been opened, and clearing unconditionally nulled the newer
                // window's reference, after which nothing could close it.
                if (ReferenceEquals(_isoWindow, sender))
                {
                    _isoWindow = null;
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
            // The reference is cleared by the Closed handler, which knows whether the window closing
            // is still the tracked one.
            _isoWindow?.Close();
        }
    }
}
