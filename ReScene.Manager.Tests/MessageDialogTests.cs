using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using ReScene.Manager.Views;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for <see cref="MessageDialog"/>. The sync-modal pump is NOT exercised here
/// (it is guarded off without a real UI); these only prove the window constructs, applies its
/// severity glyph/buttons, and renders its visual tree without throwing.
/// </summary>
public class MessageDialogTests
{
    private static TextBlock Glyph(MessageDialog dialog) =>
        dialog.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "Glyph");

    [AvaloniaFact]
    public void Confirm_ShowsCancelButton_AndWarningGlyph()
    {
        var dialog = new MessageDialog(DialogSeverity.Confirm, "Confirm", "Proceed with rebuild?");
        dialog.Show();

        Button cancel = dialog.GetVisualDescendants().OfType<Button>().First(b => b.Name == "CancelButton");
        TextBlock message = dialog.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "MessageBlock");

        Assert.True(cancel.IsVisible);
        Assert.Equal("Proceed with rebuild?", message.Text);
        Assert.Equal("⚠", Glyph(dialog).Text);
        Assert.Equal("Confirm", dialog.Title);
    }

    [AvaloniaFact]
    public void Error_HidesCancelButton_AndUsesErrorGlyphAndColor()
    {
        var dialog = new MessageDialog(DialogSeverity.Error, "Error", "Something failed");
        dialog.Show();

        Button cancel = dialog.GetVisualDescendants().OfType<Button>().First(b => b.Name == "CancelButton");

        Assert.False(cancel.IsVisible);
        Assert.Equal("✗", Glyph(dialog).Text);
        Assert.Equal(Color.Parse("#FFF44747"), ((ISolidColorBrush)Glyph(dialog).Foreground!).Color); // AccentError
    }

    [AvaloniaFact]
    public void Info_UsesInfoGlyph()
    {
        var dialog = new MessageDialog(DialogSeverity.Info, "Info", "Heads up");
        dialog.Show();

        Assert.Equal("ℹ", Glyph(dialog).Text);
    }

    [AvaloniaFact]
    public void Warning_UsesWarningGlyph()
    {
        var dialog = new MessageDialog(DialogSeverity.Warning, "Warning", "Careful");
        dialog.Show();

        Assert.Equal("⚠", Glyph(dialog).Text);
    }
}
