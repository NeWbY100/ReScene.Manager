using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.Manager.Behaviors;

namespace ReScene.Manager.Tests;

/// <summary>
/// Unit tests for <see cref="ScrollHandoffBehavior"/> in isolation: a bare DataGrid (20 rows, two
/// columns — one a directly-interactive CheckBox template column, matching every production usage
/// this behavior targets) hosted inside an outer <see cref="ScrollViewer"/> sized to clip BOTH the
/// grid's own content and the grid itself. GENUINE INPUT ONLY throughout (headless
/// <c>window.MouseWheel</c>/real Tab-less arrow-key presses) — never a synthetic Offset poke or a
/// direct <c>BringIntoView()</c> call from the test itself.
/// <para>
/// Two empirically-verified, independent mechanisms (see <see cref="ScrollHandoffBehavior"/>'s own
/// remarks for the decompiled/spiked evidence): (a) WHEEL — <see cref="DataGrid"/>'s own
/// <c>OnPointerWheelChanged</c> class handler already leaves the event unhandled whenever it
/// cannot consume the gesture internally (confirmed: <c>ScrollViewer.IsScrollChainingEnabled</c>
/// defaults to <c>true</c> and is never overridden in this app), so this behavior's plain
/// (non-handledEventsToo) instance handler on the SAME element is reached only in exactly that
/// at-extent case; (b) KEYBOARD/FOCUS — arrow-key navigation never calls the framework's
/// <c>BringIntoView</c> on anything during ordinary (non-edit) browsing (confirmed by
/// decompilation: every <c>DataGrid</c>/<c>DataGridCell</c> Focus() call targets either the grid
/// itself or an editing cell, never a plain browsing row/cell) — this behavior explicitly calls it
/// on the newly-current row via the public <c>CurrentCellChanged</c> event.
/// </para>
/// </summary>
public class ScrollHandoffBehaviorTests
{
    private sealed class Row
    {
        public bool Selected { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Builds the rig: a 20-row DataGrid (MinHeight/MaxHeight fixed at 250, mirroring
    /// SampleRestorerView's own SRSEntriesGrid) inside an outer ScrollViewer whose own viewport
    /// (via the hosting Window's height) is shorter than the grid's rendered height, and whose
    /// own content additionally exceeds that viewport below the grid — so BOTH the grid's own
    /// internal virtualized scroll AND the outer's own scroll have genuine room to move.
    /// <c>behaviors:ScrollHandoffBehavior.Handoff="True"</c> is applied unless
    /// <paramref name="handoffEnabled"/> is false (used by the negative/disclosure cases below).
    /// </summary>
    private static (Window Window, DataGrid Grid, ScrollViewer Outer) Build(int rowCount = 20, bool handoffEnabled = true)
    {
        var items = new List<Row>();
        for (int i = 0; i < rowCount; i++)
        {
            items.Add(new Row { Name = $"row{i}" });
        }

        var grid = new DataGrid
        {
            Name = "TestGrid",
            ItemsSource = items,
            AutoGenerateColumns = false,
            IsReadOnly = false,
            MinHeight = 250,
            MaxHeight = 250,
            Columns =
            {
                new DataGridTemplateColumn
                {
                    Header = string.Empty,
                    Width = new DataGridLength(30),
                    CellTemplate = new FuncDataTemplate<Row>(static (_, _) =>
                        new CheckBox { [!CheckBox.IsCheckedProperty] = new Binding(nameof(Row.Selected)) }),
                },
                new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(Row.Name)) },
            },
        };

        if (handoffEnabled)
        {
            ScrollHandoffBehavior.SetHandoff(grid, true);
        }

        var outer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Children =
                {
                    new Border { Height = 100 }, // content ABOVE the grid: room to scroll up from mid-grid
                    grid,
                    new Border { Height = 100 }, // content BELOW the grid: room to scroll down past it
                },
            },
        };

        // Panel-content layout: border-above [0,100), grid [100,350), border-below [350,450) —
        // total extent 450, outer offset range [0,300]. The viewport (150, via the window's
        // height) is deliberately SHORTER than the grid's own 250-DIP height: unlike an earlier
        // version of this rig (300-tall viewport), a viewport taller than the grid lets
        // ScrollViewer's own native BringIntoViewOnFocusChange jump (see this file's own remarks
        // on Keyboard_WithHandoffDisabled_ArrowKeyNavigationDoesNotChainToOuter) reveal the WHOLE
        // grid in one shot on the very first arrow-down's focus transition, making every row
        // trivially visible without this behavior ever mattering — silently non-discriminating.
        // With the viewport shorter than the grid, that native jump can only ever reveal PART of
        // it, so continued navigation genuinely depends on this behavior to keep reaching deeper
        // rows.
        var window = new Window { Width = 400, Height = 150, Content = outer };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, grid, outer);
    }

    private static ScrollBar GridVerticalScrollBar(DataGrid grid) =>
        grid.GetVisualDescendants().OfType<ScrollBar>().First(b => b.Orientation == Orientation.Vertical);

    /// <summary>
    /// A point guaranteed to land ON <paramref name="element"/> AND within the window's own
    /// visible bounds — the center of <paramref name="element"/>'s full bounds is NOT safe to use
    /// unconditionally here, because the outer ScrollViewer can legitimately have the grid
    /// scrolled such that its geometric center sits outside the (deliberately short) window
    /// entirely, in which case headless <c>MouseWheel</c>/hit-testing would silently land on
    /// nothing rather than the grid. Recomputed fresh at every call site (never cached across a
    /// loop or an offset change) since scrolling moves the element's on-screen position.
    /// </summary>
    private static Point VisibleCenterInWindow(Control element, Window window)
    {
        Point? topLeft = element.TranslatePoint(new Point(0, 0), window);
        Point? bottomRight = element.TranslatePoint(new Point(element.Bounds.Width, element.Bounds.Height), window);
        if (topLeft is not { } tl || bottomRight is not { } br)
        {
            throw new InvalidOperationException($"{element.GetType().Name} could not be translated into window coordinates.");
        }

        var windowBounds = new Rect(window.Bounds.Size);
        Rect elementInWindow = new Rect(tl, br);
        Rect visible = elementInWindow.Intersect(windowBounds);
        if (visible.Width <= 0 || visible.Height <= 0)
        {
            throw new InvalidOperationException(
                $"{element.GetType().Name}'s bounds ({elementInWindow}) do not intersect the window's own bounds ({windowBounds}) at all — this test's scroll setup left it fully off-screen.");
        }

        return new Point(visible.X + (visible.Width / 2), visible.Y + (visible.Height / 2));
    }

    /// <summary>Drives the grid's OWN internal scroll to its genuine top or bottom extent via real wheel ticks.
    /// Recomputes the wheel point on every tick — defensive, even though the grid's own screen position should
    /// not move during a pure internal-scroll drive (only its rendered ROWS change, never its own bounds).</summary>
    private static void DriveGridToOwnExtent(Window window, DataGrid grid, ScrollBar gridBar, bool toBottom)
    {
        double direction = toBottom ? -1 : 1;
        for (int i = 0; i < 200 && (toBottom ? gridBar.Value < gridBar.Maximum : gridBar.Value > gridBar.Minimum); i++)
        {
            window.MouseWheel(VisibleCenterInWindow(grid, window), new Vector(0, direction));
            Dispatcher.UIThread.RunJobs();
        }
    }

    // ── (a) WHEEL ────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Wheel_AtGridBottomExtent_MovesOuterOffsetDown_GridOffsetUnchanged()
    {
        (Window window, DataGrid grid, ScrollViewer outer) = Build();
        try
        {
            // Drive the grid to its own bottom extent while a genuine, on-screen slice of it is
            // visible (offset 75 intersects the grid's own top ~125px against the 150-DIP
            // viewport), THEN move the outer to its own top (0) — still a valid, non-empty
            // intersection with the grid's own top ~50px — so the outer has genuine room to grow
            // downward for the actual test action below.
            ScrollBar gridBar = GridVerticalScrollBar(grid);
            outer.Offset = new Vector(0, 75);
            Dispatcher.UIThread.RunJobs();
            DriveGridToOwnExtent(window, grid, gridBar, toBottom: true);
            Assert.True(gridBar.Value >= gridBar.Maximum, "test precondition: grid must genuinely be at its own bottom extent");

            outer.Offset = new Vector(0, 0);
            Dispatcher.UIThread.RunJobs();
            Assert.True(outer.Extent.Height > outer.Viewport.Height, "test precondition: outer must have room to scroll");

            double gridBefore = gridBar.Value;
            double outerBefore = outer.Offset.Y;

            Point at = VisibleCenterInWindow(grid, window);
            window.MouseWheel(at, new Vector(0, -1)); // wheel DOWN, grid already exhausted downward
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(gridBefore, gridBar.Value); // grid did not move further (nothing left to give)
            Assert.True(outer.Offset.Y > outerBefore, $"outer offset should have increased from {outerBefore}, was {outer.Offset.Y}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Wheel_AtGridTopExtent_MovesOuterOffsetUp_GridOffsetUnchanged()
    {
        (Window window, DataGrid grid, ScrollViewer outer) = Build();
        try
        {
            ScrollBar gridBar = GridVerticalScrollBar(grid);
            // Grid starts fresh at its own top (offset 0) — no need to drive it there.
            Assert.Equal(0, gridBar.Value);

            // Offset 75 intersects a genuine, non-empty slice of the grid (the viewport is
            // shorter than the grid, so no single offset ever shows all of it at once — see
            // Build()'s own remarks) while still leaving the outer well short of its own top (0),
            // so wheeling up has somewhere real to go.
            outer.Offset = new Vector(0, 75);
            Dispatcher.UIThread.RunJobs();
            double outerBefore = outer.Offset.Y;
            Assert.True(outerBefore > 0, "test precondition: outer must start with room to scroll up");

            Point at = VisibleCenterInWindow(grid, window);
            window.MouseWheel(at, new Vector(0, 1)); // wheel UP, grid already at its own top
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(0, gridBar.Value); // grid stayed at its own top
            Assert.True(outer.Offset.Y < outerBefore, $"outer offset should have decreased from {outerBefore}, was {outer.Offset.Y}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Wheel_MidGrid_MovesOnlyInnerOffset_OuterOffsetUntouched()
    {
        (Window window, DataGrid grid, ScrollViewer outer) = Build();
        try
        {
            ScrollBar gridBar = GridVerticalScrollBar(grid);
            outer.Offset = new Vector(0, 75); // outer mid-range, a genuine slice of the grid visible
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, gridBar.Value); // grid fresh — far from either of its own extents

            double outerBefore = outer.Offset.Y;

            Point at = VisibleCenterInWindow(grid, window);
            window.MouseWheel(at, new Vector(0, -1)); // wheel DOWN — grid has plenty of its own room
            Dispatcher.UIThread.RunJobs();

            Assert.True(gridBar.Value > 0, "grid's own internal offset should have moved");
            Assert.Equal(outerBefore, outer.Offset.Y); // outer must be completely untouched
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Wheel_AtBothGridAndOuterExtent_NoRoomAnywhere_NoExceptionNoChange()
    {
        (Window window, DataGrid grid, ScrollViewer outer) = Build();
        try
        {
            ScrollBar gridBar = GridVerticalScrollBar(grid);
            outer.Offset = new Vector(0, 75);
            Dispatcher.UIThread.RunJobs();
            DriveGridToOwnExtent(window, grid, gridBar, toBottom: true);
            outer.Offset = new Vector(0, outer.Extent.Height - outer.Viewport.Height); // outer ALSO at its own bottom
            Dispatcher.UIThread.RunJobs();

            double gridBefore = gridBar.Value;
            double outerBefore = outer.Offset.Y;

            Point at = VisibleCenterInWindow(grid, window); // a genuine slice of the grid's own bottom is still visible at outer's max
            Exception? thrown = Record.Exception(() =>
            {
                window.MouseWheel(at, new Vector(0, -1));
                Dispatcher.UIThread.RunJobs();
            });

            Assert.Null(thrown);
            Assert.Equal(gridBefore, gridBar.Value);
            Assert.Equal(outerBefore, outer.Offset.Y);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// Honest disclosure (claim hygiene), not a hidden gap: with <c>Handoff</c> left OFF, wheeling
    /// at the grid's own extent STILL moves the outer — because Avalonia's <see cref="DataGrid"/>
    /// already leaves the event unhandled at-extent (see this file's own remarks:
    /// <c>ScrollViewer.IsScrollChainingEnabled</c> defaults to <c>true</c>), so the framework's own
    /// bubble-to-ancestor-ScrollViewer chaining already produces the SAME externally-observable
    /// result with zero custom code. This behavior's wheel half is therefore not independently
    /// discriminating in THIS exact configuration — its value is making the hand-off explicit,
    /// named, and tested rather than an unannounced dependency on a framework default that could
    /// silently change (a future style setting <c>IsScrollChainingEnabled="False"</c> somewhere, or
    /// an Avalonia upgrade). Contrast with
    /// <see cref="Keyboard_WithHandoffDisabled_ArrowKeyNavigationDoesNotChainToOuter"/> below, where
    /// disabling <c>Handoff</c> DOES change the observable outcome — that is the mechanism this
    /// behavior is solely responsible for.
    /// <para>
    /// RE-VERIFIED comprehensively (fix round 1, codex finding 5), beyond this one scenario: with
    /// <see cref="ScrollHandoffBehavior"/>'s wheel registration itself temporarily removed (not
    /// just <c>Handoff</c> set false on one grid instance), every other dedicated wheel test in
    /// this file (bottom extent, top extent, mid-grid, both extents) AND the real,
    /// production-wired <c>SampleRestorerCompactTests.Handoff_WheelAtGridExtent_MovesConfigBandScroller</c>
    /// still passed unchanged. See <see cref="ScrollHandoffBehavior"/>'s own class remarks for the
    /// full disclosure — this is a genuinely redundant-today, kept-for-explicitness mechanism, not
    /// a hidden defect.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void Wheel_WithHandoffDisabled_StillChainsToOuter_ViaAvaloniasOwnDefaultScrollChaining()
    {
        (Window window, DataGrid grid, ScrollViewer outer) = Build(handoffEnabled: false);
        try
        {
            ScrollBar gridBar = GridVerticalScrollBar(grid);
            DriveGridToOwnExtent(window, grid, gridBar, toBottom: true);
            outer.Offset = new Vector(0, 0);
            Dispatcher.UIThread.RunJobs();
            double outerBefore = outer.Offset.Y;

            Point at = VisibleCenterInWindow(grid, window);
            window.MouseWheel(at, new Vector(0, -1));
            Dispatcher.UIThread.RunJobs();

            Assert.True(outer.Offset.Y > outerBefore,
                "Avalonia's own default IsScrollChainingEnabled=true should already chain this wheel gesture to the outer ScrollViewer, with or without ScrollHandoffBehavior");
        }
        finally { window.Close(); }
    }

    // ── (b) KEYBOARD / FOCUS ─────────────────────────────────────────

    /// <summary>
    /// The brief's own scenario: focus the first row's checkbox, then press real Down-arrow keys
    /// until the CURRENT row is one that is already realized within the grid's own 250-DIP
    /// viewport (no internal grid scroll needed for it — "inner arrow-key navigation stays inside
    /// the grid" per the brief) but whose absolute position is below the OUTER's own 150-DIP
    /// viewport. The current row's bounds — not <c>FocusManager.GetFocusedElement()</c> — are the
    /// meaningful target: DataGrid's own arrow-key handling ends by re-focusing the DataGrid
    /// ITSELF (see this file's own remarks and ScrollHandoffBehavior's), never a specific
    /// cell/row, so "the focused cell" is asserted here as "the current row, fully visible" —
    /// the only sense in which a specific row is genuinely trackable through this control.
    /// </summary>
    [AvaloniaFact]
    public void Keyboard_ArrowDownPastOuterViewport_ChainsBringIntoView_CurrentRowEndsFullyVisible()
    {
        (Window window, DataGrid grid, ScrollViewer outer) = Build();
        try
        {
            CheckBox firstCheckbox = window.GetVisualDescendants().OfType<CheckBox>().First();
            firstCheckbox.Focus();
            Dispatcher.UIThread.RunJobs();

            outer.Offset = new Vector(0, 0);
            Dispatcher.UIThread.RunJobs();

            // Five rows down is comfortably inside the grid's own 250-DIP realized viewport
            // (rows are ~30 DIPs tall with default DataGrid chrome) but well past the outer's
            // 150-DIP window.
            for (int i = 0; i < 5; i++)
            {
                window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(5, grid.SelectedIndex);
            ScrollBar gridBar = GridVerticalScrollBar(grid);
            Assert.Equal(0, gridBar.Value); // confirms "inner arrow-key navigation stays inside the grid" for this row

            DataGridRow currentRow = window.GetVisualDescendants().OfType<DataGridRow>().Single(r => r.Index == grid.SelectedIndex);
            AssertFullyWithinWindow(currentRow, window);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The genuinely discriminating negative case for the keyboard mechanism (contrast with
    /// <see cref="Wheel_WithHandoffDisabled_StillChainsToOuter_ViaAvaloniasOwnDefaultScrollChaining"/>):
    /// Avalonia's <c>KeyboardNavigationHandler</c> never calls <c>BringIntoView</c> on anything
    /// (confirmed by decompilation), so with <c>Handoff</c> left OFF, the outer never continues to
    /// track the CURRENT row as arrow-key navigation moves deeper into the grid.
    /// <para>
    /// NOT asserted here: "the outer's offset never changes at all". A SEPARATE, genuine Avalonia
    /// mechanism — <c>ScrollViewer.OnGotFocus</c>, gated by <c>BringIntoViewOnFocusChange</c>
    /// (defaults <c>true</c>, confirmed by decompilation) — fires once, independently of
    /// <c>Handoff</c>, on the FIRST arrow-down: literal keyboard focus transitions from the
    /// checkbox to the DataGrid itself at that point (see this file's own class remarks), which is
    /// a genuinely NEW <c>GotFocus</c> bubbling to the outer, so the outer legitimately jumps once
    /// to show as much of the newly-focused DataGrid as it can. That one-time, focus-transition
    /// jump is real, expected, and unrelated to this behavior — asserting it away would make this
    /// test assert something false. The MEANINGFUL, discriminating difference this behavior alone
    /// is responsible for is that WITHOUT it, that single jump is never followed up as navigation
    /// continues, so the current row (5 steps in) ends up genuinely not fully visible regardless.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void Keyboard_WithHandoffDisabled_ArrowKeyNavigationDoesNotChainToOuter()
    {
        (Window window, DataGrid grid, ScrollViewer outer) = Build(handoffEnabled: false);
        try
        {
            CheckBox firstCheckbox = window.GetVisualDescendants().OfType<CheckBox>().First();
            firstCheckbox.Focus();
            Dispatcher.UIThread.RunJobs();
            outer.Offset = new Vector(0, 0);
            Dispatcher.UIThread.RunJobs();

            for (int i = 0; i < 5; i++)
            {
                window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(5, grid.SelectedIndex);

            DataGridRow currentRow = window.GetVisualDescendants().OfType<DataGridRow>().Single(r => r.Index == grid.SelectedIndex);
            Assert.False(IsFullyWithinWindow(currentRow, window),
                "without ScrollHandoffBehavior, continued arrow-key navigation must NOT keep the current row fully visible " +
                "(a one-time native focus-transition jump on the first arrow-down is expected and is not what this asserts against)");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Keyboard_ArrowUpBackTowardTop_AlsoChainsBringIntoView()
    {
        (Window window, DataGrid grid, ScrollViewer outer) = Build();
        try
        {
            CheckBox firstCheckbox = window.GetVisualDescendants().OfType<CheckBox>().First();
            firstCheckbox.Focus();
            Dispatcher.UIThread.RunJobs();

            // Walk down first (scrolls the outer down to follow), then scroll the OUTER away
            // (simulating the user having since scrolled elsewhere) before walking back up —
            // the up-walk must re-chain independently of the prior down-walk's own scroll.
            for (int i = 0; i < 6; i++)
            {
                window.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
            }

            outer.Offset = new Vector(0, outer.Extent.Height - outer.Viewport.Height); // scroll far down, away from row 6
            Dispatcher.UIThread.RunJobs();

            for (int i = 0; i < 4; i++)
            {
                window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Equal(2, grid.SelectedIndex);
            DataGridRow currentRow = window.GetVisualDescendants().OfType<DataGridRow>().Single(r => r.Index == grid.SelectedIndex);
            AssertFullyWithinWindow(currentRow, window);
        }
        finally { window.Close(); }
    }

    // ── Shared containment assertion (mirrors CompactViewRig's own IsFullyVisibleWithinWindow shape) ──

    private static bool IsFullyWithinWindow(Control control, Window window)
    {
        if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
        {
            return false;
        }

        Point? topLeft = control.TranslatePoint(new Point(0, 0), window);
        Point? bottomRight = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window);
        if (topLeft is not { } tl || bottomRight is not { } br)
        {
            return false;
        }

        const double Slack = 0.5;
        Rect windowBounds = new(window.Bounds.Size);
        return tl.X >= windowBounds.X - Slack && tl.Y >= windowBounds.Y - Slack
            && br.X <= windowBounds.Right + Slack && br.Y <= windowBounds.Bottom + Slack;
    }

    private static void AssertFullyWithinWindow(Control control, Window window) =>
        Assert.True(IsFullyWithinWindow(control, window),
            $"{control.GetType().Name} (bounds {control.Bounds}) is not fully within the window (bounds {window.Bounds}).");
}
