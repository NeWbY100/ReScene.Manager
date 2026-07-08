using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ReScene.Manager.Behaviors;

/// <summary>
/// Attached behavior that keeps an <see cref="ItemsControl"/> (e.g. a log ListBox bound to an
/// <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>) scrolled to its newest
/// item as entries are appended — but only while the user is already at the bottom, so scrolling
/// up to read earlier entries is not yanked back down. Applied to the shared log ListBox style so
/// every operation log (SRR/SRS creation, reconstruction, restore) auto-scrolls.
/// </summary>
public static class ListBoxAutoScroll
{
    /// <summary>
    /// When <see langword="true"/>, the target auto-scrolls to the last item whenever items are
    /// added (or the collection resets), provided the view is currently at the bottom.
    /// </summary>
    public static readonly AttachedProperty<bool> AutoScrollToEndProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("AutoScrollToEnd", typeof(ListBoxAutoScroll));

    public static bool GetAutoScrollToEnd(ItemsControl obj) => obj.GetValue(AutoScrollToEndProperty);

    public static void SetAutoScrollToEnd(ItemsControl obj, bool value) => obj.SetValue(AutoScrollToEndProperty, value);

    // Holds each control's subscription so it can be removed if the property is toggled off.
    // The weak key means the entry is collected with the control — no leak, no explicit unhook.
    private static readonly ConditionalWeakTable<ItemsControl, NotifyCollectionChangedEventHandler> _handlers = new();

    static ListBoxAutoScroll() => AutoScrollToEndProperty.Changed.AddClassHandler<ItemsControl>(OnAutoScrollToEndChanged);

    private static void OnAutoScrollToEndChanged(ItemsControl itemsControl, AvaloniaPropertyChangedEventArgs e)
    {
        if (itemsControl.Items is not INotifyCollectionChanged incc)
        {
            return;
        }

        // Drop any prior subscription first so toggling the property never double-subscribes.
        if (_handlers.TryGetValue(itemsControl, out NotifyCollectionChangedEventHandler? previous))
        {
            incc.CollectionChanged -= previous;
            _handlers.Remove(itemsControl);
        }

        if (e.NewValue is true)
        {
            void Handler(object? _, NotifyCollectionChangedEventArgs args) => OnItemsChanged(itemsControl, args);
            _handlers.Add(itemsControl, Handler);
            incc.CollectionChanged += Handler;
        }
    }

    private static void OnItemsChanged(ItemsControl itemsControl, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not (NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset))
        {
            return;
        }

        // Collection mutations are marshalled onto the UI thread by the view-models; if that ever
        // changes, bail rather than touch the visual tree off-thread.
        if (!Dispatcher.UIThread.CheckAccess() || itemsControl.ItemCount == 0)
        {
            return;
        }

        // Only stick to the bottom if the user is already there. The scroll metrics here still
        // reflect the pre-layout state (the new item isn't measured yet), which is exactly what
        // lets us detect "was at the bottom" before the content grew.
        ScrollViewer? scrollViewer = itemsControl.FindDescendantOfType<ScrollViewer>();
        double scrollableHeight = scrollViewer is null ? 0 : scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
        bool atBottom = scrollViewer is null
            || scrollableHeight <= 0
            || scrollViewer.Offset.Y >= scrollableHeight - 1.0;

        if (!atBottom)
        {
            return;
        }

        // Defer until after the new item is laid out — scrolling before layout is a no-op.
        Dispatcher.UIThread.Post(
            () => itemsControl.FindDescendantOfType<ScrollViewer>()?.ScrollToEnd(),
            DispatcherPriority.Background);
    }
}
