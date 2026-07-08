using System.Globalization;
using Avalonia.Data.Converters;
using ReScene.App.Core.Models;

namespace ReScene.Manager.Converters;

/// <summary>
/// Maps a <see cref="FieldState"/> to the Unicode glyph shown by <c>FieldStatusLine</c>
/// (Ok=&#x2713;, Info=&#x2139;, Warning=&#x26A0;, Error=&#x2717;; None/anything else is empty).
/// </summary>
public sealed class FieldStateToGlyphConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            FieldState.Ok => "✓",
            FieldState.Info => "ℹ",
            FieldState.Warning => "⚠",
            FieldState.Error => "✗",
            _ => string.Empty,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
