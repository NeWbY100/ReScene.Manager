using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace ReScene.Manager.Behaviors;

/// <summary>
/// Attached behavior providing drag-and-drop file/folder support for <see cref="TextBox"/>
/// controls: dropping a path sets <see cref="TextBox.Text"/> directly. Replaces the WPF
/// <c>TextBoxDropHelper</c>, whose <c>SetupFileDrop</c>/<c>SetupFolderDrop</c> static methods took
/// an explicit setter callback — here the callback is unnecessary because attached properties can
/// only ever act on their own host control's <see cref="TextBox.Text"/>.
/// </summary>
public static class TextBoxDropBehavior
{
    /// <summary>What a dropped path is interpreted as.</summary>
    public enum DropMode
    {
        /// <summary>Drag-and-drop is disabled.</summary>
        None,

        /// <summary>The dropped path is used verbatim.</summary>
        File,

        /// <summary>
        /// A dropped folder is used verbatim; a dropped file resolves to its containing folder.
        /// </summary>
        Folder,
    }

    /// <summary>Configures the kind of path drag-and-drop accepts onto the TextBox.</summary>
    public static readonly AttachedProperty<DropMode> DropModeProperty =
        AvaloniaProperty.RegisterAttached<TextBox, DropMode>("DropMode", typeof(TextBoxDropBehavior));

    public static DropMode GetDropMode(TextBox obj) => obj.GetValue(DropModeProperty);

    public static void SetDropMode(TextBox obj, DropMode value) => obj.SetValue(DropModeProperty, value);

    static TextBoxDropBehavior() => DropModeProperty.Changed.AddClassHandler<TextBox>(OnDropModeChanged);

    private static void OnDropModeChanged(TextBox textBox, AvaloniaPropertyChangedEventArgs e)
    {
        // Always detach first so toggling the mode never double-subscribes.
        textBox.RemoveHandler(DragDrop.DragOverEvent, OnDragOver);
        textBox.RemoveHandler(DragDrop.DropEvent, OnDrop);

        DropMode mode = e.NewValue is DropMode m ? m : DropMode.None;
        DragDrop.SetAllowDrop(textBox, mode != DropMode.None);

        if (mode != DropMode.None)
        {
            textBox.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            textBox.AddHandler(DragDrop.DropEvent, OnDrop);
        }
    }

    /// <summary>Shows the Copy cursor for file/folder drops and None otherwise.</summary>
    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not TextBox textBox)
        {
            return;
        }

        string? path = e.DataTransfer.TryGetFile()?.TryGetLocalPath();
        if (path is null)
        {
            e.Handled = true;
            return;
        }

        if (GetDropMode(textBox) == DropMode.Folder && !Directory.Exists(path))
        {
            path = Path.GetDirectoryName(path) ?? path;
        }

        textBox.Text = path;
        e.Handled = true;
    }
}
