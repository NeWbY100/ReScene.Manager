namespace ReScene.Manager.Interop;

/// <summary>
/// Mirror of the Win32 <c>TBPFLAG</c> enum (shobjidl_core.h) passed to
/// <see cref="ITaskbarList3.SetProgressState"/>. Values are the native bit flags.
/// </summary>
[Flags]
internal enum TaskbarProgressFlags
{
    /// <summary>Stops displaying progress and returns the button to its normal state (TBPF_NOPROGRESS).</summary>
    NoProgress = 0,

    /// <summary>Marquee-style ("indeterminate") progress (TBPF_INDETERMINATE).</summary>
    Indeterminate = 0x1,

    /// <summary>Determinate progress driven by the progress value (TBPF_NORMAL).</summary>
    Normal = 0x2,

    /// <summary>Error (red) progress (TBPF_ERROR).</summary>
    Error = 0x4,

    /// <summary>Paused (yellow) progress (TBPF_PAUSED).</summary>
    Paused = 0x8,
}
