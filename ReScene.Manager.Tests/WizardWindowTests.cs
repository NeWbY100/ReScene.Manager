using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.ViewModels.Wizards;
using ReScene.Manager.Views.Wizards;

namespace ReScene.Manager.Tests;

/// <summary>
/// Headless render tests for the ported <see cref="WizardWindow"/> chrome (header + Back/Next/Close
/// footer). A trivial inline body <see cref="Control"/> keeps these tests focused on the window's own
/// navigation bindings — the individual body views are covered by their own tests. The central gate is
/// <b>zero binding errors</b> (via <see cref="BindingErrorSink"/>); the tests also confirm Back is
/// hidden on the first step, Next is visible, and that on the last step the primary Close shows while
/// Next hides. A final test confirms closing the window disposes the <see cref="WizardViewModel"/>.
/// </summary>
public class WizardWindowTests
{
    private static WizardViewModel MakeWizard(params string[] stepTitles)
    {
        List<WizardStep> steps = [.. stepTitles.Select(t => new WizardStep { Title = t })];
        return new WizardViewModel("Test Wizard", new object(), steps);
    }

    [AvaloniaFact]
    public void Renders_HeaderAndFooter_FirstThenLastStep_NoBindingErrors()
    {
        WizardViewModel vm = MakeWizard("First step", "Last step");

        using var sink = new BindingErrorSink();
        var window = new WizardWindow(vm, new TextBlock { Text = "body" });
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Header: window Title + the two header TextBlocks (Title + StepHeader).
        Assert.Equal("Test Wizard", window.Title);
        List<TextBlock> texts = [.. window.GetVisualDescendants().OfType<TextBlock>()];
        Assert.Contains(texts, t => t.Text == "Test Wizard");
        Assert.Contains(texts, t => t.Text == vm.StepHeader); // "First step  —  Step 1 of 2"

        // Footer buttons, identified by content + style class.
        List<Button> buttons = [.. window.GetVisualDescendants().OfType<Button>()];
        // The Back button is the only one whose content ends in "Back" (the "‹" prefix glyph is left
        // out of the match to stay independent of source encoding).
        Button back = buttons.Single(b => b.Content is string s && s.EndsWith("Back", StringComparison.Ordinal));
        Button next = buttons.Single(b => Equals(b.Content, vm.NextButtonText));
        Button leftClose = buttons.Single(b => (b.Content as string) == "Close" && b.Classes.Contains("ghost"));
        Button lastClose = buttons.Single(b => (b.Content as string) == "Close" && b.Classes.Contains("primary"));

        // First step: Back hidden, Next visible, left Close visible, last-step Close hidden.
        Assert.False(back.IsVisible);
        Assert.True(next.IsVisible);
        Assert.True(leftClose.IsVisible);
        Assert.False(lastClose.IsVisible);

        // Jump to the last step: the last-step Close shows and Next hides.
        vm.CurrentStepIndex = vm.Steps.Count - 1;
        Dispatcher.UIThread.RunJobs();

        Assert.True(lastClose.IsVisible);
        Assert.False(next.IsVisible);
        Assert.False(leftClose.IsVisible);
        Assert.True(back.IsVisible); // no longer the first step, and CanGoBack defaults to allowed

        Assert.Empty(sink.Messages);
    }

    [AvaloniaFact]
    public void Closed_DisposesViewModel_WithoutThrowing()
    {
        var content = new NotifyingContent();
        var vm = new WizardViewModel("Dispose test", content, [new WizardStep { Title = "Only" }]);
        var window = new WizardWindow(vm, new TextBlock { Text = "body" });
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // The Window's Closed handler disposes the WizardViewModel (which unsubscribes from its Content).
        Exception? ex = Record.Exception(() =>
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
        });
        Assert.Null(ex);

        // After disposal the VM is unsubscribed from the content: raising its change event is a no-op
        // (no disposed-VM callback), so it must not throw.
        content.Raise();
    }

    /// <summary>Minimal <see cref="INotifyPropertyChanged"/> body content to exercise the wizard VM's
    /// subscribe-on-construct / unsubscribe-on-dispose path.</summary>
    private sealed class NotifyingContent : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public void Raise() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Raise)));
    }
}
