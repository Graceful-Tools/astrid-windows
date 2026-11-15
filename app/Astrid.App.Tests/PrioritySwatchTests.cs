using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

public sealed class PrioritySwatchTests
{
    /// <summary>
    /// The chosen square is white on its own colour; the others are that colour on nothing; and
    /// the colour is the row's, from the one palette (task 204c9d98).
    /// </summary>
    [Fact]
    public void The_chosen_square_is_white_on_its_colour_and_the_rest_are_outlined_task_204c9d98()
    {
        foreach (var level in new[] { 1, 2, 3 })
        {
            var lit = PrioritySwatch.For(level, chosen: level);
            Assert.Equal(PriorityPalette.Key(level), lit.Background);
            Assert.Equal(PrioritySwatch.OnFill, lit.Foreground);
            Assert.Equal(PriorityPalette.Key(level), lit.Border);

            var off = PrioritySwatch.For(level, chosen: 0);
            Assert.Equal(PrioritySwatch.Transparent, off.Background);
            Assert.Equal(PriorityPalette.Key(level), off.Foreground);
            Assert.Equal(PriorityPalette.Key(level), off.Border);
        }

        // No priority is grey, not invisible: a square has to be seen to be chosen.
        Assert.Equal(PriorityPalette.None, PrioritySwatch.For(0, chosen: 0).Background);
        Assert.Equal(PrioritySwatch.OnFill, PrioritySwatch.For(0, chosen: 0).Foreground);
        Assert.Equal(PriorityPalette.None, PrioritySwatch.For(0, chosen: 2).Foreground);
    }

    /// <summary>
    /// What it answers with is a resource key, never a colour (task 82e4ad32).
    /// </summary>
    /// <remarks>
    /// The point of the move: a view-model has no theme, and <c>priority-low</c> is a different
    /// brand value on dark. A hex escaping back into this rule would paint a dark-theme picker in
    /// the light theme's blue, and every assertion above would still pass.
    /// </remarks>
    [Fact]
    public void A_swatch_names_a_brush_rather_than_a_colour_task_82e4ad32()
    {
        foreach (var level in new[] { 0, 1, 2, 3 })
        {
            var (background, foreground, border) = PrioritySwatch.For(level, chosen: 1);
            foreach (var value in new[] { background, foreground, border })
            {
                Assert.DoesNotContain("#", value, StringComparison.Ordinal);
            }
        }
    }
}
