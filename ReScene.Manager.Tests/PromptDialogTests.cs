using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ReScene.Manager.Views;

namespace ReScene.Manager.Tests;

/// <summary>Headless render tests for <see cref="PromptDialog"/> (the Avalonia port of WPF's PromptWindow).</summary>
public class PromptDialogTests
{
    [AvaloniaFact]
    public void Constructs_SeedsMessageTitleAndInput_AndRenders()
    {
        var dialog = new PromptDialog("Rename", "Enter a new name:", "original.srr");
        dialog.Show();

        TextBlock message = dialog.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "MessageBlock");
        TextBox input = dialog.GetVisualDescendants().OfType<TextBox>().First(t => t.Name == "InputBox");

        Assert.Equal("Rename", dialog.Title);
        Assert.Equal("Enter a new name:", message.Text);
        Assert.Equal("original.srr", input.Text);
    }

    [AvaloniaFact]
    public void HasOkAndCancelButtons()
    {
        var dialog = new PromptDialog("Input", "Value?", string.Empty);
        dialog.Show();

        Button[] buttons = [.. dialog.GetVisualDescendants().OfType<Button>()];

        Assert.Contains(buttons, b => b.Name == "OkButton");
        Assert.Contains(buttons, b => b.Name == "CancelButton");
    }
}
