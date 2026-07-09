using Avalonia.Threading;
using ReScene.App.Core.Services;

namespace ReScene.Manager.Services;

/// <summary>
/// <see cref="IUiDispatcher"/> backed by Avalonia's <see cref="Dispatcher.UIThread"/>. Avalonia's
/// UI dispatcher always exists in an initialized app (and in headless tests), so — unlike the WPF
/// head — no inline fallback is required. <see cref="Dispatcher.Invoke(Action)"/> already runs the
/// action inline when the caller is already on the UI thread, so re-entrancy is safe.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    /// <inheritdoc />
    public void Invoke(Action action) => Dispatcher.UIThread.Invoke(action);

    /// <inheritdoc />
    public void Post(Action action) => Dispatcher.UIThread.Post(action);

    /// <inheritdoc />
    public void Post(Action action, UiDispatcherPriority priority) =>
        Dispatcher.UIThread.Post(action, ToDispatcherPriority(priority));

    /// <inheritdoc />
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    private static DispatcherPriority ToDispatcherPriority(UiDispatcherPriority priority) => priority switch
    {
        UiDispatcherPriority.Normal => DispatcherPriority.Normal,
        UiDispatcherPriority.Background => DispatcherPriority.Background,
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, message: null),
    };
}
