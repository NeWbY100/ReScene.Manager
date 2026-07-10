using System.Diagnostics;

namespace ReScene.Manager.Helpers;

/// <summary>
/// Ported equivalent of the WPF app's <c>DispatcherUnhandledException</c> handler: logs an exception
/// raised on the UI thread, shows a non-fatal error dialog, and reports it handled so the application
/// keeps running instead of terminating. The message text mirrors the WPF original.
/// </summary>
/// <remarks>
/// The error dialog runs on the same (possibly broken) UI thread that faulted, so two hazards are
/// guarded: a <c>bool</c> reentrancy latch stops a dialog that itself faults — or pumps in a way that
/// re-enters this handler — from looping into an endless stack of dialogs; and the dialog call is
/// wrapped in try/catch so a throwing dialog can never turn the handler itself into a crash. Either
/// way <see cref="Handle"/> returns <see langword="true"/> (the exception is treated as handled).
/// </remarks>
internal sealed class UiThreadExceptionHandler(Action<string, string> showError)
{
    private readonly Action<string, string> _showError = showError;
    private bool _handling;

    /// <summary>
    /// Logs <paramref name="exception"/> and, unless already inside a handler invocation, shows the
    /// non-fatal error dialog. Always returns <see langword="true"/> so the caller can mark the
    /// dispatcher exception handled and keep the app alive.
    /// </summary>
    public bool Handle(Exception exception)
    {
        Trace.TraceError($"Unhandled UI exception: {exception}");

        if (_handling)
        {
            // A prior Handle call is still on the stack (the error dialog faulted, or pumping it
            // re-entered this handler). Don't open another dialog — just trace and mark handled.
            return true;
        }

        _handling = true;
        try
        {
            _showError(
                "Unexpected error",
                $"An unexpected error occurred:\n\n{exception.Message}\n\nThe application will try to continue.");
        }
        catch (Exception dialogError)
        {
            // Showing the dialog on the faulted UI thread threw; swallow it so the last-chance handler
            // itself can never crash the process.
            Trace.TraceError($"Failed to show the unhandled-exception dialog: {dialogError}");
        }
        finally
        {
            _handling = false;
        }

        return true;
    }
}
