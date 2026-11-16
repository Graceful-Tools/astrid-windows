using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows.Automation;
using System.Xml.Linq;
using Xunit;

namespace Astrid.App.UITests;

/// <summary>
/// What a selected sidebar list actually looks like, on screen (DS5, task 787c7aa5).
/// </summary>
/// <remarks>
/// <para>
/// This is the clause of task 787c7aa5 that no other test could answer. Its own Done-when ends
/// <i>"and both checked by eye: an outline's width and a spring's feel are exactly what a green
/// test suite cannot see"</i>, and the run that wrote the XAML was a scheduled one, which cannot
/// build, install and look. So the code sat on <c>main</c>, gated by five tests that all read
/// XAML, with nobody having seen it draw.
/// </para>
/// <para>
/// Reading XAML is not enough here for a specific reason. <c>SidebarRowStyle</c>'s template
/// states the outline, but WinUI's own <c>ListViewItemPresenter</c> states a selected
/// <i>background</i>, and the three <c>ListViewItemBackgroundSelected*</c> theme resources that
/// used to be overridden in <c>SidebarView.xaml</c> would beat an outline while every value
/// assertion still passed. A re-declaration anywhere up the resource chain brings the fill back.
/// Only a rendered pixel says which one won.
/// </para>
/// <para>
/// The expected colour is computed, not typed: the accent over whatever is behind the row, at
/// <c>state.selection-outline-opacity</c> — the accent read out of <c>App.xaml</c> (pinned to the
/// vendored tokens by <c>DesignTokenTests</c>) and the opacity out of
/// <c>design/tokens.resolved.json</c>. A re-vendor moves this test with the tokens rather than
/// against them.
/// </para>
/// <para>
/// <b>The backdrop is sampled from the capture, not read from XAML</b>, and that is not laziness.
/// <see cref="PaletteSelectionTests"/> can name its base colour because the palette sits on
/// <c>AstridSurface</c>, an opaque brush. The sidebar's is <c>LayerFillColorDefaultBrush</c> — a
/// WinUI system brush that is <i>translucent</i>, so what is actually behind the outline is that
/// layer composited over the window's own backdrop, and no value in <c>App.xaml</c> states it.
/// Computing one would be asserting this test's arithmetic rather than the screen. Sampling the
/// sidebar just below the row is also what an eye does: it compares the selected row with the
/// space around it.
/// </para>
/// <para>
/// Both halves are asserted, because an outline and a fill differ in two places: the border is
/// drawn, <i>and</i> the inside is left alone. Checking only the first passes a row that does
/// both — which is exactly what the old sidebar did, minus the border.
/// </para>
/// <para>
/// The sibling of <see cref="PaletteSelectionTests"/>, which asks the same question of the command
/// palette. Both exist because DS5 turned out to be per-surface: the task rows keep DS3's tint and
/// the board has no selection at all, so "the shell outlines its selection" is a claim that has to
/// be made one surface at a time.
/// </para>
/// </remarks>
[Collection("ui")]
public sealed class SidebarSelectionTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public SidebarSelectionTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>How much of the outline has to be there before it counts as drawn.</summary>
    /// <remarks>
    /// A sidebar row is a couple of hundred pixels wide and the outline runs the whole of its top
    /// and bottom edge, so a drawn one contributes a three- to four-figure count; only the rounded
    /// corners are anti-aliased away from the exact value. Fifty is far below one straight edge
    /// and comfortably above a stray pixel. The same number <see cref="PaletteSelectionTests"/>
    /// uses, for the same reason.
    /// </remarks>
    private const int Enough = 50;

    /// <summary>How far below the row to sample the sidebar it sits on, in physical pixels.</summary>
    /// <remarks>
    /// Rows are separated by more than this, and the list the seed adds is the last one, so the
    /// band is empty sidebar either way.
    /// </remarks>
    private const int Gap = 8;

    /// <summary>How far inside the row to look for a fill, in physical pixels.</summary>
    /// <remarks>
    /// Clear of the outline itself at any scaling this machine runs — 1.5 logical pixels is 3 at
    /// 200% — and clear of the corner radius, so the band is the row's inside and nothing else.
    /// </remarks>
    private const int Inset = 12;

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void The_selected_sidebar_list_is_outlined_not_filled_task_787c7aa5(string theme)
    {
        using var app = AstridApp.Launch(
            signedIn: true, seed: Seeds.ListWithEveryPriority(theme));
        Select(app, Seeds.PriorityListName);

        using var shot = Capture.Window(new IntPtr(app.Window.Current.NativeWindowHandle));
        Assert.False(
            shot.IsBlank(),
            "the window captured as one flat colour, so nothing was rendered — a locked or "
            + "non-rendering session. See docs/TESTING.md.");
        var png = shot.SavePng(
            Path.Combine(AppContext.BaseDirectory, $"sidebar-selected-{theme}.png"));

        var bounds = Row(app, Seeds.PriorityListName).Current.BoundingRectangle;

        // The sidebar immediately below the row: empty space, same horizontal band, clear of the
        // row's own outline and corner radius.
        var behind = Commonest(Counts(
            shot,
            (int)bounds.Left + Inset,
            (int)bounds.Bottom + Gap,
            (int)bounds.Width - (2 * Inset),
            Gap));
        var outline = Over(Brush(theme, "AstridAccentColor"), behind, OutlineOpacity());
        _output.WriteLine($"{theme}: the sidebar behind the row is #{behind:X6}");
        Assert.False(
            Close(behind, outline),
            $"the sample of the sidebar below the row, #{behind:X6}, is already the outline "
            + $"colour, so this test cannot tell the two apart. The picture is at {png}.");

        var whole = Counts(shot, (int)bounds.Left, (int)bounds.Top, (int)bounds.Width, (int)bounds.Height);
        var drawn = Near(whole, outline);
        _output.WriteLine($"{theme}: outline #{outline:X6} x{drawn} (picture: {png})");
        Assert.True(
            drawn >= Enough,
            $"the selected sidebar list should be outlined in #{outline:X6} — the accent over the "
            + $"sidebar at the token's opacity — and only {drawn} pixels of it are. The row holds: "
            + $"{Top(whole)}. The picture is at {png}.");

        // And the inside is the sidebar it sits on. This is the half that catches a returning
        // ListViewItemBackgroundSelected: a fill leaves the outline intact and recolours here.
        var inside = Counts(
            shot,
            (int)bounds.Left + Inset,
            (int)bounds.Top + Inset,
            (int)bounds.Width - (2 * Inset),
            (int)bounds.Height - (2 * Inset));
        var commonest = Commonest(inside);
        _output.WriteLine($"{theme}: inside the row #{commonest:X6}, against the sidebar #{behind:X6}");
        Assert.True(
            Close(commonest, behind),
            $"the selected sidebar list is filled with #{commonest:X6} rather than left as the "
            + $"sidebar it sits on, #{behind:X6} — so something is drawing a selected background "
            + $"over DS5's outline. The row holds: {Top(inside)}. The picture is at {png}.");

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
        var pattern = (SelectionItemPattern)Row(app, name)
            .GetCurrentPattern(SelectionItemPattern.Pattern);
        pattern.Select();
        Thread.Sleep(800);
    }

    private static int Commonest(Dictionary<int, int> counts) =>
        counts.MaxBy(entry => entry.Value).Key;

    private static Dictionary<int, int> Counts(Capture shot, int left, int top, int width, int height) =>
        shot.Histogram(left, top, width, height)
            .ToDictionary(entry => entry.Colour, entry => entry.Count);

    private static string Top(Dictionary<int, int> counts) =>
        string.Join(
            ", ",
            counts.OrderByDescending(entry => entry.Value).Take(6)
                .Select(entry => $"#{entry.Key:X6} x{entry.Value}"));

    /// <summary>Pixels at the expected colour, give or take one step per channel.</summary>
    /// <remarks>
    /// The blend is the composition engine's, not this test's arithmetic, so the two can land a
    /// value apart on a channel where the rounding goes the other way.
    /// </remarks>
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

    /// <summary>One colour drawn over another at an opacity, as the compositor would.</summary>
    private static int Over(int top, int bottom, double opacity)
    {
        var blended = 0;
        for (var shift = 0; shift <= 16; shift += 8)
        {
            var channel = (int)Math.Round(
                (((top >> shift) & 0xFF) * opacity) + (((bottom >> shift) & 0xFF) * (1 - opacity)));
            blended |= channel << shift;
        }
        return blended;
    }

    private static double OutlineOpacity() =>
        JsonDocument
            .Parse(File.ReadAllText(
                Path.Combine(RepositoryRoot(), "design", "tokens.resolved.json")))
            .RootElement.GetProperty("state").GetProperty("selection-outline-opacity").GetDouble();

    /// <summary>The colour the shell states for a key, in the theme on screen.</summary>
    /// <remarks>
    /// <c>AstridAccentColor</c> sits outside the theme dictionaries, because the accent does not
    /// follow the theme (astrid-core DS26) — so <see cref="ShellColours"/> looks in the themed
    /// dictionary first and in <c>App.xaml</c> second, rather than special-casing it by name.
    /// </remarks>
    private static int Brush(string theme, string key) => ShellColours.Brush(theme, key);

    private static string RepositoryRoot() => ShellColours.RepositoryRoot();
}
