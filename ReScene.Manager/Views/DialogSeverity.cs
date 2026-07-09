namespace ReScene.Manager.Views;

/// <summary>Severity of a <see cref="MessageDialog"/>, selecting its glyph, colour and button set.</summary>
public enum DialogSeverity
{
    /// <summary>Informational message (info glyph, OK button).</summary>
    Info,

    /// <summary>Warning message (warning glyph, OK button).</summary>
    Warning,

    /// <summary>Error message (error glyph, OK button).</summary>
    Error,

    /// <summary>Confirmation prompt (warning glyph, OK + Cancel buttons).</summary>
    Confirm,
}
