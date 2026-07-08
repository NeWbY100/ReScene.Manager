using System.Globalization;
using System.Windows.Data;
using System.Windows.Shell;
using ReScene.App.Core.Services;

namespace ReScene.NET.Converters;

/// <summary>
/// Maps the framework-neutral <see cref="TaskbarProgressState"/> (App.Core) onto WPF's
/// <see cref="TaskbarItemProgressState"/> for the window's <c>TaskbarItemInfo.ProgressState</c>.
/// </summary>
public sealed class TaskbarProgressStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is TaskbarProgressState state
            ? state switch
            {
                TaskbarProgressState.Normal => TaskbarItemProgressState.Normal,
                TaskbarProgressState.Indeterminate => TaskbarItemProgressState.Indeterminate,
                TaskbarProgressState.Error => TaskbarItemProgressState.Error,
                TaskbarProgressState.Paused => TaskbarItemProgressState.Paused,
                _ => TaskbarItemProgressState.None,
            }
            : TaskbarItemProgressState.None;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
