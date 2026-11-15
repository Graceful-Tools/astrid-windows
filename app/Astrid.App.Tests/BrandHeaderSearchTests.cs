using System.Xml.Linq;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// Search, inline at the far right of the brand header, with the mark at its top left
/// (task 20dcc235, the Windows half of AWTD-1180).
/// </summary>
/// <remarks>
/// <para>
/// Jon: <i>"(a) but add search icon inline on far right. Logo left header with Astrid / brand
/// logo on top left. Do across interfaces."</i> Web's half is PR #405.
/// </para>
/// <para>
/// **Windows puts that header in a different control, and on purpose.** Web's brand header is the
/// first row *inside* the sidebar. Here it is <c>TitleBarView</c> — the app draws its own title
/// bar, it wears the sidebar's layer brush, and it sits directly above the sidebar column, so the
/// top left already reads as one surface with the brand at its corner. <c>SidebarView</c>'s own
/// comment records that the mark and wordmark were moved there deliberately. So the logo half of
/// this task was already satisfied, and putting a second brand inside the sidebar would have
/// answered it by making the app worse.
/// </para>
/// <para>
/// What that leaves is a geometry problem the web half does not have. The title bar spans the
/// whole window, so "the far right of the header" is not the far right of the strip — it is the
/// far right of the *sidebar's* width, which is where web's icon lands. The header therefore
/// carries a brand block exactly as wide as the sidebar column, and the search control sits at
/// the end of it. Those two widths are the invariant, and
/// <see cref="The_brand_header_is_exactly_as_wide_as_the_sidebar_task_20dcc235"/> is what stops
/// them drifting — the failure mode the task description names, where a number restated in a
/// second place quietly stops matching the first.
/// </para>
/// <para>
/// Read off the XAML, like <see cref="WebUxParityTests"/> and <see cref="LocalisationTests"/>:
/// this is shape — which element is where, in which order, how wide — and a view-model test
/// cannot see it. The web half pins its own shape the same way, by reading
/// <c>LeftSidebar.tsx</c> rather than rendering it.
/// </para>
/// </remarks>
public sealed class BrandHeaderSearchTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The uid of the header's search control.</summary>
    private const string SearchUid = "header_search";

    private static XElement TitleBar() =>
        XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "Views", "TitleBarView.xaml")).Root!;

    private static XElement Shell() =>
        XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "ShellPage.xaml")).Root!;

    private static XElement Sidebar() =>
        XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "Views", "SidebarView.xaml")).Root!;

    private static string TitleBarCodeBehind() =>
        File.ReadAllText(Path.Combine(LocalisationTests.AppDirectory(), "Views", "TitleBarView.xaml.cs"));

    private static string ShellCodeBehind() =>
        File.ReadAllText(Path.Combine(LocalisationTests.AppDirectory(), "ShellPage.xaml.cs"));

    private static XElement Search(XElement titleBar) =>
        titleBar.Descendants().Single(element => (string?)element.Attribute(Xaml + "Uid") == SearchUid);

    /// <summary>Which grid column an element sits in; column 0 when it does not say.</summary>
    private static int Column(XElement element) =>
        int.Parse((string?)element.Attribute("Grid.Column") ?? "0");

    /// <summary>
    /// There is a search control in the brand header, and it is a control rather than a label.
    /// </summary>
    [Fact]
    public void The_brand_header_carries_a_search_control_task_20dcc235()
    {
        var search = Search(TitleBar());

        Assert.Equal("Button", search.Name.LocalName);
        // The magnifier the rest of the app already uses for search.
        Assert.Equal("", (string?)search.Attribute("Content"));
        // A bare glyph is exempt from the localisation sweep, so the name a screen reader
        // reads has to be asked for explicitly.
        Assert.Equal("Search", (string?)search.Attribute("AutomationProperties.Name"));
        Assert.NotNull((string?)search.Attribute("Click"));
    }

    /// <summary>
    /// The mark is at the top left and the search control at the far right, in that order, with
    /// nothing of the header's between the search control and its right edge.
    /// </summary>
    [Fact]
    public void The_logo_is_left_and_the_search_control_is_last_task_20dcc235()
    {
        var titleBar = TitleBar();
        var search = Search(titleBar);
        var header = search.Parent!;

        var mark = header.Elements()
            .Single(element => (string?)element.Attribute(Xaml + "Uid") == "title_mark");
        Assert.True(
            Column(mark) < Column(search),
            "the mark must come before the search control, not after it");

        // "Far right" means last: no sibling of the header's sits to the right of it.
        var rightmost = header.Elements()
            .Where(element => element.Name.LocalName != "Grid.ColumnDefinitions")
            .Max(Column);
        Assert.Equal(rightmost, Column(search));
    }

    /// <summary>
    /// The brand header is exactly as wide as the sidebar, so "the far right of the header" is
    /// the far right of the sidebar column — where web's icon is.
    /// </summary>
    /// <remarks>
    /// This is the one the task description asked for by name: <i>"Windows should state its own
    /// numbers once and pin them, rather than hand-rolling each row — that drift is what the
    /// other two platforms each had to fix."</i> WinUI has no tidy way to share one
    /// <c>GridLength</c> between two files' column definitions, so the number appears in both
    /// and this test makes them equal. Change one and this fails naming the other.
    ///
    /// <b>Where the sidebar's width lives moved in task ab4cb9ee.</b> The page's column is
    /// <c>Auto</c> now, because the sidebar is 260 wide open and a 48px rail hidden and only it
    /// knows which — so the number to compare against is the one on the sidebar's own open panel,
    /// not the page's column definition. The invariant is unchanged; what states it moved one file.
    /// </remarks>
    [Fact]
    public void The_brand_header_is_exactly_as_wide_as_the_sidebar_task_20dcc235()
    {
        var sidebarWidth = (string?)Sidebar()
            .Descendants()
            .First(element => element.Name.LocalName == "Grid" && element.Attribute("Width") is not null)
            .Attribute("Width");

        var header = Search(TitleBar()).Parent!;
        var headerWidth = (string?)header.Attribute("Width");

        Assert.Equal(sidebarWidth, headerWidth);
    }

    /// <summary>
    /// Both ways into search go through one method, so they cannot drift to different
    /// destinations.
    /// </summary>
    /// <remarks>
    /// Ctrl+K used to open the palette inline — <c>ShowPaletteAsync(true)</c> then
    /// <c>Palette.TakeFocus()</c>, two statements with no name. A second caller copying that pair
    /// is exactly how one of them later ends up not taking focus. Web's half pins the same
    /// property by counting <c>onClick={handleSearchClick}</c> twice.
    /// </remarks>
    [Fact]
    public void Both_ways_into_search_use_the_one_entry_point_task_20dcc235()
    {
        var shell = ShellCodeBehind();

        Assert.Contains("private async Task OpenSearchAsync()", shell, StringComparison.Ordinal);
        // Declared once, called twice: the Ctrl+K chord and the header's control.
        Assert.Equal(2, Occurrences(shell, "OpenSearchAsync()") - 1);
        Assert.Contains("TitleBar.SearchRequested", shell, StringComparison.Ordinal);

        // And the palette is no longer opened by hand anywhere else.
        Assert.Equal(1, Occurrences(shell, "Palette.TakeFocus()"));
    }

    /// <summary>
    /// The strip the window is dragged by stops short of the search control.
    /// </summary>
    /// <remarks>
    /// <c>Window.SetTitleBar</c> hands an element to Windows, and Windows then eats the input
    /// over it — a button inside the drag region is drawn but cannot be clicked. The header is
    /// therefore outside it, and the remaining run of the strip is what drags the window. This
    /// test exists because the failure is silent: the icon appears, and nothing happens.
    /// </remarks>
    [Fact]
    public void The_drag_region_excludes_the_search_control_task_20dcc235()
    {
        var titleBar = TitleBar();
        var search = Search(titleBar);
        var dragged = (string?)titleBar
            .Descendants()
            .Single(element => (string?)element.Attribute("AutomationProperties.AutomationId")
                               == "TitleBarDragStrip")
            .Attribute(Xaml + "Name");

        Assert.NotNull(dragged);
        Assert.Contains($"DragRegion => {dragged}", TitleBarCodeBehind(), StringComparison.Ordinal);

        // The dragged element is a sibling of the header, not an ancestor of the button.
        Assert.DoesNotContain(dragged, search.Ancestors().Select(a => (string?)a.Attribute(Xaml + "Name")));
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
