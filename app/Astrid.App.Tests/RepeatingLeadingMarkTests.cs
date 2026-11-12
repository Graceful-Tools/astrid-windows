using System.Globalization;
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
        Assert.Equal("ms-appx:///Assets/Checkboxes/check_box_repeat_2.png", repeating.RepeatFrameAsset);

        Assert.False(oneOff.LeadingIsRepeatSquare);
        Assert.True(oneOff.LeadingIsPlainSquare);

        // Both are still the same answer to "whose task is this?" — the square did not move.
        Assert.True(repeating.LeadingIsSquare);
        Assert.True(oneOff.LeadingIsSquare);
    }

    /// <summary>
    /// Your own task keeps its single-PNG mark: the frame is for the marks that are built out of
    /// controls, and drawing both would stack two sets of arrows.
    /// </summary>
    [Fact]
    public void Your_own_repeating_task_is_unchanged_task_fa63a5dd()
    {
        var yours = Row("checkbox", repeating: true);

        Assert.True(yours.LeadingIsCheckbox);
        Assert.False(yours.LeadingIsRepeatSquare);
        Assert.False(yours.LeadingIsPlainSquare);
        Assert.Equal("ms-appx:///Assets/Checkboxes/check_box_repeat_2.png", yours.CheckboxAsset);
    }

    /// <summary>
    /// A priority the server invented asks for a file this build does not carry, which would draw
    /// nothing at all and lose the control a person taps — so it is clamped, exactly as
    /// <see cref="TaskRow.CheckboxAsset"/> clamps it.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(9)]
    public void A_priority_this_build_does_not_know_still_draws_arrows_task_fa63a5dd(int priority)
    {
        var row = Row("avatar", repeating: true, priority: priority);

        Assert.Equal("ms-appx:///Assets/Checkboxes/check_box_repeat_0.png", row.RepeatFrameAsset);
    }

    /// <summary>
    /// The arrows are the unchecked art even when the task is done, as the web draws them: the
    /// square never said "done" either — the row's opacity and the struck-through title do.
    /// </summary>
    [Fact]
    public void A_completed_repeating_task_of_somebody_elses_keeps_the_open_arrows_task_fa63a5dd()
    {
        var row = Row("avatar", repeating: true, completed: true);

        Assert.Equal("ms-appx:///Assets/Checkboxes/check_box_repeat_2.png", row.RepeatFrameAsset);
    }

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

        var frameArt = mark.Descendants(Ui + "Image")
            .Single(image => ((string?)image.Attribute("Source") ?? string.Empty).Contains("RepeatFrameAsset"));
        var frameSize = Number((string?)frameArt.Attribute("Width"));

        // The same 32px the own-task mark is drawn at, so the two read as one design.
        var ownMark = mark.Descendants(Ui + "Image")
            .Single(image => ((string?)image.Attribute("Source") ?? string.Empty).Contains("CheckboxAsset"));
        Assert.Equal(Number((string?)ownMark.Attribute("Width")), frameSize);

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

    private static double Number(string? value)
    {
        Assert.False(string.IsNullOrWhiteSpace(value));
        return double.Parse(value!, CultureInfo.InvariantCulture);
    }
}
