using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace ReScene.Manager.Behaviors;

/// <summary>
/// Attached behavior that adds Home/End key handling to a <see cref="ScrollViewer"/>. Closes a
/// real platform gap discovered while implementing the small-window layout degradation's compact
/// Help body (spec rev 12 §2): Avalonia 11's stock <c>ScrollViewer.OnKeyDown</c> only wires
/// <c>Key.PageUp</c>/<c>Key.PageDown</c> to its own <c>PageUp()</c>/<c>PageDown()</c> — it does
/// NOT wire <c>Key.Home</c>/<c>Key.End</c> to its own (public) <c>ScrollToHome()</c>/
/// <c>ScrollToEnd()</c>, despite the design doc's assumption that "Avalonia's ScrollViewer
/// handles Page keys" implying Home/End work too (confirmed by decompiling
/// Avalonia.Controls.dll 11.0.0 — <c>ScrollViewer.OnKeyDown</c> has no <c>Key.Home</c>/
/// <c>Key.End</c> branch at all). Applied via the shared <c>helpBody</c> style (Styles.axaml)
/// rather than per-view code-behind, so every task's own compact Help body inherits it
/// automatically without needing to know this gap exists.
/// </summary>
public static class ScrollViewerHomeEndKeys
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("Enabled", typeof(ScrollViewerHomeEndKeys));

    public static bool GetEnabled(ScrollViewer obj) => obj.GetValue(EnabledProperty);

    public static void SetEnabled(ScrollViewer obj, bool value) => obj.SetValue(EnabledProperty, value);

    // Holds each control's subscription so it can be removed if the property is toggled off. The
    // weak key means the entry is collected with the control — no leak, no explicit unhook (same
    // rationale as ListBoxAutoScroll's own handler table).
    private static readonly ConditionalWeakTable<ScrollViewer, EventHandler<KeyEventArgs>> _handlers = new();

    static ScrollViewerHomeEndKeys() => EnabledProperty.Changed.AddClassHandler<ScrollViewer>(OnEnabledChanged);

    private static void OnEnabledChanged(ScrollViewer scrollViewer, AvaloniaPropertyChangedEventArgs e)
    {
        // Drop any prior subscription first so toggling the property never double-subscribes.
        if (_handlers.TryGetValue(scrollViewer, out EventHandler<KeyEventArgs>? previous))
        {
            scrollViewer.RemoveHandler(InputElement.KeyDownEvent, previous);
            _handlers.Remove(scrollViewer);
        }

        if (e.NewValue is true)
        {
            void Handler(object? sender, KeyEventArgs args) => OnKeyDown((ScrollViewer)sender!, args);
            _handlers.Add(scrollViewer, Handler);

            // Tunnel-free (bubbling) handler on the ScrollViewer itself: Home/End reaching here
            // unhandled from a non-interactive child (this behavior's only real target — a plain
            // prose TextBlock, which never itself handles a key) is the expected, normal path.
            scrollViewer.AddHandler(InputElement.KeyDownEvent, Handler);
        }
    }

    private static void OnKeyDown(ScrollViewer scrollViewer, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Home:
                scrollViewer.ScrollToHome();
                e.Handled = true;
                break;
            case Key.End:
                scrollViewer.ScrollToEnd();
                e.Handled = true;
                break;
        }
    }
}
