using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Astrid.App;

/// <summary>
/// Lays chips out left to right and wraps onto the next line (task 69a840a4).
/// </summary>
/// <remarks>
/// <para>
/// WinUI has no wrapping panel of its own — <c>VariableSizedWrapGrid</c> wants a cell size, which
/// chips of different widths do not have — and this app takes no dependency on the Community
/// Toolkit. So the WAITING ON row, whose chips wrap with no cap and no "+n more", brings the
/// forty lines it needs with it.
/// </para>
/// <para>
/// Layout only. There is no business logic in <c>app/</c>, and a panel that measures children is
/// as far from one as code gets.
/// </para>
/// </remarks>
public sealed partial class ChipWrapPanel : Panel
{
    /// <summary>The gap between chips, and between rows of them.</summary>
    public double Spacing { get; set; } = 6;

    protected override Size MeasureOverride(Size available)
    {
        // An unbounded width is one line, which is what a panel inside a horizontal scroller
        // would want; a bounded one wraps.
        var limit = double.IsInfinity(available.Width) ? double.MaxValue : available.Width;
        double lineWidth = 0, lineHeight = 0, widest = 0, total = 0;

        foreach (var child in Children)
        {
            child.Measure(new Size(limit, double.PositiveInfinity));
            var size = child.DesiredSize;
            var needed = lineWidth == 0 ? size.Width : lineWidth + Spacing + size.Width;
            if (needed > limit && lineWidth > 0)
            {
                widest = Math.Max(widest, lineWidth);
                total += lineHeight + Spacing;
                lineWidth = size.Width;
                lineHeight = size.Height;
                continue;
            }
            lineWidth = needed;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return new Size(Math.Max(widest, lineWidth), total + lineHeight);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0, y = 0, lineHeight = 0;

        foreach (var child in Children)
        {
            var size = child.DesiredSize;
            if (x > 0 && x + size.Width > final.Width)
            {
                x = 0;
                y += lineHeight + Spacing;
                lineHeight = 0;
            }
            child.Arrange(new Rect(x, y, size.Width, size.Height));
            x += size.Width + Spacing;
            lineHeight = Math.Max(lineHeight, size.Height);
        }

        return final;
    }
}
