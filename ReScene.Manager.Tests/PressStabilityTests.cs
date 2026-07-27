using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.App.Core.ViewModels.Reconstruction;
using ReScene.Core;
using ReScene.Manager.Services;
using ReScene.Manager.Views;

namespace ReScene.Manager.Tests;

/// <summary>
/// Pins the app-wide suppression of Fluent's :pressed scale(0.98) on Button/ToggleButton
/// (Styles.axaml). v1.9 WPF had no press animation; the Fluent scale is width-proportional
/// (each edge slides inward by 1% of width), so a full-row control — the ~1842px Versions
/// group header, Home recentItem rows — slid its left-edge content up to ~18px sideways
/// DURING the press. RenderTransform moves HIT bounds too and IsPressed re-evaluates
/// containment on pointer-move, so mid-press jitter could silently cancel the activation
/// (user-reported: "moves to the right, hard to click").
/// </summary>
/// <remarks>
/// The transform is TRANSITION-animated, so asserts must sample DURING a held press across
/// render ticks — reading immediately after the state change sees the old value and
/// false-passes (the animated-property sibling of the stale-frame rule).
/// </remarks>
[Collection("AppDataConfig")]
public class PressStabilityTests
{
    private sealed class InertBruteForceService : IBruteForceService
    {
        public event EventHandler<BruteForceProgressEventArgs>? Progress { add { } remove { } }
        public event EventHandler<BruteForceStatusChangedEventArgs>? StatusChanged { add { } remove { } }
        public event EventHandler<LogEventArgs>? LogMessage { add { } remove { } }
        public event EventHandler<FileCopyProgressEventArgs>? FileCopyProgress { add { } remove { } }
        public event EventHandler<CRCValidationProgressEventArgs>? CRCValidationProgress { add { } remove { } }
        public event EventHandler<TimestampPreservationFailedEventArgs>? TimestampPreservationFailed { add { } remove { } }
        public Task<BruteForceRunResult> RunAsync(BruteForceOptions options, CancellationToken cancellationToken = default)
            => Task.FromResult(new BruteForceRunResult(true, null));
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, UiDispatcherPriority priority) => action();
        public bool CheckAccess() => true;
    }

    private sealed class InertUiTimerFactory : IUiTimerFactory
    {
        public IUiTimer Create(TimeSpan interval, Action onTick) => new NoOpTimer();

        private sealed class NoOpTimer : IUiTimer
        {
            public void Start() { }
            public void Stop() { }
        }
    }

    private static double MaxDriftDuringHeldPress(Window window, Control target, Point clickPoint)
    {
        double startX = target.TranslatePoint(new Point(0, 0), window)!.Value.X;
        window.MouseDown(clickPoint, Avalonia.Input.MouseButton.Left);
        double maxDrift = 0;
        for (int i = 0; i < 30; i++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            double x = target.TranslatePoint(new Point(0, 0), window)!.Value.X;
            maxDrift = Math.Max(maxDrift, Math.Abs(x - startX));
        }
        window.MouseUp(clickPoint, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        return maxDrift;
    }

    [AvaloniaFact]
    public void VersionsGroupHeader_DoesNotSlideWhilePressed()
    {
        var vm = new ReconstructorViewModel(
            new InertBruteForceService(),
            new AvaloniaFileDialogService(static () => null),
            new InlineUiDispatcher(),
            new InertUiTimerFactory());
        vm.VersionGroups.Add(new RARVersionGroup(3, [new RARVersionLeaf(390, "wrar390"), new RARVersionLeaf(391, "wrar391")]));

        var window = new Window { Width = 1900, Height = 760, Content = new ReconstructorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        TabControl settingsTabs = window.GetVisualDescendants().OfType<TabControl>().Single(t => t.ItemCount == 6);
        settingsTabs.SelectedIndex = 1; // Versions
        Dispatcher.UIThread.RunJobs();

        Expander group = window.GetVisualDescendants().OfType<Expander>().Single();
        group.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        ToggleButton header = group.GetVisualDescendants().OfType<ToggleButton>().Single(t => t is not CheckBox);
        Assert.True(header.Bounds.Width > 1500, $"header width {header.Bounds.Width} — full-row premise broke");

        var origin = header.TranslatePoint(new Point(0, 0), window)!.Value;
        double drift = MaxDriftDuringHeldPress(window, header, new Point(origin.X + 20, origin.Y + header.Bounds.Height / 2));
        Assert.True(drift < 0.5, $"header slid {drift:F2}px during a held press (Fluent :pressed scale not suppressed)");
    }

    [AvaloniaFact]
    public void RecentItemButton_DoesNotSlideWhilePressed_AndJitterStillClicks()
    {
        // Synthetic full-width recentItem button: same class, same app styles, no VM plumbing.
        var button = new Button { Classes = { "recentItem" }, Content = "row", Width = 1800, Height = 30 };
        bool clicked = false;
        button.Click += (_, _) => clicked = true;
        var window = new Window { Width = 1900, Height = 200, Content = button };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var origin = button.TranslatePoint(new Point(0, 0), window)!.Value;
        var press = new Point(origin.X + 10, origin.Y + 15);
        double drift = MaxDriftDuringHeldPress(window, button, press);
        Assert.True(drift < 0.5, $"recentItem content slid {drift:F2}px during a held press");

        // Jitter case (a11y advisory; peer-verified strongest assert here): press at the left
        // edge, move mid-press, release — activation must survive. Under the old scale the hit
        // bounds slid out from under the parked pointer, after which ANY pointer movement —
        // peer measured even a zero-displacement move — cancelled the click (clicked=False).
        window.MouseDown(press, Avalonia.Input.MouseButton.Left);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        window.MouseMove(new Point(press.X + 1, press.Y));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(new Point(press.X + 1, press.Y), Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(clicked, "jittered press did not activate the button");
    }
}
