namespace ReScene.App.Core.ViewModels;

/// <summary>
/// Data attached to each tree node in the comparison view, identifying its type and associated block data.
/// </summary>
public class CompareNodeData
{
    /// <summary>
    /// Gets or sets the type of tree node this data represents.
    /// </summary>
    public CompareNodeType NodeType
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the underlying block or file data associated with this node.
    /// </summary>
    public object? Data
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the file name associated with this node, if applicable.
    /// </summary>
    public string? FileName
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets whether this node belongs to the left comparison pane.
    /// </summary>
    public bool IsLeft
    {
        get; set;
    }
}
