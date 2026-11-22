using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;
using Xunit;

namespace Astrid.App.UITests;

/// <summary>
/// The project board: its swimlanes, how it takes the window from the list, and what it says
/// when it cannot be read (task f8b844dd).
/// </summary>
/// <remarks>
/// <para>
/// The swimlanes need <c>GET /api/v1/projects/{id}/board-rows</c>, which is a network read with
/// no cache fallback by design: the core falls back to the last answer only for a RETRYABLE
/// failure and treats a refusal as an error, because "the cache is for a missing network, not for
/// a server that said no". So these drive a <see cref="StubServer"/> (task 68b00422) — the first
/// tests here to need one. The two that do not, below, are the ones about what happens when the
/// read fails, and they run without it.
/// </para>
/// <para>
/// The layout is what the built app is for. <see cref="Astrid.App.Tests.ProjectBoardViewModelTests"/>
/// has nineteen assertions over a scripted core and not one of them can see where anything lands:
/// a view that put every cell in one column, or drew the second row's cells under the first
/// row's, or let the column strip and the cells drift to different widths, passes all nineteen.
/// </para>
/// <para>
/// Both halves earned their place on their first run, each finding a bug the 685 unit tests had
/// walked past:
/// </para>
/// <para>
/// 1. A card 974 pixels wide where a swimlane cell is 264 — the LIST's row, still drawn on top of
/// the board, because <c>TaskRowsView</c>'s visibility was bound to <c>IsBoardView</c> and knew
/// nothing about a third view.
/// </para>
/// <para>
/// 2. The cross that removes a row was OFFSCREEN. The row head stretched the width of every
/// column together — the board scrolls horizontally, so a star column is measured against
/// infinity — putting it a thousand pixels right of the window. UIA reported both buttons and the
/// count was right; their <c>BoundingRectangle</c> was <c>Rect.Empty</c>. **Count a control and
/// then measure it**: one that exists and cannot be seen passes every test that only counts.
/// </para>
/// <para>
/// <b>UI Automation names and bounds, not pixels</b> — the trade
/// <see cref="BoardCalloutTests"/> documents. No colour arithmetic and no window focus, so these
/// run in a locked session. The two screenshots are a record to look at, not an assertion; only
/// that the window drew something at all is asserted.
/// </para>
/// </remarks>
[Collection("ui")]
public sealed class ProjectBoardTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public ProjectBoardTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The toggle is offered on a list that belongs to a project, and turning it on replaces the
    /// list's rows with the board rather than drawing over them.
    /// </summary>
    [Fact]
    public void The_project_board_replaces_the_list_it_was_opened_from_task_f8b844dd()
    {
        using var app = AstridApp.Launch(signedIn: true, seed: Seeds.ProjectWithTwoLists);
        Thread.Sleep(1200);
        SelectAProjectList(app);

        // Before: the list's rows are what is drawn, and both toggles are offered because this
        // list belongs to a project.
        var rowBefore = Bounds(app, Seeds.FirstRowCardTitle);
        _output.WriteLine($"the list's row: {rowBefore}");
        Assert.True(rowBefore.Width > 0, "the list's own row is on screen to begin with");
        Assert.True(app.Sees("Board view"), "the list's own board is offered");
        Assert.True(app.Sees("Project board"), "and so is the project's");

        app.Toggle("Project board");
        Thread.Sleep(1800);

        // After: the board has the window. This is the assertion that was red — the list's rows
        // were still there, underneath, because their visibility knew only two views.
        Assert.True(app.Sees("Drag a list from the sidebar to add a row"),
            "the project board is drawn");
        var rowAfter = Bounds(app, Seeds.FirstRowCardTitle);
        _output.WriteLine($"the list's row after: {rowAfter}");
        // IsEmpty, not Width == 0: WPF's Rect.Empty carries NEGATIVE infinity extents, so a
        // "width is zero" check passes for a rectangle that exists and fails for one that does
        // not — the exact wrong way round.
        Assert.True(rowAfter.IsEmpty,
            $"the list's rows are gone, not drawn under the board (found {rowAfter})");
    }

    /// <summary>
    /// A board whose rows could not be read says why, and draws no grid.
    /// </summary>
    /// <remarks>
    /// The core refuses rather than falling back when the server says no, so this is the ordinary
    /// shape of the failure rather than an exotic one: an expired session on a machine that has
    /// the project cached. An empty grid with no explanation would read as a project somebody had
    /// emptied — which is a state this board has, and a different one.
    /// </remarks>
    [Fact]
    public void A_board_that_cannot_be_read_says_so_rather_than_drawing_nothing_task_f8b844dd()
    {
        using var app = AstridApp.Launch(signedIn: true, seed: Seeds.ProjectWithTwoLists);
        Thread.Sleep(1200);
        SelectAProjectList(app);
        app.Toggle("Project board");
        Thread.Sleep(1800);

        var names = app.Names();
        _output.WriteLine("on screen: " + string.Join(" | ", names));

        // The refusal is on screen, in the core's words.
        Assert.Contains("the session is not valid", names);

        // And neither empty-board notice is: those are answers about a board that WAS read, and
        // saying "this project has no lists yet" about a board nobody could read would be a
        // sentence the app made up.
        Assert.DoesNotContain("This project has no lists yet", names);
        Assert.DoesNotContain("This board has no rows", names);

        // No rows, and so no row headers to remove.
        Assert.Empty(app.Window
            .FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "Remove this row"))
            .Cast<AutomationElement>());
    }

    /// <summary>
    /// The sidebar draws a Projects section, and a project expands to its lists (task f8b844dd).
    /// </summary>
    /// <remarks>
    /// Reachable here where the board itself is not, because the sidebar is a projection over the
    /// cache: no server is involved, so the whole section — the heading, the project rows, the
    /// chevron and the lists under it — is real in this window. The flag that gates it is a
    /// metadata row the seed writes, which is where the core reads it from.
    /// </remarks>
    [Fact]
    public void The_sidebar_draws_a_projects_section_that_expands_task_f8b844dd()
    {
        using var app = AstridApp.Launch(signedIn: true, seed: Seeds.ProjectWithTwoLists);
        Thread.Sleep(1500);

        Assert.True(app.Sees("Projects"), "the section's heading is drawn");
        var project = Bounds(app, Seeds.ProjectName);
        _output.WriteLine($"the project row: {project}");
        Assert.True(project.Width > 0, "the project has a row");

        // Shut to begin with: the chevron is there, and the lists are not under it yet.
        var chevron = app.Require("Show the project's lists");
        var listsBefore = ListRowsUnder(app, project);
        _output.WriteLine($"{listsBefore} list rows under the project before");

        ((InvokePattern)chevron.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Thread.Sleep(900);

        var listsAfter = ListRowsUnder(app, project);
        _output.WriteLine($"{listsAfter} after");
        Assert.True(listsAfter > listsBefore,
            $"expanding put the project's lists under it ({listsBefore} -> {listsAfter})");

        // And a picture of it, for the by-eye half. The assertions above say the rows are there
        // and in the right order; whether the indent, the dot and the chevron READ as a section is
        // not a rectangle, and the only way to answer it is to look. It is left in the system
        // temp directory, named by this run, so whoever ran the suite can open it afterwards —
        // the app's own data directory would not do, since Dispose takes that away with the app.
        using var shot = Capture.Window(new IntPtr(app.Window.Current.NativeWindowHandle));
        var path = shot.SavePng(Path.Combine(
            Path.GetTempPath(), $"astrid-projects-sidebar-{Environment.ProcessId}.png"));
        _output.WriteLine($"screenshot: {path}");
        Assert.False(shot.IsBlank(), "the window actually drew something");
    }

    /// <summary>
    /// How many rows named after one of the project's lists sit BELOW the project's own row.
    /// </summary>
    /// <remarks>
    /// Below, because each of those lists also has a row in the Lists section above — the core
    /// puts a project's lists in both sections on purpose — so counting them anywhere on screen
    /// would count the same number before and after.
    /// </remarks>
    private static int ListRowsUnder(AstridApp app, System.Windows.Rect project) =>
        new[] { Seeds.FirstRowName, Seeds.SecondRowName }
            .SelectMany(name => app.Window
                .FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.NameProperty, name))
                .Cast<AutomationElement>())
            .Select(element => element.Current.BoundingRectangle)
            .Count(bounds => bounds.Width > 0 && bounds.Y > project.Y);

    /// <summary>
    /// The board draws a row per list, in the order the SERVER saved, and the rows line up with
    /// the one column strip above them (task f8b844dd; the harness is task 68b00422).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the assertion set that could not run until the harness had a server. It is the one
    /// a view-model test cannot stand in for: a view that put every cell in one column, or drew
    /// the second row's cells under the first row's, or let the strip and the cells drift to
    /// different widths, passes all 19 of those and is obviously wrong to look at.
    /// </para>
    /// <para>
    /// The saved order is <c>iOS, Web</c> — the reverse of both the lists' name order and the
    /// order the cache holds them in. A board that drew its rows from the cache rather than from
    /// the core's answer would therefore draw them the other way round, and this would say so.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_project_board_draws_its_rows_in_the_saved_order_and_in_line_task_f8b844dd()
    {
        using var server = StubServer.Start()
            .Route("/api/v1/projects/p1/board-rows", """{"listIds":["lios","lweb"]}""");
        using var app = AstridApp.Launch(
            signedIn: true, seed: Seeds.ProjectWithTwoLists, server: server);
        Thread.Sleep(1200);
        SelectAProjectList(app);
        app.Toggle("Project board");
        Thread.Sleep(2000);

        var ios = CardBounds(app, Seeds.SecondRowCardTitle);
        var web = CardBounds(app, Seeds.FirstRowCardTitle);
        _output.WriteLine($"iOS card {ios}, Web card {web}");
        _output.WriteLine("unanswered: " + string.Join(", ", server.Unanswered()));

        // 1. Both rows are drawn at all, which is what the stub server bought.
        Assert.False(ios.IsEmpty, $"the {Seeds.SecondRowName} row's card is on the board");
        Assert.False(web.IsEmpty, $"the {Seeds.FirstRowName} row's card is on the board");

        // 2. They are ROWS, in the SERVER's order: iOS was saved first, so it is the top swimlane
        //    even though Web sorts first by name and is first in the cache.
        Assert.True(web.Y > ios.Bottom,
            $"Web ({web.Y}) is a row below iOS, whose card ends at {ios.Bottom}");

        // 3. Each row starts in the same column as the other — the Inbox, since neither card has
        //    a status. Rows that did not share a left edge would not be a grid.
        Assert.Equal(ios.X, web.X);

        // 4. And the cells sit under the one header strip: the first column's name is above the
        //    first row and spans the column its cards are in. This is what fails if the strip and
        //    the cells ever stop sharing a width.
        var inbox = HeaderBounds(app, "Ideas/Inbox");
        _output.WriteLine($"Inbox header {inbox}");
        Assert.False(inbox.IsEmpty, "the column header strip is drawn");
        Assert.True(inbox.Y < ios.Y, "the strip is above the first row");
        // By the card's CENTRE and by its width, not by its left edge: the edge differs from the
        // header's by the cell's padding and by however the machine's scaling rounds it, and an
        // exact-edge assertion would fail on a 2-pixel difference that nobody can see. What
        // actually has to hold is that these cards are under this column, and that the strip and
        // the cells are the same width — the second is what drifts.
        var middle = ios.X + (ios.Width / 2);
        Assert.InRange(middle, inbox.X, inbox.Right);
        Assert.True(Math.Abs(ios.Width - inbox.Width) <= 8,
            $"a cell ({ios.Width}) and a column name ({inbox.Width}) are the same width");

        // And one row header per row, each with its cross.
        Assert.Equal(2, app.Window
            .FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "Remove this row"))
            .Count);

        // Each cross is ON SCREEN, beside its row name. Counting them is not enough: the row
        // head used to stretch the width of every column together — the board scrolls
        // horizontally, so a star column is measured against infinity — which put the cross a
        // thousand pixels off the right of the window. UIA reported both buttons and the count
        // passed; their BoundingRectangle was Empty, which is what offscreen looks like, and
        // the only way to remove a row with a mouse was invisible.
        //
        // The row head spans the board's FIRST column, which is Waiting since core ADEV-128 (task
        // 8c543a42), not the inbox the cards above are in.
        var first = HeaderBounds(app, "Waiting");
        _output.WriteLine($"Waiting header {first}");
        Assert.False(first.IsEmpty, "the first column's name is on the strip");
        Assert.True(first.Right <= inbox.X, "Waiting leads the board, ahead of the inbox");
        foreach (var cross in app.Window
            .FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, "Remove this row"))
            .Cast<AutomationElement>())
        {
            var at = cross.Current.BoundingRectangle;
            _output.WriteLine("cross: " + at);
            Assert.False(at.IsEmpty, "a row cross is offscreen, so nothing can click it");
            Assert.InRange(at.X, first.X, first.Right);
        }
        // A picture of the board, for the half of "does this look right" that is not a rectangle.
        // Left in the system temp directory, named by this run, as the sidebar's one is.
        using var shot = Capture.Window(new IntPtr(app.Window.Current.NativeWindowHandle));
        _output.WriteLine("screenshot: " + shot.SavePng(Path.Combine(
            Path.GetTempPath(), $"astrid-project-board-{Environment.ProcessId}.png")));
    }

    /// <summary>
    /// The widest on-screen rectangle of the elements carrying this title, or empty for none.
    /// </summary>
    /// <remarks>
    /// The widest, because the card and the TextBlock inside it share a name and only the card
    /// moves with its cell — measuring the text would make the alignment assertions pass whatever
    /// the cell did.
    /// </remarks>
    private static System.Windows.Rect CardBounds(AstridApp app, string title) =>
        Bounds(app, title);

    /// <summary>A column name on the strip above the rows.</summary>
    private static System.Windows.Rect HeaderBounds(AstridApp app, string name)
    {
        var found = app.Find(name);
        return found?.Current.BoundingRectangle ?? System.Windows.Rect.Empty;
    }

    /// <summary>Open a list that belongs to the seeded project, which is what offers the toggle.</summary>
    private static void SelectAProjectList(AstridApp app)
    {
        var walker = TreeWalker.ControlViewWalker;
        var element = app.Require(Seeds.FirstRowName);
        while (element is not null && element.Current.ControlType != ControlType.ListItem)
        {
            element = walker.GetParent(element);
        }
        ((SelectionItemPattern)element!.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Thread.Sleep(900);
    }

    /// <summary>
    /// The widest on-screen rectangle of the elements carrying this name, or empty for none.
    /// </summary>
    /// <remarks>
    /// The widest, because a row and the TextBlock inside it share a name and the text is the
    /// narrower of the two — and it is the row that goes away when the board takes over.
    /// </remarks>
    private static System.Windows.Rect Bounds(AstridApp app, string name)
    {
        var matches = app.Window
            .FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.NameProperty, name))
            .Cast<AutomationElement>()
            .Select(element => element.Current.BoundingRectangle)
            .Where(bounds => bounds.Width > 0)
            .ToList();
        return matches.Count == 0
            ? System.Windows.Rect.Empty
            : matches.OrderByDescending(bounds => bounds.Width).First();
    }
}
