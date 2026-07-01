using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ReScene.NET.ViewModels.Reconstruction;

/// <summary>
/// A major-version group (e.g. "5.x") over its installed sub-version leaves. The header check is a
/// display-only tri-state; clicking it checks all leaves unless all are already checked, in which
/// case it unchecks all. Raises <see cref="SelectionChanged"/> on any post-construction change.
/// </summary>
public sealed partial class RarVersionGroup : ObservableObject
{
    public int Major { get; }
    public string Header { get; }
    public IReadOnlyList<RarVersionLeaf> Leaves { get; }

    public event EventHandler? SelectionChanged;

    private bool _bulkUpdating;

    public RarVersionGroup(int major, IReadOnlyList<RarVersionLeaf> leaves)
    {
        Major = major;
        Header = $"{major}.x";
        Leaves = leaves;
        foreach (RarVersionLeaf leaf in Leaves)
        {
            leaf.PropertyChanged += OnLeafChanged;
        }
    }

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
        foreach (RarVersionLeaf leaf in Leaves)
        {
            leaf.IsChecked = target;
        }

        _bulkUpdating = false;
        RaiseStateChanged();
    }

    /// <summary>Unsubscribes leaf handlers before the group is discarded on rebuild.</summary>
    public void Detach()
    {
        foreach (RarVersionLeaf leaf in Leaves)
        {
            leaf.PropertyChanged -= OnLeafChanged;
        }
    }

    private void OnLeafChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RarVersionLeaf.IsChecked) || _bulkUpdating)
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
