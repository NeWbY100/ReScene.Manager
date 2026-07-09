using Avalonia.Headless.XUnit;
using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Services;

namespace ReScene.Manager.Tests;

/// <summary>
/// Proves the full <see cref="MainWindowViewModel"/> graph wires under Avalonia, using the exact
/// seam implementations and parameter order <see cref="App.OnFrameworkInitializationCompleted"/>
/// uses. <see cref="AppDataConfig.FolderName"/> is pointed at a unique temp folder for the
/// duration of the test so it never touches the real <c>%LOCALAPPDATA%</c>; the class shares the
/// "AppDataConfig" collection with <see cref="AppDataConfigTests"/> so the two never mutate the
/// shared static concurrently.
/// </summary>
[Collection("AppDataConfig")]
public class CompositionRootTests
{
    [AvaloniaFact]
    public void Constructs_FullGraph_AllChildViewModelsNonNull()
    {
        string originalFolder = AppDataConfig.FolderName;
        AppDataConfig.FolderName = $"ReScene.Manager.Tests-{Guid.NewGuid():N}";
        try
        {
            var tempDir = new TempDirectoryService();
            var appSettings = new AppSettingsService();
            var fileDialog = new AvaloniaFileDialogService(static () => null);
            var imageLoader = new AvaloniaImageLoader();

            var vm = new MainWindowViewModel(
                new SRRCreationService(), new SRSCreationService(), new SRSReconstructionService(),
                new SampleRestorerService(tempDir), new BruteForceService(), new FileCompareService(appSettings),
                fileDialog, new RecentFilesService(appSettings), tempDir, new SRREditingService(),
                new SRRVerifyService(), new PropertyExportService(), appSettings, new HexDiffComputer(),
                new AvaloniaUiTimerFactory(),
                new AvaloniaFilePreviewService(imageLoader, static () => null),
                new AvaloniaImagePreviewService(imageLoader, fileDialog, static () => null),
                new AvaloniaUiDispatcher());

            Assert.NotNull(vm.Home);
            Assert.NotNull(vm.Inspector);
            Assert.NotNull(vm.Creator);
            Assert.NotNull(vm.SRSCreator);
            Assert.NotNull(vm.Reconstructor);
            Assert.NotNull(vm.SRSReconstructor);
            Assert.NotNull(vm.SampleRestorer);
            Assert.NotNull(vm.FileCompare);
            Assert.NotNull(vm.Beginner);
        }
        finally
        {
            AppDataConfig.FolderName = originalFolder;
        }
    }
}
