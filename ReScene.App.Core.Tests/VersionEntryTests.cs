using ReScene.App.Core.ViewModels;
using ReScene.Core;

namespace ReScene.App.Core.Tests;

public class VersionEntryTests
{
    // ── FullCommandLine: the copied text is the runnable invocation ──

    [Fact]
    public void FullCommandLine_WithInvocationDetails_IsRunnableAsPasted()
    {
        // Shell prefix into the working dir (rar stores entry names relative to it), quoted rar path,
        // switches, quoted output archive, platform input mask — mirroring RARProcess's composition.
        var row = new ReconstructorViewModel.VersionEntry
        {
            VersionDirectory = "/rars/winrar-500",
            Arguments = "a -r -s- -m0",
            ExecutedArguments = "-ma4 a -r -s- -m0",  // the engine-added -ma4 that the display form omits
            InputDirectory = "/tmp/work/input",
            OutputFilePath = "/tmp/work/rar/winrar-500-m0.rar",
        };

        string rar = $"\"{RarExecutable.ResolveIn("/rars/winrar-500")}\" -ma4 a -r -s- -m0";
        string expected = OperatingSystem.IsWindows()
            ? $"pushd \"/tmp/work/input\" && {rar} \"/tmp/work/rar/winrar-500-m0.rar\" .\\*"
            : $"cd \"/tmp/work/input\" && {rar} \"/tmp/work/rar/winrar-500-m0.rar\" './*'";
        Assert.Equal(expected, row.FullCommandLine);
    }

    [Fact]
    public void FullCommandLine_WithoutInvocationDetails_FallsBackToExeAndSwitches()
    {
        // Phase-1 comment-filter rows (and legacy events) carry no input/output — keep the old form.
        var row = new ReconstructorViewModel.VersionEntry
        {
            VersionDirectory = "/rars/winrar-500",
            Arguments = "a -m0",
        };

        Assert.Equal($"\"{RarExecutable.ResolveIn("/rars/winrar-500")}\" a -m0", row.FullCommandLine);
    }

    [Fact]
    public void FullCommandLine_WithoutVersionDirectory_IsJustTheArguments()
    {
        var row = new ReconstructorViewModel.VersionEntry { Arguments = "a -m0" };
        Assert.Equal("a -m0", row.FullCommandLine);
    }

    [Fact]
    public void ExeAndArguments_StaysShort_EvenWithInvocationDetails()
    {
        // The per-attempt "Testing …" log lines use this terse form — the runnable cd-prefix and temp
        // paths live only in FullCommandLine (Copy Full Command Line), never in the log firehose.
        var row = new ReconstructorViewModel.VersionEntry
        {
            VersionDirectory = "/rars/winrar-500",
            Arguments = "a -m0",
            ExecutedArguments = "-ma4 a -m0",
            InputDirectory = "/tmp/in",
            OutputFilePath = "/tmp/out.rar",
        };

        Assert.Equal($"\"{RarExecutable.ResolveIn("/rars/winrar-500")}\" a -m0", row.ExeAndArguments);
        Assert.DoesNotContain("/tmp/in", row.ExeAndArguments, StringComparison.Ordinal);
        Assert.DoesNotContain("-ma4", row.ExeAndArguments, StringComparison.Ordinal);
    }

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
