using System.IO;
using System.Threading;
using System.Windows.Automation;
using System.Xml.Linq;
using Xunit;

namespace Astrid.App.UITests;

/// <summary>
/// The colour a row's mark actually came out, on screen, in each appearance (task 82e4ad32).
/// </summary>
/// <remarks>
/// <para>
/// This is the clause of that task no other test could answer: *the checkbox checked by eye in both
/// themes — the colours of a 17px glyph are exactly what a green test suite cannot see*.
/// `PriorityPaletteTests` pins the four brushes in `App.xaml` to the vendored tokens per theme, and
/// `DesignTokenTests` keeps that table honest, but both of them read XAML. Neither can see whether
/// the right brush reaches the right element at run time — which is a real question here, because
/// the colour is resolved through <c>ThemeBrushes.For</c> against the theme the element is
/// <i>wearing</i>, and the appearance is applied to ShellPage's root long after the marks are
/// built. <c>Application.Current.Resources[key]</c> would answer from the light dictionary however
/// dark the window is, and that mistake is invisible to everything except a rendered pixel.
/// </para>
/// <para>
/// So the chain is held end to end: astrid-core's tokens → `App.xaml` (`DesignTokenTests`,
/// `PriorityPaletteTests`) → the screen (here). This test deliberately reads its expectations out
/// of `App.xaml` rather than restating the hexes, so it asserts the one link the others do not and
/// cannot drift from them.
/// </para>
/// </remarks>
[Collection("ui")]
public sealed class PriorityMarkColourTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public PriorityMarkColourTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>How much of the expected colour has to be there before the mark counts as drawn.</summary>
    /// <remarks>
    /// The art is a stroked outline on a 56-unit canvas drawn at 32, so most of its pixels are
    /// anti-aliased against the row. Its straight runs are not, and there are several hundred of
    /// them; fifty is comfortably clear of a stray pixel and far below what the thinnest edge
    /// contributes.
    /// </remarks>
    private const int Enough = 50;

    /// <summary>
    /// Both marks, in both appearances — the checkbox on your own task and the square on nobody's
    /// (task f1a8ff9f).
    /// </summary>
    /// <remarks>
    /// The unassigned half is the regression. The square was a priority fill with a white glyph on
    /// it, and no priority painted the fill transparent, so an unassigned task with no priority —
    /// the ordinary row on a fresh board — drew nothing at all and the white letter went with it.
    /// It is a priority outline now, as astrid-web has always drawn it, so priority 0 has a colour
    /// like the other three. No other test here can see a white-on-white mark: this one reads the
    /// pixels.
    /// </remarks>
    [Theory]
    [InlineData("light", true)]
    [InlineData("dark", true)]
    [InlineData("light", false)]
    [InlineData("dark", false)]
    public void Every_priority_mark_draws_its_own_token_task_82e4ad32(string theme, bool assigned)
    {
        var expected = PriorityBrushes(theme);
        using var app = AstridApp.Launch(
            signedIn: true, seed: Seeds.ListWithEveryPriority(theme, assigned));
        Select(app, Seeds.PriorityListName);
        Assert.True(
            app.Sees(Seeds.PriorityTitles[0]),
            $"the seeded rows are not on screen; saw: {string.Join(", ", app.Names())}");
        // The appearance reaches the marks through ActualThemeChanged, one layout pass behind.
        Thread.Sleep(1200);

        using var shot = Capture.Window(new IntPtr(app.Window.Current.NativeWindowHandle));
        Assert.False(
            shot.IsBlank(),
            "the window captured as one flat colour, so nothing was rendered — a locked or "
            + "non-rendering session. See docs/TESTING.md.");
        var whose = assigned ? "own" : "unassigned";
        var png = shot.SavePng(
            Path.Combine(AppContext.BaseDirectory, $"priority-marks-{whose}-{theme}.png"));

        for (var priority = 0; priority < Seeds.PriorityTitles.Length; priority++)
        {
            var row = Row(app, Seeds.PriorityTitles[priority]).Current.BoundingRectangle;
            var mark = shot.Histogram((int)row.Left, (int)row.Top, MarkBand, (int)row.Height)
                .ToDictionary(entry => entry.Colour, entry => entry.Count);

            var mine = expected[priority];
            mark.TryGetValue(mine, out var drawn);
            _output.WriteLine(
                $"{theme} {whose} priority {priority}: #{mine:X6} x{drawn} (picture: {png})");
            Assert.True(
                drawn >= Enough,
                $"the {theme} {whose} mark for priority {priority} should be drawn in #{mine:X6}, and only "
                + $"{drawn} pixels of it are. The band holds: "
                + string.Join(", ", mark.OrderByDescending(e => e.Value).Take(6)
                    .Select(e => $"#{e.Key:X6} x{e.Value}"))
                + $". The picture is at {png}.");

            // And not somebody else's: one brush reaching every mark would satisfy the assertion
            // above on whichever row happened to match.
            for (var other = 0; other < expected.Length; other++)
            {
                if (expected[other] == mine)
                {
                    continue;
                }
                mark.TryGetValue(expected[other], out var intruder);
                Assert.True(
                    intruder < Enough,
                    $"the {theme} {whose} mark for priority {priority} is drawing priority {other}'s "
                    + $"#{expected[other]:X6} as well ({intruder} pixels). The picture is at {png}.");
            }
        }

        Assert.False(File.Exists(app.CrashLogPath), "the app wrote a crash log");
    }

    /// <summary>
    /// How far to the right of a row's leading edge the mark can be.
    /// </summary>
    /// <remarks>
    /// Wide enough for the 32px mark and its gutter at this machine's scaling, and well short of
    /// the title — a band that reached the text would start reading the row's words, and a label
    /// that happened to be accent-coloured would pass the test for the wrong reason.
    /// </remarks>
    private const int MarkBand = 140;

    /// <summary>
    /// The four priority colours the shell declares for a theme.
    /// </summary>
    /// <remarks>
    /// Read rather than restated. Those values are themselves pinned to astrid-core's vendored
    /// tokens by `DesignTokenTests` and `PriorityPaletteTests`, so taking them from the file keeps
    /// this test about the one thing it is for — that the declared colour reaches the element — and
    /// means a token change does not have to be typed out in three places.
    /// </remarks>
    private static int[] PriorityBrushes(string theme) =>
        new[]
        {
            "AstridPriorityNone", "AstridPriorityLow", "AstridPriorityMedium", "AstridPriorityHigh",
        }
        .Select(key => ShellColours.Brush(theme, key))
        .ToArray();

    /// <summary>The row a title sits in — the mark is at its leading edge.</summary>
    private static AutomationElement Row(AstridApp app, string title)
    {
        var walker = TreeWalker.ControlViewWalker;
        var element = app.Require(title);
        while (element is not null && element.Current.ControlType != ControlType.ListItem)
        {
            element = walker.GetParent(element);
        }
        return element ?? throw new InvalidOperationException($"'{title}' is not in a row");
    }

    private static void Select(AstridApp app, string name)
    {
        var row = app.Require(name);
        var walker = TreeWalker.ControlViewWalker;
        var element = row;
        while (element is not null && element.Current.ControlType != ControlType.ListItem)
        {
            element = walker.GetParent(element);
        }
        Assert.NotNull(element);
        var pattern = (SelectionItemPattern)element!.GetCurrentPattern(SelectionItemPattern.Pattern);
        pattern.Select();
        Thread.Sleep(800);
    }
}
