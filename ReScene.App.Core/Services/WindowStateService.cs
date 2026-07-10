using ReScene.App.Core.Models;

namespace ReScene.App.Core.Services;

/// <summary>
/// Persists window state to a JSON file in local app data.
/// </summary>
public class WindowStateService : IWindowStateService
{
    // Instance (not static) field: computed at construction time so it picks up whatever
    // AppDataConfig.FolderName is current when this instance is created (mirrors AppSettingsService
    // and RecentFilesService). A static field would freeze to the folder active on the type's first
    // touch in the process, which breaks the per-head folder switch (and cross-test isolation).
    private readonly string _filePath = JsonFileStore.GetPath("window-state.json");

    public WindowStateModel? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            return JsonFileStore.Read<WindowStateModel>(_filePath);
        }
        catch
        {
            return null;
        }
    }

    public void Save(WindowStateModel state)
    {
        try
        {
            JsonFileStore.Write(_filePath, state);
        }
        catch
        {
            // Silently ignore persistence errors
        }
    }
}
