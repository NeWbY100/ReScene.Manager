namespace ReScene.NET.ViewModels;

/// <summary>
/// Holds references to the shared task ViewModels used by the Beginner hub. The hub opens a
/// pop-up wizard per card (see <c>BeginnerWizardFactory</c>); navigation now lives in the wizard.
/// </summary>
public partial class BeginnerShellViewModel : ViewModelBase
{
    // Shared task ViewModels, assigned by MainWindowViewModel via object initializer.
    public required CreatorViewModel CreateSRRWizard { get; init; }
    public required SRSCreatorViewModel SRSCreator { get; init; }
    public required ReconstructorViewModel Reconstructor { get; init; }
    public required BeginnerRestoreViewModel Restore { get; init; }
    public required SRREditorViewModel SRREditor { get; init; }
}
