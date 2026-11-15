using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// A repeating task shows that it repeats whoever it belongs to (task fa63a5dd).
/// </summary>
/// <remarks>
/// <para>
/// One mark carries three things — priority as its colour, whether it repeats, whether it is done
/// — but only on your own task, where the whole mark is a PNG that draws all three. Somebody
/// else's task and nobody's task wear a priority-coloured square built out of controls, and the
/// square had nowhere to say "repeats": a weekly task assigned to a teammate was indistinguishable
/// from a one-off. So "repeats" was visible on one of the three marks out of three.
/// </para>
/// <para>
/// The fix is the one astrid-web shipped in <c>3aa45e73</c>: the repeat arrows become the border of
/// the other two marks, drawn at the full 32px the own-task mark uses so it reads identically, with
/// the smaller mark inside the loop. These tests pin both halves — the core-facing decision about
/// which mark a row wears, and that each of the two views that draw a mark actually draws the new
/// state. A view that drops it shows a bare letter where the arrows should be, which no view-model
/// assertion would notice.
/// </para>
/// </remarks>
public sealed class RepeatingLeadingMarkTests
{
    private static readonly XNamespace Ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The shell's own namespace, which is where <c>PriorityMark</c> lives.</summary>
    private static readonly XNamespace Shell = "using:Astrid.App";

    private static TaskRow Row(string kind, bool repeating, int priority = 2, bool completed = false) =>
        new()
        {
            Leading = new LeadingControl { Kind = kind, UserId = kind == "avatar" ? "u2" : null },
            Assignee = kind == "avatar" ? new UserSummary { Id = "u2", Name = "dana" } : null,
            IsRepeating = repeating,
            Priority = priority,
            Completed = completed,
        };

    [Theory]
    [InlineData("avatar")]
    [InlineData("unassigned")]
    public void A_repeating_task_that_is_not_yours_wears_the_arrows_task_fa63a5dd(string kind)
    {
        var repeating = Row(kind, repeating: true);
        var oneOff = Row(kind, repeating: false);

        Assert.True(repeating.LeadingIsRepeatSquare);
        Assert.False(repeating.LeadingIsPlainSquare);

        Assert.False(oneOff.LeadingIsRepeatSquare);
        Assert.True(oneOff.LeadingIsPlainSquare);

        // Both are still the same answer to "whose task is this?" — the square did not move.
        Assert.True(repeating.LeadingIsSquare);
        Assert.True(oneOff.LeadingIsSquare);
    }

    /// <summary>
    /// Your own task keeps its single mark: the frame is for the marks that are built out of
    /// controls, and drawing both would stack two sets of arrows.
    /// </summary>
    [Fact]
    public void Your_own_repeating_task_is_unchanged_task_fa63a5dd()
    {
        var yours = Row("checkbox", repeating: true);

        Assert.True(yours.LeadingIsCheckbox);
        Assert.False(yours.LeadingIsRepeatSquare);
        Assert.False(yours.LeadingIsPlainSquare);
    }

    // Two assertions stood here that no longer have anything to say. Both read a PNG file name off
    // the row — that an unknown priority clamped to `check_box_repeat_0.png`, and that a completed
    // task kept the un-ticked arrows. Task 82e4ad32 replaced the sixteen pictures with one piece of
    // `currentColor` art, so there is no file name to clamp: the clamp moved into
    // `PriorityPalette.Key`, where `PriorityPaletteTests` covers it, and "the arrows never say
    // done" is now simply that the repeat frame is drawn with `ShowTick` left false, which the
    // view assertion below reads out of the XAML.

    /// <summary>
    /// Both surfaces that lead with a mark draw the frame, at the own-task mark's size, with the
    /// letter smaller than the loop that surrounds it (task fa63a5dd).
    /// </summary>
    [Theory]
    [InlineData("TaskRowsView.xaml", "OnRowMarkClicked")]
    [InlineData("BoardView.xaml", "OnCardMarkClicked")]
    public void Every_view_that_leads_with_a_mark_draws_the_arrows_task_fa63a5dd(string view, string click)
    {
        var root = XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "Views", view)).Root!;
        var mark = root.Descendants(Ui + "Button")
            .Single(button => (string?)button.Attribute("Click") == click);

        var frameArt = RepeatFrame(mark);
        var frameSize = Number((string?)frameArt.Attribute("Width"));

        // The same 32px the own-task mark is drawn at, so the two read as one design.
        var ownMark = mark.Descendants(Shell + "PriorityMark")
            .Single(element => (string?)element.Attribute("IsRepeating") != "True");
        Assert.Equal(Number((string?)ownMark.Attribute("Width")), frameSize);

        // The arrows never say "done" — the row's opacity and the struck-through title do — so the
        // frame is drawn with no tick while the own-task mark takes its from the row.
        Assert.Null(frameArt.Attribute("ShowTick"));
        Assert.Contains("Completed", (string?)ownMark.Attribute("ShowTick") ?? string.Empty);

        // The letter sits inside the loop, so it is smaller than the frame — and larger than
        // nothing, which a missing Width would silently mean.
        var inner = frameArt.Parent!.Elements(Ui + "Border").Single();
        var innerSize = Number((string?)inner.Attribute("Width"));
        Assert.InRange(innerSize, 1d, frameSize - 1d);

        // Visible exactly when the core says this is a repeating task that is not yours, and the
        // plain square steps aside for it rather than drawing underneath.
        var states = mark.Descendants()
            .Select(element => (string?)element.Attribute("Visibility") ?? string.Empty)
            .ToList();
        Assert.Contains(states, value => value.Contains("LeadingIsRepeatSquare"));
        Assert.Contains(states, value => value.Contains("LeadingIsPlainSquare"));
        Assert.DoesNotContain(states, value => value.Contains("LeadingIsSquare,"));
    }

    /// <summary>
    /// The mark sits INSIDE the loop, not on top of it (task fa63a5dd).
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Larger, with the photo inside the loop" is the whole design, and the first port got the
    /// proportion from astrid-web — a mark 0.6 of the frame — without anybody looking at it
    /// rendered. Web's own task says as much: "I have not looked at it rendered, so check how the
    /// photo sits inside the arrows." Looked at here, on a built 0.1.7.0: the disc covered the
    /// arrows' stroke and the mark read as a circle sitting on a red bracket.
    /// </para>
    /// <para>
    /// The reason is in the art, not in the ratio. <c>check_box_repeat_N.png</c> is a 56px canvas
    /// whose ink is inset, so a 32px render draws a frame about 22 wide with a hollow of about 17
    /// — and 0.6 of 32 is 20, three more than will fit. So the test measures the hollow in the
    /// shipped image rather than restating a number, and a redrawn asset moves the limit with it.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("TaskRowsView.xaml", "OnRowMarkClicked")]
    [InlineData("BoardView.xaml", "OnCardMarkClicked")]
    [SupportedOSPlatform("windows")]
    public void The_mark_fits_the_hollow_of_the_arrows_task_fa63a5dd(string view, string click)
    {
        var root = XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "Views", view)).Root!;
        var mark = root.Descendants(Ui + "Button")
            .Single(button => (string?)button.Attribute("Click") == click);
        var frameArt = RepeatFrame(mark);

        var frameSize = Number((string?)frameArt.Attribute("Width"));
        var inner = frameArt.Parent!.Elements(Ui + "Border").Single();
        var innerSize = Number((string?)inner.Attribute("Width"));

        var hollow = HollowWidth(frameSize);
        Assert.True(
            innerSize <= hollow,
            $"{view}: the mark is {innerSize} wide but the repeat art leaves only "
                + $"{hollow:0.0} inside its arrows at a {frameSize} frame");
    }

    /// <summary>The repeat arrows in one of the two views that lead with a mark.</summary>
    private static XElement RepeatFrame(XElement mark) =>
        mark.Descendants(Shell + "PriorityMark")
            .Single(element => (string?)element.Attribute("IsRepeating") == "True");

    /// <summary>
    /// How wide the gap between the arrows is, at the size the frame is drawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured out of the vendored art rather than restated, so a redrawn frame moves the limit
    /// with it — the property the old pixel-reading version had and the reason it existed. It read
    /// the widest transparent run across the middle of <c>check_box_repeat_N.png</c>; task 82e4ad32
    /// replaced those pictures with <c>design/AstridCheckbox.xaml</c>, where the same gap is stated
    /// rather than drawn.
    /// </para>
    /// <para>
    /// The two brackets are each a stroked path on a 56-unit canvas, so the clear span between them
    /// runs from the leftmost stroke's inner edge to the rightmost's: the extreme coordinates of the
    /// <c>M</c> and <c>L</c> commands, each brought in by half the stroke's thickness. The arc
    /// parameters are skipped — <c>A 2 2 0 0 1</c> is a radius and two flags, not a point, and
    /// taking its numbers as coordinates would put the minimum at zero.
    /// </para>
    /// </remarks>
    private static double HollowWidth(double frameSize)
    {
        var art = XDocument
            .Load(Path.Combine(
                DesignTokenTests.RepoRoot(), "design", "AstridCheckbox.xaml"))
            .Root!;
        var repeat = art.Elements(Ui + "Style")
            .Single(style => (string?)style.Attribute(Xaml + "Key") == "AstridCheckboxRepeatStyle");

        var canvas = repeat.Descendants(Ui + "Canvas").Single();
        var strokes = repeat.Descendants(Ui + "Path")
            .Where(path => path.Attribute("Stroke") is not null)
            .ToList();

        var points = strokes
            .SelectMany(path => Regex.Matches(
                (string)path.Attribute("Data")!, @"[ML]\s+(-?[\d.]+)\s+(-?[\d.]+)"))
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
        var thickness = Number((string?)strokes[0].Attribute("StrokeThickness"));

        var clear = (points.Max() - thickness / 2) - (points.Min() + thickness / 2);
        return clear * (frameSize / Number((string?)canvas.Attribute("Width")));
    }

    private static double Number(string? value)
    {
        Assert.False(string.IsNullOrWhiteSpace(value));
        return double.Parse(value!, CultureInfo.InvariantCulture);
    }
}
