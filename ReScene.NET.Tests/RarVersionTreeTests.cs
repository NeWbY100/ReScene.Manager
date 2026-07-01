using ReScene.NET.ViewModels.Reconstruction;

namespace ReScene.NET.Tests;

public sealed class RarVersionTreeTests
{
    private static RarVersionGroup MakeGroup(int major, params (int v, bool ticked)[] leaves)
    {
        var list = leaves
            .Select(l => new RarVersionLeaf(l.v, $"winrar-{l.v}") { IsChecked = l.ticked })
            .ToList();
        return new RarVersionGroup(major, list);
    }

    [Fact]
    public void Leaf_LabelDerivedFromVersion()
    {
        Assert.Equal("5.60", new RarVersionLeaf(560, "winrar-560").Label);
        Assert.Equal("7.00", new RarVersionLeaf(700, "winrar-700").Label);
        Assert.Equal("6.24", new RarVersionLeaf(624, "winrar-624").Label);
    }

    [Fact]
    public void Group_IsChecked_ReflectsLeafState()
    {
        Assert.True(MakeGroup(5, (500, true), (560, true)).IsChecked);
        Assert.False(MakeGroup(5, (500, false), (560, false)).IsChecked);
        Assert.Null(MakeGroup(5, (500, true), (560, false)).IsChecked);
    }

    [Fact]
    public void Group_CountText_CountsTickedOverTotal()
    {
        Assert.Equal("(1 of 2)", MakeGroup(5, (500, true), (560, false)).CountText);
    }

    [Fact]
    public void Group_LeafToggle_RaisesSelectionChangedAndRecomputes()
    {
        RarVersionGroup g = MakeGroup(5, (500, false), (560, false));
        int raised = 0;
        g.SelectionChanged += (_, _) => raised++;

        g.Leaves[0].IsChecked = true;

        Assert.Equal(1, raised);
        Assert.Null(g.IsChecked);
        Assert.Equal("(1 of 2)", g.CountText);
    }

    [Fact]
    public void Group_ToggleAll_FromUncheckedChecksAll_FromCheckedUnchecksAll()
    {
        RarVersionGroup g = MakeGroup(5, (500, false), (560, false));

        g.ToggleAllCommand.Execute(null);          // unchecked -> all checked
        Assert.True(g.IsChecked);
        Assert.All(g.Leaves, l => Assert.True(l.IsChecked));

        g.ToggleAllCommand.Execute(null);          // checked -> all unchecked
        Assert.False(g.IsChecked);
        Assert.All(g.Leaves, l => Assert.False(l.IsChecked));
    }

    [Fact]
    public void Group_ToggleAll_FromIndeterminateChecksAll()
    {
        RarVersionGroup g = MakeGroup(5, (500, true), (560, false));  // indeterminate

        g.ToggleAllCommand.Execute(null);

        Assert.True(g.IsChecked);
    }
}
