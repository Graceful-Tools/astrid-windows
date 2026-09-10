using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// What the list view does, and — more usefully — what it declines to decide.
/// </summary>
public sealed class TaskListViewModelTests
{
    private static object Window(int total, params string[] titles) => new
    {
        total,
        offset = 0,
        rows = titles.Select((title, index) => new
        {
            id = $"t{index}",
            title,
            completed = false,
            priority = 0,
            due = new { key = "none" },
            isOverdue = false,
            leading = new { kind = "unassigned" },
            action = "complete",
            depth = 0,
            isPending = false,
            isPrivate = false,
            isRepeating = false,
            hasDescription = false,
            commentCount = 0,
            attachmentCount = 0,
            subtaskCount = 0,
            listChips = Array.Empty<object>(),
            statusRole = (string?)null,
        }).ToArray(),
    };

    [Fact]
    public async Task Opening_a_list_shows_the_rows_the_core_returned()
    {
        var core = new FakeCore().AnswerOk("rowsForList", Window(2, "Buy milk", "Water plants"));
        var view = new TaskListViewModel(core);

        await view.OpenAsync("l1", "Home");

        Assert.Equal(2, view.Rows.Count);
        Assert.Equal("Buy milk", view.Rows[0].Title);
        Assert.Equal("Home", view.ListName);
        Assert.False(view.IsEmpty);
    }

    /// <summary>
    /// The window, not the account. A list of ten thousand has to cross the boundary as the rows on
    /// screen.
    /// </summary>
    [Fact]
    public async Task It_asks_for_a_window_rather_than_everything()
    {
        var core = new FakeCore().AnswerOk("rowsForList", Window(10_000, "Buy milk"));
        var view = new TaskListViewModel(core);

        await view.OpenAsync("l1", "Home");

        Assert.Contains($"\"limit\":{TaskListViewModel.PageSize}", core.Sent[0]);
        Assert.Contains("\"offset\":0", core.Sent[0]);
        Assert.Equal(10_000, view.Total);
    }

    [Fact]
    public async Task Scrolling_on_asks_for_the_next_window()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(3, "one"))
            .AnswerOk("rowsForList", Window(3, "two"));
        var view = new TaskListViewModel(core);

        await view.OpenAsync("l1", "Home");
        await view.LoadMoreAsync();

        Assert.Equal(2, core.Sent.Count);
        Assert.Contains("\"offset\":1", core.Sent[1]);
    }

    /// <summary>Nothing left to fetch means no request at all.</summary>
    [Fact]
    public async Task It_stops_asking_once_every_row_is_loaded()
    {
        var core = new FakeCore().AnswerOk("rowsForList", Window(1, "only one"));
        var view = new TaskListViewModel(core);

        await view.OpenAsync("l1", "Home");
        await view.LoadMoreAsync();

        Assert.Single(core.Sent);
    }

    /// <summary>
    /// Completing goes through <c>completeTask</c> and never through an update. A repeating task
    /// rolls forward instead of finishing, and only that command does it.
    /// </summary>
    [Fact]
    public async Task Completing_a_row_uses_the_command_that_rolls_repeating_tasks_over()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(1, "Water plants"))
            .AnswerOk("completeTask", new { id = "t0" })
            .AnswerOk("rowsForList", Window(1, "Water plants"));
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Home");

        await view.SetCompletedAsync("t0", true);

        Assert.Contains("completeTask", core.SentKinds());
        Assert.DoesNotContain("updateTask", core.SentKinds());
    }

    /// <summary>
    /// Offline is not an error. The write is in the Outbox and will go; showing a red message is
    /// how a working offline app comes to look broken.
    /// </summary>
    [Fact]
    public async Task A_write_that_has_not_reached_the_server_is_not_reported_as_a_failure()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(0))
            .AnswerFailure("createTask", AstridFailureKind.Offline, "no network");
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Home");

        await view.CreateTaskAsync("Buy milk");

        Assert.Null(view.ErrorMessage);
        Assert.False(view.NeedsSignIn);
    }

    /// <summary>
    /// The add box says where its title came from, so the core can read <c>#list</c> tags out of
    /// it when the account has smart parsing on — the shell sends the title as typed and decides
    /// nothing about it (task 6ac2639a).
    /// </summary>
    [Fact]
    public async Task The_add_box_marks_its_title_as_quick_add_task_6ac2639a()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(0))
            .AnswerOk("createTask", new { id = "t1", title = "Pushups" })
            .AnswerOk("rowsForList", Window(1, "Pushups"));
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Home");

        Assert.True(await view.CreateTaskAsync("Pushups #health"));

        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"createTask\"")
            && json.Contains("\"title\":\"Pushups #health\"")
            && json.Contains("\"quickAdd\":true"));
    }

    /// <summary>
    /// Adding a task says so, with the title the core made of it — the one that ends up on the
    /// row — and the notice can be taken down again (task 79d4604c). A failed add says nothing
    /// of the kind.
    /// </summary>
    [Fact]
    public async Task Adding_a_task_says_so_with_the_title_the_core_made_task_79d4604c()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(0))
            .AnswerOk("createTask", new { id = "t1", title = "Pushups" })
            .AnswerOk("rowsForList", Window(1, "Pushups"))
            .AnswerFailure("createTask", AstridFailureKind.Refused, "not yours");
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Home");
        Assert.False(view.HasCreatedNotice);

        Assert.True(await view.CreateTaskAsync("Pushups #health"));
        Assert.Equal("Pushups", view.LastCreatedTitle);
        Assert.True(view.HasCreatedNotice);

        view.ClearCreatedNotice();
        Assert.False(view.HasCreatedNotice);

        Assert.False(await view.CreateTaskAsync("Nope"));
        Assert.False(view.HasCreatedNotice);
    }

    [Fact]
    public async Task A_refusal_is_reported()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(0))
            .AnswerFailure("createTask", AstridFailureKind.Refused, "not yours");
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Home");

        await view.CreateTaskAsync("Buy milk");

        Assert.Equal("not yours", view.ErrorMessage);
    }

    [Fact]
    public async Task An_expired_session_asks_for_a_sign_in_rather_than_showing_an_error()
    {
        var core = new FakeCore().AnswerFailure("rowsForList", AstridFailureKind.Unauthorized);
        var view = new TaskListViewModel(core);

        await view.OpenAsync("l1", "Home");

        Assert.True(view.NeedsSignIn);
    }

    /// <summary>A stray Enter is not a task nobody wanted.</summary>
    [Fact]
    public async Task An_empty_title_creates_nothing()
    {
        var core = new FakeCore();
        var view = new TaskListViewModel(core);

        Assert.False(await view.CreateTaskAsync("   "));
        Assert.Empty(core.Sent);
    }

    [Fact]
    public async Task Deleting_takes_the_row_off_screen_without_another_round_trip()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(2, "Buy milk", "Water plants"))
            .AnswerOk("deleteTask");
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Home");

        await view.DeleteTaskAsync("t0");

        Assert.Single(view.Rows);
        Assert.Equal(1, view.Total);
        Assert.Equal(2, core.Sent.Count);
    }

    /// <summary>
    /// Search replaces what the list shows. The results are rows like any other, so they complete
    /// and open exactly the same way.
    /// </summary>
    [Fact]
    public async Task Searching_replaces_the_list_with_its_results()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(2, "Buy milk", "Book flights"))
            .AnswerOk("searchTasks", Window(1, "Book flights"));
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Home");

        await view.SearchAsync("book");

        Assert.True(view.IsShowingSearchResults);
        Assert.Single(view.Rows);
        Assert.Equal("Book flights", view.Rows[0].Title);
    }

    /// <summary>Emptying the box goes back to the list rather than leaving the results up.</summary>
    [Fact]
    public async Task Clearing_the_search_returns_to_the_list()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(2, "Buy milk", "Book flights"))
            .AnswerOk("searchTasks", Window(1, "Book flights"))
            .AnswerOk("rowsForList", Window(2, "Buy milk", "Book flights"));
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Home");
        await view.SearchAsync("book");

        await view.SearchAsync("");

        Assert.False(view.IsShowingSearchResults);
        Assert.Equal(2, view.Rows.Count);
    }

    /// <summary>
    /// A refresh while results are on screen re-runs the search. Reloading the list underneath
    /// would replace what somebody is reading every time a colleague touched anything.
    /// </summary>
    [Fact]
    public async Task A_refresh_during_a_search_keeps_the_results()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(2, "Buy milk", "Book flights"))
            .AnswerOk("searchTasks", Window(1, "Book flights"))
            .AnswerOk("searchTasks", Window(1, "Book flights"));
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Home");
        await view.SearchAsync("book");

        await view.RefreshAsync();

        Assert.True(view.IsShowingSearchResults);
        Assert.Single(view.Rows);
        Assert.Equal(2, core.SentKinds().Count(kind => kind == "searchTasks"));
    }

    /// <summary>Choosing a list is a way out of a search, and the box has to agree.</summary>
    [Fact]
    public async Task Opening_a_list_ends_the_search()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(1, "Buy milk"))
            .AnswerOk("searchTasks", Window(1, "Book flights"))
            .AnswerOk("rowsForList", Window(1, "Buy milk"));
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Home");
        await view.SearchAsync("book");

        await view.OpenAsync("l2", "Work");

        Assert.False(view.IsShowingSearchResults);
        Assert.Equal(string.Empty, view.SearchQuery);
    }

    private static object FilterOptions(bool filtered, string dueValue) => new
    {
        listId = "l1",
        isFiltered = filtered,
        groups = new[]
        {
            new
            {
                field = "filterDueDate",
                titleKey = "filter.due",
                picks = new[]
                {
                    new { field = "filterDueDate", value = "all", titleKey = "filter.any", isSelected = dueValue == "all" },
                    new { field = "filterDueDate", value = "today", titleKey = "filter.due.today", isSelected = dueValue == "today" },
                },
            },
        },
    };

    /// <summary>
    /// The choices come from the core, marked. A shell that decided which one was on would be the
    /// fourth place that knows what "today" means.
    /// </summary>
    [Fact]
    public async Task The_filter_sheet_shows_what_the_list_is_set_to()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(1, "Buy milk"))
            .AnswerOk("filterOptions", FilterOptions(false, "all"));
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Home");

        await view.LoadFiltersAsync();

        Assert.Single(view.FilterGroups);
        Assert.True(view.FilterGroups[0].Picks[0].IsSelected);
        Assert.False(view.IsFiltered);
    }

    /// <summary>
    /// Choosing one writes the field the core named, then redraws — the list is what changed, and
    /// the sheet has to agree with it afterwards.
    /// </summary>
    [Fact]
    public async Task Choosing_a_filter_writes_it_and_redraws_the_list()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(2, "Buy milk", "Book flights"))
            .AnswerOk("filterOptions", FilterOptions(false, "all"))
            .AnswerOk("setFilter")
            .AnswerOk("rowsForList", Window(1, "Book flights"))
            .AnswerOk("filterOptions", FilterOptions(true, "today"));
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Home");
        await view.LoadFiltersAsync();

        Assert.True(await view.SetFilterAsync("filterDueDate", "today"));

        // Through setFilter rather than updateList: where a filter is written depends on what is
        // being filtered, and that decision belongs in the core.
        var sent = core.Sent.First(item => item.Contains("setFilter"));
        Assert.Contains("\"field\":\"filterDueDate\"", sent);
        Assert.Contains("\"value\":\"today\"", sent);
        Assert.Single(view.Rows);
        Assert.True(view.IsFiltered);
    }

    /// <summary>
    /// A refresh keeps as many rows as were on screen, so it does not scroll the list back to the
    /// top under somebody's cursor.
    /// </summary>
    [Fact]
    public async Task A_refresh_asks_for_what_is_already_shown()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(500, "one"))
            .AnswerOk("rowsForList", Window(500, "one"))
            .AnswerOk("rowsForList", Window(500, "one"));
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Home");
        await view.LoadMoreAsync();

        await view.RefreshAsync();

        Assert.Contains("\"offset\":0", core.Sent[^1]);
        Assert.Contains($"\"limit\":{TaskListViewModel.PageSize}", core.Sent[^1]);
    }

    /// <summary>
    /// Choosing a second list while the first is still loading shows the second.
    /// </summary>
    /// <remarks>
    /// Reported as "it isn't filtering to lists". The load guarded itself with a single IsLoading
    /// flag, so a second choice made while the first was in flight returned immediately and fetched
    /// nothing — and then the first list's answer arrived and filled the rows that had just been
    /// cleared for the second. The header said one list and the rows were another's, which is
    /// indistinguishable from filtering being broken.
    /// </remarks>
    [Fact]
    public async Task Choosing_a_second_list_while_the_first_is_loading_shows_the_second()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(1, "a task from the first list"))
            .AnswerOk("rowsForList", Window(1, "a task from the second list"));
        var view = new TaskListViewModel(core);

        core.Hold("rowsForList");
        var first = view.OpenAsync("l1", "First");
        var second = view.OpenAsync("l2", "Second");
        core.Release("rowsForList");
        await first;
        await second;

        Assert.Equal("l2", view.ListId);
        Assert.Equal("a task from the second list", Assert.Single(view.Rows).Title);
    }

    /// <summary>An answer for a list nobody is looking at any more is not drawn.</summary>
    [Fact]
    public async Task An_answer_for_the_previous_list_is_dropped_rather_than_appended()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Window(2, "first list one", "first list two"))
            .AnswerOk("rowsForList", Window(1, "second list only"));
        var view = new TaskListViewModel(core);

        core.Hold("rowsForList");
        var first = view.OpenAsync("l1", "First");
        var second = view.OpenAsync("l2", "Second");
        core.Release("rowsForList");
        await first;
        await second;

        Assert.Single(view.Rows);
        Assert.DoesNotContain(view.Rows, row => row.Title.StartsWith("first list"));
    }
}
