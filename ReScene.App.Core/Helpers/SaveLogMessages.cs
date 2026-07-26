namespace ReScene.App.Core.Helpers;

/// <summary>
/// The Save-log outcome strings announced on every surface (bound to the views' live
/// SaveLogStatus line). Shared by <c>OperationViewModelBase</c> and <c>ReconstructorViewModel</c> —
/// the two SaveLog implementations already duplicate flow; the user-facing strings must not
/// drift too (WCAG 3.2.4 Consistent Identification).
/// </summary>
public static class SaveLogMessages
{
    /// <summary>Announced when Save log is activated on an empty log (the button is always enabled).</summary>
    public const string Empty = "Nothing to save yet — the log is empty";

    /// <summary>Announced on success; filename only, matching the log line's wording.</summary>
    public static string Saved(string path) => $"Log saved to {Path.GetFileName(path)}";

    /// <summary>
    /// Announced on failure. Only the lead-in differs from the log line ("Could not save the log:"
    /// vs "ERROR saving log:") — the exception text itself is passed through unsanitized, so unlike
    /// <see cref="Saved"/>'s filename-only form it may carry full paths.
    /// </summary>
    public static string Failed(string message) => $"Could not save the log: {message}";
}
