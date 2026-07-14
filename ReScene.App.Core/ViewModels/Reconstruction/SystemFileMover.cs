namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>The production <see cref="IFileMover"/>: a non-overwriting <see cref="File.Move(string, string, bool)"/>.</summary>
internal sealed class SystemFileMover : IFileMover
{
    public void Move(string source, string destination) => File.Move(source, destination, overwrite: false);
}
