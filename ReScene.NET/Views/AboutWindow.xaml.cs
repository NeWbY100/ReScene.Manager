using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using ReScene.NET.Helpers;

namespace ReScene.NET.Views;

public partial class AboutWindow : Window
{
    public AboutWindow(string appVersion)
    {
        InitializeComponent();
        DataContext = new AboutInfo(appVersion);
        SourceInitialized += (_, _) => DarkTitleBar.Enable(this);
    }

    private void OnHyperlinkRequestNavigate(object _, RequestNavigateEventArgs e)
    {
        // A missing URL handler or malformed URI throws Win32Exception; opening a link should
        // never crash the app (or pop the generic unhandled-exception dialog).
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open link {e.Uri.AbsoluteUri}: {ex.Message}");
        }

        e.Handled = true;
    }

    private sealed record AboutInfo(string AppVersion);
}
