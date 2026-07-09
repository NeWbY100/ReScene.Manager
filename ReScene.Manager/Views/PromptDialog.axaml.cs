using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace ReScene.Manager.Views;

/// <summary>
/// Single-line text-input dialog (Avalonia port of the WPF <c>PromptWindow</c>). Returns the entered
/// text via <c>ShowDialog&lt;string?&gt;</c> on OK, or <see langword="null"/> on Cancel / window close.
/// </summary>
public partial class PromptDialog : Window
{
    private readonly TextBlock _message;
    private readonly TextBox _input;

    /// <summary>Parameterless constructor for the XAML designer / loader only.</summary>
    public PromptDialog()
        : this("Input", string.Empty, string.Empty)
    {
    }

    public PromptDialog(string title, string message, string initialValue)
    {
        AvaloniaXamlLoader.Load(this);

        _message = this.FindControl<TextBlock>("MessageBlock")!;
        _input = this.FindControl<TextBox>("InputBox")!;

        Title = string.IsNullOrEmpty(title) ? "Input" : title;
        _message.Text = message;
        _input.Text = initialValue;

        Opened += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(_input.Text ?? string.Empty);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
