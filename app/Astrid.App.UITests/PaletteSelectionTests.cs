using System.IO;
using System.Text.Json;
using System.Threading;
using System.Windows.Automation;
using System.Xml.Linq;
using Xunit;

namespace Astrid.App.UITests;

/// <summary>
/// What a highlighted command-palette row actually looks like, on screen (task b37d76c3).
/// </summary>
/// <remarks>
/// <para>
/// The palette was a bare <c>ListView</c> with <c>SelectionMode="Single"</c>, so WinUI's own
/// <c>ListViewItemPresenter</c> drew its highlight: the row filled, with the accent indicator bar
/// down the left. Every other selectable surface in the shell had been answered — the sidebar and
/// the settings cards outline (DS5, task 787c7aa5), the task rows tint (DS3) — and this one had
/// never been asked. The fix is one attribute, and that is exactly why it needs looking at: a
/// template swap is the change a green gate cannot see. <c>DesignTokenTests</c> can say the
/// attribute is there; only a rendered pixel says an outline came out.
/// </para>
/// <para>
/// The expected colour is computed rather than typed: the accent over the card at
/// <c>state.selection-outline-opacity</c>, with the accent and the card read out of
/// <c>App.xaml</c> — themselves pinned to the vendored tokens by <c>DesignTokenTests</c> — and the
/// opacity read out of <c>design/tokens.resolved.json</c>. So a re-vendor moves this test with the
/// tokens instead of against them.
/// </para>
/// <para>
/// Both halves are asserted, because an outline and a fill differ in two places: the border is
/// drawn, <i>and</i> the inside of the row is left alone. Checking only the first would pass on a
/// row that outlined and filled.
/// </para>
/// </remarks>
[Collection("ui")]
public sealed class PaletteSelectionTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public PaletteSelectionTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>
    /// How much of the outline has to be there before it counts as drawn.
    /// </summary>
    /// <remarks>
    /// The row is some five hundred pixels wide and the outline runs along the whole of its top and
    /// bottom edge, so a drawn one contributes a four-figure count. Only the rounded corners are
    /// anti-aliased away from the exact value. Fifty is far below what one straight edge gives and
    /// comfortably above a stray pixel.
    /// </remarks>
    private const int Enough = 50;

    /// <summary>How far inside the row to look for a fill, in physical pixels.</summary>
    /// <remarks>
    /// Clear of the outline itself at any scaling this machine runs — 1.5 logical pixels is 3 at
    /// 200% — and clear of the 8-pixel corner radius, so the band is the row's inside and nothing
    /// else.
    /// </remarks>
    private const int Inset = 12;

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void The_highlighted_palette_row_is_outlined_not_filled_task_b37d76c3(string theme)
    {
        var card = Brush(theme, "AstridSurface");
        var outline = Over(Brush(theme, "AstridAccentColor"), card, OutlineOpacity());

        using var app = AstridApp.Launch(signedIn: true, seed: Seeds.ListWithEveryPriority(theme));
        app.Invoke("Search");

        var list = app.FindById("PaletteList")
            ?? throw new InvalidOperationException(
                $"the palette did not open; saw: {string.Join(", ", app.Names())}");
        var rows = list.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
        Assert.True(rows.Count > 0, "the palette opened with no rows to highlight");

        var row = rows[0];
        ((SelectionItemPattern)row.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Thread.Sleep(800);

        using var shot = Capture.Window(new IntPtr(app.Window.Current.NativeWindowHandle));
        Assert.False(
            shot.IsBlank(),
            "the window captured as one flat colour, so nothing was rendered — a locked or "
            + "non-rendering session. See docs/TESTING.md.");
        var png = shot.SavePng(Path.Combine(AppContext.BaseDirectory, $"palette-selected-{theme}.png"));

        var bounds = row.Current.BoundingRectangle;
        var whole = Counts(shot, (int)bounds.Left, (int)bounds.Top, (int)bounds.Width, (int)bounds.Height);
        var drawn = Near(whole, outline);
        _output.WriteLine($"{theme}: outline #{outline:X6} x{drawn} (picture: {png})");
        Assert.True(
            drawn >= Enough,
            $"the {theme} palette's highlighted row should be outlined in #{outline:X6} — the "
            + "accent over the card at the token's opacity — and only "
            + $"{drawn} pixels of it are. The row holds: {Top(whole)}. The picture is at {png}.");

        // And the inside of the row is the card it sits on: an outline leaves it alone, where
        // WinUI's presenter and DS3's tint both colour it.
        var inside = Counts(
            shot,
            (int)bounds.Left + Inset,
            (int)bounds.Top + Inset,
            (int)bounds.Width - (2 * Inset),
            (int)bounds.Height - (2 * Inset));
        var commonest = inside.MaxBy(entry => entry.Value).Key;
        _output.WriteLine($"{theme}: inside the row #{commonest:X6}, against the card #{card:X6}");
        Assert.True(
            commonest == card,
            $"the {theme} palette's highlighted row is filled with #{commonest:X6} rather than "
            + $"left as the card it sits on, #{card:X6}. The row holds: {Top(inside)}. The picture "
            + $"is at {png}.");

        Assert.False(File.Exists(app.CrashLogPath), "the app wrote a crash log");
    }

    private static Dictionary<int, int> Counts(Capture shot, int left, int top, int width, int height) =>
        shot.Histogram(left, top, width, height)
            .ToDictionary(entry => entry.Colour, entry => entry.Count);

    private static string Top(Dictionary<int, int> counts) =>
        string.Join(
            ", ",
            counts.OrderByDescending(entry => entry.Value).Take(6)
                .Select(entry => $"#{entry.Key:X6} x{entry.Value}"));

    /// <summary>
    /// Pixels at the expected colour, give or take one step per channel.
    /// </summary>
    /// <remarks>
    /// The blend is the composition engine's, not this test's arithmetic, so the two can land a
    /// value apart on a channel where the rounding goes the other way. A window of one keeps that
    /// from being a failure while staying far away from any other colour on a palette row.
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

    /// <summary>
    /// A colour <c>App.xaml</c> states, from the theme dictionary that is on screen.
    /// </summary>
    /// <remarks>
    /// <c>AstridAccentColor</c> sits outside the theme dictionaries, because the accent does not
    /// follow the theme (astrid-core DS26) — so it is looked for in the dictionary first and in the
    /// file second, rather than being special-cased by name.
    /// </remarks>
    private static int Brush(string theme, string key)
    {
        var xaml = XDocument.Load(Path.Combine(RepositoryRoot(), "app", "Astrid.App", "App.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var wanted = theme == "dark" ? "Dark" : "Light";
        var dictionary = xaml.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "ResourceDictionary"
                && (string?)element.Attribute(x + "Key") == wanted)
            ?? throw new InvalidOperationException($"App.xaml has no '{wanted}' theme dictionary");

        var stated = dictionary.Elements()
                .FirstOrDefault(element => (string?)element.Attribute(x + "Key") == key)
            ?? xaml.Descendants()
                .FirstOrDefault(element => (string?)element.Attribute(x + "Key") == key)
            ?? throw new InvalidOperationException($"App.xaml does not state {key}");

        var value = (string?)stated.Attribute("Color") ?? stated.Value;
        return Convert.ToInt32(value.Trim().TrimStart('#'), 16);
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
}
