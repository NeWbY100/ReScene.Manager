using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ReScene.App.Core.Helpers;
using ReScene.App.Core.ViewModels;
using ReScene.App.Core.ViewModels.Wizards;
using ReScene.Manager.Views.Wizards;

namespace ReScene.Manager.Tests;

/// <summary>
/// The Restore wizard's single-sample rebuild reported its outcome in a banner that toggles
/// <c>IsVisible</c>, and nothing else — so the SAME rebuild, driven by the SAME ViewModel property,
/// announced itself aloud on the Advanced tab (<c>SRSReconstructorView.ResultStatus</c>) and silently
/// in the wizard. That asymmetry is the bug: identical work must report identically (WCAG 3.2.4), and
/// an element not realized when its text arrives announces nothing at all (4.1.3).
/// </summary>
public class RestoreAnnouncementTests
{
    private const string Summary = "CRC32 match: A1B2C3D4 (734,003,200 bytes)";

    [AvaloniaFact]
    public void SingleRebuildResult_AnnouncesThroughAnAlwaysInTreeLiveLine_AtNoLayoutCost()
    {
        BeginnerShellViewModel shell = BeginnerShellTestFactory.Create();
        BeginnerRestoreViewModel vm = shell.Restore;
        var wizard = new WizardViewModel("Restore a sample", vm,
            [.. Enumerable.Range(0, 3).Select(i => new WizardStep { Title = $"s{i}" })]);
        var window = new WizardWindow(wizard, new RestoreWizardBody()) { Width = 900, Height = 760 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            // The single sub-flow only exists once an .srs has been routed to it.
            vm.Kind = SampleRestoreKind.SRS;
            wizard.CurrentStepIndex = 2;
            Dispatcher.UIThread.RunJobs();

            TextBlock status = window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ResultStatus");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            Assert.Equal(string.Empty, status.Text);
            Assert.True(status.IsEffectivelyVisible, "the live line must be realized BEFORE the result arrives");

            var row = (DockPanel)status.GetVisualParent()!;
            TextBlock caption = row.Children.OfType<TextBlock>().Single(t => t.Text == "Details");
            double idleRowHeight = row.Bounds.Height;
            Assert.Equal(caption.Bounds.Height, idleRowHeight, precision: 1);

            vm.SingleRebuilder!.ResultSummary = Summary;
            vm.SingleRebuilder.ShowResult = true;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Summary, status.Text);
            Assert.Equal(idleRowHeight, row.Bounds.Height, precision: 1);

            // The visible banner still appears and still says nothing. If someone "fixes" the
            // announcement by putting LiveSetting on the banner instead — where it cannot fire,
            // because the banner is not in the tree when the text lands — this fails and says so.
            Border banner = window.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Child is TextBlock t && t.Text == Summary);
            Assert.True(banner.IsVisible);
            Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting(banner));
            Assert.Equal(AutomationLiveSetting.Off, AutomationProperties.GetLiveSetting((TextBlock)banner.Child!));
        }
        finally { window.Close(); }
    }
}
