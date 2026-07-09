using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ReScene.App.Core;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Services;
using ReScene.Manager.Views;

namespace ReScene.Manager;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Surface otherwise-silent background failures (mirrors the WPF App handlers).
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Trace.TraceError($"Fatal unhandled exception (terminating={e.IsTerminating}): {e.ExceptionObject}");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Trace.TraceError($"Unobserved task exception: {e.Exception}");
                e.SetObserved();
            };

            // Fresh-start settings folder — set BEFORE any settings service reads/writes.
            AppDataConfig.FolderName = "ReScene.Manager";
            // Rebrand: this head shows "ReScene Manager"; the WPF app never sets this, so it
            // keeps the "ReScene.NET" default.
            AppInfo.DisplayName = "ReScene Manager";

            var window = new MainWindow
            {
                // Mirrors the WPF app: the window persists its position/size/tab across runs.
                WindowStateService = new WindowStateService(),
            };
            // Resolve the currently-active window first so a sync dialog (e.g. a wizard's confirm)
            // is owned by the active pop-up (the WizardWindow when open), not always MainWindow —
            // otherwise it renders behind the modal wizard. Falls back to MainWindow, then the
            // freshly-built window. Resolved lazily when a dialog is requested.
            Window Owner() =>
                desktop.Windows.FirstOrDefault(w => w.IsActive)
                ?? desktop.MainWindow as Window
                ?? window;

            var tempDir = new TempDirectoryService();
            var appSettings = new AppSettingsService();
            var fileDialog = new AvaloniaFileDialogService(Owner);
            var imageLoader = new AvaloniaImageLoader();

            window.DataContext = new MainWindowViewModel(
                new SRRCreationService(), new SRSCreationService(), new SRSReconstructionService(),
                new SampleRestorerService(tempDir), new BruteForceService(), new FileCompareService(appSettings),
                fileDialog, new RecentFilesService(appSettings), tempDir, new SRREditingService(),
                new SRRVerifyService(), new PropertyExportService(), appSettings, new HexDiffComputer(),
                new AvaloniaUiTimerFactory(),
                new AvaloniaFilePreviewService(imageLoader, Owner),
                new AvaloniaImagePreviewService(imageLoader, fileDialog, Owner),
                new AvaloniaUiDispatcher());

            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
