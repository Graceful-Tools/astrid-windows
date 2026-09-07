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
}
