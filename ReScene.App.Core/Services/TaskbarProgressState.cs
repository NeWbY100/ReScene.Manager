namespace ReScene.App.Core.Services;

/// <summary>
/// Framework-neutral taskbar progress state. The WPF head maps this onto
/// <c>System.Windows.Shell.TaskbarItemProgressState</c> (a later Avalonia head maps it likewise),
/// so view-models can drive taskbar progress without referencing a UI framework.
/// </summary>
public enum TaskbarProgressState
{
    /// <summary>No progress indicator is shown.</summary>
    None,

    /// <summary>A determinate progress indicator driven by the progress value.</summary>
    Normal,

    /// <summary>An indeterminate ("marquee") progress indicator.</summary>
    Indeterminate,

    /// <summary>An error (red) progress indicator.</summary>
    Error,

    /// <summary>A paused (yellow) progress indicator.</summary>
    Paused,
}
