using Avalonia.Platform.Storage;
using ReScene.Manager.Services;

namespace ReScene.Manager.Tests;

/// <summary>Unit tests for the pure <see cref="FilePickerFilters.ToFileTypes"/> WPF-filter conversion.</summary>
public class FilePickerFiltersTests
{
    [Fact]
    public void ToFileTypes_SplitsDescriptionAndPatterns()
    {
        FilePickerFileType[] types = FilePickerFilters.ToFileTypes(["Scene files|*.srr;*.srs"]);

        FilePickerFileType type = Assert.Single(types);
        Assert.Equal("Scene files", type.Name);
        Assert.Equal(["*.srr", "*.srs"], type.Patterns);
    }

    [Fact]
    public void ToFileTypes_AllFilesEntry_NormalizedToStar()
    {
        FilePickerFileType[] types = FilePickerFilters.ToFileTypes(["All files|*.*"]);

        FilePickerFileType type = Assert.Single(types);
        Assert.Equal("All files", type.Name);
        Assert.Equal(["*"], type.Patterns);
    }

    [Fact]
    public void ToFileTypes_BareStarPattern_KeptAsStar()
    {
        FilePickerFileType[] types = FilePickerFilters.ToFileTypes(["All|*"]);

        Assert.Equal(["*"], Assert.Single(types).Patterns);
    }

    [Fact]
    public void ToFileTypes_MultipleEntries_PreserveOrder()
    {
        FilePickerFileType[] types = FilePickerFilters.ToFileTypes(["SRR|*.srr", "All files|*.*"]);

        Assert.Equal(2, types.Length);
        Assert.Equal("SRR", types[0].Name);
        Assert.Equal(["*.srr"], types[0].Patterns);
        Assert.Equal("All files", types[1].Name);
        Assert.Equal(["*"], types[1].Patterns);
    }

    [Fact]
    public void ToFileTypes_TrimsPatternWhitespace()
    {
        FilePickerFileType[] types = FilePickerFilters.ToFileTypes(["Media|*.mkv; *.avi ;*.mp4"]);

        Assert.Equal(["*.mkv", "*.avi", "*.mp4"], Assert.Single(types).Patterns);
    }

    [Fact]
    public void ToFileTypes_BlankEntries_AreSkipped()
    {
        FilePickerFileType[] types = FilePickerFilters.ToFileTypes(["", "   ", "SRS|*.srs"]);

        Assert.Equal("SRS", Assert.Single(types).Name);
    }

    [Fact]
    public void ToFileTypes_NoSeparator_UsesEntryAsNameAndPattern()
    {
        FilePickerFileType[] types = FilePickerFilters.ToFileTypes(["*.txt"]);

        FilePickerFileType type = Assert.Single(types);
        Assert.Equal("*.txt", type.Name);
        Assert.Equal(["*.txt"], type.Patterns);
    }

    [Fact]
    public void ToFileTypes_EmptyInput_ReturnsEmpty() =>
        Assert.Empty(FilePickerFilters.ToFileTypes([]));

    [Fact]
    public void ToFileTypes_Null_Throws() =>
        Assert.Throws<ArgumentNullException>(() => FilePickerFilters.ToFileTypes(null!));
}
