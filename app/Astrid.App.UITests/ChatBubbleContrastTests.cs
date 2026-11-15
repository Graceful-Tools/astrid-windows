using System.IO;
using System.Threading;
using System.Windows.Automation;
using System.Xml.Linq;
using Xunit;

namespace Astrid.App.UITests;

/// <summary>
/// A chat bubble's ground follows the window's appearance (task 770625f8).
/// </summary>
/// <remarks>
/// <para>
/// The bug this guards was white-on-white: in dark mode both bubbles came out with LIGHT grounds
/// and the message text drawn in white on top, so a conversation was very nearly unreadable.
/// </para>
/// <para>
/// The cause is worth stating because it is invisible to every other kind of test.
/// <c>Application.Current.Resources[key]</c> resolves a key in <c>ThemeDictionaries</c> against
/// <c>Application.RequestedTheme</c> — and this app never sets that. <c>ShellPage.ApplyTheme</c>
/// sets <c>RequestedTheme</c> on the content root instead, because that is the only way to change
/// the appearance after startup. So every XAML <c>ThemeResource</c> follows the element while
/// every lookup from code went on answering out of the LIGHT dictionary, however dark the window
/// was. <c>ThemeBrushes.For</c> asks the element.
/// </para>
/// <para>
/// <see cref="ThemedBrushReadsTests"/> guards the mechanism — no shell code reads a themeable
/// brush off the application — and is the stronger of the two, because it catches every such site
/// rather than this one. This test guards the OUTCOME, which is what somebody actually complained
/// about, and it is the only one that would notice if the brushes themselves were repointed at
/// the wrong values.
/// </para>
/// <para>
/// Both bubbles are checked, because they are different brushes: the reader's own is
/// <c>AstridBgSelected</c> and somebody else's is <c>AstridSurfaceHover</c>. The original bug hit
/// both, and a fix that reached only one would still read as broken.
/// </para>
/// </remarks>
[Collection("ui")]
public sealed class ChatBubbleContrastTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public ChatBubbleContrastTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>How much of the ground has to be there before the bubble counts as drawn in it.</summary>
    /// <remarks>
    /// Sampled from a band inside the bubble and clear of its text, so a drawn ground contributes
    /// a four-figure count. Two hundred is far below that and far above an anti-aliased edge.
    /// </remarks>
    private const int Enough = 200;

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public void A_chat_bubbles_ground_follows_the_window_task_770625f8(string theme)
    {
        using var app = AstridApp.Launch(
            signedIn: true, seed: Seeds.ListWithEveryPriority(theme));
        Select(app, Seeds.PriorityListName);
        Thread.Sleep(1200);

        using var shot = Capture.Window(new IntPtr(app.Window.Current.NativeWindowHandle));
        Assert.False(
            shot.IsBlank(),
            "the window captured as one flat colour, so nothing was rendered — a locked or "
            + "non-rendering session. See docs/TESTING.md.");
        var png = shot.SavePng(
            Path.Combine(AppContext.BaseDirectory, $"chat-bubbles-{theme}.png"));

        // The reader's own bubble, and somebody else's. Different brushes on purpose.
        Ground(app, shot, theme, Seeds.MineSaid, "AstridBgSelected", png);
        Ground(app, shot, theme, Seeds.TheirsSaid, "AstridSurfaceHover", png);

        Assert.False(File.Exists(app.CrashLogPath), "the app wrote a crash log");
    }

    private void Ground(AstridApp app, Capture shot, string theme, string said, string key, string png)
    {
        var wanted = Brush(theme, key);
        var text = Message(app, said).Current.BoundingRectangle;

        // A band to the LEFT of the text and inside the bubble: ground, never glyph. The bubble's
        // padding is comfortably wider than this, and sampling beside the text rather than over it
        // keeps the count free of anti-aliased letter edges.
        var counts = shot.Histogram(
            (int)text.Left - 20, (int)text.Top, 16, (int)text.Height);
        var drawn = counts.Where(entry => Close(entry.Colour, wanted)).Sum(entry => entry.Count);

        _output.WriteLine($"{theme}/{said}: {key} #{wanted:X6} x{drawn}");
        Assert.True(
            drawn >= Enough,
            $"the {theme} bubble for \"{said}\" is not drawn on {key} (#{wanted:X6}) — only "
            + $"{drawn} pixels of it beside the text. In dark mode this is the white-on-white "
            + $"bug: the ground came out of the LIGHT dictionary. Found: "
            + $"{string.Join(", ", counts.OrderByDescending(e => e.Count).Take(4).Select(e => $"#{e.Colour:X6} x{e.Count}"))}. "
            + $"The picture is at {png}.");
    }

    /// <summary>
    /// The element carrying a message's text, matched on its TRIMMED name.
    /// </summary>
    /// <remarks>
    /// The bubble's accessible name carries a trailing newline — the text block's own — so an
    /// exact match finds nothing while the name is plainly there in the tree dump. Trimming is
    /// the difference between a test that runs and one that reports the message missing.
    /// </remarks>
    private static AutomationElement Message(AstridApp app, string said)
    {
        var found = app.Window
            .FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .FirstOrDefault(element => (element.Current.Name ?? string.Empty).Trim() == said);
        return found ?? throw new InvalidOperationException(
            $"no bubble whose text is \"{said}\"; the chat pane may not have opened");
    }

    private static void Select(AstridApp app, string name)
    {
        var walker = TreeWalker.ControlViewWalker;
        var element = app.Require(name);
        while (element is not null && element.Current.ControlType != ControlType.ListItem)
        {
            element = walker.GetParent(element);
        }
        ((SelectionItemPattern)element!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Thread.Sleep(800);
    }

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

    /// <summary>A colour <c>App.xaml</c> states, from the theme dictionary that is on screen.</summary>
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
            ?? throw new InvalidOperationException($"App.xaml's {wanted} dictionary does not state {key}");

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
            ?? throw new InvalidOperationException("no repository root above " + AppContext.BaseDirectory);
    }
}
