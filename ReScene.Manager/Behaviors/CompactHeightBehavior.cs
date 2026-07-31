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

    // LostFocus bubbles: a descendant losing focus (e.g. because the root terminal is about to
    // steal it away via Focus()) raises the SAME event, which arrives here with sender=root
    // just like root's own direct loss of focus does. Resetting on every bubbled occurrence
    // would clear the just-granted transient Focusable mid-hand-off (root.Focus() stealing
    // focus from a still-focused captured element fires the captured element's OWN LostFocus,
    // which bubbles through root before the grant even settles). Only e.Source == root — the
    // event genuinely originating on root itself — means root is the one that lost focus.
    private static void OnControlLostFocus(object? sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, e.Source))
        {
            ((Control)sender!).Focusable = false;
        }
    }

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
        bool isTransition = wantCompact != state.IsCompact;

        // A fresh control's very first evaluation must establish the expander/HelpOpen state
        // for whatever mode it starts in even when that mode matches state.IsCompact's false
        // default (so isTransition is false) — otherwise a view that starts, and stays, at
        // normal height never runs ApplyHelpExpanderDirection at all, and its Help expander
        // just keeps its OWN IsExpanded=false default forever instead of the required
        // flat-mode force-expanded state.
        if (!isTransition && state.Established)
        {
            return;
        }

        state.Established = true;

        // (1) CAPTURE before any change — both directions, since restoring can just as
        // easily strand focus on a hiding compact-only control (e.g. the header toggle).
        // Only meaningful for an actual transition: a first-touch establishment pass with no
        // mode change has nothing to relocate focus away from.
        Control? captured = isTransition ? CaptureFocusedElement(control) : null;

        // Entering compact captures each PixelRestore row's CURRENT (possibly user-dragged)
        // Height before it gets overwritten below. This must happen strictly before
        // state.IsCompact flips and before ApplyRowsEverywhere runs, and only on the actual
        // normal-to-compact transition edge — a later HelpOpen-triggered reapplication while
        // already compact must never recapture (it would capture the just-applied compact
        // pixel value instead of the user's drag).
        if (isTransition && wantCompact)
        {
            CaptureDragHeights(control, state);
        }

        // Every real transition bumps the generation — regardless of whether anything was
        // focused to capture — so "a newer transition has happened since" is detectable even
        // when the transition that made it stale had nothing of its own to relocate.
        if (isTransition)
        {
            ++state.Generation;
        }

        // (2) apply styles/rows.
        state.IsCompact = wantCompact;
        ApplyHelpExpanderDirection(control, state, wantCompact);
        ApplyRowsEverywhere(control, state);
        ToggleClass(control, wantCompact);

        if (captured is null)
        {
            return;
        }

        // (3)-(6) staged: run only after a layout pass reflects the just-applied class/row/
        // visibility changes (Loaded is lower priority than the layout-driving priorities,
        // so the dispatcher services any pending layout before this posted job runs).
        Dispatcher.UIThread.Post(
            () => RelocateFocusIfNeeded(control, captured, wantCompact, state.Generation, state),
            DispatcherPriority.Loaded);
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

    private static Control? CurrentFocusedElement(Control root) =>
        TopLevel.GetTopLevel(root)?.FocusManager?.GetFocusedElement() as Control;

    /// <summary>
    /// The currently-focused element, but ONLY if it is focused AND a descendant of
    /// <paramref name="root"/> (spec rev 8 precondition) — otherwise null, so a resize while
    /// focus sits in the shell menu, the tab strip, another window, or nowhere can never
    /// pull focus into this view.
    /// </summary>
    private static Control? CaptureFocusedElement(Control root)
    {
        if (CurrentFocusedElement(root) is not { } focused)
        {
            return null;
        }

        return ReferenceEquals(focused, root) || root.IsVisualAncestorOf(focused) ? focused : null;
    }

    /// <summary>
    /// Runs the post-layout obscurement check and, if needed, the fallback chain — but only if
    /// this job is still current, and on whatever element actually still needs recovering.
    /// Rejected outright if a NEWER transition has superseded this one (generation) or the mode
    /// has since changed away from the direction this job was queued for. Otherwise,
    /// <see cref="ResolveRecoveryTarget"/> decides what (if anything) to act on: it can differ
    /// from <paramref name="captured"/> — see its own doc for why.
    /// </summary>
    private static void RelocateFocusIfNeeded(Control root, Control captured, bool enteringCompact, int generation, State state)
    {
        if (IsSuperseded(enteringCompact, generation, state))
        {
            return;
        }

        if (ResolveRecoveryTarget(root, captured) is not { } target)
        {
            return;
        }

        bool obscured = IsObscured(target);
        if (IsSettled(target, obscured))
        {
            return;
        }

        if (obscured)
        {
            // Scrollable ancestors may recover it — never relocate merely-clipped focus.
            target.BringIntoView();
            target.UpdateLayout();
            obscured = IsObscured(target);
            if (IsSettled(target, obscured))
            {
                return;
            }

            // Re-check staleness: BringIntoView/UpdateLayout can themselves cascade into
            // further layout and, in principle, another transition — the same rejection
            // applies to acting on the fallback chain below.
            if (IsSuperseded(enteringCompact, generation, state))
            {
                return;
            }
        }

        FocusFallbackChain(root, target, enteringCompact);
    }

    private static bool IsSuperseded(bool enteringCompact, int generation, State state) =>
        state.Generation != generation || state.IsCompact != enteringCompact;

    /// <summary>
    /// What this job should actually recover, if anything — not necessarily
    /// <paramref name="captured"/> itself. Nothing currently focused is the ordinary,
    /// expected transient state the instant <paramref name="captured"/> becomes invisible or
    /// unfocusable (precisely what this job exists to recover from, not evidence that some
    /// unrelated action has taken over), so that case, and the "still the same element" case,
    /// both mean: recover <paramref name="captured"/>. Focus that moved to something OUTSIDE
    /// this root entirely is never this job's business (the rev-8 precondition, re-checked
    /// here since scope can change between capture and this job running) — return null. Focus
    /// that moved to a DIFFERENT, USABLE element still inside this root means somebody else
    /// already decided where focus belongs — respect it, return null. Focus that moved to a
    /// DIFFERENT element inside this root that is ITSELF unusable means the same transition (or
    /// something concurrent with it) stranded THAT element instead of the one originally
    /// captured — recovery must now target it, or this job would report `captured` as
    /// "settled" while the REAL, currently-focused element sits broken.
    /// </summary>
    private static Control? ResolveRecoveryTarget(Control root, Control captured)
    {
        Control? current = CurrentFocusedElement(root);
        if (current is null || ReferenceEquals(current, captured))
        {
            return captured;
        }

        bool inScope = ReferenceEquals(current, root) || root.IsVisualAncestorOf(current);
        if (!inScope)
        {
            return null;
        }

        return IsUsable(current) ? null : current;
    }

    private static bool IsSettled(Control captured, bool obscured) =>
        !obscured && captured.Focusable && captured.IsEffectivelyEnabled;

    /// <summary>
    /// True if <paramref name="element"/> is detached, invisible anywhere in its ancestor
    /// chain, or clipped out by the CUMULATIVE intersection of every clipping ancestor's
    /// viewport — <c>IsEffectivelyVisible</c> alone does not see the clipping case (a
    /// scrolled-away row stays "visible"), and testing each clipper INDEPENDENTLY is not
    /// equivalent to testing against their intersection: an element can overlap clipper A in
    /// one part of itself and clipper B in a disjoint part, passing both checks separately,
    /// while A∩B (what is actually visible through both at once) doesn't overlap it at all.
    /// So every clipper's own viewport is transformed into ONE common space (the visual root)
    /// and progressively intersected together FIRST, and the element is tested against that
    /// single combined rect.
    /// </summary>
    private static bool IsObscured(Control element)
    {
        if (!element.IsAttachedToVisualTree() || !element.IsEffectivelyVisible)
        {
            return true;
        }

        if (element.GetVisualRoot() is not Visual root)
        {
            return true;
        }

        if (TransformRect(element, new Rect(element.Bounds.Size), root) is not { } elementInRoot)
        {
            return true; // no common coordinate space with its own root
        }

        Rect? visible = null;
        foreach (Visual ancestor in element.GetVisualAncestors())
        {
            if (ancestor is not Control clipper || !clipper.ClipToBounds)
            {
                continue;
            }

            if (TransformRect(clipper, new Rect(clipper.Bounds.Size), root) is not { } clipperInRoot)
            {
                return true; // no common coordinate space with the clipper
            }

            visible = visible is { } current ? current.Intersect(clipperInRoot) : clipperInRoot;
        }

        return visible is { } combined && !elementInRoot.Intersects(combined);
    }

    private static Rect? TransformRect(Visual from, Rect localRect, Visual to)
    {
        Point? topLeft = from.TranslatePoint(new Point(localRect.X, localRect.Y), to);
        Point? bottomRight = from.TranslatePoint(new Point(localRect.Right, localRect.Bottom), to);
        return topLeft is { } tl && bottomRight is { } br ? new Rect(tl, br) : null;
    }

    /// <summary>
    /// Attempts, in order, the resolved direction target, then every other usable descendant
    /// in tree order, then the root itself (granted transient focusability) — the guaranteed
    /// terminal. Every intermediate candidate is validated (attached, unobscured, focusable,
    /// enabled) AND excludes <paramref name="captured"/> itself before <c>Focus()</c> is even
    /// attempted; a candidate whose own <c>Focus()</c> call returns false (it became unusable
    /// in the instant between validation and the call, or the framework refused it for some
    /// other reason) is skipped rather than silently ending the chain there. The terminal step
    /// is never gated behind the same usability check — it is the guaranteed last resort,
    /// spec rev 8's requirement that a silent no-op is forbidden at every step.
    /// </summary>
    private static void FocusFallbackChain(Control root, Control captured, bool enteringCompact)
    {
        Control? resolved = enteringCompact
            ? GetHelpExpander(root)?.GetVisualDescendants().OfType<ToggleButton>().FirstOrDefault()
            : GetRestoreFocusTarget(root);

        if (TryFocus(resolved, captured))
        {
            return;
        }

        foreach (Control candidate in root.GetVisualDescendants().OfType<Control>())
        {
            if (TryFocus(candidate, captured))
            {
                return;
            }
        }

        // Terminal: TopLevel is not focusable by default, so Focusable is granted here ONLY
        // for the hand-off; OnControlLostFocus resets it the moment focus moves on, so no
        // permanent Tab stop is added. Unconditional — never gated behind IsUsable.
        root.Focusable = true;
        root.Focus();
    }

    private static bool TryFocus(Control? candidate, Control captured) =>
        candidate is not null && !ReferenceEquals(candidate, captured) && IsUsable(candidate) && candidate.Focus();

    private static bool IsUsable(Control? control) =>
        control is not null && control.Focusable && control.IsEffectivelyEnabled && !IsObscured(control);

    /// <summary>
    /// Per-control state: mode flag, the coalescing guard, the Bounds subscription, captured
    /// row values (keyed by the owning Grid — root or descendant — since a descendant grid
    /// never gets its own state entry), and the expander's IsExpanded subscription.
    /// </summary>
    private sealed class State
    {
        public bool IsCompact { get; set; }

        /// <summary>
        /// Set the first time <see cref="Evaluate"/> ever runs to completion for this control
        /// (transition or not). Distinguishes "nothing to do, already evaluated" from "nothing
        /// to do YET — this is the very first look at a fresh instance", so a fresh instance
        /// that starts (and stays) at normal height still gets one establishing pass instead of
        /// being short-circuited by the "no mode change" early-return before anything (e.g. the
        /// Help expander) is ever synchronized to that mode.
        /// </summary>
        public bool Established { get; set; }

        /// <summary>
        /// Bumped on every actual transition. A deferred focus-recovery job captures the
        /// generation at post time and rejects itself if this no longer matches when it
        /// finally runs — defense against a later transition (or, checked separately, a mode
        /// flip or an intervening focus move) invalidating the job's premise before it runs.
        /// </summary>
        public int Generation { get; set; }

        public bool UpdateQueued { get; set; }

        public bool LifecycleHooked { get; set; }

        public EventHandler<AvaloniaPropertyChangedEventArgs>? BoundsHandler { get; set; }

        public Dictionary<(Control Grid, int RowIndex), double> CapturedDragHeight { get; } = [];

        public Dictionary<(Control Grid, int RowIndex), double> CapturedMinHeight { get; } = [];

        public EventHandler<AvaloniaPropertyChangedEventArgs>? ExpanderIsExpandedHandler { get; set; }
    }
}
