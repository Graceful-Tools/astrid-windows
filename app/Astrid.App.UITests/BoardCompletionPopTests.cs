using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using Xunit;

namespace Astrid.App.UITests;

/// <summary>
/// Completing a card on the board pops its mark, and the pop settles (DS16, task 27c773f5).
/// </summary>
/// <remarks>
/// <para>
/// The board's half of <see cref="CompletionPopTests"/>, and the same instrument: UI Automation
/// reports the Composition scale, so the mark's width is read every frame and the shape is
/// asserted — bigger than at rest, then back.
/// </para>
/// <para>
/// The board finishes a task through the picker ("Mark as done"), and the board view model then
/// rebuilds its columns, so the completed card comes back with a <b>new</b> mark that is ticked
/// before it is loaded — the cause task 4717a2fd fixed for the list. Read by its own automation
/// name each frame for the same reason the list test does: the element held before the click is
/// not the one on screen afterwards.
/// </para>
/// </remarks>
[Collection("ui")]
public sealed class BoardCompletionPopTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public BoardCompletionPopTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>How long to watch for, comfortably past the token's 280 ms spring.</summary>
    private const int WatchMs = 1200;

    [Fact]
    public void Completing_a_board_card_pops_its_mark_and_the_pop_settles_task_27c773f5()
    {
        var title = Seeds.MineCardTitle;
        using var app = AstridApp.Launch(signedIn: true, seed: Seeds.BoardWithEveryMark);
        Thread.Sleep(1200);
        // The finished card moves to Done, the last column, which at the launcher's window size is
        // scrolled out of view — and a mark nobody can see reports no size, so nothing to sample.
        // Maximised, every column of this four-card board is on screen, as it is for a reader
        // watching the card they just finished land.
        ((WindowPattern)app.Window.GetCurrentPattern(WindowPattern.Pattern))
            .SetWindowVisualState(WindowVisualState.Maximized);
        Thread.Sleep(600);
        BoardCardMarkTests.OpenTheBoard(app);

        var markName = $"Options for {title}";
        var rest = TickOf(app, title);
        Assert.True(rest.Width > 0, $"'{title}' has no mark to pop");
        _output.WriteLine($"at rest: {rest.Width}x{rest.Height}");

        app.Invoke(markName);
        Thread.Sleep(600);

        var widths = new List<double>();
        app.Invoke("Mark as done");
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < WatchMs)
        {
            widths.Add(TickOf(app, title).Width);
            Thread.Sleep(10);
        }
        _output.WriteLine($"widths: {string.Join(", ", widths)}");

        var biggest = widths.Max();
        Assert.True(
            biggest > rest.Width + 1,
            $"completing '{title}' on the board never made its mark bigger than its resting "
            + $"{rest.Width} — the widest reading in {WatchMs}ms was {biggest}. DS16's pop did not "
            + $"run. The readings were: {string.Join(", ", widths)}.");

        var settled = widths[^1];
        Assert.True(
            Math.Abs(settled - rest.Width) < 1,
            $"the mark ended at {settled} rather than back at its resting {rest.Width}, so the "
            + $"spring never settled. The readings were: {string.Join(", ", widths)}.");

        Assert.False(File.Exists(app.CrashLogPath), "the app wrote a crash log");
    }

    /// <summary>
    /// The size of the PriorityMark inside the card's mark button, found fresh.
    /// </summary>
    /// <remarks>
    /// The button is a fixed 32 and does not scale; the pop is on the mark inside it, so it is the
    /// button's first descendant with a size that is read. Zero when the card is not found, which
    /// the readings then show rather than throwing mid-sample.
    /// </remarks>
    private static System.Windows.Rect TickOf(AstridApp app, string title)
    {
        var names = new[] { $"Options for {title}", $"Complete {title}" };
        var button = app.Window.FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .FirstOrDefault(element => names.Contains(element.Current.Name));
        if (button is null)
        {
            return default;
        }
        var inner = button.FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Select(element => element.Current.BoundingRectangle)
            .Where(bounds => !bounds.IsEmpty && bounds.Width > 0)
            .OrderByDescending(bounds => bounds.Width)
            .FirstOrDefault();
        return inner.IsEmpty || inner.Width <= 0 ? button.Current.BoundingRectangle : inner;
    }
}
