using Avalonia.Controls;
using ReScene.App.Core.ViewModels;

namespace ReScene.Manager.Helpers;

/// <summary>
/// Avalonia port of the button-feedback half of the WPF
/// <c>ReScene.NET.Helpers.ProgressWindowLifecycle</c>: gives the Cancel button on a nested file-copy /
/// CRC-validation progress dialog a <c>"Cancelling..."</c> grace period instead of an abrupt close.
/// <para>
/// Unlike the WPF helper, this does NOT auto-close the window when the busy flag clears — that is owned
/// by <see cref="ModalProgressWindowController{TWindow}"/> (it closes the dialog programmatically once
/// the flag drops). Duplicating the auto-close here would double-close. This helper only:
/// </para>
/// <list type="bullet">
///   <item>routes the Cancel button's click to <c>StopCommand</c>, then disables and relabels it, and</item>
///   <item>guards <see cref="Window.Closing"/> so a native close (X / Alt-F4) while still busy is
///   turned into a cancel (<c>e.Cancel = true</c>) rather than tearing the dialog down mid-operation.</item>
/// </list>
/// <para>
/// Call once the window's DataContext is available (its <see cref="Control.Loaded"/> handler — the
/// controller sets DataContext before <c>ShowDialog</c>). Callers only invoke it when the DataContext
/// is a <see cref="ReconstructorViewModel"/>, so it is headless-safe.
/// </para>
/// </summary>
internal static class ProgressWindowLifecycle
{
    public static void Attach(Window window, ReconstructorViewModel vm, Func<bool> isBusy, Button cancelButton)
    {
        void Cancel()
        {
            vm.StopCommand.Execute(null);
            cancelButton.IsEnabled = false;
            cancelButton.Content = "Cancelling...";
        }

        cancelButton.Click += (_, _) => Cancel();

        window.Closing += (_, e) =>
        {
            if (isBusy())
            {
                // Don't close while the operation is in progress — cancel instead. The controller
                // closes the window programmatically once the busy flag clears, so no close is lost.
                e.Cancel = true;
                Cancel();
            }
        };
    }
}
