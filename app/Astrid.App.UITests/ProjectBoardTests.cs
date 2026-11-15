using System.Linq;
using System.Threading;
using System.Windows.Automation;
using Xunit;

namespace Astrid.App.UITests;

/// <summary>
/// The project board takes the window over from the list, and says so when it cannot be read
/// (task f8b844dd).
/// </summary>
/// <remarks>
/// <para>
/// <b>What this suite can and cannot reach.</b> The swimlanes themselves need
/// <c>GET /api/v1/projects/{id}/board-rows</c>, which is a network read: the core falls back to
/// the last answer only for a RETRYABLE failure, and treats a refusal as an error, deliberately —
/// "the cache is for a missing network, not for a server that said no". This harness has no
/// server and its session is a written cookie, so the read comes back
/// <em>the session is not valid</em> and there are no rows to measure. Seeding the cached rows
/// does not help: the read still goes out first, and still fails the same way.
/// </para>
/// <para>
/// So the layout assertions live in <see cref="Astrid.App.Tests.ProjectBoardViewModelTests"/>
/// (nineteen of them, over a scripted core), and what is left for the built app is the half that
/// no view-model test can see: that the view actually takes the window, and that a board which
/// could not be read shows the refusal rather than an empty grid.
/// </para>
/// <para>
/// That half earned its place immediately. The first run of this file failed with a card 974
/// pixels wide — the LIST's row, still drawn on top of the board, because
/// <c>TaskRowsView</c>'s visibility was bound to <c>IsBoardView</c> and knew nothing about a
/// third view. Every view-model and XAML test passed with the two views overlapping.
/// </para>
/// <para>
/// A swimlane-layout test over a real board is <b>Windows board task 68b00422</b>: it needs a stub
/// server in this harness, which every network-backed surface would then be testable against.
/// </para>
/// <para>
/// <b>UI Automation names and bounds, not pixels</b> — the trade
/// <see cref="BoardCalloutTests"/> documents. No screenshot, no colour arithmetic, no window
/// focus, so this runs in a locked session.
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
