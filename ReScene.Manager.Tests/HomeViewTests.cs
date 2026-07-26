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
/// Headless render tests for the ported <see cref="HomeView"/>. The central gate is <b>zero binding
/// errors</b> (via <see cref="BindingErrorSink"/>), plus: a seeded recent-files list renders one row
/// per entry (each row reaching the VM commands through the <c>RelativeSource</c> ancestor binding),
/// and an empty list hides the recent-files panel.
/// </summary>
public class HomeViewTests
{
    /// <summary>In-memory recent-files service so the view test never touches disk.</summary>
    private sealed class FakeRecentFiles(List<RecentFileEntry> entries) : IRecentFilesService
    {
        public List<RecentFileEntry> LoadEntries() => [.. entries];

        public void AddEntry(string filePath)
        {
        }

        public void RemoveEntry(string filePath) => entries.RemoveAll(e => e.FilePath == filePath);

        public void Clear() => entries.Clear();
    }

    private static HomeViewModel CreateViewModel(List<RecentFileEntry> entries) =>
        new(
            new FakeRecentFiles(entries),
            openFile: static _ => { },
            switchToCreator: static () => { },
            openDialog: static () => Task.CompletedTask,
            fileDialog: new AvaloniaFileDialogService(static () => null),
            launcher: new SystemLauncherService());

    private static List<RecentFileEntry> SampleEntries() =>
    [
        new() { FileName = "release.one.srr", FilePath = @"C:\scene\release.one.srr", LastOpened = new DateTime(2026, 3, 1, 9, 30, 0) },
        new() { FileName = "release.two.srs", FilePath = @"C:\scene\release.two.srs", LastOpened = new DateTime(2026, 3, 2, 14, 15, 0) },
        new() { FileName = "release.three.srr", FilePath = @"C:\scene\release.three.srr", LastOpened = new DateTime(2026, 3, 3, 18, 45, 0) },
    ];

    [AvaloniaFact]
    public void SeededRecentFiles_RenderOneRowPerEntry_NoBindingErrors()
    {
        List<RecentFileEntry> entries = SampleEntries();
        HomeViewModel vm = CreateViewModel(entries);

        using var sink = new BindingErrorSink();
        var window = new Window { Content = new HomeView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.HasRecentFiles);

        // Each recent-file row is a Button carrying the "recentItem" class.
        int rows = window.GetVisualDescendants().OfType<Button>()
            .Count(b => b.Classes.Contains("recentItem"));
        Assert.Equal(entries.Count, rows);

        // The recent-files panel is visible.
        DockPanel panel = window.GetVisualDescendants().OfType<DockPanel>().Single(d => d.Name == "RecentFilesPanel");
        Assert.True(panel.IsVisible);

        // The list's scroller keeps Auto visibility WITH AllowAutoHide=false: the Fluent overlay
        // bar otherwise draws over the right edge of the full-width file-row buttons.
        ScrollViewer scroll = window.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(sv => sv.TemplatedParent is null);
        Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, scroll.VerticalScrollBarVisibility);
        Assert.False(ScrollViewer.GetAllowAutoHide(scroll));

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void EmptyRecentFiles_HidesThePanel_NoBindingErrors()
    {
        HomeViewModel vm = CreateViewModel([]);

        using var sink = new BindingErrorSink();
        var window = new Window { Content = new HomeView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.HasRecentFiles);

        DockPanel panel = window.GetVisualDescendants().OfType<DockPanel>().Single(d => d.Name == "RecentFilesPanel");
        Assert.False(panel.IsVisible);

        Assert.DoesNotContain(
            window.GetVisualDescendants().OfType<Button>(),
            b => b.Classes.Contains("recentItem"));

        Assert.Empty(sink.Messages);
    }
}
