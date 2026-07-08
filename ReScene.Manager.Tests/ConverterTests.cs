using System.Globalization;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using ReScene.App.Core.Models;
using ReScene.Manager.Converters;

namespace ReScene.Manager.Tests;

public class ConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(FieldState.Ok, "✓")]
    [InlineData(FieldState.Info, "ℹ")]
    [InlineData(FieldState.Warning, "⚠")]
    [InlineData(FieldState.Error, "✗")]
    [InlineData(FieldState.None, "")]
    public void FieldStateToGlyph_ReturnsExpectedGlyph(FieldState state, string expected)
    {
        var converter = new FieldStateToGlyphConverter();

        object? result = converter.Convert(state, typeof(string), null, Culture);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void FieldStateToGlyph_NonFieldStateValue_ReturnsEmpty()
    {
        var converter = new FieldStateToGlyphConverter();

        object? result = converter.Convert(null, typeof(string), null, Culture);

        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void InverseBool_InvertsBoolean(bool input, bool expected)
    {
        var converter = new InverseBoolConverter();

        object? result = converter.Convert(input, typeof(bool), null, Culture);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void InverseBool_NonBoolValue_DefaultsToTrue()
    {
        var converter = new InverseBoolConverter();

        object? result = converter.Convert("not a bool", typeof(bool), null, Culture);

        Assert.Equal(true, result);
    }

    [Fact]
    public void InverseBool_ConvertBack_InvertsBoolean()
    {
        var converter = new InverseBoolConverter();

        Assert.Equal(false, converter.ConvertBack(true, typeof(bool), null, Culture));
        Assert.Equal(true, converter.ConvertBack(false, typeof(bool), null, Culture));
    }

    [Theory]
    [InlineData(2, "2", true)]
    [InlineData(2, "3", false)]
    [InlineData(0, "0", true)]
    public void IndexEquals_ComparesToStringParameter(int index, string parameter, bool expected)
    {
        var converter = new IndexEqualsConverter();

        object? result = converter.Convert(index, typeof(bool), parameter, Culture);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void IndexEquals_IntParameter_AlsoMatches()
    {
        var converter = new IndexEqualsConverter();

        object? result = converter.Convert(5, typeof(bool), 5, Culture);

        Assert.Equal(true, result);
    }

    [Fact]
    public void IndexEquals_NonIntValue_ReturnsFalse()
    {
        var converter = new IndexEqualsConverter();

        object? result = converter.Convert("nope", typeof(bool), "0", Culture);

        Assert.Equal(false, result);
    }

    [AvaloniaFact]
    public void FieldStateToBrush_GlyphMode_ReturnsAccentBrushPerState()
    {
        var converter = new FieldStateToBrushConverter();

        var ok = (ISolidColorBrush)converter.Convert(FieldState.Ok, typeof(IBrush), null, Culture)!;
        var warning = (ISolidColorBrush)converter.Convert(FieldState.Warning, typeof(IBrush), null, Culture)!;

        Assert.Equal(Color.Parse("#FF1ABC9C"), ok.Color); // AccentSuccess
        Assert.Equal(Color.Parse("#FFFFC107"), warning.Color); // AccentWarning
    }

    [AvaloniaFact]
    public void FieldStateToBrush_MessageMode_OnlyWarningAndErrorOverrideSecondary()
    {
        var converter = new FieldStateToBrushConverter();

        var ok = (ISolidColorBrush)converter.Convert(FieldState.Ok, typeof(IBrush), "Message", Culture)!;
        var error = (ISolidColorBrush)converter.Convert(FieldState.Error, typeof(IBrush), "Message", Culture)!;

        Assert.Equal(Color.Parse("#FF9E9E9E"), ok.Color); // ForegroundSecondary
        Assert.Equal(Color.Parse("#FFF44747"), error.Color); // AccentError
    }
}
