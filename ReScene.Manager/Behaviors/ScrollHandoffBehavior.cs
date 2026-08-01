using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ReScene.Manager.Behaviors;

/// <summary>
/// Chains vertical scroll input from an inner <see cref="DataGrid"/> — whose own scrolling is
/// entirely self-contained virtualization, oblivious to any ancestor — out to the nearest ANCESTOR
/// <see cref="ScrollViewer"/> once the grid itself is exhausted in the requested direction (spec's
/// codex round-1 #8: a small-window config band scrolls its DataGrid host, so a user gesture that
/// would otherwise dead-end at the grid's own edge must continue past it). Two independent
/// mechanisms, each verified empirically (headless spikes) and by decompiling
/// <c>Avalonia.Controls.DataGrid</c> 11.3.13 before writing this — see the per-mechanism remarks
/// below for exactly what was confirmed and why it rules out relying on some existing, undocumented
/// framework hook instead:
/// <list type="bullet">
///   <item><b>WHEEL</b> — <see cref="DataGrid"/>'s own <c>OnPointerWheelChanged</c> class handler
///     already leaves the event unhandled whenever it cannot consume the gesture internally
///     (confirmed: it computes <c>UpdateScroll(...)</c>, and when that reports no movement, sets
///     <c>e.Handled = e.Handled || !ScrollViewer.GetIsScrollChainingEnabled(this)</c> — with
///     <c>IsScrollChainingEnabled</c> defaulting to <c>true</c> and never overridden in this app,
///     that leaves <c>e.Handled</c> false). Avalonia's routed-event pipeline runs CLASS handlers
///     (which is how <see cref="DataGrid"/>'s own override is wired) before INSTANCE handlers added
///     via <see cref="Interactive.AddHandler"/> for the same element/phase — confirmed empirically:
///     a plain (non-handledEventsToo) instance handler added here to the SAME grid is invoked ONLY
///     when the class handler left the event unhandled, i.e. exactly the at-extent case, and sees
///     the correctly-still-false <c>e.Handled</c> at that point. This behavior owns that hand-off
///     EXPLICITLY (rather than leaving the outcome to depend on that ambient default staying wired)
///     because <see cref="PointerWheelEventArgs"/> cannot be re-raised synthetically once consumed
///     — if this reasoning about class-handler-first ordering were ever wrong, or a future style
///     change disabled chaining, there would be no way to recover the gesture after the fact.</item>
///   <item><b>KEYBOARD/FOCUS</b> — confirmed by decompiling every <c>Focus()</c> call site in the
///     DataGrid package: ordinary (non-edit) arrow-key browsing NEVER focuses a specific cell or
///     row — <c>ProcessDataGridKey</c> ends by focusing the GRID ITSELF unconditionally, and the
///     only per-cell <c>Focus()</c> calls are inside cell-EDIT entry. Consequently
///     <c>Control.BringIntoView()</c> — the sole way <c>RequestBringIntoView</c> is ever raised in
///     Avalonia (also confirmed by decompilation: it is a plain extension method, never invoked
///     automatically by any focus-change machinery) — is never called by the grid's own arrow-key
///     handling either; it only moves the grid's OWN internal virtualized offset
///     (<c>ScrollSlotIntoView</c>) to keep the new current row within ITS OWN viewport. This
///     behavior calls <c>BringIntoView()</c> itself on the newly-current row (found via the public
///     <see cref="DataGrid.CurrentCellChanged"/> event and <see cref="DataGrid.SelectedIndex"/>),
///     which is genuinely necessary — not merely explicit-for-robustness like the wheel half —
///     since nothing else in the framework ever performs it for this control.</item>
/// </list>
/// Both mechanisms key off <see cref="DataGrid.CurrentCellChanged"/>/<c>PointerWheelChanged</c> at
/// the ROW level, not the individual cell: <see cref="DataGridRow"/> exposes no public per-column
/// cell lookup, and a row's bounds are a strict superset of every cell within it, so bringing the
/// row fully into view is sufficient to satisfy "the current cell ends fully visible" without
/// depending on <see cref="DataGridCell"/> internals that are not part of the public API.
/// </summary>
internal static class ScrollHandoffBehavior
{
    public static readonly AttachedProperty<bool> HandoffProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Handoff", typeof(ScrollHandoffBehavior));

    public static bool GetHandoff(Control obj) => obj.GetValue(HandoffProperty);

    public static void SetHandoff(Control obj, bool value) => obj.SetValue(HandoffProperty, value);

    /// <summary>Mirrors <c>ScrollContentPresenter.OnPointerWheelChanged</c>'s own per-tick constant exactly, so a
    /// gesture that hands off feels identical in speed to one the outer ScrollViewer handled natively throughout.</summary>
    private const double WheelScrollAmount = 50.0;

    // Weakly keyed so a grid's state dies with the grid — no leak, no explicit unhook required on
    // the caller's part (same rationale as ListBoxAutoScroll's / ScrollViewerHomeEndKeys' own handler tables).
    private static readonly ConditionalWeakTable<DataGrid, State> _states = new();

    static ScrollHandoffBehavior() => HandoffProperty.Changed.AddClassHandler<Control>(OnHandoffChanged);

    private static void OnHandoffChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (control is not DataGrid grid)
        {
            return;
        }

        State state = _states.GetValue(grid, static _ => new State());

        if ((bool)e.NewValue!)
        {
            if (state.LifecycleHooked)
            {
                return;
            }

            state.LifecycleHooked = true;
            grid.AttachedToVisualTree += OnGridAttachedToVisualTree;
            grid.DetachedFromVisualTree += OnGridDetachedFromVisualTree;
            if (grid.IsAttachedToVisualTree())
            {
                Attach(grid, state);
            }
        }
        else
        {
            Detach(grid, state);
        }
    }

    private static void OnGridAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var grid = (DataGrid)sender!;
        Attach(grid, _states.GetValue(grid, static _ => new State()));
    }

    private static void OnGridDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var grid = (DataGrid)sender!;
        if (_states.TryGetValue(grid, out State? state))
        {
            Detach(grid, state);
        }
    }

    /// <summary>
    /// Re-resolves the outer <see cref="ScrollViewer"/> and re-wires both mechanisms every time the
    /// grid (re)joins the visual tree — not just once — mirroring <c>CompactHeightBehavior</c>'s own
    /// "reattach re-evaluates" rule: a tab-hosted view is detached/reattached on every tab switch in
    /// this app (only the selected TabItem's content stays in the live visual tree), and the
    /// ancestor chain is only walkable while attached.
    /// </summary>
    private static void Attach(DataGrid grid, State state)
    {
        if (state.Outer is not null)
        {
            return;
        }

        if (grid.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is not { } outer)
        {
            return; // nothing to hand off to — leave the grid's own (unaffected) scrolling as-is
        }

        state.Outer = outer;

        void OnPointerWheelChanged(object? _, PointerWheelEventArgs args) => HandleWheelAtExtent(outer, args);
        void OnCurrentCellChanged(object? _, EventArgs __) => BringCurrentRowIntoView(grid);

        grid.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged);
        grid.CurrentCellChanged += OnCurrentCellChanged;

        state.WheelHandler = OnPointerWheelChanged;
        state.CurrentCellHandler = OnCurrentCellChanged;
    }

    private static void Detach(DataGrid grid, State state)
    {
        if (state.WheelHandler is { } wheelHandler)
        {
            grid.RemoveHandler(InputElement.PointerWheelChangedEvent, wheelHandler);
            state.WheelHandler = null;
        }

        if (state.CurrentCellHandler is { } currentCellHandler)
        {
            grid.CurrentCellChanged -= currentCellHandler;
            state.CurrentCellHandler = null;
        }

        state.Outer = null;
    }

    /// <summary>
    /// Reached only when the grid's own class handler left <paramref name="e"/> unhandled (see this
    /// class's own remarks) — i.e. the grid could not scroll further internally in the requested
    /// direction. Applies the SAME clamped offset formula <c>ScrollContentPresenter</c> itself uses,
    /// directly to <paramref name="outer"/>, and marks the event handled. If the outer is ALSO
    /// already at its own extent in this direction, nothing changes and the event is deliberately
    /// left unhandled (matching <c>ScrollContentPresenter</c>'s own convention) so a THIRD,
    /// further-out scrollable ancestor — none exists in this app today, but nothing here should
    /// assume that permanently — still gets its chance.
    /// </summary>
    private static void HandleWheelAtExtent(ScrollViewer outer, PointerWheelEventArgs e)
    {
        if (outer.Extent.Height <= outer.Viewport.Height)
        {
            return;
        }

        double newY = outer.Offset.Y + ((0 - e.Delta.Y) * WheelScrollAmount);
        newY = Math.Max(0, Math.Min(newY, outer.Extent.Height - outer.Viewport.Height));
        if (newY == outer.Offset.Y)
        {
            return;
        }

        outer.Offset = new Vector(outer.Offset.X, newY);
        e.Handled = true;
    }

    /// <summary>
    /// The current row is looked up fresh on every call (never cached) — DataGridRow instances are
    /// recycled/re-realized by the grid's own virtualization as it scrolls, so a cached reference
    /// would go stale silently.
    /// </summary>
    private static void BringCurrentRowIntoView(DataGrid grid)
    {
        if (grid.SelectedIndex < 0)
        {
            return;
        }

        grid.GetVisualDescendants().OfType<DataGridRow>()
            .FirstOrDefault(row => row.Index == grid.SelectedIndex)
            ?.BringIntoView();
    }

    private sealed class State
    {
        public bool LifecycleHooked { get; set; }

        public ScrollViewer? Outer { get; set; }

        public EventHandler<PointerWheelEventArgs>? WheelHandler { get; set; }

        public EventHandler<EventArgs>? CurrentCellHandler { get; set; }
    }
}
