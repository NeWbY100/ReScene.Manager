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

    /// <summary>
    /// Opens (when <paramref name="busy"/> is <see langword="true"/>) or closes the progress dialog.
    /// Call from the view model's property-changed notification for the busy flag, and once up front
    /// to catch up with state that may already be true when the owner's DataContext is wired.
    /// </summary>
    public void OnBusyChanged(bool busy)
    {
        if (busy)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // The owner here is the BruteForceProgressWindow itself, so it is always its own
                // TopLevel — a bare "is Window" check never skips. Require a VISIBLE owner: if this
                // catch-up call runs before the owner is shown (headless, or DataContext wired before
                // the window is displayed), ShowDialog over a not-yet-shown window throws, so skip.
                if (TopLevel.GetTopLevel(_owner) is not Window { IsVisible: true } ownerWindow)
                {
                    return;
                }

                _window = new TWindow { DataContext = _owner.DataContext };

                _window.Closed += (_, _) =>
                {
                    // If the window was closed by the user (not programmatically by the "not busy"
                    // branch below), treat it as a cancel request.
                    if (_isBusy())
                    {
                        _cancel();
                    }

                    _window = null;
                };

                // Avalonia's ShowDialog is async (returns a Task) unlike WPF's synchronous
                // ShowDialog(); fire-and-forget it here — the window shows modally over ownerWindow and
                // is closed programmatically by the "not busy" branch below (or by the user, which the
                // Closed handler above turns into a cancel).
                _ = _window.ShowDialog(ownerWindow);
            });
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                _window?.Close();
                _window = null;
            });
        }
    }
}
