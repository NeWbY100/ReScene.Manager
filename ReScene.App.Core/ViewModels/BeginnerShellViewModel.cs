using ReScene.App.Core.Services;
using ReScene.App.Core.ViewModels;
namespace ReScene.App.Core.ViewModels;

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

    /// <summary>
    /// The dialog service the Avalonia Beginner wizard factory uses for its synchronous overwrite/
    /// warning confirms (replacing the WPF factory's <c>MessageBox.Show</c>). Set by
    /// <c>MainWindowViewModel</c>'s Beginner initializer; the WPF factory ignores it.
    /// </summary>
    public required IFileDialogService FileDialog { get; init; }
}
