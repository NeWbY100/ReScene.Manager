using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Styling;
using ReScene.App.Core.Models;

namespace ReScene.Manager.Converters;

/// <summary>
/// Maps a <see cref="FieldState"/> to a brush resource from Tokens.axaml.
/// <para>
/// Default (no <c>ConverterParameter</c>, used by the glyph): Ok→AccentSuccess, Info→AccentPrimary,
/// Warning→AccentWarning, Error→AccentError.
/// </para>
/// <para>
/// <c>ConverterParameter="Message"</c> (used by the message text): only Warning/Error override the
/// resting ForegroundSecondary color, matching the WPF original's per-TextBlock DataTriggers.
/// </para>
/// </summary>
public sealed class FieldStateToBrushConverter : IValueConverter
{
    private const string MessageParameter = "Message";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool forMessage = MessageParameter.Equals(parameter as string, StringComparison.Ordinal);

        string? key = value switch
        {
            FieldState.Ok => forMessage ? null : "AccentSuccess",
            FieldState.Info => forMessage ? null : "AccentPrimary",
            FieldState.Warning => "AccentWarning",
            FieldState.Error => "AccentError",
            _ => null,
        };

        key ??= forMessage ? "ForegroundSecondary" : null;

        if (key is null || Application.Current is null)
        {
            return AvaloniaProperty.UnsetValue;
        }

        return Application.Current.Resources.TryGetResource(key, ThemeVariant.Default, out object? brush)
            ? brush
            : AvaloniaProperty.UnsetValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
