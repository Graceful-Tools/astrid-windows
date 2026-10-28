using System.Text.Json;
using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The number beside each list in the sidebar.
/// </summary>
/// <remarks>
/// The count is the core's — <c>listBadges</c>, which iOS's <c>ListTaskCount</c> draws from too.
/// The sidebar used to draw <c>lists</c>' own <c>taskCount</c>, which is the server's total for the
/// list and counts the completed tasks with the open ones (task 8d37457b).
/// </remarks>
public class SidebarViewModelTests
{
    private static FakeCore Core(object lists) =>
        new FakeCore().AnswerOk("lists", lists);

    private static object OneList(int serverTotal) => new[]
    {
        new { id = "l1", name = "Home", taskCount = serverTotal },
    };

    /// <summary>
    /// Regression, task 8d37457b: "Astrid Windows To-do is 124 tasks" beside a list holding a
    /// dozen open ones. The server's total is every task the list has ever held.
    /// </summary>
    [Fact]
    public async Task The_number_beside_a_list_is_its_open_tasks_not_the_servers_total()
    {
        var core = Core(OneList(124))
            .AnswerOk("listBadges", new Dictionary<string, int> { ["l1"] = 12 });
        var sidebar = new SidebarViewModel(core);

        await sidebar.LoadAsync();

        Assert.Equal(12, Assert.Single(sidebar.Lists).TaskCount);
    }

    /// <summary>
    /// My Tasks' number is what is assigned to the reader, which only the core can work out — so
    /// it has to be told which list is My Tasks.
    /// </summary>
    [Fact]
    public async Task My_tasks_is_named_so_the_core_counts_what_is_assigned_to_the_reader()
    {
        var core = Core(OneList(124))
            .AnswerOk("myTasksList", new { id = "virtual:my-tasks", name = "My Tasks", isVirtual = true })
            .AnswerOk("listBadges", new Dictionary<string, int>
            {
                ["l1"] = 12,
                ["virtual:my-tasks"] = 3,
            });
        var sidebar = new SidebarViewModel(core);

        await sidebar.LoadAsync();

        Assert.Equal(3, Assert.Single(sidebar.MyTasks).TaskCount);
        Assert.Equal("virtual:my-tasks", SentListBadges(core).GetProperty("myTasksId").GetString());
    }

    /// <summary>
    /// The lists go to the core as they arrived, not rebuilt from <c>ListSummary</c>. What a list
    /// counts depends on fields the sidebar never draws — its privacy, and its saved filters — and
    /// a shell that reassembled the payload would be deciding what counts.
    /// </summary>
    [Fact]
    public async Task The_lists_reach_the_core_with_the_fields_the_sidebar_never_draws()
    {
        var core = Core(new[]
            {
                new
                {
                    id = "l1",
                    name = "Shared",
                    privacy = "PUBLIC",
                    taskCount = 124,
                    filterCompleted = "hide",
                },
            })
            .AnswerOk("listBadges", new Dictionary<string, int> { ["l1"] = 12 });
        var sidebar = new SidebarViewModel(core);

        await sidebar.LoadAsync();

        var sent = SentListBadges(core).GetProperty("lists")[0];
        Assert.Equal("PUBLIC", sent.GetProperty("privacy").GetString());
        Assert.Equal("hide", sent.GetProperty("filterCompleted").GetString());
    }

    /// <summary>
    /// A core that cannot answer for the badges leaves the badge off rather than falling back to
    /// the server's total: that total is the wrong number, and drawing it is the bug.
    /// </summary>
    [Fact]
    public async Task A_core_that_cannot_count_draws_no_number_rather_than_the_wrong_one()
    {
        var core = Core(OneList(124))
            .AnswerFailure("listBadges", AstridFailureKind.BadRequest);
        var sidebar = new SidebarViewModel(core);

        await sidebar.LoadAsync();

        Assert.Null(Assert.Single(sidebar.Lists).TaskCount);
    }

    /// <summary>The <c>listBadges</c> command as it was sent.</summary>
    private static JsonElement SentListBadges(FakeCore core)
    {
        var json = core.Sent.Single(sent =>
        {
            using var document = JsonDocument.Parse(sent);
            return document.RootElement.GetProperty("kind").GetString() == "listBadges";
        });
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
