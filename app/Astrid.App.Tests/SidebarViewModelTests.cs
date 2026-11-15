using System.Text.Json;
using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// What the sidebar draws, and where it comes from.
/// </summary>
/// <remarks>
/// All of it is the core's answer to one <c>sidebar</c> call (task e22621e2): which lists are
/// places, the order of both sections, and the number beside each entry — the open tasks, not the
/// server's lifetime total for the list (task 8d37457b). The shell used to make three calls and
/// run a filter and two sorts over the result, which is how Android came to offer a GitHub
/// milestone as somewhere to file a task.
/// </remarks>
public class SidebarViewModelTests
{
    private static object Sections(object? myTasks = null, object[]? favorites = null,
        object[]? lists = null) => new
        {
            myTasks,
            favorites = favorites ?? [],
            lists = lists ?? [],
        };

    private static FakeCore Core(object sections) => new FakeCore().AnswerOk("sidebar", sections);

    /// <summary>
    /// Regression, task e22621e2: one call. The acceptance of the task is that <c>LoadAsync</c>
    /// asks once and sorts nothing — the three calls it replaced were three chances for this shell
    /// to disagree with web about what a sidebar holds.
    /// </summary>
    [Fact]
    public async Task The_sidebar_is_one_call_and_the_shell_re_orders_nothing()
    {
        var core = Core(Sections(
            myTasks: new { id = "virtual:my-tasks", name = "My Tasks", isVirtual = true, taskCount = 3 },
            favorites: [new { id = "f1", name = "Zoo", isFavorite = true, favoriteOrder = 0 }],
            // Deliberately not alphabetical: this is the core's order, and the shell keeps it.
            lists: [
                new { id = "l2", name = "Work", isFavorite = false, favoriteOrder = (int?)null },
                new { id = "l1", name = "Home", isFavorite = false, favoriteOrder = (int?)null },
            ]));
        var sidebar = new SidebarViewModel(core);

        await sidebar.LoadAsync();

        Assert.Equal(["sidebar"], core.SentKinds());
        Assert.Equal("My Tasks", Assert.Single(sidebar.MyTasks).Name);
        Assert.Equal("Zoo", Assert.Single(sidebar.Favorites).Name);
        Assert.Equal(["Work", "Home"], sidebar.Lists.Select(list => list.Name));
    }

    /// <summary>
    /// Regression, task 8d37457b: "Astrid Windows To-do is 124 tasks" beside a list holding a
    /// dozen open ones. The badge arrives on the entry, already the open count.
    /// </summary>
    [Fact]
    public async Task The_number_beside_a_list_is_the_one_the_core_put_there()
    {
        var core = Core(Sections(
            myTasks: new { id = "virtual:my-tasks", name = "My Tasks", isVirtual = true, taskCount = 3 },
            lists: [new { id = "l1", name = "Home", taskCount = 12 }]));
        var sidebar = new SidebarViewModel(core);

        await sidebar.LoadAsync();

        Assert.Equal(12, Assert.Single(sidebar.Lists).TaskCount);
        Assert.Equal(3, Assert.Single(sidebar.MyTasks).TaskCount);
    }

    /// <summary>
    /// A core that cannot answer leaves the sidebar as it was and says why. It used to be able to
    /// half-answer — lists without badges — and drawing the server's total instead was the bug.
    /// </summary>
    [Fact]
    public async Task A_core_that_cannot_answer_says_so_and_draws_nothing()
    {
        var core = new FakeCore().AnswerFailure("sidebar", AstridFailureKind.BadRequest, "nope");
        var sidebar = new SidebarViewModel(core);

        await sidebar.LoadAsync();

        Assert.Equal("nope", sidebar.ErrorMessage);
        Assert.Empty(sidebar.Lists);
        Assert.Empty(sidebar.Favorites);
    }

    /// <summary>
    /// My Tasks has no row on the server, so the core says whether there is one at all. A build
    /// whose core does not offer it draws no My Tasks rather than inventing an id for it.
    /// </summary>
    [Fact]
    public async Task No_my_tasks_in_the_answer_draws_no_my_tasks()
    {
        var core = Core(Sections(lists: [new { id = "l1", name = "Home" }]));
        var sidebar = new SidebarViewModel(core);

        await sidebar.LoadAsync();

        Assert.Empty(sidebar.MyTasks);
        Assert.Equal("Home", sidebar.Selected?.Name);
    }

    /// <summary>The selection survives a refresh, which happens several times a minute.</summary>
    [Fact]
    public async Task The_open_list_stays_open_across_a_refresh()
    {
        var core = Core(Sections(lists: [
            new { id = "l1", name = "Home" },
            new { id = "l2", name = "Work" },
        ]));
        var sidebar = new SidebarViewModel(core);
        await sidebar.LoadAsync();
        sidebar.Selected = sidebar.Lists.Single(list => list.Id == "l2");

        core.AnswerOk("sidebar", Sections(lists: [
            new { id = "l1", name = "Home" },
            new { id = "l2", name = "Work" },
        ]));
        await sidebar.LoadAsync();

        Assert.Equal("l2", sidebar.Selected?.Id);
    }

    /// <summary>
    /// The sidebar command carries nothing: whose tasks to count is the signed-in reader's, which
    /// the core knows and the shell must not second-guess.
    /// </summary>
    [Fact]
    public async Task The_command_asks_for_nothing_but_the_sidebar()
    {
        var core = Core(Sections(lists: [new { id = "l1", name = "Home" }]));
        var sidebar = new SidebarViewModel(core);

        await sidebar.LoadAsync();

        using var document = JsonDocument.Parse(Assert.Single(core.Sent));
        Assert.Equal(["kind"], document.RootElement.EnumerateObject().Select(field => field.Name));
    }
}
