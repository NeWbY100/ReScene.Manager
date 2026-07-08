using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ReScene.App.Core.ViewModels.Reconstruction;

/// <summary>
/// A major-version group (e.g. "5.x") over its installed sub-version leaves. The header check is a
/// display-only tri-state; clicking it checks all leaves unless all are already checked, in which
/// case it unchecks all. Raises <see cref="SelectionChanged"/> on any post-construction change.
/// </summary>
public sealed partial class RARVersionGroup : ObservableObject
{
    public int Major { get; }
    public string Header { get; }
    public IReadOnlyList<RARVersionLeaf> Leaves { get; }

    public event EventHandler? SelectionChanged;

    private bool _bulkUpdating;

    public RARVersionGroup(int major, IReadOnlyList<RARVersionLeaf> leaves)
    {
        Major = major;
        Header = $"{major}.x";
        Leaves = leaves;
        IsExpanded = leaves.Any(l => l.IsChecked);
        foreach (RARVersionLeaf leaf in Leaves)
        {
            leaf.PropertyChanged += OnLeafChanged;
        }
    }

    /// <summary>
    /// Whether the group's leaves are shown. Initialised to "any leaf ticked" so an SRR import or
    /// config load auto-expands the relevant groups; the user can toggle freely afterwards (a
    /// rescan/reconcile rebuilds groups and re-derives it from the new tick state).
    /// </summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public bool? IsChecked
    {
        get
        {
            int ticked = Leaves.Count(l => l.IsChecked);
            if (ticked == 0)
            {
                return false;
            }

            return ticked == Leaves.Count ? true : null;
        }
    }

    public string CountText => $"({Leaves.Count(l => l.IsChecked)} of {Leaves.Count})";

    [RelayCommand]
    private void ToggleAll()
    {
        bool target = IsChecked != true;  // all-checked -> uncheck; unchecked/indeterminate -> check
        _bulkUpdating = true;
        foreach (RARVersionLeaf leaf in Leaves)
        {
            leaf.IsChecked = target;
        }

        _bulkUpdating = false;
        RaiseStateChanged();
    }

    /// <summary>Unsubscribes leaf handlers before the group is discarded on rebuild.</summary>
    public void Detach()
    {
        foreach (RARVersionLeaf leaf in Leaves)
        {
            leaf.PropertyChanged -= OnLeafChanged;
        }
    }

    private void OnLeafChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RARVersionLeaf.IsChecked) || _bulkUpdating)
        {
            return;
        }

        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(CountText));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
