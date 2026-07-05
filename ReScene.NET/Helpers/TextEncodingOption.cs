using System.Text;

namespace ReScene.NET.Helpers;

/// <summary>A selectable text encoding: a human-friendly name plus the backing <see cref="Encoding"/>.</summary>
public sealed record TextEncodingOption(string DisplayName, Encoding Encoding)
{
    // The themed ComboBox's selection box falls back to ToString() (DisplayMemberPath only drives
    // the dropdown items), so present the friendly name rather than the record's default rendering.
    public override string ToString() => DisplayName;
}
