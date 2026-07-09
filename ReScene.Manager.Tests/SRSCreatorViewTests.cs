using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Models;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Services;
using ReScene.Manager.Views;
using ReScene.SRS;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported <see cref="SRSCreatorView"/>. The central gate is
/// <b>zero binding errors</b> (via <see cref="BindingErrorSink"/>), plus: the Sample/Output path
/// TextBoxes are two-way bound to the VM, and the ISO member-selection row appears/disappears with
/// <c>ShowISOSelection</c>. The creation pipeline and file dialogs are inert fakes — only the view
/// wiring is exercised; the shared ISO progress modal's live open/close is the controller's
/// launch-smoke, not this test.
/// </summary>
public class SRSCreatorViewTests
{
    // ── Inert service doubles (the view test never runs a creation) ──

    private sealed class InertSrsCreationService : ISRSCreationService
    {
        public event EventHandler<SRSCreationProgressEventArgs>? Progress { add { } remove { } }
        public event EventHandler<SRSScanProgressEventArgs>? ScanProgress { add { } remove { } }

        public Task<SRSCreationResult> CreateAsync(string outputPath, string sampleFilePath, SRSCreationOptions options, CancellationToken ct)
            => Task.FromResult(new SRSCreationResult { Success = true });
    }

    private sealed class InertTempDirectoryService : ITempDirectoryService
    {
        public string CreateTempDirectory() => Path.GetTempPath();
        public void Cleanup(string? tempDir) { }
    }

    private sealed class DefaultAppSettingsService : IAppSettingsService
    {
        public event EventHandler? Changed { add { } remove { } }
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public void Invoke(Action action) => action();
        public void Post(Action action) => action();
        public void Post(Action action, UiDispatcherPriority priority) => action();
        public bool CheckAccess() => true;
    }

    private static SRSCreatorViewModel CreateViewModel() =>
        new(
            new InertSrsCreationService(),
            new AvaloniaFileDialogService(static () => null), // headless: dialogs no-op, never block
            new InertTempDirectoryService(),
            new DefaultAppSettingsService(),
            new InlineUiDispatcher());

    [AvaloniaFact]
    public void KeyInputs_AreTwoWayBound_NoBindingErrors()
    {
        SRSCreatorViewModel vm = CreateViewModel();
        vm.AppName = string.Empty; // default settings supply an empty DefaultAppName already

        using var sink = new BindingErrorSink();
        var window = new Window { Width = 900, Height = 700, Content = new SRSCreatorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // VM -> view: the Sample TextBox mirrors InputPath.
        TextBox input = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "InputTextBox");
        vm.InputPath = string.Empty; // avoid triggering ISO/file-exists side effects for this assertion
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(string.Empty, input.Text);

        // view -> VM: typing into the Output TextBox writes back to OutputPath.
        TextBox output = window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "OutputTextBox");
        output.Text = @"C:\rel\sample.srs";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(@"C:\rel\sample.srs", vm.OutputPath);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void ISOSelectionRow_TracksShowISOSelection_NoBindingErrors()
    {
        SRSCreatorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        var window = new Window { Width = 900, Height = 700, Content = new SRSCreatorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        ComboBox isoCombo = window.GetVisualDescendants().OfType<ComboBox>().Single();
        // IsVisible is not inherited down the visual tree in Avalonia, so the row's own IsVisible
        // (bound to ShowISOSelection) is asserted on its containing DockPanel, not the ComboBox itself.
        DockPanel isoRow = Assert.IsType<DockPanel>(isoCombo.GetVisualParent());

        Assert.False(vm.ShowISOSelection);
        Assert.False(isoRow.IsVisible);

        vm.ISOMediaFiles.Add("VIDEO_TS/VTS_01_1.VOB");
        vm.IsISOSource = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.ShowISOSelection);
        Assert.True(isoRow.IsVisible);
        Assert.Same(vm.ISOMediaFiles, isoCombo.ItemsSource);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void ProgressAndCancel_TrackIsCreating_NoBindingErrors()
    {
        SRSCreatorViewModel vm = CreateViewModel();

        using var sink = new BindingErrorSink();
        var window = new Window { Width = 900, Height = 700, Content = new SRSCreatorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Button cancel = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content is "Cancel");
        ProgressBar bar = window.GetVisualDescendants().OfType<ProgressBar>().Single();

        Assert.False(cancel.IsVisible);
        Assert.False(bar.IsVisible);

        vm.IsCreating = true;
        vm.ShowProgress = true;
        vm.ProgressPercent = 42;
        Dispatcher.UIThread.RunJobs();

        Assert.True(cancel.IsVisible);
        Assert.True(bar.IsVisible);
        Assert.Equal(42, bar.Value);

        Assert.Empty(sink.Messages);
    }
}
