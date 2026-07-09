using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ReScene.App.Core;
using ReScene.App.Core.Services;

namespace ReScene.Manager.Views;

/// <summary>
/// Small fixed-width "About" dialog, ported from the WPF <c>ReScene.NET.Views.AboutWindow</c>.
/// DataContext is a private <see cref="AboutInfo"/> record exposing the app name (from
/// <see cref="AppInfo.DisplayName"/>) and the version passed into the constructor. Links open via
/// <see cref="SystemLauncherService"/> (cross-platform) instead of WPF's Windows-only
/// <c>Process.Start(UseShellExecute)</c> — that service already swallows launch failures, so a
/// missing browser or malformed URL can never crash the app.
/// </summary>
public partial class AboutWindow : Window
{
    /// <summary>Parameterless constructor for the XAML designer / loader only.</summary>
    public AboutWindow()
        : this(string.Empty)
    {
    }

    public AboutWindow(string appVersion)
    {
        AvaloniaXamlLoader.Load(this);
        Title = $"About {AppInfo.DisplayName}";
        DataContext = new AboutInfo(AppInfo.DisplayName, appVersion);
    }

    private void OnLinkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url })
        {
            new SystemLauncherService().OpenUrl(url);
        }
    }

    private sealed record AboutInfo(string AppName, string AppVersion);
}
