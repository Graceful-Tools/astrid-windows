using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// That the frame and the tick still compose into one mark (task 82e4ad32).
/// </summary>
/// <remarks>
/// <para>
/// The mark used to be sixteen PNGs, where the frame and the tick were one picture and registered
/// because a designer had drawn them that way. <see cref="PriorityMark"/> now stacks two
/// <c>ContentControl</c>s in one <c>Grid</c> cell, each templated to a <c>Viewbox</c> over its own
/// <c>Canvas</c> — so they line up only as long as those canvases stay the same size and neither
/// Viewbox scales differently. That is an invariant of the art, and nothing was holding it: the
/// completion report for 82e4ad32 said they "should register exactly, since both canvases are the
/// same size", which is a fact about the file that no test was reading.
/// </para>
/// <para>
/// These assertions are deliberately about the vendored art rather than about rendered pixels. A
/// tick sliding half a stroke off its box is a thing a person sees instantly and a green suite
/// cannot — but the *cause* is always one of these three properties, and those are checkable here,
/// at the point where a regenerated <c>design/AstridCheckbox.xaml</c> would introduce them.
/// </para>
/// </remarks>
public sealed class CheckboxArtCompositionTests
{
    private static readonly XNamespace Ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The three styles <see cref="PriorityMark"/> composes, frames first.</summary>
    private const string BoxStyle = "AstridCheckboxBoxStyle";
    private const string RepeatStyle = "AstridCheckboxRepeatStyle";
    private const string CheckStyle = "AstridCheckboxCheckStyle";

    private static XElement Art() =>
        XDocument.Load(DesignTokenTests.CheckboxArtPath()).Root!;

    private static XElement Style(string key) =>
        Art().Elements(Ui + "Style")
            .Single(style => (string?)style.Attribute(Xaml + "Key") == key);

    /// <summary>
    /// Every canvas is the same size, so the frame and the tick scale by the same factor.
    /// </summary>
    /// <remarks>
    /// A <c>Viewbox</c> scales its child to fill the slot it is given. Both controls sit in the same
    /// <c>Grid</c> cell, so they are given the same slot — which means equal canvases scale equally
    /// and the tick lands on the box's own coordinates. Redraw the tick on a 64-unit canvas and it
    /// shrinks by an eighth relative to the frame, silently, in every row in the app.
    /// </remarks>
    [Fact]
    public void Frame_and_tick_share_one_canvas_size_task_82e4ad32()
    {
        var sizes = new[] { BoxStyle, RepeatStyle, CheckStyle }
            .Select(key =>
            {
                var canvas = Style(key).Descendants(Ui + "Canvas").Single();
                return (Key: key,
                        Width: Number((string?)canvas.Attribute("Width")),
                        Height: Number((string?)canvas.Attribute("Height")));
            })
            .ToList();

        var first = sizes[0];
        foreach (var size in sizes)
        {
            Assert.True(
                size.Width == first.Width && size.Height == first.Height,
                $"{size.Key} draws on {size.Width}x{size.Height}, but {first.Key} draws on "
                + $"{first.Width}x{first.Height}. PriorityMark stacks these in one Grid cell, so "
                + "unequal canvases put the tick off its box.");
        }

        // Square, so the Uniform fit is the same in both directions whatever slot the row gives it.
        Assert.Equal(first.Width, first.Height);
    }

    /// <summary>
    /// No Viewbox overrides <c>Stretch</c>, which is the other way the two could scale apart.
    /// </summary>
    /// <remarks>
    /// Equal canvases are only half of it: <c>Uniform</c> on one and <c>Fill</c> on the other would
    /// register in a square slot and come apart in any other. The default is <c>Uniform</c>, so the
    /// art states this by saying nothing — and this test is what makes that silence deliberate.
    /// </remarks>
    [Fact]
    public void No_viewbox_sets_its_own_stretch_task_82e4ad32()
    {
        foreach (var viewbox in Art().Descendants(Ui + "Viewbox"))
        {
            Assert.True(
                viewbox.Attribute("Stretch") is null,
                "A Viewbox in the checkbox art sets Stretch explicitly. The frame and the tick are "
                + "scaled independently, so they register only while both take the default Uniform.");
        }
    }

    /// <summary>
    /// The frames take their colour from the mark; the tick brings its own.
    /// </summary>
    /// <remarks>
    /// This is the division <see cref="PriorityMark.Apply"/> relies on, and it reads as an omission
    /// rather than a decision: the control sets <c>Foreground</c> on the frame and never on the
    /// tick. That is right — the frame carries the priority token (DS2/DS21) while the tick is the
    /// fixed "done" green in every theme and at every priority, so it templates against its own
    /// brush. Someone tidying <c>PriorityMark</c> would reasonably "fix" the missing line; if the
    /// art ever did bind the tick to <c>Foreground</c>, that tidy-up would be required instead, and
    /// a done high-priority task would show a red tick. Pin which way round it is.
    /// </remarks>
    [Fact]
    public void Only_the_frame_colours_from_the_marks_foreground_task_82e4ad32()
    {
        foreach (var frame in new[] { BoxStyle, RepeatStyle })
        {
            var brushes = Style(frame).Descendants(Ui + "Path")
                .SelectMany(path => new[] { (string?)path.Attribute("Stroke"), (string?)path.Attribute("Fill") })
                .Where(value => value is not null)
                .ToList();

            Assert.NotEmpty(brushes);
            Assert.All(brushes, value => Assert.Equal("{TemplateBinding Foreground}", value));
        }

        var tick = Style(CheckStyle).Descendants(Ui + "Path")
            .SelectMany(path => new[] { (string?)path.Attribute("Stroke"), (string?)path.Attribute("Fill") })
            .Where(value => value is not null)
            .ToList();

        Assert.NotEmpty(tick);
        Assert.DoesNotContain("{TemplateBinding Foreground}", tick);
    }

    private static double Number(string? value)
    {
        Assert.False(string.IsNullOrWhiteSpace(value));
        return double.Parse(value!, CultureInfo.InvariantCulture);
    }
}
