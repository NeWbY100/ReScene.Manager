using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ReScene.App.Core.Services;
using ReScene.Manager.Interop;
using ReScene.Manager.Services;

namespace ReScene.Manager.Tests;

/// <summary>
/// Unit tests for the Windows taskbar-progress consumer. The <see cref="WindowsTaskbarProgress.ToFlags"/>
/// mapping is pure and asserted per <see cref="TaskbarProgressState"/> member; the headless-safety test
/// proves <see cref="WindowsTaskbarProgress.TryCreate"/> never reaches COM under the headless platform
/// (whose windows report the <c>"STUB"</c> handle descriptor, not <c>"HWND"</c>), so unit tests and CI
/// stay off the native taskbar path.
/// </summary>
public class WindowsTaskbarProgressTests
{
    // One assert per App.Core TaskbarProgressState member. TaskbarProgressFlags is internal, so it can't
    // appear in a public [Theory] signature (CS0051) — the mapping is covered by explicit facts instead.
    [Fact]
    public void ToFlags_None_MapsToNoProgress()
        => Assert.Equal(TaskbarProgressFlags.NoProgress, WindowsTaskbarProgress.ToFlags(TaskbarProgressState.None));

    [Fact]
    public void ToFlags_Normal_MapsToNormal()
        => Assert.Equal(TaskbarProgressFlags.Normal, WindowsTaskbarProgress.ToFlags(TaskbarProgressState.Normal));

    [Fact]
    public void ToFlags_Indeterminate_MapsToIndeterminate()
        => Assert.Equal(TaskbarProgressFlags.Indeterminate, WindowsTaskbarProgress.ToFlags(TaskbarProgressState.Indeterminate));

    [Fact]
    public void ToFlags_Error_MapsToError()
        => Assert.Equal(TaskbarProgressFlags.Error, WindowsTaskbarProgress.ToFlags(TaskbarProgressState.Error));

    [Fact]
    public void ToFlags_Paused_MapsToPaused()
        => Assert.Equal(TaskbarProgressFlags.Paused, WindowsTaskbarProgress.ToFlags(TaskbarProgressState.Paused));

    [Fact]
    public void ToFlags_UnknownState_MapsToNoProgress()
        => Assert.Equal(TaskbarProgressFlags.NoProgress, WindowsTaskbarProgress.ToFlags((TaskbarProgressState)999));

    [AvaloniaFact]
    public void TryCreate_HeadlessWindow_ReturnsNull()
    {
        // The headless window exposes a non-Win32 ("STUB") handle, so TryCreate must bail before any COM
        // activation — this is the gate that keeps unit tests/CI off the real ITaskbarList3 path even on
        // a Windows build agent (where OperatingSystem.IsWindows() is true).
        var window = new Window();
        Assert.Null(WindowsTaskbarProgress.TryCreate(window));
    }
}
