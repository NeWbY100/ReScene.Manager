using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using ReScene.App.Core.Services;
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
        CreatorViewModel creator = Assert.IsType<CreatorViewModel>(createSrr.Content);
        Assert.False(creator.AutoCreateSRS);
        Assert.False(creator.CreateVobsubSRR);
        Assert.True(creator.ComputeOSOHashes);
        createSrr.Dispose();

        // Reconstruct: beginners want the full archive set, so CompleteAllVolumes is pre-checked.
        (WizardViewModel reconstruct, _) = BeginnerWizardFactory.Create(BeginnerCard.Reconstruct, shell);
        ReconstructorViewModel reconstructor = Assert.IsType<ReconstructorViewModel>(reconstruct.Content);
        Assert.True(reconstructor.CompleteAllVolumes);
        reconstruct.Dispose();
    }

    /// <summary>
    /// Locks the confirm-gate polarity against a future inversion (a flipped gate would silently skip a
    /// warning / overwrite a file). Drives the CreateSRS "Choose where to save" step's <c>ConfirmLeave</c>
    /// through a stub <see cref="IFileDialogService.Confirm"/>: with no full movie selected the no-movie
    /// confirmation fires, and its result must decide whether the step advances (and sets the one-shot
    /// suppress flag). Chosen because this path needs no real files (empty OutputPath skips the overwrite
    /// confirm). A declined confirm must keep the user on the step; an accepted one must advance + suppress.
    /// </summary>
    [AvaloniaFact]
    public void CreateSRS_NoMovieConfirmGate_HonorsConfirmResult_WithoutInversion()
    {
        // Declined → stay on the step, and the suppress flag is NOT set.
        var declining = new ConfirmStub { Result = false };
        (WizardViewModel declinedVm, _) = BeginnerWizardFactory.Create(
            BeginnerCard.CreateSRS, BeginnerShellTestFactory.Create(declining));
        SRSCreatorViewModel declinedSrs = Assert.IsType<SRSCreatorViewModel>(declinedVm.Content);
        Assert.False(declinedSrs.HasValidMainFile);           // no movie → the no-movie confirm fires
        Assert.False(declinedVm.Steps[1].ConfirmLeave!());    // user declined → do not advance
        Assert.False(declinedSrs.SuppressNoMovieConfirm);     // and the one-shot flag stays off
        Assert.Equal(1, declining.Count);
        declinedVm.Dispose();

        // Accepted → advance, and the suppress flag IS set (so Create doesn't re-ask).
        var accepting = new ConfirmStub { Result = true };
        (WizardViewModel acceptedVm, _) = BeginnerWizardFactory.Create(
            BeginnerCard.CreateSRS, BeginnerShellTestFactory.Create(accepting));
        SRSCreatorViewModel acceptedSrs = Assert.IsType<SRSCreatorViewModel>(acceptedVm.Content);
        Assert.True(acceptedVm.Steps[1].ConfirmLeave!());     // user accepted → advance
        Assert.True(acceptedSrs.SuppressNoMovieConfirm);      // and the one-shot flag is set
        acceptedVm.Dispose();
    }

    /// <summary>Stub dialog service whose synchronous <see cref="Confirm"/> returns a fixed result and
    /// counts calls; all other members are inert (never reached by the confirm-gate test).</summary>
    private sealed class ConfirmStub : IFileDialogService
    {
        public bool Result { get; init; }
        public int Count { get; private set; }

        public bool Confirm(string title, string message)
        {
            Count++;
            return Result;
        }

        public Task<bool> ShowConfirmAsync(string title, string message) => Task.FromResult(Result);
        public Task<string?> OpenFileAsync(string title, IReadOnlyList<string> filters) => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<string>> OpenFilesAsync(string title, IReadOnlyList<string> filters) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<string?> SaveFileAsync(string title, string defaultExtension, IReadOnlyList<string> filters, string? defaultFileName = null) => Task.FromResult<string?>(null);
        public Task<string?> OpenFolderAsync(string title) => Task.FromResult<string?>(null);
        public Task<string?> PromptForTextAsync(string title, string message, string initialValue) => Task.FromResult<string?>(null);
        public void ShowError(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowInfo(string title, string message) { }
    }
}
