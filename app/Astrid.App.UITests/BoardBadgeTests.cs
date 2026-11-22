using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using Xunit;

namespace Astrid.App.UITests;

/// <summary>
/// The Done pill draws as the DS33 pill in Done's meaning, the accent (task <c>ec8cab8a</c>).
/// </summary>
/// <remarks>
/// <para>
/// <c>BoardBadgeStyleTests</c> holds the style's numbers to the vendored tokens; this is the check
/// that the style actually reaches the pixels — that the wash resolves at run time rather than
/// silently falling back to no brush, which a <c>ThemeResource</c> to a missing key does.
/// </para>
/// <para>
/// <b>What it measures.</b> The pill's label rectangle, from UI Automation, sits on the pill's
/// wash. Before, that wash was <c>AstridSurfaceHover</c>, a neutral grey (#F3F4F6: blue 3 over
/// red); the accent at 15% over the card is blue by 20 or more. So the most common colour in the
/// label's rectangle has to lean blue. The capture is saved beside the test results as well, as
/// the by-eye record the task asks for.
/// </para>
/// </remarks>
[Collection("ui")]
public sealed class BoardBadgeTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public BoardBadgeTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Fact]
    public void The_done_pill_wears_the_accent_wash_task_ec8cab8a()
    {
        using var app = AstridApp.Launch(signedIn: true, seed: Seeds.BoardWithADoneCard);
        Thread.Sleep(1200);
        BoardCardMarkTests.OpenTheBoard(app);

        // Done is the last column. Since DS34 (task 9fedf44e) the columns share the board's width,
        // so at the harness's size they may all fit; when they do not, scroll the board to its end
        // so the pill is inside the capture.
        var board = app.Window.FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Select(element => element.TryGetCurrentPattern(ScrollPattern.Pattern, out var pattern)
                ? (ScrollPattern)pattern : null)
            .FirstOrDefault(scroll => scroll is { Current.HorizontallyScrollable: true });
        if (board is not null)
        {
            board.SetScrollPercent(100, ScrollPattern.NoScroll);
            Thread.Sleep(700);
        }

        using var shot = Capture.Window(new IntPtr(app.Window.Current.NativeWindowHandle));
        var saved = shot.SavePng(Path.Combine(Path.GetTempPath(), "astrid-uitests", "board-badges-ec8cab8a.png"));
        _output.WriteLine($"capture: {saved}");

        var pill = app.Window.FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .FirstOrDefault(element => element.Current.Name.StartsWith("Completed ", StringComparison.Ordinal)
                && element.Current.ControlType == ControlType.Text);
        Assert.True(pill is not null, "the Done card drew no finished-on pill");

        var bounds = pill!.Current.BoundingRectangle;
        var colours = shot.Histogram((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);
        var wash = colours.OrderByDescending(entry => entry.Count).First().Colour;
        int red = (wash >> 16) & 0xFF, blue = wash & 0xFF;
        _output.WriteLine($"pill {bounds}: wash #{wash:X6}");
        Assert.True(blue - red >= 15,
            $"the pill's wash is #{wash:X6}, not the accent at 15% — the DS33 pill did not reach the screen");
    }
}
