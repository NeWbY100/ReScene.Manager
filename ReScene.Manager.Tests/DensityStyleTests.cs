using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;

namespace ReScene.Manager.Tests;

/// <summary>
/// Locks the WPF-density parity pass (Resources/Density.axaml + FluentTheme DensityStyle="Compact"):
/// the Fluent theme's touch metrics (24px pivot tab headers, 32px min-heights, wide paddings) made
/// the ported app render visibly larger than the WPF original. These tests pin the load-bearing
/// overrides on REAL controls under the real App resources, so a theme upgrade or an accidental
/// removal of the Density.axaml merge surfaces here instead of as a silent visual regression.
/// </summary>
public class DensityStyleTests
{
    private static Window Show(Control content)
    {
        var window = new Window { Content = content, SizeToContent = SizeToContent.WidthAndHeight };
        window.Show();
        return window;
    }

    [AvaloniaFact]
    public void TabItem_UsesWpfDensity_NotFluentPivotHeaders()
    {
        using var sink = new BindingErrorSink();

        var tab = new TabItem { Header = "Home" };
        Window window = Show(new TabControl { ItemsSource = null, Items = { tab } });
        try
        {
            // WPF original: FontSize 12, Normal weight, Padding 12,6, no MinHeight
            // (ReScene.NET App.xaml:359-394). Fluent default was 24 SemiLight / MinHeight 48.
            Assert.Equal(12, tab.FontSize);
            Assert.Equal(FontWeight.Normal, tab.FontWeight);
            Assert.Equal(new Avalonia.Thickness(12, 6), tab.Padding);
            Assert.Equal(0, tab.MinHeight);
            Assert.Empty(sink.Messages);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Button_And_CheckBox_UseWpfDensity()
    {
        using var sink = new BindingErrorSink();

        var button = new Button { Content = "Browse..." };
        var checkBox = new CheckBox { Content = "Option" };
        Window window = Show(new StackPanel { Children = { button, checkBox } });
        try
        {
            // WPF: Button Padding 8,4 (App.xaml:61); CheckBox rows ~20px with a 4px glyph→label gap.
            Assert.Equal(new Avalonia.Thickness(8, 4), button.Padding);
            Assert.Equal(20, checkBox.MinHeight);
            Assert.Equal(new Avalonia.Thickness(4, 0, 0, 0), checkBox.Padding);
            Assert.Empty(sink.Messages);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void FontChain_PrefersSegoeUi_WithEmbeddedInterFallback()
    {
        // The UIFontFamily token must lead with Segoe UI (WPF-original optics on Windows) and carry
        // the embedded Inter as the cross-platform fallback. Asserted on the token itself: the
        // resolved primary differs per OS, which is the point of the chain.
        Assert.True(Avalonia.Application.Current!.TryGetResource(
            "UIFontFamily", null, out object? value));
        var family = Assert.IsType<FontFamily>(value);
        // A parsed composite stringifies as "compositefont:Segoe UI, fonts:Inter#Inter, ..." —
        // assert both members are present and Segoe UI comes first.
        string chain = family.ToString();
        int segoe = chain.IndexOf("Segoe UI", StringComparison.Ordinal);
        int inter = chain.IndexOf("Inter", StringComparison.Ordinal);
        Assert.True(segoe >= 0, $"Segoe UI missing from font chain: {chain}");
        Assert.True(inter > segoe, $"Inter must be the fallback after Segoe UI: {chain}");
    }
}
