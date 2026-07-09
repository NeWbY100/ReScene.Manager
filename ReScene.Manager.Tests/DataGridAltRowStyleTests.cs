using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ReScene.Manager.Tests;

/// <summary>
/// Verifies the global alternating-row tint added to <c>Resources/Styles.axaml</c> — the replacement
/// for WPF's dropped <c>DataGrid.AlternatingRowBackground</c>. Confirms the
/// <c>DataGridRow:nth-child(even)</c> selector actually matches even rows in Avalonia 11.3.13 by
/// hosting a small read-only grid headless and asserting even (1-based) rows resolve the tint brush
/// (<c>SystemControlBackgroundListLowBrush</c> = #FF2D2D30) while odd rows do not. Selection/hover/diff
/// interactions (the <c>:not(:selected):not(:pointerover)</c> exclusions and the Compare grids'
/// cell-level diff tint that renders on top) are pure-visual states and are launch-smoke.
/// </summary>
public class DataGridAltRowStyleTests
{
    private static Color AltRowBackground => Color.Parse("#FF2D2D30");

    private sealed record Item(string Name);

    [AvaloniaFact]
    public void EvenRows_ResolveTheAlternatingTint_OddRowsDoNot()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            Height = 300,
            ItemsSource = new[] { new Item("a"), new Item("b"), new Item("c"), new Item("d") },
        };
        grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Avalonia.Data.Binding("Name") });

        using var sink = new BindingErrorSink();
        var window = new Window { Width = 400, Height = 320, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Rows come out of the pre-order visual walk in sibling (child) order — exactly what
        // :nth-child counts over. 1-based even positions (2nd, 4th) should carry the tint.
        DataGridRow[] rows = [.. window.GetVisualDescendants().OfType<DataGridRow>()];
        Assert.Equal(4, rows.Length);

        for (int i = 0; i < rows.Length; i++)
        {
            bool isNthChildEven = (i + 1) % 2 == 0;
            Color? background = (rows[i].Background as ISolidColorBrush)?.Color;
            if (isNthChildEven)
            {
                Assert.Equal(AltRowBackground, background);
            }
            else
            {
                Assert.NotEqual(AltRowBackground, background);
            }
        }

        Assert.Empty(sink.Messages);
    }
}
