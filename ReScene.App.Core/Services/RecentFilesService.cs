using ReScene.App.Core.Models;

namespace ReScene.App.Core.Services;

public class RecentFilesService(IAppSettingsService appSettingsService, string? filePath = null) : IRecentFilesService
{
    // filePath is a seam for tests; production uses the default per-user store path.
    private readonly string _filePath = filePath ?? JsonFileStore.GetPath("recent.json");

    public List<RecentFileEntry> LoadEntries()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            return JsonFileStore.Read<List<RecentFileEntry>>(_filePath) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public void AddEntry(string filePath)
    {
        List<RecentFileEntry> entries = LoadEntries();

        entries.RemoveAll(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

        entries.Insert(0, new RecentFileEntry
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            LastOpened = DateTime.Now
        });

        // Clamp the persisted limit (settings.json can be hand-edited): 0 or a negative value
        // would otherwise wipe the whole list or throw in RemoveRange. Range mirrors SettingsViewModel.Save.
        int maxEntries = Math.Clamp(appSettingsService.Load().RecentFilesLimit, 1, 100);

        if (entries.Count > maxEntries)
        {
            entries.RemoveRange(maxEntries, entries.Count - maxEntries);
        }

        Save(entries);
    }

    public void RemoveEntry(string filePath)
    {
        List<RecentFileEntry> entries = LoadEntries();
        entries.RemoveAll(e => string.Equals(e.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        Save(entries);
    }

    public void Clear() => Save([]);

    private void Save(List<RecentFileEntry> entries)
    {
        try
        {
            JsonFileStore.Write(_filePath, entries);
        }
        catch
        {
            // Silently ignore persistence errors
        }
    }
}
