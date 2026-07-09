using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ReScene.App.Core.ViewModels;
using ReScene.App.Core.ViewModels.Wizards;
using ReScene.Manager.Views.Wizards;

namespace ReScene.Manager.Tests;

/// <summary>
/// Unit tests for the Avalonia <see cref="BeginnerWizardFactory"/>: each <see cref="BeginnerCard"/>
/// must produce the matching body <see cref="Control"/> type and a <see cref="WizardViewModel"/> with
/// the ported <c>Title</c> and step count. These run under <c>[AvaloniaFact]</c> because building a
/// body view calls <c>AvaloniaXamlLoader.Load</c>, which needs the headless app. No window is shown.
/// </summary>
public class BeginnerWizardFactoryTests
{
    [AvaloniaFact]
    public void Create_EachCard_ReturnsExpectedBodyAndWizardShape()
    {
        BeginnerShellViewModel shell = BeginnerShellTestFactory.Create();

        (BeginnerCard Card, Type Body, string Title, int Steps)[] cases =
        [
            (BeginnerCard.CreateSRR, typeof(CreateSRRWizardBody), "Create an SRR", 5),
            (BeginnerCard.CreateSRS, typeof(CreateSRSWizardBody), "Create a sample SRS", 3),
            (BeginnerCard.Reconstruct, typeof(ReconstructWizardBody), "Reconstruct RAR archives", 3),
            (BeginnerCard.Restore, typeof(RestoreWizardBody), "Restore a sample", 3),
            (BeginnerCard.EditSRR, typeof(EditSRRWizardBody), "Edit an SRR", 4),
        ];

        foreach ((BeginnerCard card, Type expectedBody, string expectedTitle, int expectedSteps) in cases)
        {
            (WizardViewModel vm, Control body) = BeginnerWizardFactory.Create(card, shell);

            Assert.IsType(expectedBody, body);
            Assert.Equal(expectedTitle, vm.Title);
            Assert.Equal(expectedSteps, vm.Steps.Count);

            vm.Dispose();
        }
    }

    [AvaloniaFact]
    public void Create_AppliesBeginnerPreSets_FaithfulToWpfFactory()
    {
        BeginnerShellViewModel shell = BeginnerShellTestFactory.Create();

        // CreateSRR: the Manage step generates sample/subtitle files at create time, so the Advanced
        // tab's create-time scan generation is off; OSO hashes are on by default for beginners.
        (WizardViewModel createSrr, _) = BeginnerWizardFactory.Create(BeginnerCard.CreateSRR, shell);
        var creator = Assert.IsType<CreatorViewModel>(createSrr.Content);
        Assert.False(creator.AutoCreateSRS);
        Assert.False(creator.CreateVobsubSRR);
        Assert.True(creator.ComputeOSOHashes);
        createSrr.Dispose();

        // Reconstruct: beginners want the full archive set, so CompleteAllVolumes is pre-checked.
        (WizardViewModel reconstruct, _) = BeginnerWizardFactory.Create(BeginnerCard.Reconstruct, shell);
        var reconstructor = Assert.IsType<ReconstructorViewModel>(reconstruct.Content);
        Assert.True(reconstructor.CompleteAllVolumes);
        reconstruct.Dispose();
    }
}
