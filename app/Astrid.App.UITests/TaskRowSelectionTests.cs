using System.IO;
using System.Threading;
using System.Windows.Automation;
using System.Xml.Linq;
using Xunit;

namespace Astrid.App.UITests;

/// <summary>
/// A selected task row is a tint with a line around it (DS3, task 066ae38c).
/// </summary>
/// <remarks>
/// <para>
/// <c>AstridBgSelectedBorder</c> was declared in all three theme dictionaries, asserted against the
/// vendored tokens by <c>DesignTokenTests</c>, and <b>read nowhere</b>. So the shell had a
/// translated, token-checked, test-covered colour that nothing drew — which is the shape of bug a
/// XAML-reading test cannot see by construction: every assertion about it passed.
/// </para>
/// <para>
/// The register pairs the two: <c>bg-selected</c> is the fill and <c>bg-selected-border</c> is
/// "the line around a selected row", and DS26 keeps both deliberately among the values that stay.
/// Task <c>16392d99</c> decided that a task row takes <b>this</b> answer rather than DS5's outline
/// — the outline is the chrome's selection, the tint is content's — so a row that drew only the
/// tint was drawing that answer half way.
/// </para>
/// <para>
/// Both halves are asserted, and in this direction they are genuinely independent: the tint was
/// already right, and asserting only the line would pass a row that had lost it.
/// </para>
/// <para>
/// Sibling of <see cref="SidebarSelectionTests"/>, which asks the opposite question of the chrome:
/// an outline with the surface <i>untouched</i> inside it. The two together are why DS5 and DS3
/// cannot be collapsed into one rule.
/// </para>
/// </remarks>
[Collection("ui")]
public sealed class TaskRowSelectionTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public TaskRowSelectionTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>How much of the line has to be there before it counts as drawn.</summary>
    /// <remarks>
    /// A task row is some eight hundred pixels wide and the line runs the whole of its top and
    /// bottom edge, so a drawn one contributes a four-figure count.
    /// </remarks>
    private const int Enough = 50;

    /// <summary>How far inside the row to look for the fill, in physical pixels.</summary>
    private const int Inset = 14;

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void A_selected_task_row_is_a_tint_with_a_line_around_it_task_066ae38c(string theme)
    {
        var line = Brush(theme, "AstridBgSelectedBorder");
        var tint = Brush(theme, "AstridBgSelected");

        using var app = AstridApp.Launch(
            signedIn: true, seed: Seeds.ListWithEveryPriority(theme));
        Select(app, Seeds.PriorityListName);
        var row = Row(app, Seeds.PriorityTitles[0]);
        ((SelectionItemPattern)row.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Thread.Sleep(800);

        using var shot = Capture.Window(new IntPtr(app.Window.Current.NativeWindowHandle));
        Assert.False(
            shot.IsBlank(),
            "the window captured as one flat colour, so nothing was rendered — a locked or "
            + "non-rendering session. See docs/TESTING.md.");
        var png = shot.SavePng(
            Path.Combine(AppContext.BaseDirectory, $"task-row-selected-{theme}.png"));

        var bounds = row.Current.BoundingRectangle;
        var whole = Counts(shot, (int)bounds.Left, (int)bounds.Top, (int)bounds.Width, (int)bounds.Height);

        var drawn = Near(whole, line);
        _output.WriteLine($"{theme}: line #{line:X6} x{drawn}, tint #{tint:X6} (picture: {png})");
        Assert.True(
            drawn >= Enough,
            $"the selected task row has no line around it: only {drawn} pixels of "
            + $"#{line:X6} (AstridBgSelectedBorder). The row holds: {Top(whole)}. The picture is "
            + $"at {png}.");

        var inside = Counts(
            shot,
            (int)bounds.Left + Inset,
            (int)bounds.Top + Inset,
            (int)bounds.Width - (2 * Inset),
            (int)bounds.Height - (2 * Inset));
        var commonest = Commonest(inside);
        _output.WriteLine($"{theme}: inside the row #{commonest:X6}");
        Assert.True(
            Close(commonest, tint),
            $"the selected task row is #{commonest:X6} inside rather than the selected tint "
            + $"#{tint:X6} (AstridBgSelected). The row holds: {Top(inside)}. The picture is "
            + $"at {png}.");

        Assert.False(File.Exists(app.CrashLogPath), "the app wrote a crash log");
    }

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
        ((SelectionItemPattern)Row(app, name).GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Thread.Sleep(800);
    }

    private static int Commonest(Dictionary<int, int> counts) => counts.MaxBy(entry => entry.Value).Key;

    private static Dictionary<int, int> Counts(Capture shot, int left, int top, int width, int height) =>
        shot.Histogram(left, top, width, height)
            .ToDictionary(entry => entry.Colour, entry => entry.Count);

    private static string Top(Dictionary<int, int> counts) =>
        string.Join(
            ", ",
            counts.OrderByDescending(entry => entry.Value).Take(6)
                .Select(entry => $"#{entry.Key:X6} x{entry.Value}"));

    private static int Near(Dictionary<int, int> counts, int wanted) =>
        counts.Where(entry => Close(entry.Key, wanted)).Sum(entry => entry.Value);

    private static bool Close(int colour, int wanted)
    {
        for (var shift = 0; shift <= 16; shift += 8)
        {
            if (Math.Abs(((colour >> shift) & 0xFF) - ((wanted >> shift) & 0xFF)) > 1)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>The colour the shell states for a key, in the theme on screen.</summary>
    private static int Brush(string theme, string key) => ShellColours.Brush(theme, key);
}
