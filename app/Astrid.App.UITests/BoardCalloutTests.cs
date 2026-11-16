using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using Xunit;

namespace Astrid.App.UITests;

/// <summary>
/// The board opens a task CALLOUT over its card, not a slot inside the column (task eb2684bb).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Astrid.App.Tests.CalloutPlacementTests"/> proves the shell asks the core and gets the
/// fixture's answers. What it cannot prove is that the answer is then USED — a view that computes
/// a placement and lays out by its old arithmetic passes every one of those. So this drives the
/// built app and measures what actually arrived on screen.
/// </para>
/// <para>
/// <b>UI Automation bounds, not pixels.</b> Everything asserted here is a rectangle, and UIA
/// reports real on-screen ones in physical pixels — so the test needs no screenshot, no colour
/// arithmetic and no window focus, which is what lets it run in a locked session.
/// </para>
/// <para>
/// What was wrong before, and what each assertion pins: the board spliced an <c>InlineDetailSlot</c>
/// into the column's item list and widened the column to hold it, so opening a card re-laid out the
/// board under the reader's cursor and the detail arrived as another card-shaped block in the
/// column. A callout floats above and the cards do not move.
/// </para>
/// </remarks>
[Collection("ui")]
public sealed class BoardCalloutTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public BoardCalloutTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>The seeded board's one card.</summary>
    private const string CardTitle = "Write the announcement";

    [Fact]
    public void Opening_a_board_card_opens_a_callout_over_it_task_eb2684bb()
    {
        using var app = AstridApp.Launch(signedIn: true, seed: Seeds.ListWithBoard);
        Thread.Sleep(1200);
        OpenTheBoard(app);

        var cardBefore = CardBounds(app);
        Assert.True(cardBefore.Width > 0, "the seeded card is on the board");

        app.Invoke(CardTitle);
        Thread.Sleep(1500);

        var callout = Bounds(app, "Open task");
        _output.WriteLine($"card {cardBefore}, callout {callout}");

        // 1. The callout exists at all. Before this task there was no callout on the board —
        //    opening a card put a slot INSIDE the column, which is what the task reported.
        Assert.True(callout.Width > 0 && callout.Height > 0, "the callout is on screen");

        // 2. The cards did not move. The slot used to take the open card's place and the column
        //    grew wider around it, so everything below shifted and the column itself resized.
        var cardAfter = CardBounds(app);
        Assert.Equal(cardBefore.X, cardAfter.X);
        Assert.Equal(cardBefore.Y, cardAfter.Y);
        Assert.Equal(cardBefore.Width, cardAfter.Width);

        // 3. It FLOATS over the board rather than sitting in the column's flow: a callout laid out
        //    inside the column would be no wider than the column and would start at its left edge.
        //    It used to say "wider than the card", which stopped being true — and stopped being
        //    the point — when the callout began narrowing to the room beside the card
        //    (task 914fe805): on a board with the chat pane open it is now NARROWER than a card,
        //    and still floating. What a callout laid out inside the column could not do, at any
        //    width, is extend past the column's right edge. That is the property.
        Assert.True(callout.Right > cardAfter.Right,
            $"the callout ends at {callout.Right}, past the card's {cardAfter.Right}, so it is "
            + "not laid out inside the column");

        // 4. It opened on the side the core chose, and the core's answer is what moved it.
        //
        //    This is the assertion that pins the ADOPTION rather than the appearance. The card is
        //    in the board's LEFT column, so `place` answers side=right: the callout is put at the
        //    card's right edge plus a gutter, and then clamped leftward if it would overflow the
        //    board. Clamped or not, a callout that opened rightward cannot BEGIN left of the card
        //    it opened beside.
        //
        //    Checked by breaking it: with `HorizontalOffset` forced to 0 — a view that asks the
        //    core and then lays out by its own arithmetic — the callout begins 16px LEFT of the
        //    card and this fails. Every other assertion here still passed, which is why this one
        //    is written out rather than left implied.
        Assert.True(callout.X > cardAfter.X,
            $"the callout opened to the right of its card: callout.X {callout.X} > card.X {cardAfter.X}");

        // 5. The card's middle is inside the callout's vertical span — which is what makes an arrow
        //    pointing at the row possible at all. The core clamps the callout into the viewport and
        //    then walks the arrow to the row's middle; if that middle were outside the callout, the
        //    arrow would be drawn on a corner, which is the case the rule's margin exists for.
        var middle = cardAfter.Y + (cardAfter.Height / 2);
        Assert.InRange(middle, callout.Y, callout.Y + callout.Height);

        // 6. The ARROW reaches the card, and the panel does not (task 914fe805; astrid-ios
        //    AITD-679). Jon, on the iPad build: "the arrow doesn't touch the task row… It should."
        //
        //    The core answers with a box whose near edge sits ON the card's edge; the gutter in
        //    the rule is only the clearance from the window. So the shell asks for a box of the
        //    panel's width PLUS the arrow's reach and lays the panel inside it, leaving exactly
        //    that reach between the card and the panel for the arrow to cross.
        //
        //    What UIA reports is the PANEL — the named "Open task" frame — so the gap measured
        //    here is the arrow's room. It is the one number that tells the three states apart:
        //    before the core changed, the panel stood a gutter off the card and the arrow
        //    overhung it; asking for the panel's width alone against the new rule docks the panel
        //    ON the card and draws the arrow inside it (gap 0); and this is the third.
        // 6. A picture of it, for the arrow — which is the part of task 914fe805 that no
        //    rectangle here answers. Jon, on the iPad build: "the arrow doesn't touch the task
        //    row… It should."
        //
        //    Deliberately not asserted. The arrow is a rotated Border with no automation peer of
        //    its own, and the named "Open task" element is the detail pane moved into the
        //    callout, which sizes itself — so every number UIA offers here is a number about
        //    something other than the arrow. An assertion over one of those would read as a
        //    guarantee about the arrow and be a guarantee about nothing; the placement itself is
        //    pinned where it is decided, against the core's fixture, in
        //    <see cref="Astrid.App.Tests.CalloutPlacementTests"/>.
        using var shot = Capture.Window(new IntPtr(app.Window.Current.NativeWindowHandle));
        _output.WriteLine("screenshot: " + shot.SavePng(Path.Combine(
            Path.GetTempPath(), $"astrid-board-callout-{Environment.ProcessId}.png")));
        Assert.False(shot.IsBlank(), "the window drew something");
    }

    private static void OpenTheBoard(AstridApp app)
    {
        var walker = TreeWalker.ControlViewWalker;
        var element = app.Require(Seeds.BoardListName);
        while (element is not null && element.Current.ControlType != ControlType.ListItem)
        {
            element = walker.GetParent(element);
        }
        ((SelectionItemPattern)element!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Thread.Sleep(900);
        app.Toggle("Board view");
        Thread.Sleep(1500);
    }

    /// <summary>
    /// The CARD's rectangle, which is the outer of the two elements carrying the title.
    /// </summary>
    /// <remarks>
    /// The card and the TextBlock inside it share a name, so taking the first match would measure
    /// the text rather than the card — and the text does not change width when the column does,
    /// which would make assertion 2 pass whatever happened.
    /// </remarks>
    private static System.Windows.Rect CardBounds(AstridApp app) =>
        app.Window.FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Where(element => element.Current.Name == CardTitle)
            .Select(element => element.Current.BoundingRectangle)
            .OrderByDescending(rectangle => rectangle.Width)
            .First();

    private static System.Windows.Rect Bounds(AstridApp app, string name) =>
        app.Window.FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Where(element => element.Current.Name == name)
            .Select(element => element.Current.BoundingRectangle)
            .OrderByDescending(rectangle => rectangle.Width)
            .First();
}
