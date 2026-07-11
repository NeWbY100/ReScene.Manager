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

    /// <summary>
    /// Opens (when <paramref name="processing"/> is <see langword="true"/>) or closes the ISO progress
    /// dialog. Call from the view model's <c>ISOProcessing</c> property-changed notification.
    /// </summary>
    public void OnProcessingChanged(bool processing)
    {
        if (processing)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // WPF resolved the owner via Window.GetWindow(_owner); Avalonia's equivalent is the
                // owning control's attached TopLevel, cast down to a Window. Require a VISIBLE owner:
                // if the control isn't attached to a shown window yet (running headless, or the
                // DataContext wired before the view is placed in a shown visual tree), ShowDialog over
                // it throws, so skip rather than crash.
                if (TopLevel.GetTopLevel(_owner) is not Window { IsVisible: true } ownerWindow)
                {
                    return;
                }

                _isoWindow = new ISOProgressWindow
                {
                    DataContext = _windowDataContext is not null ? _windowDataContext() : _owner.DataContext,
                };

                _isoWindow.Closed += (_, _) =>
                {
                    // If the window was cancelled (not closed by code), cancel the operation.
                    if (_isProcessing())
                    {
                        _cancel();
                    }

                    _isoWindow = null;
                };

                // Avalonia's ShowDialog is async (returns a Task) unlike WPF's synchronous
                // ShowDialog(); fire-and-forget it here — the window shows modally over ownerWindow
                // and is closed programmatically by the "not processing" branch below (or by the
                // user, which the Closed handler above turns into a cancel).
                _ = _isoWindow.ShowDialog(ownerWindow);
            });
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                _isoWindow?.Close();
                _isoWindow = null;
            });
        }
    }
}
