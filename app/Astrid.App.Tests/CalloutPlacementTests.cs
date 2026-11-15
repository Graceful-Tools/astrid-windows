using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// Where a task callout opens, and where its arrow points — the core's, not the shell's
/// (task eb2684bb, the Windows half of 011080d4).
/// </summary>
/// <remarks>
/// <para>
/// The board used to splice an <c>InlineDetailSlot</c> into the column's item list: detail appeared
/// as another card-shaped block pushed in between cards, so there was no side to choose and no
/// arrow to aim. Four clients draw this callout; the arithmetic is
/// <c>astrid_rules::rows::callout::place</c>, locked by
/// <c>contracts/fixtures/callout-placement.json</c>.
/// </para>
/// <para>
/// These are that fixture's eight cases, driven through the REAL door rather than against a copy
/// of the rule, so a pin that moved the placement fails here rather than in a callout somebody is
/// looking at. The numbers are the fixture's; none of them is invented here.
/// </para>
/// </remarks>
public class CalloutPlacementTests
{
    /// <summary>A 1400x900 window, which is what every fixture case is framed in.</summary>
    private static readonly CalloutRect Window = new(0, 0, 1400, 900);

    /// <summary>A 320-wide column with a row <paramref name="height"/> tall in it.</summary>
    private static CalloutRect Row(double left, double top, double height = 64) =>
        new(left, top, 320, height);

    /// <summary>
    /// The side is whichever has more room, and a tie opens right (task eb2684bb).
    /// </summary>
    /// <remarks>
    /// Not a preferred side with a fallback: on a board the column that was tapped is as likely to
    /// be at the right edge as the left. The tie is stated rather than left to an accident of
    /// <c>&gt;=</c>, which is why it has a case of its own.
    /// </remarks>
    [Fact]
    public void A_callout_opens_on_the_side_with_more_room_task_eb2684bb()
    {
        // side/room-on-the-right
        var right = AstridClient.CalloutPlacement(Row(40, 300), Window, 420, 520);
        Assert.Equal("right", right.Side);
        Assert.Equal(368, right.Left);

        // side/room-on-the-left — the callout's own RIGHT edge sits a gutter clear of the anchor.
        var left = AstridClient.CalloutPlacement(Row(1000, 300), Window, 420, 520);
        Assert.Equal("left", left.Side);
        Assert.Equal(572, left.Left);

        // side/equal-room-opens-right — a 320 column centred in 1400 leaves 540 each side.
        var tie = AstridClient.CalloutPlacement(Row(540, 300), Window, 420, 520);
        Assert.Equal("right", tie.Side);
        Assert.Equal(868, tie.Left);
    }

    /// <summary>
    /// The arrow points at the ROW's middle wherever the callout ended up (task eb2684bb).
    /// </summary>
    /// <remarks>
    /// After clamping, the callout's own midpoint and the row's are different — and drawing the
    /// arrow at the callout's midpoint is the mistake this rule exists to stop. Both edge cases
    /// assert that <c>top + arrowOffset</c> still lands on the row's middle.
    /// </remarks>
    [Fact]
    public void The_arrow_follows_the_row_not_the_callout_task_eb2684bb()
    {
        // arrow/points-at-the-rows-middle — nothing clamped, so the arrow is at the half-height.
        var middle = AstridClient.CalloutPlacement(Row(40, 300), Window, 420, 520);
        Assert.Equal(72, middle.Top);
        Assert.Equal(260, middle.ArrowOffset);
        Assert.Equal(332, middle.Top + middle.ArrowOffset);

        // arrow/row-near-the-top-walks-the-arrow-up — the callout is pushed down to the gutter, so
        // the arrow has to RISE up its edge to keep pointing at the row.
        var high = AstridClient.CalloutPlacement(Row(40, 10), Window, 420, 520);
        Assert.Equal(8, high.Top);
        Assert.Equal(34, high.ArrowOffset);
        Assert.Equal(42, high.Top + high.ArrowOffset);
        Assert.True(high.ArrowOffset < 260, "the arrow rose up the edge");

        // arrow/row-near-the-bottom-walks-it-down — and the same at the other end.
        var low = AstridClient.CalloutPlacement(Row(40, 800), Window, 420, 520);
        Assert.Equal(372, low.Top);
        Assert.Equal(460, low.ArrowOffset);
        Assert.Equal(832, low.Top + low.ArrowOffset);
        Assert.True(low.ArrowOffset > 260, "the arrow fell down the edge");
    }

    /// <summary>
    /// The arrow is kept clear of both corners (task eb2684bb).
    /// </summary>
    /// <remarks>
    /// A rounded corner eats the edge the triangle stands on, so an arrow inside the margin draws
    /// as a notch rather than a point. A COMPACT row — 24 tall, which a dense list draws — at the
    /// very top of the window reaches the near clamp; a row of ordinary height never does, which is
    /// why the fixture uses a short one rather than invented numbers.
    /// </remarks>
    [Fact]
    public void The_arrow_is_kept_off_both_corners_task_eb2684bb()
    {
        // arrow/kept-off-the-near-corner
        var near = AstridClient.CalloutPlacement(Row(40, 0, height: 24), Window, 420, 120);
        Assert.Equal(8, near.Top);
        Assert.Equal(20, near.ArrowOffset);

        // arrow/kept-off-the-far-corner — a short callout beside a low row would otherwise point
        // below its own bottom edge.
        var far = AstridClient.CalloutPlacement(Row(40, 880), Window, 420, 120);
        Assert.Equal(772, far.Top);
        Assert.Equal(100, far.ArrowOffset);
    }

    /// <summary>
    /// A callout bigger than the window it must fit in is pinned, not a crash (task eb2684bb).
    /// </summary>
    /// <remarks>
    /// A small window, or a tablet in split view. <c>f64::clamp</c> panics when its bounds cross,
    /// which is exactly what a too-large callout produces — the core pins to the near edge instead,
    /// and this asserts the shell gets an answer rather than an exception across the FFI boundary.
    /// </remarks>
    [Fact]
    public void A_callout_bigger_than_the_window_is_pinned_task_eb2684bb()
    {
        var placed = AstridClient.CalloutPlacement(Row(10, 10), new CalloutRect(0, 0, 400, 300), 420, 520);

        Assert.Equal(8, placed.Left);
        Assert.Equal(8, placed.Top);
    }

    /// <summary>
    /// The size is the shell's and comes back unchanged (task eb2684bb).
    /// </summary>
    /// <remarks>
    /// How big a detail pane wants to be is a design decision per platform and per density, so the
    /// rule places what it is given and never resizes it. Asserted because a client that found its
    /// callout silently shrunk would look for the bug in its own layout.
    /// </remarks>
    [Fact]
    public void The_rule_places_the_callout_but_never_sizes_it_task_eb2684bb()
    {
        var placed = AstridClient.CalloutPlacement(Row(40, 300), Window, 420, 520);

        Assert.Equal(420, placed.Width);
        Assert.Equal(520, placed.Height);
    }
}
