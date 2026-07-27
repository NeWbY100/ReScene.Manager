using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Styling;

namespace ReScene.Manager.Converters;

/// <summary>
/// Resolves the Compare property grids' NAME-column foreground from
/// <c>[IsIndented, IsDifferent]</c>: indented names keep the secondary look
/// (SystemControlForegroundBaseMediumBrush), non-indented names follow the row's diff state
/// (AccentError on diff rows — v1.9's DataGridRow trigger reached them by inheritance — else
/// ForegroundPrimary).
/// </summary>
/// <remarks>
/// A real brush is returned for EVERY state on purpose. The predecessor single-key
/// <see cref="BoolToBrushConverter"/> binding returned UnsetValue for non-indented names; that
/// inherits correctly on FIRST bind, but the grid repopulates on every structure-tree click and
/// a RECYCLED container rebinding from an indented item to a plain one lands on TextBlock's
/// BLACK default instead of re-inheriting (user-reported; rig-reproduced). Same immunity
/// rationale as the 840fb8f cell-level fix, one level deeper.
/// </remarks>
public sealed class IndentDiffBrushConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (Application.Current is null)
        {
            return AvaloniaProperty.UnsetValue; // nothing resolvable without an app
        }

        // Fail-safe: a short values list resolves ForegroundPrimary rather than UnsetValue —
        // returning the exact failure mode this class exists to prevent would be self-defeating.
        string key = values.Count >= 1 && values[0] is true ? "SystemControlForegroundBaseMediumBrush"
            : values.Count >= 2 && values[1] is true ? "AccentError"
            : "ForegroundPrimary";

        return Application.Current.Resources.TryGetResource(key, ThemeVariant.Default, out object? brush)
            ? brush
            : AvaloniaProperty.UnsetValue;
    }
}
