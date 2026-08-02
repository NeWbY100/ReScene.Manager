using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ReScene.App.Core.ViewModels;
using ReScene.Manager.Helpers;

namespace ReScene.Manager.Views;

/// <summary>
/// The SRS Creator tab, ported from the WPF <c>ReScene.NET.Views.SRSCreatorView</c>. Bound to a
/// <see cref="SRSCreatorViewModel"/> (supplied by the shell via <c>DataContext="{Binding SRSCreator}"</c>).
/// Path TextBox file-drop is declarative via <c>behaviors:TextBoxDropBehavior.DropMode="File"</c> in
/// the XAML (the WPF original wired it imperatively in <c>Loaded</c> since it had no such attached
/// property). The only remaining code-behind responsibility is opening/closing the shared
/// <see cref="IsoProgressWindowController"/> modal in step with the VM's <c>ISOProcessing</c> flag.
/// </summary>
public partial class SRSCreatorView : UserControl
{
    private IsoProgressWindowController? _isoController;
    private SRSCreatorViewModel? _subscribedVm;

    public SRSCreatorView()
    {
        AvaloniaXamlLoader.Load(this);

        DataContextChanged += OnDataContextChanged;

        // Small-window layout degradation: compact below 520 inner DIPs.
        // x:CompileBindings="False" means x:Name elements are NOT wired to auto-generated fields
        // here (same as every other ported view in this project — see ReconstructorView's own
        // note); resolved once via FindControl instead.
        Grid root = (Grid)Content!;
        Expander helpDisclosure = this.FindControl<Expander>("HelpDisclosure")!;
        TextBox inputTextBox = this.FindControl<TextBox>("InputTextBox")!;
        Behaviors.CompactHeightBehavior.SetThreshold(root, 520);
        Behaviors.CompactHeightBehavior.SetRowSizes(root,
            [new Behaviors.CompactRowSize(RowIndex: 1, NormalHeight: double.NaN,
                CompactMinHeight: 110, HelpOpenMinHeight: 80, Mode: Behaviors.CompactRowMode.AutoToStar)]);
        Behaviors.CompactHeightBehavior.SetHelpExpander(root, helpDisclosure);
        Behaviors.CompactHeightBehavior.SetHelpBodyMaxHeight(root, 40);
        Behaviors.CompactHeightBehavior.SetRestoreFocusTarget(root, inputTextBox);
    }

    // Avalonia's DataContextChanged carries no old/new values (unlike WPF's
    // DependencyPropertyChangedEventArgs), so the previously-subscribed VM is tracked in a field.
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedVm is not null)
        {
            _subscribedVm.PropertyChanged -= OnVmPropertyChanged;
        }

        _isoController = null;
        _subscribedVm = DataContext as SRSCreatorViewModel;

        if (_subscribedVm is not { } vm)
        {
            return;
        }

        // Forward cancellation to the existing generated CancelCreationCommand.
        _isoController = new IsoProgressWindowController(
            this, () => vm.ISOProcessing, () => vm.CancelCreationCommand.Execute(null));
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SRSCreatorViewModel.ISOProcessing))
        {
            return;
        }

        if (sender is SRSCreatorViewModel vm)
        {
            _isoController?.OnProcessingChanged(vm.ISOProcessing);
        }
    }
}
