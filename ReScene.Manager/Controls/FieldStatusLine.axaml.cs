using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using ReScene.App.Core.Models;

namespace ReScene.Manager.Controls;

/// <summary>
/// Renders a <see cref="FieldStatus"/> as a colored glyph (✓/ℹ/⚠/✗) plus its message.
/// Hidden when the status state is <see cref="FieldState.None"/>.
/// </summary>
public partial class FieldStatusLine : UserControl
{
    /// <summary>
    /// Control-local converter (not an app-wide resource, since only this control's own XAML uses
    /// it): maps <see cref="Status"/>'s <see cref="FieldState"/> to the root grid's
    /// <see cref="Avalonia.Visual.IsVisible"/> — hidden exactly when the state is
    /// <see cref="FieldState.None"/>, mirroring the WPF original's Visibility DataTrigger.
    /// </summary>
    public static readonly IValueConverter StateVisibleConverter =
        new FuncValueConverter<FieldState, bool>(state => state != FieldState.None);

    public static readonly StyledProperty<FieldStatus?> StatusProperty =
        AvaloniaProperty.Register<FieldStatusLine, FieldStatus?>(nameof(Status), FieldStatus.None);

    public FieldStatusLine()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>The status to display. Defaults to <see cref="FieldStatus.None"/> (hidden).</summary>
    public FieldStatus? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }
}
