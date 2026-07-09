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
/// Headless render/behavior tests for the ported <see cref="SettingsWindow"/>. The central gate is
/// <b>zero binding errors</b> (via <see cref="BindingErrorSink"/>) when the window renders against a
/// real <see cref="SettingsViewModel"/>, plus the key inputs actually being two-way bound (text/int
/// fields and the Beginner/Advanced radios). Live dialog interaction — Save actually persisting to
/// disk, Browse opening a real folder picker — is the controller's Phase-4 launch-smoke, not
/// exercised here.
/// </summary>
/// <remarks>
/// Shares the "AppDataConfig" collection with <see cref="AppDataConfigTests"/>,
/// <see cref="AppInfoTests"/> and <see cref="CompositionRootTests"/>: each test here points
/// <see cref="AppDataConfig.FolderName"/> at a unique temp folder for a real
/// <see cref="AppSettingsService"/>, so none of the four classes may run concurrently.
/// </remarks>
[Collection("AppDataConfig")]
public class SettingsWindowTests
{
    private static SettingsViewModel CreateViewModel(AppSettings? seed = null)
    {
        var settingsService = new AppSettingsService();
        if (seed is not null)
        {
            settingsService.Save(seed);
        }

        var fileDialog = new AvaloniaFileDialogService(static () => null);
        return new SettingsViewModel(settingsService, fileDialog);
    }

    private static string UseTempAppDataFolder()
    {
        string tempFolder = $"ReScene.Manager.Tests-{Guid.NewGuid():N}";
        AppDataConfig.FolderName = tempFolder;
        return tempFolder;
    }

    private static void CleanUpTempAppDataFolder(string tempFolder)
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), tempFolder);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Renders_ReflectsViewModelValues_NoBindingErrors()
    {
        string originalFolder = AppDataConfig.FolderName;
        string tempFolder = UseTempAppDataFolder();
        try
        {
            SettingsViewModel vm = CreateViewModel(new AppSettings
            {
                DefaultAppName = "seeded-app",
                RecentFilesLimit = 42,
                MKVMaxElements = 5000,
                Mode = UserMode.Advanced,
            });

            using var sink = new BindingErrorSink();
            var window = new SettingsWindow(vm);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            TextBox appNameBox = window.GetVisualDescendants().OfType<TextBox>()
                .Single(t => t.Text == "seeded-app");
            Assert.Equal(vm.DefaultAppName, appNameBox.Text);

            NumericUpDown[] numerics = [.. window.GetVisualDescendants().OfType<NumericUpDown>()];
            Assert.Equal(2, numerics.Length);
            Assert.Contains(numerics, n => n.Value == 42m);
            Assert.Contains(numerics, n => n.Value == 5000m);

            RadioButton[] radios = [.. window.GetVisualDescendants().OfType<RadioButton>()];
            RadioButton beginner = radios.Single(r => (string?)r.Content == "Beginner");
            RadioButton advanced = radios.Single(r => (string?)r.Content == "Advanced");
            Assert.False(beginner.IsChecked);
            Assert.True(advanced.IsChecked);

            Assert.Empty(sink.Messages);
        }
        finally
        {
            AppDataConfig.FolderName = originalFolder;
            CleanUpTempAppDataFolder(tempFolder);
        }
    }

    [AvaloniaFact]
    public void EditingDefaultAppNameTextBox_UpdatesViewModel()
    {
        string originalFolder = AppDataConfig.FolderName;
        string tempFolder = UseTempAppDataFolder();
        try
        {
            SettingsViewModel vm = CreateViewModel();
            var window = new SettingsWindow(vm);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            TextBox appNameBox = window.GetVisualDescendants().OfType<TextBox>().First();
            appNameBox.Text = "Renamed Tool";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Renamed Tool", vm.DefaultAppName);
        }
        finally
        {
            AppDataConfig.FolderName = originalFolder;
            CleanUpTempAppDataFolder(tempFolder);
        }
    }

    [AvaloniaFact]
    public void TogglingViewModelMode_UpdatesRadioButtons_BothDirections()
    {
        string originalFolder = AppDataConfig.FolderName;
        string tempFolder = UseTempAppDataFolder();
        try
        {
            SettingsViewModel vm = CreateViewModel(new AppSettings { Mode = UserMode.Beginner });
            var window = new SettingsWindow(vm);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            RadioButton[] radios = [.. window.GetVisualDescendants().OfType<RadioButton>()];
            RadioButton beginner = radios.Single(r => (string?)r.Content == "Beginner");
            RadioButton advanced = radios.Single(r => (string?)r.Content == "Advanced");

            // VM -> View: precondition seeded Beginner.
            Assert.True(beginner.IsChecked);
            Assert.False(advanced.IsChecked);

            // VM -> View: flipping the VM's mode after construction updates the radios.
            vm.IsAdvancedMode = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(beginner.IsChecked);
            Assert.True(advanced.IsChecked);

            // View -> VM: checking the Beginner radio (as a user click would) flows back to the VM.
            beginner.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsBeginnerMode);
            Assert.False(vm.IsAdvancedMode);
        }
        finally
        {
            AppDataConfig.FolderName = originalFolder;
            CleanUpTempAppDataFolder(tempFolder);
        }
    }

    [AvaloniaFact]
    public void HasSaveAndCancelButtons_WithDefaultAndCancelFlags()
    {
        string originalFolder = AppDataConfig.FolderName;
        string tempFolder = UseTempAppDataFolder();
        try
        {
            SettingsViewModel vm = CreateViewModel();
            var window = new SettingsWindow(vm);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Button[] buttons = [.. window.GetVisualDescendants().OfType<Button>()];
            Button save = buttons.Single(b => b.Content is "Save");
            Button cancel = buttons.Single(b => b.Content is "Cancel");

            Assert.True(save.IsDefault);
            Assert.True(cancel.IsCancel);
        }
        finally
        {
            AppDataConfig.FolderName = originalFolder;
            CleanUpTempAppDataFolder(tempFolder);
        }
    }
}
