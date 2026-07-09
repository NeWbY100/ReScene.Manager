using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using ReScene.App.Core.Services;
using ReScene.Manager.Services;

namespace ReScene.Manager.Tests;

/// <summary>Headless tests for <see cref="AvaloniaUiDispatcher"/> (backed by <c>Dispatcher.UIThread</c>).</summary>
public class AvaloniaUiDispatcherTests
{
    [AvaloniaFact]
    public void CheckAccess_OnUiThread_IsTrue()
    {
        var dispatcher = new AvaloniaUiDispatcher();

        Assert.True(dispatcher.CheckAccess());
    }

    [AvaloniaFact]
    public void Invoke_RunsActionSynchronously()
    {
        var dispatcher = new AvaloniaUiDispatcher();
        bool ran = false;

        dispatcher.Invoke(() => ran = true);

        Assert.True(ran);
    }

    [AvaloniaFact]
    public void Post_QueuesAction_ThatRunsWhenPumped()
    {
        var dispatcher = new AvaloniaUiDispatcher();
        bool ran = false;

        dispatcher.Post(() => ran = true);
        Assert.False(ran); // not yet — Post is fire-and-forget

        Dispatcher.UIThread.RunJobs();

        Assert.True(ran);
    }

    [AvaloniaFact]
    public void Post_WithBackgroundPriority_RunsWhenPumped()
    {
        var dispatcher = new AvaloniaUiDispatcher();
        bool ran = false;

        dispatcher.Post(() => ran = true, UiDispatcherPriority.Background);
        Dispatcher.UIThread.RunJobs();

        Assert.True(ran);
    }
}
