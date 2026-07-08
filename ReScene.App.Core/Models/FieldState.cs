namespace ReScene.App.Core.Models;

/// <summary>
/// Severity of a field's detection/validation feedback.
/// </summary>
public enum FieldState
{
    /// <summary>No feedback to show; the status line is hidden.</summary>
    None,
    /// <summary>The value looks correct.</summary>
    Ok,
    /// <summary>Neutral information about the value.</summary>
    Info,
    /// <summary>The value is usable but something looks off.</summary>
    Warning,
    /// <summary>The value is missing or invalid.</summary>
    Error
}
