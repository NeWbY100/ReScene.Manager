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
    public SampleRestorerView() => AvaloniaXamlLoader.Load(this);
}
