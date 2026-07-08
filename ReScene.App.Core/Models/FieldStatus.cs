namespace ReScene.App.Core.Models;

/// <summary>
/// Per-field guidance shown beneath an input: a severity plus a short message.
/// </summary>
public sealed record FieldStatus(FieldState State, string Message)
{
    /// <summary>A hidden, empty status.</summary>
    public static readonly FieldStatus None = new(FieldState.None, string.Empty);

    /// <summary>Creates an <see cref="FieldState.Ok"/> status with the given message.</summary>
    public static FieldStatus Ok(string message) => new(FieldState.Ok, message);
    /// <summary>Creates an <see cref="FieldState.Info"/> status with the given message.</summary>
    public static FieldStatus Info(string message) => new(FieldState.Info, message);
    /// <summary>Creates a <see cref="FieldState.Warning"/> status with the given message.</summary>
    public static FieldStatus Warning(string message) => new(FieldState.Warning, message);
    /// <summary>Creates an <see cref="FieldState.Error"/> status with the given message.</summary>
    public static FieldStatus Error(string message) => new(FieldState.Error, message);
}
