using Avalonia;
using Avalonia.Controls;

namespace ReScene.Manager.Tests;

/// <summary>
/// Shared floor-measurement for the per-view threshold-invariant tests (spec §1's four
/// one-sum checks). Measures in inner-content DIPs at width 676 (the 700×450 inner width).
/// </summary>
internal static class CompactInvariantRig
{
    public const double InnerBudget = 319;   // measured: 450 − 26 − 58 − 23 − 24
    public const double CiBound = 307;       // InnerBudget − 12 jitter slack (spec §1)
    public const double InnerWidth = 676;

    /// <summary>
    /// The ROW-AWARE floor of an inner Grid (codex round-1 #5: a naive
    /// Measure(∞) reports CONTENT height for star and scrolling rows, not their
    /// minimums): Σ per RowDefinition — star rows contribute MinHeight; pixel rows their
    /// Height; Auto rows the max desired height of their children measured at
    /// InnerWidth×∞ — plus inter-row margins. Callers force conditional rows visible and
    /// set the mode class BEFORE calling.
    /// </summary>
    public static double MeasureFloor(Grid innerRoot)
    {
        innerRoot.Measure(new Size(InnerWidth, double.PositiveInfinity));
        double total = 0;
        for (int i = 0; i < innerRoot.RowDefinitions.Count; i++)
        {
            RowDefinition row = innerRoot.RowDefinitions[i];
            if (row.Height.IsAbsolute) { total += row.Height.Value; continue; }
            if (row.Height.IsStar) { total += row.MinHeight; continue; }
            double rowDesired = 0;
            foreach (Control child in innerRoot.Children.OfType<Control>())
            {
                if (Grid.GetRow(child) != i) continue;
                rowDesired = Math.Max(rowDesired,
                    child.DesiredSize.Height + child.Margin.Top + child.Margin.Bottom);
            }
            total += rowDesired;
        }
        return total;
    }

    /// <summary>
    /// Arrangement assertion: arrange the root at InnerWidth × the given height and
    /// verify NO child's rendered bounds extend past the bottom edge (the rendered form
    /// of "the floor fits"). Complements MeasureFloor — the invariant tests run both.
    /// </summary>
    public static void AssertArrangesWithin(Grid innerRoot, double height)
    {
        innerRoot.Measure(new Size(InnerWidth, height));
        innerRoot.Arrange(new Rect(0, 0, InnerWidth, height));
        foreach (Control child in innerRoot.Children.OfType<Control>())
        {
            if (!child.IsVisible) continue;
            double bottom = child.Bounds.Y + child.Bounds.Height;
            if (bottom > height + 0.5)
                throw new Xunit.Sdk.XunitException(
                    $"{child.GetType().Name} bottom {bottom:F1} exceeds {height}");
        }
    }
}
