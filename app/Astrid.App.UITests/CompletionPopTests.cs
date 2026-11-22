using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Astrid.App.UITests;

/// <summary>
/// Completing a task pops its mark, and the pop settles (DS16, task 787c7aa5).
/// </summary>
/// <remarks>
/// <para>
/// The other half of that task's "checked by eye" clause, beside
/// <see cref="SidebarSelectionTests"/>. <c>DesignTokenTests</c> already pins
/// <c>AstridCompletePopScale</c>, <c>AstridSpringDamping</c> and <c>AstridSpringMs</c> to the
/// vendored tokens, and another test asserts that <c>PriorityMark</c> looks all three up — so what
/// was left unproven was only whether anything moves on screen. A resource that is read into a
/// Composition animation nobody ever starts agrees with the tokens for ever.
/// </para>
/// <para>
/// <b>This reads UI Automation bounds, not pixels</b>, which is worth saying because it is
/// surprising. The pop is a <c>SpringVector3NaturalMotionAnimation</c> on the element's visual
/// <c>Scale</c>, run by the compositor off the UI thread, and the obvious expectation is that
/// neither UIA nor <c>PrintWindow</c> can see it. Measured, UIA reports the scaled size: a 64-unit
/// mark reads 69, 67, 65, 64 over four frames as the spring settles. So the cheap instrument is
/// also the right one, and the test needs no screenshot, no colour arithmetic and no window
/// focus — which is what lets it run in a locked session where the drag tests cannot.
/// </para>
/// <para>
/// <b>Why it samples rather than asserting a peak.</b> The spring is <c>motion.spring</c>'s 280 ms,
/// and the first reading after the invoke already catches it decaying — the full 1.28 is gone
/// before anything can look. Pinning a number would be pinning this machine's scheduling. What is
/// asserted instead is the shape, which is what DS16 actually says: <i>it got bigger than it ends
/// up, and then it came back</i>. Both halves matter — a pop that never settles is as wrong as one
/// that never fires, and only the pair catches an animation left running.
/// </para>
/// <para>
/// The completed row also <b>sinks to the bottom of the list</b> rather than disappearing, so the
/// mark moves while it pops. That is why only the size is read and never the position: an earlier
/// attempt at this measured a fixed rectangle and found blank background, which reads exactly like
/// "nothing animated".
/// </para>
/// </remarks>
[Collection("ui")]
public sealed class CompletionPopTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public CompletionPopTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>How long to watch for, comfortably past the token's 280 ms spring.</summary>
    private const int WatchMs = 900;

    /// <remarks>
    /// Came to read 64 at every sample after it landed (task 4717a2fd): the completed row arrives as
    /// a new item, so its mark is built already ticked and the mark's own false → true check never
    /// fires. The list now asks for the pop when it realises a row it last drew unfinished.
    /// </remarks>
    [Fact]
    public void Completing_a_task_pops_its_mark_and_the_pop_settles_task_787c7aa5_4717a2fd()
    {
        var title = Seeds.PriorityTitles[2];
        using var app = AstridApp.Launch(
            signedIn: true, seed: Seeds.ListWithEveryPriority("light"));
        Select(app, Seeds.PriorityListName);

        var mark = app.Require($"Complete {title}");
        var rest = mark.Current.BoundingRectangle;
        Assert.True(rest.Width > 0, $"'{title}' has no mark to pop");
        _output.WriteLine($"at rest: {rest.Width}x{rest.Height}");

        // Found by name EVERY frame, not cached. The completed row sinks to the bottom of the
        // list, and the ListView builds a new container for it there, so an element captured
        // before the click stops reporting the live mark part-way through the pop. A by-name
        // search costs more than reading a held element and is the only one that follows it.
        var widths = new List<double>();
        app.Invoke($"Complete {title}");
        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < WatchMs)
        {
            widths.Add(app.Require($"Complete {title}").Current.BoundingRectangle.Width);
            Thread.Sleep(10);
        }
        _output.WriteLine($"widths: {string.Join(", ", widths)}");

        var biggest = widths.Max();
        Assert.True(
            biggest > rest.Width,
            $"completing '{title}' never made its mark bigger than its resting {rest.Width} — the "
            + $"widest reading in {WatchMs}ms was {biggest}. DS16's pop did not run. The readings "
            + $"were: {string.Join(", ", widths)}.");

        var settled = widths[^1];
        Assert.True(
            Math.Abs(settled - rest.Width) < 1,
            $"the mark ended at {settled} rather than back at its resting {rest.Width}, so the "
            + $"spring never settled. The readings were: {string.Join(", ", widths)}.");

        Assert.False(File.Exists(app.CrashLogPath), "the app wrote a crash log");
    }

    private static void Select(AstridApp app, string name)
    {
        var row = app.Require(name);
        var walker = System.Windows.Automation.TreeWalker.ControlViewWalker;
        var element = row;
        while (element is not null
            && element.Current.ControlType != System.Windows.Automation.ControlType.ListItem)
        {
            element = walker.GetParent(element);
        }
        Assert.NotNull(element);
        ((System.Windows.Automation.SelectionItemPattern)element!
            .GetCurrentPattern(System.Windows.Automation.SelectionItemPattern.Pattern)).Select();
        Thread.Sleep(800);
    }
}
