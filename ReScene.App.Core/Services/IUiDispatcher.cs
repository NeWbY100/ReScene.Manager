namespace ReScene.App.Core.Services;

/// <summary>
/// Abstraction over the platform UI dispatcher so ViewModels can marshal work to the UI thread
/// without a hard dependency on a specific UI framework, making them testable.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Synchronously marshals <paramref name="action"/> onto the UI thread.</summary>
    public void Invoke(Action action);

    /// <summary>Asynchronously queues <paramref name="action"/> on the UI thread (fire-and-forget, like BeginInvoke).</summary>
    public void Post(Action action);

    /// <summary>Asynchronously queues <paramref name="action"/> on the UI thread at the given priority.</summary>
    public void Post(Action action, UiDispatcherPriority priority);

    /// <summary>Returns true when the caller is already on the UI thread.</summary>
    public bool CheckAccess();
}
