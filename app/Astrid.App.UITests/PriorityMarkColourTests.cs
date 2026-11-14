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

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void Every_priority_mark_draws_its_own_token_task_82e4ad32(string theme)
    {
        var expected = PriorityBrushes(theme);
        using var app = AstridApp.Launch(signedIn: true, seed: Seeds.ListWithEveryPriority(theme));
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
        var png = shot.SavePng(Path.Combine(AppContext.BaseDirectory, $"priority-marks-{theme}.png"));

        for (var priority = 0; priority < Seeds.PriorityTitles.Length; priority++)
        {
            var row = Row(app, Seeds.PriorityTitles[priority]).Current.BoundingRectangle;
            var mark = shot.Histogram((int)row.Left, (int)row.Top, MarkBand, (int)row.Height)
                .ToDictionary(entry => entry.Colour, entry => entry.Count);

            var mine = expected[priority];
            mark.TryGetValue(mine, out var drawn);
            _output.WriteLine(
                $"{theme} priority {priority}: #{mine:X6} x{drawn} (picture: {png})");
            Assert.True(
                drawn >= Enough,
                $"the {theme} mark for priority {priority} should be drawn in #{mine:X6}, and only "
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
                    $"the {theme} mark for priority {priority} is drawing priority {other}'s "
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
    /// The four priority colours a theme dictionary in <c>App.xaml</c> declares.
    /// </summary>
    /// <remarks>
    /// Read rather than restated. Those values are themselves pinned to astrid-core's vendored
    /// tokens by `DesignTokenTests` and `PriorityPaletteTests`, so taking them from the file keeps
    /// this test about the one thing it is for — that the declared colour reaches the element — and
    /// means a token change does not have to be typed out in three places.
    /// </remarks>
    private static int[] PriorityBrushes(string theme)
    {
        var xaml = XDocument.Load(Path.Combine(RepositoryRoot(), "app", "Astrid.App", "App.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var wanted = theme == "dark" ? "Dark" : "Light";
        var dictionary = xaml.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "ResourceDictionary"
                && (string?)element.Attribute(x + "Key") == wanted)
            ?? throw new InvalidOperationException($"App.xaml has no '{wanted}' theme dictionary");

        return new[]
            {
                "AstridPriorityNone", "AstridPriorityLow", "AstridPriorityMedium", "AstridPriorityHigh",
            }
            .Select(key => dictionary.Elements()
                .FirstOrDefault(element => (string?)element.Attribute(x + "Key") == key)
                ?? throw new InvalidOperationException($"the {wanted} dictionary has no {key}"))
            .Select(brush => Convert.ToInt32(
                ((string?)brush.Attribute("Color") ?? throw new InvalidOperationException(
                    "a priority brush has no Color")).TrimStart('#'),
                16))
            .ToArray();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "design")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException("could not find the repository root");
    }

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
