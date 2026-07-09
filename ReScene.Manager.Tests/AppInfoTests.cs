using Avalonia.Headless.XUnit;
using ReScene.App.Core;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Services;

namespace ReScene.Manager.Tests;

/// <summary>
/// Verifies <see cref="AppInfo"/>'s default and that a freshly-constructed
/// <see cref="MainWindowViewModel"/> picks up whatever display name is set beforehand — the same
/// sequencing <see cref="App.OnFrameworkInitializationCompleted"/> uses for the Avalonia head.
/// Shares the "AppDataConfig" collection with <see cref="AppDataConfigTests"/> and
/// <see cref="CompositionRootTests"/>: constructing the full VM graph also touches
/// <see cref="AppDataConfig.FolderName"/>, so none of the three may run concurrently.
/// </summary>
[Collection("AppDataConfig")]
public class AppInfoTests
{
    [Fact]
    public void DisplayName_DefaultsTo_ReSceneNET() => Assert.Equal("ReScene.NET", AppInfo.DisplayName);

    [AvaloniaFact]
    public void SettingDisplayName_FlowsInto_FreshlyConstructedMainWindowViewModel_WindowTitle()
    {
        string originalDisplayName = AppInfo.DisplayName;
        string originalFolder = AppDataConfig.FolderName;
        AppDataConfig.FolderName = $"ReScene.Manager.Tests-{Guid.NewGuid():N}";
        try
        {
            AppInfo.DisplayName = "ReScene Manager";

            var tempDir = new TempDirectoryService();
            var appSettings = new AppSettingsService();
            var fileDialog = new AvaloniaFileDialogService(static () => null);
            var imageLoader = new AvaloniaImageLoader();

            // Same constructor/parameter order as App.OnFrameworkInitializationCompleted and
            // CompositionRootTests — proving the title seam works through the real wiring, not a
            // hand-rolled shortcut.
            var vm = new MainWindowViewModel(
                new SRRCreationService(), new SRSCreationService(), new SRSReconstructionService(),
                new SampleRestorerService(tempDir), new BruteForceService(), new FileCompareService(appSettings),
                fileDialog, new RecentFilesService(appSettings), tempDir, new SRREditingService(),
                new SRRVerifyService(), new PropertyExportService(), appSettings, new HexDiffComputer(),
                new AvaloniaUiTimerFactory(),
                new AvaloniaFilePreviewService(imageLoader, static () => null),
                new AvaloniaImagePreviewService(imageLoader, fileDialog, static () => null),
                new AvaloniaUiDispatcher());

            Assert.Equal("ReScene Manager", vm.WindowTitle);
        }
        finally
        {
            AppInfo.DisplayName = originalDisplayName;
            AppDataConfig.FolderName = originalFolder;
        }
    }
}
