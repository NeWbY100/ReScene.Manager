using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ReScene.Manager.Converters;

/// <summary>
/// Maps a boolean "word wrap" flag to a <see cref="TextWrapping"/> value:
/// <see langword="true"/> → <see cref="TextWrapping.Wrap"/>, <see langword="false"/> →
/// <see cref="TextWrapping.NoWrap"/>. Replaces the WPF <c>DataTrigger</c> that switched the
/// preview <c>TextBox</c>'s wrapping (Avalonia has no triggers).
/// </summary>
public sealed class BoolToTextWrappingConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? TextWrapping.Wrap : TextWrapping.NoWrap;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is TextWrapping.Wrap;
}
