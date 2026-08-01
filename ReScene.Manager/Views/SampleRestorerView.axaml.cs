using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace ReScene.Manager.Views;

/// <summary>
/// The SRS Restorer tab, ported from the WPF <c>ReScene.NET.Views.SampleRestorerView</c>. Bound to a
/// <see cref="ReScene.App.Core.ViewModels.SampleRestorerViewModel"/> (supplied by the shell via
/// <c>DataContext="{Binding SampleRestorer}"</c>): an SRR-file row, a Media Directory row, an Output
/// Directory row, an editable <see cref="DataGrid"/> of the SRR's embedded SRS entries (a
/// <c>DataGridCheckBoxColumn</c> toggling which samples to restore, plus an editable Media File
/// column for entries the automatic match missed), a Restore All/Cancel action row with progress, and
/// a log. Path TextBox file/folder drop is declarative via
/// <c>behaviors:TextBoxDropBehavior.DropMode</c> in the XAML (the WPF original wired it imperatively
/// in <c>Loaded</c> via <c>TextBoxDropHelper</c>, which had no such attached property). Unlike the SRR
/// Creator's "Stored As" column, the grid's editable Media File column needs no inline-edit dedup
/// guard, so no <c>BeginningEdit</c>/<c>CellEditEnding</c> code-behind is required here.
/// </summary>
public partial class SampleRestorerView : UserControl
{
    public SampleRestorerView()
    {
        AvaloniaXamlLoader.Load(this);

        // Small-window layout degradation (spec rev 12 §1/§2): compact below 535 inner DIPs — the
        // headline defect view (action row + log measured 0px at 700×450 BASE state under the
        // pre-conversion DockPanel). x:CompileBindings="False" means x:Name elements are NOT
        // wired to auto-generated fields here (same as every other ported view in this project) —
        // resolved once via FindControl instead.
        Grid root = (Grid)Content!;
        Expander helpDisclosure = this.FindControl<Expander>("HelpDisclosure")!;
        TextBox srrFileTextBox = this.FindControl<TextBox>("SRRFileTextBox")!;
        Behaviors.CompactHeightBehavior.SetThreshold(root, 535);
        Behaviors.CompactHeightBehavior.SetRowSizes(root,
            [new Behaviors.CompactRowSize(RowIndex: 1, NormalHeight: double.NaN,
                CompactMinHeight: 110, HelpOpenMinHeight: 80, Mode: Behaviors.CompactRowMode.AutoToStar)]);
        Behaviors.CompactHeightBehavior.SetHelpExpander(root, helpDisclosure);
        Behaviors.CompactHeightBehavior.SetHelpBodyMaxHeight(root, 40);
        Behaviors.CompactHeightBehavior.SetRestoreFocusTarget(root, srrFileTextBox);
    }
}
