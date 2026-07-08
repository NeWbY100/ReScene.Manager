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

        Assert.Equal(Color.Parse("#FF9E9E9E"), ((ISolidColorBrush)message.Foreground!).Color); // ForegroundSecondary
    }

    [AvaloniaFact]
    public void None_IsCollapsed()
    {
        (_, Grid grid) = ShowInWindow(FieldStatus.None);

        Assert.False(grid.IsVisible);
    }

    [AvaloniaFact]
    public void DefaultStatus_IsNoneAndCollapsed()
    {
        var control = new FieldStatusLine();
        var window = new Window { Content = control };
        window.Show();

        Grid grid = control.GetVisualDescendants().OfType<Grid>().Single();

        Assert.Equal(FieldState.None, control.Status?.State);
        Assert.False(grid.IsVisible);
    }
}
