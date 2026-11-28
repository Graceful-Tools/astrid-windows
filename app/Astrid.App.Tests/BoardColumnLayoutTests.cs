using System.Text.RegularExpressions;
using System.Xml.Linq;
using Astrid.App.ViewModels;
using Xunit;
using static Astrid.App.Tests.BoardBadgeStyleTests;

namespace Astrid.App.Tests;

/// <summary>
/// A board column follows astrid-core's register DS34 in its compact (pointer) form, and sits on
/// a surface the DS37 header badge separates from (task <c>9fedf44e</c>).
/// </summary>
/// <remarks>
/// <para>
/// DS34 compact: the board inset 16 (<c>board-padding</c>), columns at least
/// <c>board-column-min-width</c> 250 sharing the rest, 12 apart, the header 10 across, 10 above
/// and 8 below, the body 10 across and none down, cards 8 apart, radius 8 — ONE surface, so no
/// rule under the header. The numbers are read from the vendored <c>tokens.resolved.json</c>, not
/// typed here, so a re-vendor that moves one fails this until the resources follow.
/// </para>
/// <para>
/// The surface: the column was <c>AstridSurfaceHover</c>, the same colour as the badge's
/// <c>AstridBgTertiary</c> in Light and Dark, so the count read as plain caption text. It is now
/// <c>AstridBgSecondary</c>, the surface astrid-web's <c>board-column.tsx</c> draws
/// (<c>theme-bg-secondary</c>) and a tier off the badge in both themes.
/// </para>
/// </remarks>
public sealed class BoardColumnLayoutTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string Layout(string name) =>
        Num(DesignTokenTests.Tokens().GetProperty("layout").GetProperty("compact").GetProperty(name).GetDouble());

    /// <summary>A resource stated in <c>Views/ShellResources.xaml</c>, by element name and key.</summary>
    private static string Resource(string element, string key)
    {
        var value = XDocument.Load(AppPath("Views", "ShellResources.xaml"))
            .Descendants()
            .SingleOrDefault(e => e.Name.LocalName == element
                && (string?)e.Attribute(Xaml + "Key") == key);
        Assert.True(value is not null, $"ShellResources.xaml has no {element} {key}");
        return value!.Value.Trim();
    }

    /// <summary>A brush's colour in one theme of the generated <c>design/AstridTokens.xaml</c>.</summary>
    private static string ThemeColour(string theme, string key)
    {
        var dictionary = XDocument.Load(Path.Combine(DesignTokenTests.RepoRoot(), "design", "AstridTokens.xaml"))
            .Descendants()
            .Single(e => e.Name.LocalName == "ResourceDictionary" && (string?)e.Attribute(Xaml + "Key") == theme);
        return (string)dictionary.Elements()
            .Single(e => (string?)e.Attribute(Xaml + "Key") == key)
            .Attribute("Color")!;
    }

    /// <summary>A layout token as the generated <c>design/AstridTokens.xaml</c> states it (task 5988cf90).</summary>
    private static string Generated(string element, string key)
    {
        var value = XDocument.Load(DesignTokenTests.TokenDictionaryPath()).Root!
            .Elements()
            .SingleOrDefault(e => e.Name.LocalName == element && (string?)e.Attribute(Xaml + "Key") == key);
        Assert.True(value is not null, $"the generated dictionary has no {element} {key}");
        return value!.Value.Trim();
    }

    private static string Board() => File.ReadAllText(AppPath("Views", "BoardView.xaml"));

    [Fact]
    public void Task9fedf44e_the_column_is_one_bordered_surface_on_bg_secondary_at_the_column_radius()
    {
        var column = Setters("BoardColumnStyle");
        Assert.Equal("{ThemeResource AstridBgSecondary}", column["Background"]);
        Assert.Equal("{ThemeResource AstridBorder}", column["BorderBrush"]);
        Assert.Equal("1", column["BorderThickness"]);
        // The generated board-column-radius, through ShellResources' own copy (task 5988cf90).
        Assert.Equal("{StaticResource AstridBoardColumnRadius}", column["CornerRadius"]);
        Assert.Equal(Layout("board-column-radius"), Resource("CornerRadius", "AstridBoardColumnRadius"));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Task9fedf44e_the_header_badge_separates_from_the_column_surface(string theme)
    {
        var column = Setters("BoardColumnStyle")["Background"];
        var badge = Setters("BoardHeaderBadgeStyle")["Background"];
        static string Key(string value) => Regex.Match(value, @"Astrid\w+").Value;
        Assert.NotEqual(ThemeColour(theme, Key(badge)), ThemeColour(theme, Key(column)));
    }

    [Fact]
    public void Task9fedf44e_the_header_pads_10_across_10_above_8_below_with_no_rule()
    {
        var header = Setters("BoardColumnHeaderStyle");
        var x = Layout("board-column-header-padding-x");
        Assert.Equal("{StaticResource AstridBoardColumnHeaderPadding}", header["Padding"]);
        Assert.Equal(
            $"{x},{Layout("board-column-header-padding-top")},{x},{Layout("board-column-header-padding-bottom")}",
            Resource("Thickness", "AstridBoardColumnHeaderPadding"));
        Assert.False(header.ContainsKey("BorderThickness"), "compact DS34 is one surface: no rule under the header");
    }

    [Fact]
    public void Task9fedf44e_the_body_pads_10_across_and_none_down()
    {
        Assert.Equal(
            $"{Layout("board-column-body-padding-x")},{Layout("board-column-body-padding-y")}",
            Generated("Thickness", "AstridBoardColumnBodyPadding"));
    }

    [Fact]
    public void Task9fedf44e_columns_are_at_least_the_min_width_12_apart_inside_the_board_inset()
    {
        Assert.Equal(Layout("board-column-min-width"), Generated("Double", "AstridBoardColumnMinWidth"));
        Assert.Equal(Layout("board-column-gap"), Generated("Double", "AstridBoardColumnGap"));
        var inset = Layout("board-padding");
        Assert.Equal($"{inset},0,{inset},{inset}", Resource("Thickness", "AstridBoardInset"));
    }

    [Fact]
    public void Task9fedf44e_cards_are_the_card_gap_apart()
    {
        // The gap is the card list's Spacing, from the generated token (task 5988cf90).
        Assert.Contains("Spacing=\"{StaticResource AstridBoardCardGap}\"", Board());
        Assert.Equal(Layout("board-card-gap"), Generated("Double", "AstridBoardCardGap"));
    }

    [Fact]
    public void Task9fedf44e_the_board_draws_its_columns_from_the_ds34_resources()
    {
        var board = Board();
        Assert.DoesNotContain("Width=\"288\"", board);
        Assert.DoesNotContain("AstridSurfaceHover", board);
        Assert.Contains("Style=\"{StaticResource BoardColumnStyle}\"", board);
        Assert.Contains("Style=\"{StaticResource BoardColumnHeaderStyle}\"", board);
        Assert.Contains("Padding=\"{StaticResource AstridBoardColumnBodyPadding}\"", board);
        Assert.Contains("Margin=\"{StaticResource AstridBoardInset}\"", board);
        Assert.Contains("<views:BoardColumnsPanel", board);
        Assert.Contains("MinColumnWidth=\"{StaticResource AstridBoardColumnMinWidth}\"", board);
        Assert.Contains("Spacing=\"{StaticResource AstridBoardColumnGap}\"", board);
    }

    [Theory]
    // Three columns in a wide board share it: (1000 - 2 gaps of 12) / 3.
    [InlineData(1000, 3, 976.0 / 3)]
    // Too many to fit: each keeps the minimum and the board scrolls.
    [InlineData(600, 4, 250)]
    // Exactly at the edge.
    [InlineData(774, 3, 250)]
    // One column takes the whole board.
    [InlineData(800, 1, 800)]
    // Before the board has a size, or with nothing to lay out, the minimum.
    [InlineData(0, 3, 250)]
    [InlineData(800, 0, 250)]
    public void Task9fedf44e_columns_share_the_board_but_never_go_under_the_minimum(
        double viewport, int count, double expected)
    {
        Assert.Equal(expected, BoardColumnLayout.ColumnWidth(viewport, count, minWidth: 250, gap: 12), 6);
    }
}
