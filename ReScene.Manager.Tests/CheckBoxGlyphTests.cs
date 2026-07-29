using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ReScene.Manager.Tests;

/// <summary>
/// Pins the app-wide 14x14 checkbox glyph (<c>CheckBoxGlyphSize</c> token) and its a11y
/// contract: the VISUAL box shrinks to the v1.9 look, but the pointer target — the labeled
/// row — keeps <c>CheckBoxMinHeight</c> (20). Editable DataGrid checkbox cells are excluded
/// (no label there, so the glyph IS the target): they keep Fluent's 20x20.
/// </summary>
public class CheckBoxGlyphTests
{
    private static Border GlyphBox(CheckBox cb) =>
        cb.GetVisualDescendants().OfType<Border>().First(b => b.Name == "NormalRectangle");

    [AvaloniaFact]
    public void LabeledCheckBox_Glyph14_RowKeepsMinHeight20()
    {
        var cb = new CheckBox { Content = "Recurse subdirectories" };
        var window = new Window { Width = 400, Height = 120, Content = new StackPanel { Children = { cb } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Border box = GlyphBox(cb);
            Assert.Equal(14, box.Bounds.Width, 3);
            Assert.Equal(14, box.Bounds.Height, 3);

            Viewbox glyph = cb.GetVisualDescendants().OfType<Viewbox>().First();
            Assert.Equal(14, glyph.Bounds.Height, 3);

            // The a11y contract behind the shrink: the labeled row (the pointer target) must
            // not follow the box down — height = max(CheckBoxMinHeight, content).
            Assert.True(cb.Bounds.Height >= 19.99,
                $"labeled row measured {cb.Bounds.Height:F1}px — the 14px glyph must not pull the target under CheckBoxMinHeight (20)");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DataGridCell_FullSizeGlyphClass_OptsOutOfShrink()
    {
        // Mirrors SampleRestorerView's authored column: unlabeled editable cell, so the glyph
        // IS the pointer target — Classes="fullSizeGlyph" keeps Fluent's 20x20 there.
        var optOut = new DataGridTemplateColumn
        {
            Header = "",
            Width = new DataGridLength(30),
            CellTemplate = new FuncDataTemplate<Row>((_, _) =>
            {
                var cb = new CheckBox();
                cb.Classes.Add("fullSizeGlyph");
                cb.Bind(CheckBox.IsCheckedProperty, new Avalonia.Data.Binding(nameof(Row.IsSelected)));
                return cb;
            }),
        };
        var grid = new DataGrid
        {
            ItemsSource = new[] { new Row { IsSelected = true }, new Row() },
            AutoGenerateColumns = false,
        };
        grid.Columns.Add(optOut);
        // The landmine the class exists for: a RAW DataGridCheckBoxColumn cell has no class
        // and follows the app-wide shrink — which is exactly why SampleRestorerView authors
        // its column instead of using DataGridCheckBoxColumn.
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "raw",
            Binding = new Avalonia.Data.Binding(nameof(Row.IsSelected)),
            Width = new DataGridLength(30),
        });
        var window = new Window { Width = 300, Height = 200, Content = grid };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            CheckBox[] cells = [.. window.GetVisualDescendants().OfType<CheckBox>()];
            CheckBox classed = cells.First(c => c.Classes.Contains("fullSizeGlyph"));
            CheckBox raw = cells.First(c => !c.Classes.Contains("fullSizeGlyph"));

            Border classedBox = GlyphBox(classed);
            Assert.Equal(20, classedBox.Bounds.Width, 3);
            Assert.Equal(20, classedBox.Bounds.Height, 3);

            Border rawBox = GlyphBox(raw);
            Assert.Equal(14, rawBox.Bounds.Width, 3);
            Assert.Equal(14, rawBox.Bounds.Height, 3);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class Row
    {
        public bool IsSelected { get; set; }
    }
}
