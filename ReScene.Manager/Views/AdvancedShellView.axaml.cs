using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ReScene.Manager.Views;

/// <summary>
/// The advanced-mode shell: an 8-tab <see cref="TabControl"/> whose selected index is bound to
/// <c>MainWindowViewModel.SelectedTabIndex</c>. Home hosts the real
/// <see cref="HomeView"/>; the other seven tabs are placeholders that still bind their child
/// ViewModel so later tasks only swap in the real view.
/// </summary>
public partial class AdvancedShellView : UserControl
{
    public AdvancedShellView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
