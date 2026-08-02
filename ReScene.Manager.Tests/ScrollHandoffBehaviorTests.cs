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
/// grid's own content and the grid itself. GENUINE INPUT ONLY throughout (real Tab-less arrow-key
/// presses) — never a synthetic Offset poke or a direct <c>BringIntoView()</c> call from the test
/// itself.
/// <para>
/// One mechanism only — KEYBOARD/FOCUS (see <see cref="ScrollHandoffBehavior"/>'s own remarks for
/// the decompiled evidence): arrow-key navigation never calls the framework's
/// <c>BringIntoView</c> on anything during ordinary (non-edit) browsing (confirmed by
/// decompilation: every <c>DataGrid</c>/<c>DataGridCell</c> Focus() call targets either the grid
/// itself or an editing cell, never a plain browsing row/cell) — this behavior explicitly calls it
/// on the newly-current row via the public <c>CurrentCellChanged</c> event.
/// </para>
/// <para>
/// NO WHEEL TESTS (removed): an earlier version of this file also covered a wheel
/// mechanism this behavior once implemented (4 dedicated tests plus a disclosure test). That
/// mechanism was removed outright — see
/// <see cref="ScrollHandoffBehavior"/>'s own remarks for why it was not just redundant with
/// Avalonia's native <c>IsScrollChainingEnabled</c> default but could never have provided the
/// "future insurance" it was kept for either. The platform-level regression guard for wheel
/// handoff (a spec-level user expectation, independent of which code provides it) now lives in the
/// real view's own test:
/// <c>SampleRestorerCompactTests.WheelHandoffAtGridExtent_PlatformDefaultMovesConfigBandScroller</c>.
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
    /// <paramref name="handoffEnabled"/> is false (used by the negative/disclosure case below).
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

    // ── KEYBOARD / FOCUS ─────────────────────────────────────────

    /// <summary>
    /// Focus the first row's checkbox, then press real Down-arrow keys
    /// until the CURRENT row is one that is already realized within the grid's own 250-DIP
    /// viewport (no internal grid scroll needed for it — inner arrow-key navigation stays inside
    /// the grid) but whose absolute position is below the OUTER's own 150-DIP
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
    /// The genuinely discriminating negative case for this behavior's one remaining mechanism
    /// (contrast with the now-removed wheel mechanism's own disclosure test, which showed the
    /// OPPOSITE — disabling <c>Handoff</c> made no observable difference there, see
    /// <see cref="ScrollHandoffBehavior"/>'s own remarks): Avalonia's
    /// <c>KeyboardNavigationHandler</c> never calls <c>BringIntoView</c> on anything (confirmed by
    /// decompilation), so with <c>Handoff</c> left OFF, the outer never continues to track the
    /// CURRENT row as arrow-key navigation moves deeper into the grid.
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
