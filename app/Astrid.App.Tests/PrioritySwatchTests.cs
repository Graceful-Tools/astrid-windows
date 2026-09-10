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
            Assert.Equal(PriorityPalette.Hex(level), lit.Background);
            Assert.Equal(PrioritySwatch.White, lit.Foreground);
            Assert.Equal(PriorityPalette.Hex(level), lit.Border);

            var off = PrioritySwatch.For(level, chosen: 0);
            Assert.Equal(PrioritySwatch.Transparent, off.Background);
            Assert.Equal(PriorityPalette.Hex(level), off.Foreground);
            Assert.Equal(PriorityPalette.Hex(level), off.Border);
        }

        // No priority is grey, not invisible: a square has to be seen to be chosen.
        Assert.Equal(PriorityPalette.None, PrioritySwatch.For(0, chosen: 0).Background);
        Assert.Equal(PrioritySwatch.White, PrioritySwatch.For(0, chosen: 0).Foreground);
        Assert.Equal(PriorityPalette.None, PrioritySwatch.For(0, chosen: 2).Foreground);
    }
}
