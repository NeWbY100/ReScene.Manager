using ReScene.App.Core.Models;

namespace ReScene.App.Core.Services;

public interface IRecentFilesService
{
    public List<RecentFileEntry> LoadEntries();
    public void AddEntry(string filePath);
    public void RemoveEntry(string filePath);
    public void Clear();
}
