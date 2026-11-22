using Xunit;
using static Astrid.App.Tests.BoardBadgeStyleTests;

namespace Astrid.App.Tests;

/// <summary>
/// A board column's count wears astrid-core's DS37 header badge (task <c>ec8cab8a</c>): iOS's
/// <c>BoardHeaderBadge</c>, <c>caption1</c> in <c>text-secondary</c> on a <c>bg-tertiary</c>
/// capsule with no stroke, 8 across and 4 down.
/// </summary>
/// <remarks>
/// The 8 × 4 is the register's prose, not a token (the generator emits none for the badge), so it
/// is stated here; the type step is read from the vendored tokens.
/// </remarks>
public sealed class BoardHeaderBadgeTests
{
    [Fact]
    public void TaskEc8cab8a_the_header_badge_is_a_tertiary_capsule_with_no_stroke()
    {
        var badge = Setters("BoardHeaderBadgeStyle");
        Assert.Equal("{ThemeResource AstridBgTertiary}", badge["Background"]);
        Assert.Equal("8,4", badge["Padding"]);
        Assert.Equal("{StaticResource AstridRadiusXLarge}", badge["CornerRadius"]);
        Assert.False(badge.ContainsKey("BorderBrush"), "the header badge has no stroke");
        Assert.False(badge.ContainsKey("BorderThickness"), "the header badge has no stroke");
    }

    [Fact]
    public void TaskEc8cab8a_the_header_badge_label_is_caption1_in_text_secondary()
    {
        var label = Setters("BoardHeaderBadgeTextStyle");
        Assert.Equal("{ThemeResource AstridTextSecondary}", label["Foreground"]);
        Assert.Equal(Num(Caption1()), label["FontSize"]);
    }

    [Fact]
    public void TaskEc8cab8a_the_board_draws_its_column_count_in_the_header_badge()
    {
        var board = File.ReadAllText(AppPath("Views", "BoardView.xaml"));
        Assert.Contains("Style=\"{StaticResource BoardHeaderBadgeStyle}\"", board);
        Assert.Contains("Style=\"{StaticResource BoardHeaderBadgeTextStyle}\"", board);
    }
}
