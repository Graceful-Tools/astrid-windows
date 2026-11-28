using System.Xml.Linq;
using Xunit;
using static Astrid.App.Tests.BoardBadgeStyleTests;

namespace Astrid.App.Tests;

/// <summary>
/// An empty board column is astrid-core's DS36 empty state (task <c>e0692268</c>): the art beside
/// a speech bubble whose tail points at it. The bubble pads 12 compact, radius 16, a 2 border, and
/// the message is semibold <c>body</c>.
/// </summary>
/// <remarks>
/// <para>
/// This shell is a pointer layout with no density setting of its own, so it takes the compact
/// steps (<c>layout.compact.empty-art</c>, 96) — the same reading as
/// <c>AstridSelectionOutlineThickness</c>. The compact type layer has no step named <c>body</c>;
/// its body text is <c>detail-body</c> (13, the Mac's body), which the message takes. The
/// comfortable <c>body</c>, 17, wrapped two words a line in a 288-wide column.
/// </para>
/// <para>
/// What is NOT here yet, and why: the column's OWN message (web's <c>boardColumnEmptyKey</c>, iOS's
/// <c>BoardColumnEmptyMessage</c>) and its empathy frame are rules the core does not hand the
/// shell yet, so every column says web's generic <c>board.empty.custom</c> and the art slot holds
/// the brand mark. Both are filed on the Astrid Web board as the core half.
/// </para>
/// </remarks>
public sealed class BoardEmptyColumnTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static double Layout(string name) =>
        DesignTokenTests.Tokens().GetProperty("layout").GetProperty("compact").GetProperty(name).GetDouble();

    /// <summary>The compact layer's body: it has no step named <c>body</c>, so <c>detail-body</c>.</summary>
    private static double Body() =>
        DesignTokenTests.Tokens().GetProperty("type").GetProperty("compact")
            .GetProperty("detail-body").GetProperty("size").GetDouble();

    /// <summary>An <c>x:Double</c> stated in <c>Views/ShellResources.xaml</c>, by key.</summary>
    private static string Double(string key)
    {
        var value = XDocument.Load(AppPath("Views", "ShellResources.xaml"))
            .Descendants()
            .SingleOrDefault(element => element.Name.LocalName == "Double"
                && (string?)element.Attribute(Xaml + "Key") == key);
        Assert.True(value is not null, $"ShellResources.xaml has no x:Double {key}");
        return value!.Value.Trim();
    }

    [Fact]
    public void TaskE0692268_the_bubble_pads_12_with_radius_16_and_a_2_border()
    {
        var bubble = Setters("BoardEmptyBubbleStyle");
        Assert.Equal("12", bubble["Padding"]);
        Assert.Equal("{StaticResource AstridRadiusXLarge}", bubble["CornerRadius"]);
        Assert.Equal("2", bubble["BorderThickness"]);
        Assert.Equal("{ThemeResource AstridBorder}", bubble["BorderBrush"]);
        Assert.Equal("{ThemeResource AstridBgSecondary}", bubble["Background"]);
    }

    [Fact]
    public void TaskE0692268_the_message_is_semibold_body_in_the_primary_text()
    {
        var message = Setters("BoardEmptyMessageStyle");
        // The generated detail-body step, through ShellResources' own copy (task 737b31a8).
        Assert.Equal("{StaticResource AstridTypeCompactDetailBodySize}", message["FontSize"]);
        Assert.Equal(Num(Body()), Double("AstridTypeCompactDetailBodySize"));
        Assert.Equal("SemiBold", message["FontWeight"]);
        Assert.Equal("{ThemeResource AstridTextPrimary}", message["Foreground"]);
        Assert.Equal("Wrap", message["TextWrapping"]);
    }

    [Fact]
    public void TaskE0692268_the_art_is_the_compact_empty_art_size()
    {
        Assert.Equal(Num(Layout("empty-art")), Double("AstridEmptyArt"));
    }

    [Fact]
    public void TaskE0692268_the_board_draws_its_empty_column_as_the_bubble_with_a_tail()
    {
        var board = File.ReadAllText(AppPath("Views", "BoardView.xaml"));
        Assert.Contains("Style=\"{StaticResource BoardEmptyBubbleStyle}\"", board);
        Assert.Contains("Style=\"{StaticResource BoardEmptyMessageStyle}\"", board);
        Assert.Contains("Width=\"{StaticResource AstridEmptyArt}\"", board);
        Assert.Contains("x:Name=\"EmptyBubbleTail\"", board);
        Assert.DoesNotContain("nothing_here_yet", board);
    }

    [Fact]
    public void TaskE0692268_the_message_is_webs_generic_column_message_in_both_languages()
    {
        static string Value(string language) =>
            XDocument.Load(AppPath("Strings", language, "Resources.resw"))
                .Descendants("data")
                .Single(data => (string?)data.Attribute("name") == "board_empty_custom.Text")
                .Element("value")!.Value;

        // astrid-web lib/i18n/locales/{en,de}.json → board.empty.custom.
        Assert.Equal("Nothing here yet! Drag a task in to move it along.", Value("en-US"));
        Assert.Equal("Noch nichts hier! Ziehe eine Aufgabe hinein, um sie voranzubringen.", Value("de-DE"));
    }
}
