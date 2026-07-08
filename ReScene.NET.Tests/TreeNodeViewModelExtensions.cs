using ReScene.App.Core.ViewModels;
using ReScene.NET.ViewModels;

namespace ReScene.NET.Tests;

/// <summary>Tree helpers shared by tests that walk a <see cref="TreeNodeViewModel"/> structure.</summary>
internal static class TreeNodeViewModelExtensions
{
    /// <summary>Depth-first flatten of a node forest (each node followed by its descendants).</summary>
    public static IEnumerable<TreeNodeViewModel> Flatten(this IEnumerable<TreeNodeViewModel> nodes)
    {
        foreach (TreeNodeViewModel node in nodes)
        {
            yield return node;
            foreach (TreeNodeViewModel child in node.Children.Flatten())
            {
                yield return child;
            }
        }
    }
}
