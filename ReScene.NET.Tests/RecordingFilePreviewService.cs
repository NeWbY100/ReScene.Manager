using ReScene.App.Core.Models;

using ReScene.App.Core.Services;
namespace ReScene.NET.Tests;

/// <summary>Records every <see cref="IFilePreviewService.Preview"/> call for assertions.</summary>
public sealed class RecordingFilePreviewService : ReScene.App.Core.Services.IFilePreviewService
{
    public List<(byte[] Data, string FileName)> Calls { get; } = [];

    public void Preview(byte[] data, string fileName) => Calls.Add((data, fileName));
}
