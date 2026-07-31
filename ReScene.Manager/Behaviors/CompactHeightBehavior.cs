using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ReScene.Manager.Behaviors;

/// <summary>
/// Toggles the <c>compactHeight</c> style class on a view's inner layout root from its own
/// bounds height (spec §1): compact when height &lt; Threshold, restore at ≥ Threshold+12
/// (restore-only hysteresis — a fresh instance at Threshold+1 starts expanded). Applies
/// per-view <see cref="CompactRowSize"/> values on the root AND on descendant grids
/// carrying their own RowSizes attachment (collected at each apply), applies help-open
/// donation, manages the Help expander's per-mode state, and runs the spec rev-7 staged
/// focus algorithm across transitions.
/// </summary>
internal static class CompactHeightBehavior
{
    private const string ClassName = "compactHeight";
    private const double RestoreSlack = 12;

    public static readonly AttachedProperty<double> ThresholdProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("Threshold", typeof(CompactHeightBehavior), double.NaN);
    public static readonly AttachedProperty<IReadOnlyList<CompactRowSize>?> RowSizesProperty =
        AvaloniaProperty.RegisterAttached<Control, IReadOnlyList<CompactRowSize>?>("RowSizes", typeof(CompactHeightBehavior));
    public static readonly AttachedProperty<bool> HelpOpenProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("HelpOpen", typeof(CompactHeightBehavior));
    public static readonly AttachedProperty<Expander?> HelpExpanderProperty =
        AvaloniaProperty.RegisterAttached<Control, Expander?>("HelpExpander", typeof(CompactHeightBehavior));
    public static readonly AttachedProperty<Control?> RestoreFocusTargetProperty =
        AvaloniaProperty.RegisterAttached<Control, Control?>("RestoreFocusTarget", typeof(CompactHeightBehavior));
    public static readonly AttachedProperty<double> HelpBodyMaxHeightProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("HelpBodyMaxHeight", typeof(CompactHeightBehavior), double.NaN);

    public static double GetThreshold(Control obj) => obj.GetValue(ThresholdProperty);

    public static void SetThreshold(Control obj, double value) => obj.SetValue(ThresholdProperty, value);

    public static IReadOnlyList<CompactRowSize>? GetRowSizes(Control obj) => obj.GetValue(RowSizesProperty);

    public static void SetRowSizes(Control obj, IReadOnlyList<CompactRowSize>? value) => obj.SetValue(RowSizesProperty, value);

    public static bool GetHelpOpen(Control obj) => obj.GetValue(HelpOpenProperty);

    public static void SetHelpOpen(Control obj, bool value) => obj.SetValue(HelpOpenProperty, value);

    public static Expander? GetHelpExpander(Control obj) => obj.GetValue(HelpExpanderProperty);

    public static void SetHelpExpander(Control obj, Expander? value) => obj.SetValue(HelpExpanderProperty, value);

    public static Control? GetRestoreFocusTarget(Control obj) => obj.GetValue(RestoreFocusTargetProperty);

    public static void SetRestoreFocusTarget(Control obj, Control? value) => obj.SetValue(RestoreFocusTargetProperty, value);

    public static double GetHelpBodyMaxHeight(Control obj) => obj.GetValue(HelpBodyMaxHeightProperty);

    public static void SetHelpBodyMaxHeight(Control obj, double value) => obj.SetValue(HelpBodyMaxHeightProperty, value);

    // Per-control state, held weakly so it dies with the control — no leak, no explicit
    // unhook (same rationale as ListBoxAutoScroll's handler table). Captured row values are
    // stored here (keyed by the owning Grid, root OR descendant) rather than on the state's
    // owner, because a descendant grid never gets its own entry — it is only ever reached
    // by walking the root's visual tree at apply time.
    private static readonly ConditionalWeakTable<Control, State> _states = new();

    static CompactHeightBehavior()
    {
        ThresholdProperty.Changed.AddClassHandler<Control>(OnThresholdChanged);
        HelpOpenProperty.Changed.AddClassHandler<Control>(OnHelpOpenChanged);
        HelpExpanderProperty.Changed.AddClassHandler<Control>(OnHelpExpanderChanged);
    }

    private static State GetOrCreateState(Control control) => _states.GetValue(control, static _ => new State());

    // ── Lifecycle wiring ─────────────────────────────────────────────

    private static void OnThresholdChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        State state = GetOrCreateState(control);
        if (!state.LifecycleHooked)
        {
            state.LifecycleHooked = true;
            control.AttachedToVisualTree += OnControlAttachedToVisualTree;
            control.DetachedFromVisualTree += OnControlDetachedFromVisualTree;
            control.LostFocus += OnControlLostFocus;
            if (control.IsAttachedToVisualTree())
            {
                HookBounds(control, state);
            }
        }
        else if (control.IsAttachedToVisualTree())
        {
            // Threshold value changed at runtime on an already-live control: reassess now.
            QueueEvaluate(control, state);
        }
    }

    private static void OnControlAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var control = (Control)sender!;
        HookBounds(control, GetOrCreateState(control));
    }

    private static void OnControlDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        var control = (Control)sender!;
        if (_states.TryGetValue(control, out State? state) && state.BoundsHandler is { } handler)
        {
            control.PropertyChanged -= handler;
            state.BoundsHandler = null;
        }
    }

    private static void OnControlLostFocus(object? sender, RoutedEventArgs e) =>
        ((Control)sender!).Focusable = false;

    // Re-subscribing (rather than subscribing once for the control's lifetime), plus the
    // explicit QueueEvaluate below, means every (re)hook forces one evaluation attempt against
    // the CURRENT bounds, even if the numeric value happens to match whatever it was before
    // detaching — "reattach re-evaluates" is a guarantee, not an accident of the value having
    // changed.
    private static void HookBounds(Control control, State state)
    {
        if (state.BoundsHandler is { } previous)
        {
            control.PropertyChanged -= previous;
        }

        void Handler(object? _, AvaloniaPropertyChangedEventArgs args)
        {
            if (args.Property == Visual.BoundsProperty)
            {
                QueueEvaluate(control, state);
            }
        }

        control.PropertyChanged += Handler;
        state.BoundsHandler = Handler;
        QueueEvaluate(control, state);
    }

    private static void QueueEvaluate(Control control, State state)
    {
        if (state.UpdateQueued)
        {
            return;
        }

        state.UpdateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            state.UpdateQueued = false;
            Evaluate(control, state);
        });
    }

    // ── Threshold evaluation ─────────────────────────────────────────

    private static void Evaluate(Control control, State state)
    {
        double threshold = GetThreshold(control);
        if (double.IsNaN(threshold))
        {
            return;
        }

        double height = control.Bounds.Height;
        if (height <= 0)
        {
            return;
        }

        bool wantCompact = state.IsCompact ? height < threshold + RestoreSlack : height < threshold;
        if (wantCompact == state.IsCompact)
        {
            return;
        }

        // (1) CAPTURE before any change — both directions, since restoring can just as
        // easily strand focus on a hiding compact-only control (e.g. the header toggle).
        Control? captured = CaptureFocusedElement(control);

        // Entering compact captures each PixelRestore row's CURRENT (possibly user-dragged)
        // Height before it gets overwritten below. This must happen strictly before
        // state.IsCompact flips and before ApplyRowsEverywhere runs, and only on this one
        // edge — a later HelpOpen-triggered reapplication while already compact must never
        // recapture (it would capture the just-applied compact pixel value instead of the
        // user's drag).
        if (wantCompact)
        {
            CaptureDragHeights(control, state);
        }

        // (2) apply styles/rows.
        state.IsCompact = wantCompact;
        ApplyHelpExpanderDirection(control, state, wantCompact);
        ApplyRowsEverywhere(control, state);
        ToggleClass(control, wantCompact);

        // (3)-(6) staged: run only after a layout pass reflects the just-applied class/row/
        // visibility changes (Loaded is lower priority than the layout-driving priorities,
        // so the dispatcher services any pending layout before this posted job runs).
        if (captured is not null)
        {
            Dispatcher.UIThread.Post(() => RelocateFocusIfNeeded(control, captured, wantCompact), DispatcherPriority.Loaded);
        }
    }

    private static void ToggleClass(Control control, bool compact)
    {
        if (compact)
        {
            control.Classes.Add(ClassName);
        }
        else
        {
            control.Classes.Remove(ClassName);
        }
    }

    // ── Row application ──────────────────────────────────────────────

    private static void CaptureDragHeights(Control root, State state)
    {
        CaptureDragHeightForGrid(root, state);
        foreach (Visual descendant in root.GetVisualDescendants())
        {
            if (descendant is Grid grid)
            {
                CaptureDragHeightForGrid(grid, state);
            }
        }
    }

    private static void CaptureDragHeightForGrid(Control control, State state)
    {
        if (control is not Grid grid || GetRowSizes(grid) is not { } rows)
        {
            return;
        }

        foreach (CompactRowSize rowSize in rows)
        {
            if (rowSize.Mode != CompactRowMode.PixelRestore || rowSize.RowIndex >= grid.RowDefinitions.Count)
            {
                continue;
            }

            GridLength currentHeight = grid.RowDefinitions[rowSize.RowIndex].Height;
            if (currentHeight.IsAbsolute)
            {
                state.CapturedDragHeight[(grid, rowSize.RowIndex)] = currentHeight.Value;
            }
        }
    }

    /// <summary>
    /// Applies RowSizes on the root AND every descendant grid carrying its own RowSizes
    /// attachment. Descendants are collected fresh on every call (a cheap visual-tree walk
    /// that only runs on mode/help changes) rather than cached at attach time, so attachment
    /// order and late tree construction can never leave a descendant grid stuck on stale
    /// values.
    /// </summary>
    private static void ApplyRowsEverywhere(Control root, State state)
    {
        bool isCompact = state.IsCompact;
        bool helpOpen = GetHelpOpen(root);

        ApplyGridRows(root, isCompact, helpOpen, state);
        foreach (Visual descendant in root.GetVisualDescendants())
        {
            if (descendant is Grid grid)
            {
                ApplyGridRows(grid, isCompact, helpOpen, state);
            }
        }
    }

    private static void ApplyGridRows(Control control, bool isCompact, bool helpOpen, State state)
    {
        if (control is not Grid grid || GetRowSizes(grid) is not { } rows)
        {
            return;
        }

        foreach (CompactRowSize rowSize in rows)
        {
            ApplyOneRow(grid, rowSize, isCompact, helpOpen, state);
        }
    }

    private static void ApplyOneRow(Grid grid, CompactRowSize rowSize, bool isCompact, bool helpOpen, State state)
    {
        if (rowSize.RowIndex >= grid.RowDefinitions.Count)
        {
            return;
        }

        RowDefinition rowDef = grid.RowDefinitions[rowSize.RowIndex];

        // The XAML-authored MinHeight, captured the first time this row is ever touched
        // (before any mutation below) — never re-captured afterwards, so it survives any
        // number of later compact/restore round-trips.
        (Control Grid, int RowIndex) minKey = (grid, rowSize.RowIndex);
        if (!state.CapturedMinHeight.TryGetValue(minKey, out double originalMinHeight))
        {
            originalMinHeight = rowDef.MinHeight;
            state.CapturedMinHeight[minKey] = originalMinHeight;
        }

        double compactValue = helpOpen ? rowSize.HelpOpenMinHeight : rowSize.CompactMinHeight;

        switch (rowSize.Mode)
        {
            case CompactRowMode.MinOnly:
                rowDef.MinHeight = isCompact ? compactValue : originalMinHeight;
                break;

            case CompactRowMode.PixelRestore:
                if (isCompact)
                {
                    rowDef.Height = new GridLength(compactValue, GridUnitType.Pixel);
                    rowDef.MinHeight = compactValue;
                }
                else
                {
                    double restoreHeight = state.CapturedDragHeight.TryGetValue((grid, rowSize.RowIndex), out double dragHeight)
                        ? dragHeight
                        : rowSize.NormalHeight;
                    rowDef.Height = new GridLength(restoreHeight, GridUnitType.Pixel);
                    rowDef.MinHeight = originalMinHeight;
                }
                break;

            case CompactRowMode.AutoToStar:
                rowDef.Height = isCompact ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
                rowDef.MinHeight = isCompact ? compactValue : 0;
                break;
        }
    }

    private static void OnHelpOpenChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        State state = GetOrCreateState(control);
        if (!state.IsCompact)
        {
            return;
        }

        // Donation swap only: ApplyRowsEverywhere never captures, so replaying it here
        // (whether HelpOpen was flipped directly, as in the unit tests, or indirectly via
        // the expander wiring below) is always safe to repeat.
        ApplyRowsEverywhere(control, state);
        QueueApplyHelpBodyMaxHeight(control, state);
    }

    // ── Help expander wiring ─────────────────────────────────────────

    private static void ApplyHelpExpanderDirection(Control control, State state, bool enteringCompact)
    {
        Expander? expander = GetHelpExpander(control);
        if (expander is not null)
        {
            // Entering compact: collapsed by default (condition-5 reset — re-entering a
            // compact session never resumes a previous session's open Help). Leaving:
            // flat mode always renders the body expanded.
            expander.IsExpanded = !enteringCompact;
        }

        RecomputeHelpOpen(control, expander, state);

        // Queued directly (not left to OnHelpOpenChanged's cascade alone): when LEAVING
        // compact with Help open, state.IsCompact is already false by the time SetHelpOpen's
        // Changed handler runs, so its "if (!state.IsCompact) return;" guard would otherwise
        // skip resetting the body's MaxHeight, leaving normal/flat mode wrongly constrained
        // by the last donated budget.
        QueueApplyHelpBodyMaxHeight(control, state);
    }

    private static void OnHelpExpanderChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        State state = GetOrCreateState(control);
        if (e.OldValue is Expander oldExpander && state.ExpanderIsExpandedHandler is { } oldHandler)
        {
            oldExpander.PropertyChanged -= oldHandler;
            state.ExpanderIsExpandedHandler = null;
        }

        if (e.NewValue is Expander expander)
        {
            void Handler(object? _, AvaloniaPropertyChangedEventArgs args)
            {
                if (args.Property == Expander.IsExpandedProperty)
                {
                    RecomputeHelpOpen(control, expander, state);
                }
            }

            expander.PropertyChanged += Handler;
            state.ExpanderIsExpandedHandler = Handler;
        }
    }

    // Runs whenever anything that feeds HelpOpen changes: the behavior's own forced
    // IsExpanded set on a transition, OR the user toggling the header while compact.
    private static void RecomputeHelpOpen(Control control, Expander? expander, State state) =>
        SetHelpOpen(control, state.IsCompact && expander is { IsExpanded: true });

    // The body's ContentPresenter realizes its child lazily, tied to layout: while IsExpanded
    // is false the wrapping content area is never measured, so the ScrollViewer never attaches
    // to the visual tree at all. IsExpanded has already been SET by the time ApplyHelpBodyMaxHeight
    // is queued, but the resulting visibility/layout consequences are not settled yet at that
    // point — Loaded is lower priority than the layout-driving priorities, so by the time this
    // runs, the content area (if now expanded) has had a chance to actually realize.
    private static void QueueApplyHelpBodyMaxHeight(Control control, State state) =>
        Dispatcher.UIThread.Post(() => ApplyHelpBodyMaxHeight(control, state), DispatcherPriority.Loaded);

    private static void ApplyHelpBodyMaxHeight(Control control, State state)
    {
        if (GetHelpExpander(control) is not { } expander)
        {
            return;
        }

        expander.UpdateLayout();
        if (expander.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } body)
        {
            return;
        }

        bool donating = state.IsCompact && GetHelpOpen(control);
        body.MaxHeight = donating ? GetHelpBodyMaxHeight(control) : double.PositiveInfinity;
    }

    // ── Staged focus (spec rev 7/8/11) ───────────────────────────────

    /// <summary>
    /// The currently-focused element, but ONLY if it is focused AND a descendant of
    /// <paramref name="root"/> (spec rev 8 precondition) — otherwise null, so a resize while
    /// focus sits in the shell menu, the tab strip, another window, or nowhere can never
    /// pull focus into this view.
    /// </summary>
    private static Control? CaptureFocusedElement(Control root)
    {
        if (TopLevel.GetTopLevel(root)?.FocusManager?.GetFocusedElement() is not Control focused)
        {
            return null;
        }

        return ReferenceEquals(focused, root) || root.IsVisualAncestorOf(focused) ? focused : null;
    }

    private static void RelocateFocusIfNeeded(Control root, Control captured, bool enteringCompact)
    {
        bool obscured = IsObscured(captured);
        if (IsSettled(captured, obscured))
        {
            return;
        }

        if (obscured)
        {
            // Scrollable ancestors may recover it — never relocate merely-clipped focus.
            captured.BringIntoView();
            captured.UpdateLayout();
            obscured = IsObscured(captured);
            if (IsSettled(captured, obscured))
            {
                return;
            }
        }

        Control target = ResolveFallbackTarget(root, enteringCompact);
        target.Focus();
    }

    private static bool IsSettled(Control captured, bool obscured) =>
        !obscured && captured.Focusable && captured.IsEffectivelyEnabled;

    /// <summary>
    /// True if <paramref name="element"/> is detached, invisible anywhere in its ancestor
    /// chain, or clipped out by any ancestor that clips its content — <c>IsEffectivelyVisible</c>
    /// alone does not see the clipping case (a scrolled-away row stays "visible").
    /// </summary>
    private static bool IsObscured(Control element)
    {
        if (!element.IsAttachedToVisualTree() || !element.IsEffectivelyVisible)
        {
            return true;
        }

        foreach (Visual ancestor in element.GetVisualAncestors())
        {
            if (ancestor is not Control clipper || !clipper.ClipToBounds)
            {
                continue;
            }

            Point? topLeft = element.TranslatePoint(new Point(0, 0), clipper);
            Point? bottomRight = element.TranslatePoint(new Point(element.Bounds.Width, element.Bounds.Height), clipper);
            if (topLeft is not { } tl || bottomRight is not { } br)
            {
                return true; // no common ancestor with the clipper: effectively detached from it
            }

            if (!new Rect(tl, br).Intersects(new Rect(clipper.Bounds.Size)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Fallback chain (spec rev 8): the resolved direction target → the first focusable
    /// descendant of the root → the root itself, granted transient focusability for the
    /// hand-off. Never returns null — a silent no-op is forbidden at every step.
    /// </summary>
    private static Control ResolveFallbackTarget(Control root, bool enteringCompact)
    {
        Control? resolved = enteringCompact
            ? GetHelpExpander(root)?.GetVisualDescendants().OfType<ToggleButton>().FirstOrDefault()
            : GetRestoreFocusTarget(root);
        if (IsUsable(resolved))
        {
            return resolved!;
        }

        Control? firstFocusable = root.GetVisualDescendants().OfType<Control>().FirstOrDefault(IsUsable);
        if (firstFocusable is not null)
        {
            return firstFocusable;
        }

        // TopLevel is not focusable by default: grant it here, only for the hand-off.
        // OnControlLostFocus resets it the moment focus moves on, so no permanent Tab stop
        // is added.
        root.Focusable = true;
        return root;
    }

    private static bool IsUsable(Control? control) =>
        control is not null && control.Focusable && control.IsEffectivelyVisible && control.IsEffectivelyEnabled;

    /// <summary>
    /// Per-control state: mode flag, the coalescing guard, the Bounds subscription, captured
    /// row values (keyed by the owning Grid — root or descendant — since a descendant grid
    /// never gets its own state entry), and the expander's IsExpanded subscription.
    /// </summary>
    private sealed class State
    {
        public bool IsCompact { get; set; }

        public bool UpdateQueued { get; set; }

        public bool LifecycleHooked { get; set; }

        public EventHandler<AvaloniaPropertyChangedEventArgs>? BoundsHandler { get; set; }

        public Dictionary<(Control Grid, int RowIndex), double> CapturedDragHeight { get; } = [];

        public Dictionary<(Control Grid, int RowIndex), double> CapturedMinHeight { get; } = [];

        public EventHandler<AvaloniaPropertyChangedEventArgs>? ExpanderIsExpandedHandler { get; set; }
    }
}
