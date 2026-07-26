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
/// One looped pass over ALL SEVEN Save-log surfaces pinning the live outcome line: a TextBlock
/// named SaveLogStatus, <c>AutomationProperties.LiveSetting=Polite</c> (matching the
/// BruteForceProgressWindow precedent), always in the tree with empty initial text (never
/// visibility-toggled — an element added at event time would not announce), and bound so a VM
/// outcome reaches the text. The two wizard bodies realize their log step first (unselected
/// steps materialize nothing).
/// </summary>
public class SaveLogStatusTests
{
    private static void SetAnnouncement(object vm, string value)
    {
        switch (vm)
        {
            case OperationViewModelBase op:
                op.SaveLogAnnouncement = value;
                break;
            case ReconstructorViewModel rec:
                rec.SaveLogAnnouncement = value;
                break;
            default:
                throw new InvalidOperationException($"Unexpected VM type {vm.GetType().Name}");
        }
    }

    [AvaloniaFact]
    public void AllSevenSurfaces_HaveLiveSaveLogStatusLine()
    {
        BeginnerShellViewModel shell = BeginnerShellTestFactory.Create();

        using var sink = new BindingErrorSink();

        (string Name, Control View, object Vm, int? LogStep)[] cases =
        [
            ("CreatorView", new CreatorView { DataContext = shell.CreateSRRWizard }, shell.CreateSRRWizard, null),
            ("SRSCreatorView", new SRSCreatorView { DataContext = shell.SRSCreator }, shell.SRSCreator, null),
            ("SRSReconstructorView", new SRSReconstructorView { DataContext = shell.Restore.SingleRebuilder }, shell.Restore.SingleRebuilder!, null),
            ("SampleRestorerView", new SampleRestorerView { DataContext = shell.Restore.BulkRestorer }, shell.Restore.BulkRestorer!, null),
            ("ReconstructorView", new ReconstructorView { DataContext = shell.Reconstructor }, shell.Reconstructor, null),
            ("CreateSRRWizardBody", new CreateSRRWizardBody { DataContext = shell.CreateSRRWizard }, shell.CreateSRRWizard, 4),
            ("ReconstructWizardBody", new ReconstructWizardBody { DataContext = shell.Reconstructor }, shell.Reconstructor, 2),
        ];

        foreach ((string name, Control view, object vm, int? logStep) in cases)
        {
            Window window;
            if (logStep is int step)
            {
                // Wizard bodies read the hosting Window's WizardViewModel.CurrentStepIndex.
                var wizard = new WizardViewModel(
                    name, vm,
                    [.. Enumerable.Range(0, step + 1).Select(i => new WizardStep { Title = $"step {i}" })]);
                window = new Window { Width = 1000, Height = 760, DataContext = wizard, Content = view };
                window.Show();
                wizard.CurrentStepIndex = step;
            }
            else
            {
                window = new Window { Width = 1000, Height = 760, Content = view };
                window.Show();
            }

            Dispatcher.UIThread.RunJobs();

            TextBlock status = view.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Name == "SaveLogStatus");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(status));
            Assert.True(status.IsVisible);
            Assert.True(string.IsNullOrEmpty(status.Text));

            SetAnnouncement(vm, $"probe {name}");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal($"probe {name}", status.Text);

            // Reset: ReconstructorView and ReconstructWizardBody share one VM (as do the Creator
            // pair by construction here), so the next case must start blank again.
            SetAnnouncement(vm, string.Empty);
            window.Close();
        }

        Assert.Empty(sink.Messages);
    }
}
