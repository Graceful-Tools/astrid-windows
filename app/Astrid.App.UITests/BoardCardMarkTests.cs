using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using Xunit;

namespace Astrid.App.UITests;

/// <summary>
/// Every board card wears a mark, and never a blank (task e17707a2).
/// </summary>
/// <remarks>
/// <para>
/// Jon's rule, 2026-11-14: <i>"it should show the checkbox or the unassigned or the assignee. but
/// never nothing. windows had previously showed nothing which is a bug."</i>
/// </para>
/// <para>
/// <b>Why this has to read pixels.</b> The blank was real, and it was not a missing element — the
/// mark was built, laid out and reported to UI Automation, and simply never asked for a brush.
/// <c>PriorityOutline.Priority</c> defaulted to <c>0</c>, and a dependency property only calls its
/// changed handler when the value <i>changes</i>, so a priority-0 mark's binding set the number it
/// already held, the handler never ran, and the square drew with a null <c>BorderBrush</c> over a
/// glyph with no foreground. It passed 626 shell tests. Nothing short of looking at the drawn
/// result can catch that class of bug, which is why this test exists at all rather than another
/// assertion about view-model flags.
/// </para>
/// <para>
/// <b>It reads the mark's own rectangle</b>, taken from UI Automation, rather than a region guessed
/// from the card's. A fixed offset would drift the first time the card's padding changed and would
/// then be sampling the title.
/// </para>
/// <para>
/// <b>What this does NOT cover: the checkbox.</b> The harness's session carries a cookie and no
/// user id, so no seeded task can be the reader's and all four cards come back as squares — the
/// test prints each mark's automation name, and all four read <c>Options for …</c> rather than
/// <c>Complete …</c>. Said here rather than papered over with a card called "mine". The checkbox is
/// a single image, and <c>PriorityMarkColourTests</c> reads its pixels on the list side; the
/// squares are the ones assembled out of a Border, a TextBlock and an attached property, which is
/// where this bug came from.
/// </para>
/// </remarks>
[Collection("ui")]
public sealed class BoardCardMarkTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public BoardCardMarkTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>
    /// How much of the mark's square has to be something other than its background.
    /// </summary>
    /// <remarks>
    /// Calibrated by breaking it rather than chosen: with <c>PriorityOutline</c>'s sentinel put
    /// back to <c>0</c> — the historical bug — the priority-0 mark draws 3.0% ink, and every mark
    /// drawing properly is above 20%. A tenth sits well clear of both, so this fails on the bug and
    /// does not fail on a font that antialiases differently.
    /// </remarks>
    private const double MinimumInk = 0.10;

    [Fact]
    public void Every_board_card_wears_a_mark_and_never_a_blank_task_e17707a2()
    {
        using var app = AstridApp.Launch(signedIn: true, seed: Seeds.BoardWithEveryMark);
        Thread.Sleep(1200);
        OpenTheBoard(app);

        using var shot = Capture.Window(new IntPtr(app.Window.Current.NativeWindowHandle));

        var blank = new List<string>();
        var checkedCards = 0;
        foreach (var title in new[]
                 {
                     Seeds.BoardCardTitle,       // nobody's, priority 0
                     Seeds.TheirsCardTitle,      // somebody else's
                     Seeds.RepeatingCardTitle,   // somebody else's, repeating
                     Seeds.HighPriorityCardTitle, // nobody's, priority 3
                 })
        {
            if (MarkOf(app, title) is not { } found)
            {
                blank.Add($"{title}: no mark element at all");
                continue;
            }
            var mark = found.Bounds;

            // INK COVERAGE, not a colour count. The mark's own rectangle, with its most common
            // colour taken as the background and everything else counted as ink.
            //
            // A colour count alone is too weak, and this is measured rather than assumed: with the
            // historical bug put back, the priority-0 mark fell from 69 distinct colours to 14 and
            // a "more than one colour" test still passed. What survives a missing brush is the
            // antialiasing of whatever the glyph inherited — a few hundred near-background pixels,
            // not a drawn mark. Coverage separates them cleanly: 20.2% to 34.3% when the marks
            // draw, and 3.0% for the one that does not.
            var colours = shot.Histogram(
                (int)mark.X, (int)mark.Y, (int)mark.Width, (int)mark.Height);
            var total = colours.Sum(entry => entry.Count);
            var background = colours.Max(entry => entry.Count);
            var ink = total == 0 ? 0 : (double)(total - background) / total;
            _output.WriteLine(
                $"{title}: [{found.Name}] mark {mark}, {colours.Count} colour(s), ink {ink:P1}");
            checkedCards++;
            if (ink < MinimumInk)
            {
                blank.Add(
                    $"{title}: the mark drew nothing — {ink:P1} ink in {mark}, "
                    + $"{colours.Count} colour(s)");
            }
        }

        Assert.Equal(4, checkedCards);
        Assert.True(blank.Count == 0, string.Join("; ", blank));
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
        Thread.Sleep(1800);
    }

    /// <summary>
    /// The mark button belonging to the card with this title.
    /// </summary>
    /// <remarks>
    /// Found by the mark's OWN automation name — <c>LeadingActionName</c>, which carries the title
    /// so a screen reader says what the control would do to which task. That makes it addressable
    /// here without walking from the card, and it is the same string a screen reader reads, so a
    /// name that stopped carrying the title would fail this too.
    /// </remarks>
    private static (System.Windows.Rect Bounds, string Name)? MarkOf(AstridApp app, string title)
    {
        var names = new[] { $"Options for {title}", $"Complete {title}", $"Copy {title}" };
        return app.Window.FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Where(element => names.Contains(element.Current.Name))
            .Select(element => ((System.Windows.Rect, string)?)
                (element.Current.BoundingRectangle, element.Current.Name))
            .FirstOrDefault();
    }
}
