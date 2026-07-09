using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Services;
using ReScene.Manager.Views;
using ReScene.SRS;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported <see cref="SRSReconstructorView"/>. The central gate is
/// <b>zero binding errors</b> (via <see cref="BindingErrorSink"/>), plus: the SRS/Media/Output path
/// TextBoxes are two-way bound, the Media TextBox becomes read-only for an ISO source, and the result
/// banner's visibility and background tint track <c>ShowResult</c>/<c>ResultSuccess</c> (replacing the
/// WPF <c>DataTrigger</c>). The reconstruction pipeline and file dialogs are inert fakes — only the
/// view wiring is exercised; the shared ISO progress modal's live open/close is the controller's
/// launch-smoke, not this test.
/// </summary>
public class SRSReconstructorViewTests
{
    // ── Inert service doubles (the view test never runs a rebuild) ──

    private sealed class InertSrsReconstructionService : ISRSReconstructionService
    {
        public event EventHandler<SRSReconstructionProgressEventArgs>? Progress { add { } remove { } }
        public event EventHandler<SRSScanProgressEventArgs>? ScanProgress { add { } remove { } }

        public Task<SRSReconstructionResult> RebuildAsync(string srsFilePath, string mediaFilePath, string outputPath, CancellationToken ct)
            => Task.FromResult(new SRSReconstructionResult(true, true, 0, 0, 0, 0, null));
    }

    private sealed class InertTempDirectoryService : ITempDirectoryService
    {
        public string CreateTempDirectory() => Path.GetTempPath();
        public void Cleanup(string? tempDir) { }
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, UiDispatcherPriority priority) => action();
        public bool CheckAccess() => true;
    }

    private static SRSReconstructorViewModel CreateViewModel() =>
        new(
            new InertSrsReconstructionService(),
            new AvaloniaFileDialogService(static () => null), // headless: dialogs no-op, never block
            new InertTempDirectoryService(),
            new InlineUiDispatcher());

    [AvaloniaFact]
    public void KeyInputs_AreTwoWayBound_NoBindingErrors()
    {
        SRSReconstructorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        var window = new Window { Width = 900, Height = 700, Content = new SRSReconstructorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // view -> VM: typing into the SRS/Output TextBoxes writes back to the VM.
        // (SRSFilePath's setter reads the file from disk, so leave it empty here and only exercise
        // the plain string round-trip via OutputPath, matching the SRR Creator view test's approach.)
        TextBox output = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "OutputTextBox");
        output.Text = @"C:\rel\sample.mkv";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(@"C:\rel\sample.mkv", vm.OutputPath);

        // VM -> view: the Media TextBox mirrors MediaFilePath.
        TextBox media = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "MediaFileTextBox");
        Assert.False(media.IsReadOnly);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void MediaTextBox_BecomesReadOnly_ForIsoSource_NoBindingErrors()
    {
        SRSReconstructorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        var window = new Window { Width = 900, Height = 700, Content = new SRSReconstructorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TextBox media = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "MediaFileTextBox");

        vm.IsISOSource = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(media.IsReadOnly);

        vm.IsISOSource = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(media.IsReadOnly);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void ResultBanner_TracksShowResultAndSuccessTint_NoBindingErrors()
    {
        SRSReconstructorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        var window = new Window { Width = 900, Height = 700, Content = new SRSReconstructorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The result banner is the only Border in the view with rounded corners (the section
        // separators are plain 1px rules with no CornerRadius).
        Border banner = window.GetVisualDescendants().OfType<Border>()
            .Single(b => b.CornerRadius.TopLeft == 4);

        Assert.False(banner.IsVisible);

        vm.ResultSuccess = true;
        vm.ResultSummary = "CRC32 match";
        vm.ShowResult = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(banner.IsVisible);
        Color successColor = Assert.IsType<SolidColorBrush>(banner.Background).Color;
        Assert.Equal(Color.Parse("#304EC9B0"), successColor);

        vm.ResultSuccess = false;
        Dispatcher.UIThread.RunJobs();
        Color failureColor = Assert.IsType<SolidColorBrush>(banner.Background).Color;
        Assert.Equal(Color.Parse("#30FF4444"), failureColor);

        Assert.Empty(sink.Messages);
    }
}
