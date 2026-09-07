using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

public sealed class TaskDetailViewModelTests
{
    private static object Detail(string title = "Plan the trip", int priority = 3,
        bool completed = false, string[]? subtasks = null, string[]? comments = null) => new
        {
            task = new { id = "t1", title, description = "two weeks", priority, completed },
            fieldOrder = new[] { "assignee", "when", "priority", "lists" },
            priorityGlyph = "!!!",
            due = new { key = "today" },
            isOverdue = false,
            listChips = new[] { new { id = "l1", name = "Home", color = "#3b82f6" } },
            assignee = (object?)null,
            comments = (comments ?? []).Select((content, index) => new
            {
                id = $"c{index}",
                content,
                createdAt = "2026-09-07T12:00:00Z",
            }).ToArray(),
            subtasks = (subtasks ?? []).Select((sub, index) => new
            {
                id = $"s{index}",
                title = sub,
                completed = false,
                isPending = false,
            }).ToArray(),
        };

    [Fact]
    public async Task Opening_a_task_fills_the_pane_from_one_command()
    {
        var core = new FakeCore().AnswerOk("taskDetail",
            Detail(subtasks: ["Book flights"], comments: ["asked Sam"]));
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.True(view.IsOpen);
        Assert.Equal("Plan the trip", view.Title);
        Assert.Equal("two weeks", view.Description);
        Assert.Equal(3, view.Priority);
        Assert.Equal("!!!", view.PriorityGlyph);
        Assert.Equal("today", view.Due.Key);
        Assert.Equal("Home", view.ListChips[0].Name);
        Assert.Equal("Book flights", view.Subtasks[0].Title);
        Assert.Equal("asked Sam", view.Comments[0].Content);
        Assert.Single(core.Sent);
    }

    /// <summary>
    /// The order is the core's. Both Apple platforms put Priority first and Who second before it
    /// was written down once, which is what happens when two views each decide for themselves.
    /// </summary>
    [Fact]
    public async Task The_field_order_comes_from_the_core()
    {
        var core = new FakeCore().AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.Equal(["assignee", "when", "priority", "lists"], view.FieldOrder);
    }

    /// <summary>Completing goes through the command that rolls a repeating task forward.</summary>
    [Fact]
    public async Task Completing_from_the_detail_uses_the_complete_command()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("completeTask")
            .AnswerOk("taskDetail", Detail(completed: true));
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.True(await view.SetCompletedAsync(true));
        Assert.Contains("completeTask", core.SentKinds());
        Assert.DoesNotContain("updateTask", core.SentKinds());
        Assert.True(view.Completed);
    }

    /// <summary>An empty title leaves a row nobody can identify.</summary>
    [Fact]
    public async Task An_empty_title_is_refused_rather_than_saved()
    {
        var core = new FakeCore().AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.False(await view.SaveTitleAsync("   "));
        Assert.DoesNotContain("updateTask", core.SentKinds());
    }

    /// <summary>
    /// A task that has gone — deleted here or by somebody else — closes the pane rather than
    /// leaving a screen showing something that is not there.
    /// </summary>
    [Fact]
    public async Task A_task_that_has_gone_closes_the_pane()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerFailure("taskDetail", AstridFailureKind.NotFound, "no task with id t1");
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.ReloadAsync();

        Assert.False(view.IsOpen);
        Assert.Null(view.TaskId);
    }

    /// <summary>Offline is not an error to show: the edit is in the Outbox.</summary>
    [Fact]
    public async Task An_edit_made_offline_is_not_reported_as_a_failure()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerFailure("updateTask", AstridFailureKind.Offline, "no network");
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.SaveTitleAsync("Plan the other trip");

        Assert.Null(view.ErrorMessage);
    }

    [Fact]
    public async Task Clearing_the_due_date_sends_a_null()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.SetDueDateAsync(null, isAllDay: true);

        var sent = core.Sent.Last(json => json.Contains("updateTask", StringComparison.Ordinal));
        Assert.Contains("\"dueDateTime\":null", sent, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reloading swaps rows in place. Clearing and re-adding would collapse everything and lose
    /// the caret in a comment being typed, after every single edit.
    /// </summary>
    [Fact]
    public async Task A_reload_keeps_the_rows_that_did_not_change()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(comments: ["one", "two"]))
            .AnswerOk("taskDetail", Detail(comments: ["one", "two changed"]));
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");
        var first = view.Comments[0];

        await view.ReloadAsync();

        Assert.Same(first, view.Comments[0]);
        Assert.Equal("two changed", view.Comments[1].Content);
    }

    /// <summary>
    /// The detail pane only reloads for the task it is showing. Reloading for every task somebody
    /// else touches would make the open one flicker while a colleague works in the same list.
    /// </summary>
    [Fact]
    public async Task A_notification_for_another_task_leaves_the_open_one_alone()
    {
        var core = new FakeCore()
            .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false })
            .AnswerOk("lists", new object[] { new { id = "l1", name = "Home" } })
            .AnswerOk("rowsForList", new { total = 0, offset = 0, rows = Array.Empty<object>() })
            .AnswerOk("outboxStats", new { hasUnsentWork = false })
            .AnswerOk("sync", new { fetched = false })
            .AnswerOk("taskDetail", Detail());
        using var shell = new ShellViewModel(core, work => work().GetAwaiter().GetResult());
        await shell.StartAsync();
        await shell.OpenTaskAsync("t1");

        core.AnswerOk("rowsForList", new { total = 0, offset = 0, rows = Array.Empty<object>() })
            .AnswerOk("outboxStats", new { hasUnsentWork = false });
        var before = core.SentKinds().Count(kind => kind == "taskDetail");

        core.Notify("task", "somebody-elses-task");

        Assert.Equal(before, core.SentKinds().Count(kind => kind == "taskDetail"));
    }
}
