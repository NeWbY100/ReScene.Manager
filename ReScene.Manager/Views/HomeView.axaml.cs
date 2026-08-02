using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ReScene.Manager.Views;

/// <summary>
/// The Home tab, ported from the WPF <c>ReScene.NET.Views.HomeView</c>. Bound to a
/// <see cref="ReScene.App.Core.ViewModels.HomeViewModel"/> (supplied by the shell via
/// <c>DataContext="{Binding Home}"</c>): an Open/Create toolbar, a Resources card whose links open
/// through <c>OpenUrlCommand</c>, and a recent-files list.
/// </summary>
public partial class HomeView : UserControl
{
    public HomeView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
