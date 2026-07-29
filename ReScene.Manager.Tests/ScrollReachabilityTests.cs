using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Models;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Services;
using ReScene.Manager.Views;

namespace ReScene.Manager.Tests;

/// <summary>
/// Pins that every tab page's ScrollViewer can reach the END of its content once it overflows.
/// Regression (reported on a small VM window): <c>ScrollViewer.Padding</c> shifts the content's
/// arranged position but is excluded from the reported scroll extent, so the trailing ~padding of
/// content sits permanently below the maximum offset — the RAR Reconstructor Options tab's last
/// checkboxes could not be scrolled into view. The inset therefore lives on the content panel
/// (<c>Margin</c>), never on the ScrollViewer; these tests fail against the Padding form.
/// </summary>
public class ScrollReachabilityTests
{
    /// <summary>
    /// Selects every sub-tab, scrolls its page ScrollViewer to the bottom edge, and returns
    /// (headers that actually overflowed, failures where the content's bottom stayed beyond the
    /// viewport). Pages whose content fits prove nothing; callers guard rig validity by asserting
    /// which pages overflowed.
    /// </summary>
    private static (List<string> Scrollable, List<string> Unreachable) ProbePages(TabControl tabs)
    {
        List<string> scrollable = [];
        List<string> unreachable = [];
        for (int i = 0; i < tabs.ItemCount; i++)
        {
            tabs.SelectedIndex = i;
            Dispatcher.UIThread.RunJobs();
            // Headers may be composite panels (e.g. Paths carries a warning icon) — only plain
            // string headers are used for naming; composites fall back to the index.
            string header = (tabs.Items[i] as TabItem)?.Header as string ?? $"#{i}";

            // The page's own ScrollViewer is unnamed; TextBox template internals are PART_ScrollViewer.
            ScrollViewer? sv = tabs.GetVisualDescendants().OfType<ScrollViewer>()
                .FirstOrDefault(s => s.Name is null && s.IsEffectivelyVisible);
            Assert.NotNull(sv);
            if (sv!.Extent.Height <= sv.Viewport.Height + 0.5)
            {
                continue;
            }

            scrollable.Add(header);
            sv.Offset = new Vector(sv.Offset.X, sv.Extent.Height - sv.Viewport.Height);
            Dispatcher.UIThread.RunJobs();

            var content = Assert.IsAssignableFrom<Control>(sv.Content);
            Point bottom = content.TranslatePoint(new Point(0, content.Bounds.Height), sv)!.Value;
            if (bottom.Y > sv.Viewport.Height + 0.5)
            {
                unreachable.Add($"{header}: content bottom {bottom.Y:F1} exceeds viewport {sv.Viewport.Height:F1} at max scroll");
            }
        }

        return (scrollable, unreachable);
    }

    [AvaloniaFact]
    public void ReconstructorView_TabPages_ScrollToEnd_ReachesLastContent()
    {
        ReconstructorViewModel vm = BeginnerShellTestFactory.Create().Reconstructor;
        var view = new ReconstructorView { DataContext = vm };
        // 1012x640 sits ABOVE the view's fixed-minimums floor (~565px: header rows + tab strip +
        // TabControl MinHeight 220 + splitter + log MinHeight 140), so every page ScrollViewer is
        // fully inside the window and this fact isolates extent-reachability. Heights BELOW the
        // floor clip the grid itself — a separate structural defect tracked for its own fix.
        var window = new Window { Width = 1012, Height = 640, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            TabControl subTabs = view.GetVisualDescendants().OfType<TabControl>().First();
            (List<string> scrollable, List<string> unreachable) = ProbePages(subTabs);

            // Rig validity: the window is sized so the reported page (Options) genuinely
            // overflows — a page that fits passes trivially and would mask the regression.
            Assert.Contains("Options", scrollable);
            Assert.True(scrollable.Count >= 3,
                $"rig validity: only [{string.Join(", ", scrollable)}] overflowed at 1012x560");

            Assert.True(unreachable.Count == 0, string.Join("; ", unreachable));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SettingsWindow_TabPages_ScrollToEnd_ReachesLastContent()
    {
        var vm = new SettingsViewModel(new InertAppSettingsService(), new AvaloniaFileDialogService(static () => null));
        // The probe overrides the window's own minimums: it exercises ScrollViewer mechanics,
        // which must hold at any viewport the pages can be given (display scaling can shrink
        // the effective space below the design minimums).
        var window = new SettingsWindow(vm) { Width = 560, MinHeight = 0, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            TabControl tabs = window.GetVisualDescendants().OfType<TabControl>().First();
            (List<string> scrollable, List<string> unreachable) = ProbePages(tabs);

            Assert.True(scrollable.Count >= 1,
                $"rig validity: no settings page overflowed at 560x300");

            Assert.True(unreachable.Count == 0, string.Join("; ", unreachable));
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class InertAppSettingsService : IAppSettingsService
    {
        public event EventHandler? Changed { add { } remove { } }
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }
}
