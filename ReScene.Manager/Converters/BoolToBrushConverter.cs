using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Styling;

namespace ReScene.Manager.Converters;

/// <summary>
/// Maps a <see cref="bool"/> flag to a brush resource from Tokens.axaml when the flag is
/// <see langword="true"/>, otherwise returns <see cref="AvaloniaProperty.UnsetValue"/> so the target
/// property falls back to its inherited/theme default. The resource key is supplied via the
/// <c>ConverterParameter</c>.
/// <para>
/// Replaces the WPF Compare view's <c>DataTrigger</c>s that swapped a <c>Foreground</c>/<c>Background</c>
/// Setter on a flag (Avalonia has no style triggers). Used for: tree-node and property-cell diff
/// foreground (<c>IsDifferent</c> → <c>AccentError</c>), property-row diff background (<c>IsDifferent</c>
/// → <c>DiffRowBackground</c>), and the indented property-name foreground (<c>IsIndented</c> →
/// <c>SystemControlForegroundBaseMediumBrush</c>).
/// </para>
/// </summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true || parameter is not string key || Application.Current is null)
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
