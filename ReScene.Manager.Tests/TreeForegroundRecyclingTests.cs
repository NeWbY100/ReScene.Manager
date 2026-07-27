using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Core.Comparison;
using ReScene.Hex;
using ReScene.Manager.Views;
using ReScene.RAR;

namespace ReScene.Manager.Tests;

/// <summary>
/// Pins the Compare structure trees' label foreground through BOTH black-landing triggers:
/// container recycling across in-place repopulation (compare pair A, then pair B without
/// leaving the tab — the flow that turned the property grids' name column black, see
/// <see cref="FileCompareViewTests.PropertyNameColumn_RealizesV19Foregrounds_NeverTheBlackDefault"/>)
/// and an IN-PLACE IsDifferent flip on a live node (TreeNodeViewModel is observable, so the
/// binding re-evaluates without a rebind — peer-proven black under the old single-key form).
/// The template now uses the two-key form (AccentError|SystemControlForegroundBaseHighBrush);
/// BaseHigh matches the tree's measured inherited steady state (White), NOT ForegroundPrimary,
/// which would visibly dim every plain label.
/// </summary>
public class TreeForegroundRecyclingTests
{
    private sealed class InertFileCompareService : IFileCompareService
    {
        public object? LoadFileData(string filePath) => null;
        public IReadOnlyList<RARDetailedBlock>? ParseDetailedBlocks(string filePath) => null;
        public CompareResult Compare(object? leftData, object? rightData,
            IReadOnlyList<RARDetailedBlock>? leftBlocks = null, IReadOnlyList<RARDetailedBlock>? rightBlocks = null,
            IHexDataSource? leftSource = null, IHexDataSource? rightSource = null) => new();
    }

    private sealed class InertHexDiffComputer : IHexDiffComputer
    {
        public Task<HexDiffResult> ComputeAsync(
            IHexDataSource leftSource, long leftOffset, long leftLength,
            IHexDataSource rightSource, long rightOffset, long rightLength,
            IProgress<HexDiffProgress>? progress, CancellationToken ct) => Task.FromResult(new HexDiffResult([], []));
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, UiDispatcherPriority priority) => action();
        public bool CheckAccess() => true;
    }

    [AvaloniaFact]
    public void TreeLabels_SurviveDiffToPlainRepopulation_NeverBlackNeverStaleRed()
    {
        var vm = new FileCompareViewModel(
            new InertFileCompareService(),
            new ReScene.Manager.Services.AvaloniaFileDialogService(static () => null),
            new InertHexDiffComputer(),
            new InlineUiDispatcher());
        var window = new Window { Width = 1200, Height = 900, Content = new FileCompareView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Color FgOf(string text) => Assert.IsAssignableFrom<ISolidColorBrush>(
            window.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == text).Foreground).Color;

        vm.LeftTreeRoots.Add(new TreeNodeViewModel { Text = "A-diff", IsDifferent = true });
        vm.LeftTreeRoots.Add(new TreeNodeViewModel { Text = "A-plain" });
        Dispatcher.UIThread.RunJobs();

        Color accentError = Color.Parse("#FFF44747");
        Color black = Colors.Black;
        Assert.Equal(accentError, FgOf("A-diff"));
        Color steadyPlain = FgOf("A-plain"); // Fluent inherited default (White-class today)
        Assert.NotEqual(black, steadyPlain);
        Assert.NotEqual(accentError, steadyPlain);

        // Repopulate in place: plain nodes land in containers that held a DIFF node.
        vm.LeftTreeRoots.Clear();
        vm.LeftTreeRoots.Add(new TreeNodeViewModel { Text = "B-plain1" });
        vm.LeftTreeRoots.Add(new TreeNodeViewModel { Text = "B-plain2" });
        Dispatcher.UIThread.RunJobs();

        // Recycled labels must revert to the steady-state default: not black (the DataGrid
        // failure mode), not the previous item's red.
        Assert.Equal(steadyPlain, FgOf("B-plain1"));
        Assert.Equal(steadyPlain, FgOf("B-plain2"));

        // IN-PLACE mutation — the tree's SECOND trigger (peer finding): IsDifferent is
        // [ObservableProperty], so flipping it on a live node re-evaluates the binding without
        // any rebind. Under the single-key form this landed on BLACK (UnsetValue clears the
        // local value); the two-key form must round-trip red -> steady White.
        var live = new TreeNodeViewModel { Text = "SwapMe", IsDifferent = true };
        vm.LeftTreeRoots.Add(live);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(accentError, FgOf("SwapMe"));
        live.IsDifferent = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(steadyPlain, FgOf("SwapMe"));
    }
}
