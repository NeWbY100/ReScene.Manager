using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using ReScene.App.Core.Models;
using ReScene.Manager.Controls;

namespace ReScene.Manager.Tests;

public class FieldStatusLineTests
{
    private static (FieldStatusLine Control, Grid Root) ShowInWindow(FieldStatus status)
    {
        var control = new FieldStatusLine { Status = status };
        var window = new Window { Content = control };
        window.Show();

        Grid grid = control.GetVisualDescendants().OfType<Grid>().Single();
        return (control, grid);
    }

    [AvaloniaFact]
    public void Ok_ShowsCheckGlyph_AndIsVisible()
    {
        (FieldStatusLine control, Grid grid) = ShowInWindow(FieldStatus.Ok("Found 3 volumes"));

        TextBlock glyph = control.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "Glyph");

        Assert.True(grid.IsVisible);
        Assert.Equal("✓", glyph.Text);
        Assert.Equal(Color.Parse("#FF1ABC9C"), ((ISolidColorBrush)glyph.Foreground!).Color); // AccentSuccess
    }

    [AvaloniaFact]
    public void Error_ShowsCrossGlyph_AndErrorColoredMessage()
    {
        (FieldStatusLine control, Grid grid) = ShowInWindow(FieldStatus.Error("Missing volume"));

        TextBlock glyph = control.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "Glyph");
        TextBlock message = control.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name != "Glyph");

        Assert.True(grid.IsVisible);
        Assert.Equal("✗", glyph.Text);
        Assert.Equal("Missing volume", message.Text);
        Assert.Equal(Color.Parse("#FFF44747"), ((ISolidColorBrush)glyph.Foreground!).Color); // AccentError
        Assert.Equal(Color.Parse("#FFF44747"), ((ISolidColorBrush)message.Foreground!).Color); // AccentError
    }

    [AvaloniaFact]
    public void Info_MessageUsesSecondaryForeground_NotAccentColor()
    {
        (_, _) = ShowInWindow(FieldStatus.None); // warm up a window/app instance first
        (FieldStatusLine control, _) = ShowInWindow(FieldStatus.Info("fyi"));

        TextBlock message = control.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name != "Glyph");

        Assert.Equal(Color.Parse("#FFAAAAAA"), ((ISolidColorBrush)message.Foreground!).Color); // ForegroundSecondary (a11y-bumped #AAAAAA)
    }

    [AvaloniaFact]
    public void None_RendersNothing_ButStaysInTheTree()
    {
        (FieldStatusLine control, Grid grid) = ShowInWindow(FieldStatus.None);

        AssertRendersNothing(control, grid);
    }

    [AvaloniaFact]
    public void DefaultStatus_IsNone_AndRendersNothing()
    {
        var control = new FieldStatusLine();
        var window = new Window { Content = control };
        window.Show();

        Grid grid = control.GetVisualDescendants().OfType<Grid>().Single();

        Assert.Equal(FieldState.None, control.Status?.State);
        AssertRendersNothing(control, grid);
    }

    /// <summary>
    /// An idle line shows nothing — but by having nothing to show, NOT by being hidden. These two
    /// tests used to assert <c>IsVisible == false</c>, which was the old mechanism and also the bug:
    /// a hidden subtree has no automation nodes, so the message's live region could not announce the
    /// first status a field produced. The visible outcome is unchanged, which is what these assert;
    /// the announcement is covered by <see cref="FieldStatusAnnouncementTests"/>.
    /// </summary>
    private static void AssertRendersNothing(FieldStatusLine control, Grid grid)
    {
        Assert.True(grid.IsVisible,
            "the status row is hidden again — its live message then has no automation node, and the first status " +
            "a field produces cannot be announced");

        foreach (TextBlock text in control.GetVisualDescendants().OfType<TextBlock>())
        {
            Assert.True(string.IsNullOrEmpty(text.Text),
                $"an idle status line renders \"{text.Text}\", so it is not idle to look at");
        }
    }
}
