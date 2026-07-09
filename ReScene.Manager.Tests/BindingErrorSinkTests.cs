using Avalonia.Logging;

namespace ReScene.Manager.Tests;

/// <summary>
/// Tests for <see cref="BindingErrorSink"/> itself — proving the helper actually captures
/// binding-area warnings/errors (so the "zero binding errors" assertions in the view tests have
/// teeth) and restores the previous sink on dispose.
/// </summary>
public class BindingErrorSinkTests
{
    [Fact]
    public void CapturesBindingWarnings_AndIgnoresOtherAreas()
    {
        using var sink = new BindingErrorSink();

        Logger.Sink!.Log(LogEventLevel.Warning, LogArea.Binding, null, "bad binding {0}", 42);
        Logger.Sink!.Log(LogEventLevel.Error, LogArea.Binding, this, "worse binding");
        Logger.Sink!.Log(LogEventLevel.Warning, LogArea.Layout, null, "unrelated layout warning");
        Logger.Sink!.Log(LogEventLevel.Verbose, LogArea.Binding, null, "below-threshold binding note");

        Assert.Equal(2, sink.Messages.Count);
        Assert.Contains(sink.Messages, m => m.Contains("bad binding 42", StringComparison.Ordinal));
        Assert.Contains(sink.Messages, m => m.Contains("worse binding", StringComparison.Ordinal));
    }

    [Fact]
    public void Dispose_RestoresPreviousSink()
    {
        ILogSink? original = Logger.Sink;

        using (var _ = new BindingErrorSink())
        {
            Assert.NotSame(original, Logger.Sink);
        }

        Assert.Same(original, Logger.Sink);
    }
}
