using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The square mark for somebody else's task, and for nobody's, is a priority **outline** — so it is
/// still there when there is no priority (task f1a8ff9f).
/// </summary>
/// <remarks>
/// <para>
/// Found by the on-screen check added for task 82e4ad32: an unassigned task with no priority drew
/// nothing at all. The square was filled with the priority's colour and carried the mark in
/// <c>White</c>, and priority 0 painted the fill transparent on purpose — so on a light card the
/// square vanished and the white letter vanished with it. That is the ordinary case on a fresh
/// board, where the agent lists default to <c>defaultAssigneeId="unassigned"</c>.
/// </para>
/// <para>
/// The fill was the real divergence, not the transparency. astrid-web draws both square marks as a
/// 2px border in the priority colour with the glyph in the same colour and no background at all
/// (<c>components/task-leading-control.tsx</c>, <c>SQUARE_CLASS</c>), and its
/// <c>getPriorityColor</c> answers <c>priority-none</c> for 0 rather than nothing; astrid-core's
/// design register says it from the other side, DS6 calling the repeat arrows "the photo's border,
/// in place of the priority outline". Windows was the only client filling, and <c>White</c> only
/// ever worked because of that fill.
/// </para>
/// <para>
/// Read off the XAML, as <see cref="RepeatingLeadingMarkTests"/> and
/// <see cref="TaskRowsAlignmentTests"/> read these same views: three surfaces draw this square and
/// a fix that reached two of them would look right wherever it was checked. This runs in the
/// standard gate; <c>PriorityMarkColourTests</c> holds the other half — that the colour reaches the
/// pixels — and runs in <c>predeploy:full</c>.
/// </para>
/// </remarks>
public sealed class PlainSquareMarkTests
{
    private static readonly XNamespace Ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>The shell's own namespace, which is where <c>PriorityOutline</c> lives.</summary>
    private static readonly XNamespace Shell = "using:Astrid.App";

    private static readonly XName Priority = Shell + "PriorityOutline.Priority";

    /// <summary>
    /// Every surface that draws the square draws it as an outline, with the mark in the same colour.
    /// </summary>
    /// <remarks>
    /// The counts are stated because the failure this test exists for is a square that quietly stops
    /// asking for a priority: the loop below has nothing to say about a site it cannot find.
    /// TaskRowsView draws two — quick add's preview and the row itself — and BoardView one.
    /// </remarks>
    [Theory]
    [InlineData("TaskRowsView.xaml", 2)]
    [InlineData("BoardView.xaml", 1)]
    public void Every_square_mark_is_a_priority_outline_task_f1a8ff9f(string view, int expected)
    {
        var root = XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "Views", view)).Root!;
        var squares = root.Descendants(Ui + "Border")
            .Where(border => border.Attribute(Priority) is not null)
            .ToList();

        Assert.Equal(expected, squares.Count);

        foreach (var square in squares)
        {
            // No fill. A fill is what made White readable, and what left nothing behind at
            // priority 0.
            Assert.Null(square.Attribute("Background"));

            // The line IS the mark, so it has to be drawn — the same 2 the copy square and
            // astrid-web both use.
            Assert.Equal(2d, Number((string?)square.Attribute("BorderThickness")));

            // ...and the glyph inside takes the priority colour from the same property rather than
            // a literal. Asked for on the element itself, not inherited from the Border: a
            // template's child is attached after its parent's attributes are set, so a parent that
            // reached down for its child would be painting a child that is not there yet.
            var mark = Assert.Single(square.Elements(Ui + "TextBlock"));
            Assert.Null(mark.Attribute("Foreground"));
            Assert.NotNull(mark.Attribute(Priority));
            Assert.Equal((string?)square.Attribute(Priority), (string?)mark.Attribute(Priority));
        }
    }

    /// <summary>
    /// No priority is a colour — <c>priority-none</c> — not an absence (task f1a8ff9f).
    /// </summary>
    /// <remarks>
    /// Read out of the source the way <c>PriorityPaletteTests</c> reads <c>PriorityPalette.cs</c>
    /// for a stray hex: this test project is plain net9.0 and cannot load a WinUI type, and the one
    /// thing worth pinning is that nothing between the row and the brush decides to draw nothing.
    /// <c>PriorityPalette.Key</c> already answers <c>AstridPriorityNone</c> for 0, and
    /// <c>PriorityPaletteTests</c> holds it there.
    /// </remarks>
    [Fact]
    public void The_outline_never_paints_itself_away_task_f1a8ff9f()
    {
        var source = File.ReadAllText(
            Path.Combine(LocalisationTests.AppDirectory(), "PriorityOutline.cs"));

        Assert.Contains("PriorityPalette.Key", source);
        Assert.DoesNotContain("Transparent", source);

        // ...and the property's default is not a priority. A dependency property calls its changed
        // handler only when the value CHANGES, so a default of 0 leaves every unprioritised row
        // unpainted — the binding sets the number the element already holds. That half of this bug
        // survived the XAML fix and a green gate, and only the rendered pixel found it.
        Assert.Contains("new PropertyMetadata(Unset, OnChanged)", source);
        Assert.DoesNotContain("new PropertyMetadata(0, OnChanged)", source);
    }

    private static double Number(string? value)
    {
        Assert.False(string.IsNullOrWhiteSpace(value));
        return double.Parse(value!, CultureInfo.InvariantCulture);
    }
}
