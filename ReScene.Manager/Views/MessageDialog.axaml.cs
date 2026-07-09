using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace ReScene.Manager.Views;

/// <summary>
/// Reusable message/confirmation dialog (Avalonia has no MessageBox). Shows a severity glyph, the
/// wrapped message and either an OK button (Info/Warning/Error) or OK + Cancel (Confirm). Returns a
/// <see cref="bool"/> via <c>ShowDialog&lt;bool&gt;</c>: <see langword="true"/> for OK,
/// <see langword="false"/> for Cancel or a window close.
/// </summary>
public partial class MessageDialog : Window
{
    private readonly TextBlock _glyph;
    private readonly TextBlock _message;
    private readonly Button _cancelButton;

    /// <summary>Parameterless constructor for the XAML designer / loader only.</summary>
    public MessageDialog()
        : this(DialogSeverity.Info, "ReScene Manager", string.Empty)
    {
    }

    public MessageDialog(DialogSeverity severity, string title, string message)
    {
        AvaloniaXamlLoader.Load(this);

        _glyph = this.FindControl<TextBlock>("Glyph")!;
        _message = this.FindControl<TextBlock>("MessageBlock")!;
        _cancelButton = this.FindControl<Button>("CancelButton")!;

        Title = string.IsNullOrEmpty(title) ? "ReScene Manager" : title;
        _message.Text = message;

        (string glyph, string brushKey) = SeverityVisual(severity);
        _glyph.Text = glyph;
        if (Application.Current is { } app &&
            app.Resources.TryGetResource(brushKey, ThemeVariant.Default, out object? brush) &&
            brush is IBrush resolved)
        {
            _glyph.Foreground = resolved;
        }

        _cancelButton.IsVisible = severity == DialogSeverity.Confirm;
    }

    private static (string Glyph, string BrushKey) SeverityVisual(DialogSeverity severity) => severity switch
    {
        DialogSeverity.Info => ("ℹ", "AccentPrimary"),
        DialogSeverity.Warning => ("⚠", "AccentWarning"),
        DialogSeverity.Error => ("✗", "AccentError"),
        DialogSeverity.Confirm => ("⚠", "AccentWarning"), // WPF used the warning icon for confirm
        _ => ("ℹ", "AccentPrimary"),
    };

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
