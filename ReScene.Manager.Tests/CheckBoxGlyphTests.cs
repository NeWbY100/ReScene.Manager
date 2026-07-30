using Avalonia;
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

            // And the shrunken cell must CENTER in the row: Fluent top-aligns its glyph cell
            // (fine when the 20px cell filled the row), which beaches a 14px box ~3px above
            // the label's centerline — user-reported against the Output tab.
            Avalonia.Point boxCenter = box.TranslatePoint(new Avalonia.Point(0, box.Bounds.Height / 2), cb)!.Value;
            Assert.True(Math.Abs(boxCenter.Y - (cb.Bounds.Height / 2)) <= 1.0,
                $"glyph center y={boxCenter.Y:F1} vs row center y={cb.Bounds.Height / 2:F1} — the shrunken cell must vertically center in the row");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void VersionsDensityRow_GlyphCentersWithOnePixelShift()
    {
        // The versions tree's scoped MinHeight=16 leaves (16-14)/2 = 1px of slack: centering
        // moves those glyphs DOWN exactly 1px vs the old top alignment — assert the shift is
        // exactly that, not "unchanged" (a11y review correction).
        var cb = new CheckBox { Content = "3.00 b1", MinHeight = 16 };
        var window = new Window { Width = 300, Height = 100, Content = new StackPanel { Children = { cb } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Border box = GlyphBox(cb);
            Avalonia.Point boxTop = box.TranslatePoint(default, cb)!.Value;
            Assert.Equal(1, boxTop.Y, 1);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void WrapLabelClass_RestoresTopAlignment()
    {
        // Forward-compat opt-out for a future WRAPPING label (magnification: the box must stay
        // co-visible with the label's first line): .wrapLabel reverts the cell to top-aligned.
        var cb = new CheckBox { Content = "future wrapping label" };
        cb.Classes.Add("wrapLabel");
        var window = new Window { Width = 300, Height = 100, Content = new StackPanel { Children = { cb } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            Border box = GlyphBox(cb);
            Avalonia.Point boxTop = box.TranslatePoint(default, cb)!.Value;
            Assert.Equal(0, boxTop.Y, 1);
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
