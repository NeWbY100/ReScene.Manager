using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Styling;

namespace ReScene.Manager.Converters;

/// <summary>
/// Maps a <see cref="bool"/> flag to a brush resource from Tokens.axaml. The <c>ConverterParameter</c>
/// holds the resource key(s): a single key <c>"TrueKey"</c> resolves when the flag is
/// <see langword="true"/> and otherwise returns <see cref="AvaloniaProperty.UnsetValue"/> (the target
/// falls back to its inherited/theme default); a pair <c>"TrueKey|FalseKey"</c> resolves a brush for
/// <em>both</em> states.
/// <para>
/// Prefer the two-key form when the target must show a concrete colour in both states: returning
/// <see cref="AvaloniaProperty.UnsetValue"/> leans on inherited <c>Foreground</c>, which does not
/// reliably reach every DataGrid row (e.g. the alternating tinted rows fell back to
/// <c>TextBlock</c>'s black default). A resolved brush is a local value that always renders.
/// </para>
/// <para>
/// Replaces the WPF Compare view's <c>DataTrigger</c>s that swapped a <c>Foreground</c>/<c>Background</c>
/// Setter on a flag (Avalonia has no style triggers). Used for: tree-node and property-cell diff
/// foreground (<c>IsDifferent</c> → <c>AccentError</c>), property-row diff background (<c>IsDifferent</c>
/// → <c>DiffRowBackground</c>), the Inspector property-name foreground (<c>IsIndented</c> →
/// <c>SystemControlForegroundBaseMediumBrush</c>|<c>ForegroundPrimary</c>), and the Inspector value
/// foreground (<c>IsWarning</c> → <c>WarningForeground</c>|<c>ForegroundPrimary</c>).
/// </para>
/// </summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string param || Application.Current is null)
        {
            return AvaloniaProperty.UnsetValue;
        }

        // Parameter is "TrueKey" or "TrueKey|FalseKey"; the false branch is absent (-> UnsetValue) for
        // the single-key form.
        string[] keys = param.Split('|');
        string? key = value is true ? keys[0] : keys.Length > 1 ? keys[1] : null;

        if (string.IsNullOrEmpty(key))
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
