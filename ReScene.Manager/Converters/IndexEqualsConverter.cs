using System.Globalization;
using Avalonia.Data.Converters;

namespace ReScene.Manager.Converters;

/// <summary>
/// Compares a bound integer value to a <c>ConverterParameter</c> integer, returning
/// <see langword="true"/> when they match. Used to show/hide contextual toolbar buttons based on a
/// selected tab index. Replaces the WPF <c>IndexToVisibilityConverter</c>, which returned
/// <c>Visibility</c> instead of <see langword="bool"/>.
/// </summary>
public sealed class IndexEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index && TryParseTarget(parameter, out int target) && index == target;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static bool TryParseTarget(object? parameter, out int target)
    {
        switch (parameter)
        {
            case int i:
                target = i;
                return true;
            case string s when int.TryParse(s, out int parsed):
                target = parsed;
                return true;
            default:
                target = 0;
                return false;
        }
    }
}
