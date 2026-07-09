using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core;
using ReScene.Manager.Views;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported <see cref="AboutWindow"/>. The central gate is
/// <b>zero binding errors</b> (via <see cref="BindingErrorSink"/>) when the window renders, plus the
/// version text, the "About {AppInfo.DisplayName}" title, and the three GitHub/Wiki links. Actually
/// following a link (which would launch a real browser via <c>SystemLauncherService</c>) is the
/// controller's Phase-4 launch-smoke, not exercised here.
/// </summary>
/// <remarks>
/// Shares the "AppDataConfig" collection with <see cref="AppInfoTests"/>: that class temporarily
/// mutates the shared <see cref="AppInfo.DisplayName"/> static (restoring it in a finally), so this
/// class — which only reads it — must not run concurrently with that mutation.
/// </remarks>
[Collection("AppDataConfig")]
public class AboutWindowTests
{
    [AvaloniaFact]
    public void Renders_VersionAndTitle_NoBindingErrors()
    {
        string expectedName = AppInfo.DisplayName;

        using var sink = new BindingErrorSink();
        var window = new AboutWindow("1.6.0");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal($"About {expectedName}", window.Title);

        TextBlock versionBlock = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text is not null && t.Text.Contains("1.6.0", StringComparison.Ordinal));
        Assert.Equal("Version 1.6.0", versionBlock.Text);

        TextBlock nameBlock = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == expectedName);
        Assert.Equal(expectedName, nameBlock.Text);

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void Renders_AllThreeLinks_ViaLauncherStyledButtons()
    {
        var window = new AboutWindow("2.0.0");
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Button[] links = [.. window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("link"))];

        Assert.Equal(3, links.Length);
        Assert.Contains(links, b => (string?)b.Tag == "https://github.com/NeWbY100/ReScene.NET");
        Assert.Contains(links, b => (string?)b.Tag == "https://github.com/NeWbY100/ReScene.Lib");
        Assert.Contains(links, b => (string?)b.Tag == "https://rescene.wikidot.com");
    }
}
