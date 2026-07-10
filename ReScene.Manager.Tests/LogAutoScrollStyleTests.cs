using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.Manager.Behaviors;

namespace ReScene.Manager.Tests;

/// <summary>
/// Proves the shared <c>ListBox.logList</c> style actually wires up the
/// <see cref="ListBoxAutoScroll"/> behavior (F3 — the attached property was ported but no style set
/// it, so no log ever auto-scrolled). A headless <c>logList</c> ListBox is filled past its viewport;
/// the test asserts the style applied the attached property and that appending items scrolls the view
/// to the bottom (the behavior ran), with zero binding errors.
/// </summary>
public class LogAutoScrollStyleTests
{
    [AvaloniaFact]
    public void LogListStyle_EnablesAutoScroll_AndSticksToNewestItem_NoBindingErrors()
    {
        var items = new ObservableCollection<string> { "line 1", "line 2" };
        var list = new ListBox
        {
            Classes = { "logList" },
            ItemsSource = items,
            Width = 200,
            Height = 60, // small enough that a few dozen rows overflow the viewport
        };

        using var sink = new BindingErrorSink();
        var window = new Window { Width = 240, Height = 120, Content = list };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The style set the attached property to true (previously no style did, so this was always
        // false and the behavior never subscribed).
        Assert.True(ListBoxAutoScroll.GetAutoScrollToEnd(list));

        // Append well past the viewport height; the behavior should keep the view pinned to the end.
        for (int i = 3; i <= 50; i++)
        {
            items.Add($"line {i}");
        }

        Dispatcher.UIThread.RunJobs();

        ScrollViewer scrollViewer = list.GetVisualDescendants().OfType<ScrollViewer>().First();
        double scrollableHeight = scrollViewer.Extent.Height - scrollViewer.Viewport.Height;
        Assert.True(scrollableHeight > 0, "the list must overflow its viewport for auto-scroll to be meaningful");
        Assert.True(
            scrollViewer.Offset.Y >= scrollableHeight - 1.0,
            $"expected the view scrolled to the bottom (offset {scrollViewer.Offset.Y} of {scrollableHeight})");

        Assert.Empty(sink.Messages);
    }
}
