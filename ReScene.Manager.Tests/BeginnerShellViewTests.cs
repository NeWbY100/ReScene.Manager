using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Views;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported Beginner card hub (<see cref="BeginnerShellView"/>). The
/// central gate is <b>zero binding errors</b> (via <see cref="BindingErrorSink"/>) when the hub renders
/// against a fully-assembled <see cref="BeginnerShellViewModel"/>, plus that every hub card
/// (<c>Classes="hubCard"</c>) carries a <see cref="BeginnerCard"/> in its <c>Tag</c> and that the cards
/// cover all five card kinds. Actually clicking a card opens a <c>WizardWindow</c> — that live path is
/// the controller's launch-smoke and is not exercised here.
/// </summary>
/// <remarks>
/// The hub renders <b>five</b> cards, one per <see cref="BeginnerCard"/> value (CreateSRR + EditSRR,
/// CreateSRS + Restore, Reconstruct), faithfully mirroring the WPF <c>BeginnerShellView.xaml</c>. (The
/// T5.2 brief's prose says "6-card hub"; that count is a brief typo — the WPF source and the
/// <see cref="BeginnerCard"/> enum both define five.)
/// </remarks>
public class BeginnerShellViewTests
{
    [AvaloniaFact]
    public void Renders_AllHubCards_WithBeginnerCardTags_NoBindingErrors()
    {
        BeginnerShellViewModel shell = BeginnerShellTestFactory.Create();

        using var sink = new BindingErrorSink();
        var view = new BeginnerShellView { DataContext = shell };
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Button[] cards = [.. window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("hubCard"))];

        Assert.Equal(5, cards.Length);
        Assert.All(cards, c => Assert.IsType<BeginnerCard>(c.Tag));

        var tags = cards.Select(c => (BeginnerCard)c.Tag!).ToHashSet();
        Assert.Contains(BeginnerCard.CreateSRR, tags);
        Assert.Contains(BeginnerCard.EditSRR, tags);
        Assert.Contains(BeginnerCard.CreateSRS, tags);
        Assert.Contains(BeginnerCard.Restore, tags);
        Assert.Contains(BeginnerCard.Reconstruct, tags);

        Assert.Empty(sink.Messages);
    }
}
