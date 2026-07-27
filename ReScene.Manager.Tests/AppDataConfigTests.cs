using ReScene.App.Core.Models;
using ReScene.App.Core.Services;

namespace ReScene.Manager.Tests;

/// <summary>
/// Verifies <see cref="AppDataConfig"/>'s default and its effect on where
/// <see cref="AppSettingsService"/> persists. Shares the "AppDataConfig" collection with
/// <see cref="CompositionRootTests"/> so the two never mutate the shared static concurrently.
/// </summary>
[Collection("AppDataConfig")]
public class AppDataConfigTests
{
    [Fact]
    public void FolderName_DefaultsTo_ReSceneManager() => Assert.Equal("ReScene.Manager", AppDataConfig.FolderName);

    [Fact]
    public void AppSettingsService_SaveThenLoad_RoundTripsThroughConfiguredFolder()
    {
        string originalFolder = AppDataConfig.FolderName;
        string tempFolder = $"ReScene.Manager.Tests-{Guid.NewGuid():N}";
        string expectedDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), tempFolder);
        try
        {
            AppDataConfig.FolderName = tempFolder;

            // Constructed AFTER the folder switch: AppSettingsService resolves its file path at
            // construction time, so this proves the new folder is actually picked up.
            var service = new AppSettingsService();
            var settings = new AppSettings { DefaultAppName = "unit-test-app", Mode = UserMode.Advanced };
            service.Save(settings);

            string expectedFile = Path.Combine(expectedDir, "settings.json");
            Assert.True(File.Exists(expectedFile));

            AppSettings loaded = service.Load();
            Assert.Equal("unit-test-app", loaded.DefaultAppName);
            Assert.Equal(UserMode.Advanced, loaded.Mode);
        }
        finally
        {
            AppDataConfig.FolderName = originalFolder;
            if (Directory.Exists(expectedDir))
            {
                Directory.Delete(expectedDir, recursive: true);
            }
        }
    }
}
