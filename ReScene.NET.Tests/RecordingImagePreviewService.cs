using ReScene.NET.Models;
using ReScene.NET.Services;

namespace ReScene.NET.Tests;

/// <summary>Records every <see cref="IImagePreviewService.Preview"/> call for assertions.</summary>
public sealed class RecordingImagePreviewService : ReScene.NET.Services.IImagePreviewService
{
    public List<(byte[] Data, string FileName)> Calls { get; } = [];

    public void Preview(byte[] data, string fileName) => Calls.Add((data, fileName));
}
