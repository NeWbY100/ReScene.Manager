using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.ViewModels;
using ReScene.App.Core.ViewModels.Wizards;
using ReScene.Manager.Views;
using ReScene.Manager.Views.Wizards;

namespace ReScene.Manager.Tests;

/// <summary>
/// The two Reconstructor outcomes the accessibility gate found to be announced through no channel
/// at all: the custom-packer warning, and the Import/Export Configuration result.
/// <para>
/// Both follow the pattern <c>SaveLogStatus</c> established across all seven Save-log surfaces and
/// <c>SRSReconstructorView</c>'s <c>ResultStatus</c> extends: an ALWAYS-IN-TREE TextBlock with
/// <c>AutomationProperties.LiveSetting=Polite</c>, empty when idle (empty text renders nothing), no
/// explicit name because the announced name IS the text. The reason it must be always-in-tree is
/// the whole point — an element that is added, or made visible, at the moment its text arrives was
/// not realized when the change happened, so there is no empty-to-text transition for an assistive
/// technology to notice. The custom-packer BORDER is exactly that shape (IsVisible-bound), which is
/// why it stays announcement-free on BOTH surfaces and its text is mirrored into a live line
/// elsewhere: the log header on the Advanced tab, and the Import row on the wizard's step 0, which
/// has no log header. Same pattern, different host — see the wizard test's own doc for why that
/// row is the right home and what the alternative was measured to cost.
/// </para>
/// </summary>
public class ReconstructorAnnouncementTests
{
    private static (Window Window, ReconstructorViewModel Vm) Host()
    {
        BeginnerShellViewModel shell = BeginnerShellTestFactory.Create();
        ReconstructorViewModel vm = shell.Reconstructor;
        var window = new Window { Width = 1000, Height = 800, Content = new ReconstructorView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static TextBlock Status(Window window, string testId) =>
        window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == testId);

    private static void AssertLiveAndIdle(TextBlock status)
    {
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
        Assert.True(status.IsVisible, "the live line must be in the tree BEFORE its text arrives, not shown with it");
        Assert.True(string.IsNullOrEmpty(status.Text));
        Assert.Null(AutomationProperties.GetName(status));
    }

    /// <summary>
    /// The custom-packer warning reaches a live region. Also asserts the thing that was actually
    /// broken: the visible Border carrying the same text toggles IsVisible and therefore carries no
    /// LiveSetting of its own — if someone "fixes" the announcement by putting LiveSetting on the
    /// banner instead, this fails and says why.
    /// </summary>
    [AvaloniaFact]
    public void CustomPackerWarning_AnnouncesThroughAnAlwaysInTreeLiveLine_NotTheVisibleBanner()
    {
        using var sink = new BindingErrorSink();
        (Window window, ReconstructorViewModel vm) = Host();
        try
        {
            const string Warning = "Custom RAR packer detected: reconstruction may not be byte-identical.";

            TextBlock status = Status(window, "CustomPackerStatus");
            AssertLiveAndIdle(status);
            Assert.False(vm.HasCustomPackerWarning, "precondition: no warning yet, so the banner is collapsed");

            vm.CustomPackerWarning = Warning;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Warning, status.Text);

            // The banner is resolved by the text it now displays — the same text, in a different
            // element — rather than by markup shape, so this cannot drift with the layout.
            Border banner = window.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Child is TextBlock t && t.Text == Warning);
            Assert.True(banner.IsVisible, "the visual banner still appears — the live line supplements it, it does not replace it");
            Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(banner));
            Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting((TextBlock)banner.Child!));

            Assert.Empty(sink.Messages);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The Import/Export Configuration outcome reaches a live region. Both commands previously
    /// reported ONLY into <c>LogEntries</c>, which is deliberately not a live region, so a screen
    /// reader was told nothing whether the import succeeded or failed.
    /// </summary>
    [AvaloniaFact]
    public void ConfigOutcome_AnnouncesThroughAnAlwaysInTreeLiveLine()
    {
        using var sink = new BindingErrorSink();
        (Window window, ReconstructorViewModel vm) = Host();
        try
        {
            TextBlock status = Status(window, "ConfigStatus");
            AssertLiveAndIdle(status);

            vm.ConfigAnnouncement = "Configuration imported from reconstructor-config.json";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Configuration imported from reconstructor-config.json", status.Text);

            Assert.Empty(sink.Messages);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The SAME defect on the Beginner surface: <c>ReconstructWizardBody</c>'s custom-packer banner
    /// toggles <c>IsVisible</c>, so it announced nothing at all. Recorded when the Advanced tab's
    /// was fixed and deferred with the obstacle "step 0 has no neutral home for a live line" — the
    /// Advanced fix works by mirroring the text into the LOG HEADER, deliberately away from the
    /// banner, and this step has no log header.
    /// <para>
    /// The home is the Import row. The button and a live outcome line share it, which is exactly
    /// what <c>SRSReconstructorView</c> does with <c>ResultStatus</c> beside Save-log and what the
    /// Advanced Reconstructor does with its own <c>CustomPackerStatus</c> — an established pattern
    /// rather than a new one. The alternative of giving the live line its own block was MEASURED to
    /// cost 31 DIPs of permanent empty space, because an empty <see cref="TextBlock"/> still
    /// reserves a line box; in this row the button sets the height and an empty line costs nothing,
    /// which is asserted below rather than asserted about.
    /// </para>
    /// <para>
    /// No ViewModel change was needed and that was CHECKED, not assumed: both surfaces drive the
    /// same <c>ImportSRRCommand</c> on the same <c>ReconstructorViewModel</c> instance, whose
    /// clear-at-start already re-arms the empty-to-text transition so a second import carrying the
    /// same warning still announces. <c>ReScene.App.Core.Tests</c>'
    /// <c>FailedSRRImport_ClearsAPreviousCustomPackerWarning</c> pins that clear.
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void WizardCustomPackerWarning_AnnouncesThroughAnAlwaysInTreeLiveLine_AtNoLayoutCost()
    {
        using var sink = new BindingErrorSink();
        BeginnerShellViewModel shell = BeginnerShellTestFactory.Create();
        ReconstructorViewModel vm = shell.Reconstructor;
        var wizard = new WizardViewModel("Reconstruct", vm,
            [.. Enumerable.Range(0, 3).Select(i => new WizardStep { Title = $"step {i}" })]);
        var window = new Window { Width = 900, Height = 760, DataContext = wizard, Content = new ReconstructWizardBody { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            TextBlock status = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "CustomPackerStatus");
            AssertLiveAndIdle(status);

            var row = (DockPanel)status.GetVisualParent()!;
            double idleRowHeight = row.Bounds.Height;
            Button import = row.Children.OfType<Button>().Single();
            Assert.Equal(import.Bounds.Height, idleRowHeight, precision: 1);

            const string Warning = "Custom RAR packer detected: reconstruction may not be byte-identical.";
            vm.CustomPackerWarning = Warning;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Warning, status.Text);
            Assert.Equal(idleRowHeight, row.Bounds.Height, precision: 1);

            // The visible banner still appears and still says nothing — the live line supplements
            // it. If someone "fixes" the announcement by putting LiveSetting on the banner instead,
            // this fails and says why.
            Border banner = window.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Child is TextBlock t && t.Text == Warning);
            Assert.True(banner.IsVisible);
            Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(banner));
            Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting((TextBlock)banner.Child!));

            Assert.Empty(sink.Messages);
        }
        finally { window.Close(); }
    }

    /// <summary>
    /// The three live lines coexist without any of them being starved to zero width. This is the
    /// specific failure <c>SRSReconstructorView</c> hit and documented when it paired two of them:
    /// several same-direction docked TextBlocks left DockPanel's remaining-rect bookkeeping
    /// unpredictable under long text, and an Auto column can stop tracking its child's DesiredSize.
    /// Fixed-proportion Star columns are immune to both, and that is asserted here with all three
    /// carrying long text at once rather than assumed from the markup.
    /// </summary>
    [AvaloniaFact]
    public void AllThreeLiveLines_KeepANonZeroShare_WithLongTextInEachAtOnce()
    {
        (Window window, ReconstructorViewModel vm) = Host();
        try
        {
            string longText = string.Join(" ", Enumerable.Repeat("a fairly long outcome sentence", 6));
            vm.CustomPackerWarning = longText;
            vm.ConfigAnnouncement = longText;
            vm.SaveLogAnnouncement = longText;
            Dispatcher.UIThread.RunJobs();

            foreach (string id in new[] { "CustomPackerStatus", "ConfigStatus", "SaveLogStatus" })
            {
                TextBlock status = Status(window, id);
                Assert.True(status.Bounds.Width > 0, $"{id} was arranged at zero width, so its text is invisible");
                Assert.Equal(longText, status.Text);
            }
        }
        finally { window.Close(); }
    }
}
