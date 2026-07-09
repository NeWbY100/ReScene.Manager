using System.ComponentModel;
using ReScene.App.Core.ViewModels;

namespace ReScene.App.Core.Tests;

public class VersionEntryTests
{
    [Fact]
    public void NewRow_HasStartText_LiveDuration_BlankEnd()
    {
        var row = new ReconstructorViewModel.VersionEntry();
        Assert.Equal(8, row.StartText.Length);              // HH:mm:ss
        Assert.Equal(string.Empty, row.EndText);            // no end yet
        Assert.False(string.IsNullOrEmpty(row.DurationText)); // live (e.g. "00:00")
    }

    [Fact]
    public void Complete_StampsEnd_AndDuration()
    {
        var row = new ReconstructorViewModel.VersionEntry();
        row.Status = "Complete";
        Assert.NotNull(row.EndedAt);
        Assert.False(string.IsNullOrEmpty(row.EndText));
        Assert.False(string.IsNullOrEmpty(row.DurationText));
    }

    [Fact]
    public void TerminalStatus_IsIdempotent_DoesNotMoveEnd()
    {
        var row = new ReconstructorViewModel.VersionEntry();
        row.Status = "Complete";
        DateTime? first = row.EndedAt;
        row.Status = "Error";
        Assert.Equal(first, row.EndedAt);
    }

    [Fact]
    public void WhileTesting_EndBlank_DurationLive()
    {
        var row = new ReconstructorViewModel.VersionEntry();
        row.Status = "Testing"; // no-op vs the default; must not stamp an end
        Assert.Null(row.EndedAt);
        Assert.Equal(string.Empty, row.EndText);
        Assert.False(string.IsNullOrEmpty(row.DurationText)); // live, not blank
    }

    [Fact]
    public void RefreshLiveDuration_RaisesDurationTextChanged()
    {
        var row = new ReconstructorViewModel.VersionEntry();
        var raised = new List<string?>();
        row.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        row.RefreshLiveDuration();

        Assert.Contains(nameof(ReconstructorViewModel.VersionEntry.DurationText), raised);
    }
}
