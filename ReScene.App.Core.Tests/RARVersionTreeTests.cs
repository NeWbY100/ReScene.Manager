using ReScene.App.Core.ViewModels.Reconstruction;

namespace ReScene.App.Core.Tests;

public sealed class RARVersionTreeTests
{
    private static RARVersionGroup MakeGroup(int major, params (int v, bool ticked)[] leaves)
    {
        var list = leaves
            .Select(l => new RARVersionLeaf(l.v, $"winrar-{l.v}") { IsChecked = l.ticked })
            .ToList();
        return new RARVersionGroup(major, list);
    }

    [Fact]
    public void Leaf_LabelDerivedFromVersion()
    {
        Assert.Equal("5.60", new RARVersionLeaf(560, "winrar-560").Label);
        Assert.Equal("7.00", new RARVersionLeaf(700, "winrar-700").Label);
        Assert.Equal("6.24", new RARVersionLeaf(624, "winrar-624").Label);
    }

    [Fact]
    public void Leaf_LabelWithTag_AppendsTagOnlyWhenPresent()
    {
        Assert.Equal("2.50", new RARVersionLeaf(250, "winrar-250").LabelWithTag);
        Assert.Equal("2.50 beta1", new RARVersionLeaf(250, "winrar-250-beta1", "beta1").LabelWithTag);
    }

    [Fact]
    public void Leaf_FolderDisplay_WrapsFolderNameInParentheses() => Assert.Equal("(winrar-250-beta1)", new RARVersionLeaf(250, "winrar-250-beta1", "beta1").FolderDisplay);

    [Fact]
    public void Group_IsExpanded_InitialisedFromTickState()
    {
        Assert.False(MakeGroup(2, (200, false), (250, false)).IsExpanded);  // nothing ticked -> collapsed
        Assert.True(MakeGroup(3, (300, false), (320, true)).IsExpanded);    // any tick -> expanded
    }

    [Fact]
    public void Group_IsExpanded_UserToggleIsWritable()
    {
        RARVersionGroup g = MakeGroup(2, (200, false));

        g.IsExpanded = true;

        Assert.True(g.IsExpanded);
    }

    [Fact]
    public void Group_IsChecked_ReflectsLeafState()
    {
        Assert.True(MakeGroup(5, (500, true), (560, true)).IsChecked);
        Assert.False(MakeGroup(5, (500, false), (560, false)).IsChecked);
        Assert.Null(MakeGroup(5, (500, true), (560, false)).IsChecked);
    }

    [Fact]
    public void Group_CountText_CountsTickedOverTotal() => Assert.Equal("(1 of 2)", MakeGroup(5, (500, true), (560, false)).CountText);

    [Fact]
    public void Group_LeafToggle_RaisesSelectionChangedAndRecomputes()
    {
        RARVersionGroup g = MakeGroup(5, (500, false), (560, false));
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
        RARVersionGroup g = MakeGroup(5, (500, false), (560, false));

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
        RARVersionGroup g = MakeGroup(5, (500, true), (560, false));  // indeterminate

        g.ToggleAllCommand.Execute(null);

        Assert.True(g.IsChecked);
    }
}
