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

    private static object NoDuePicks() =>
        new { isAllDay = true, dates = Array.Empty<object>(), times = Array.Empty<object>() };

    private static FakeCore OpenedTask() => new FakeCore()
        .AnswerOk("taskDetail", Detail())
        .AnswerOk("dueDateOptions", NoDuePicks())
        .AnswerOk("refreshComments");

    /// <summary>
    /// A timer that is running keeps the section on screen; one that has recorded something keeps
    /// a caption, so hiding the section never hides the data.
    /// </summary>
    [Fact]
    public async Task The_timer_says_whether_it_is_running_and_what_it_has_recorded()
    {
        var core = OpenedTask()
            .AnswerOk("startTimer", new { isRunning = true, startedAt = "2026-09-07T09:00:00Z", loggedMinutes = 0 })
            .AnswerOk("stopTimer", new { isRunning = false, loggedMinutes = 65, lastValue = "1h 5m" });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.SetTimingAsync(true);
        Assert.True(view.IsTiming);
        Assert.False(view.HasLoggedTime);

        await view.SetTimingAsync(false);
        Assert.False(view.IsTiming);
        Assert.True(view.HasLoggedTime);
        Assert.Equal(65, view.Timer.LoggedMinutes);
    }

    /// <summary>
    /// The files on a task come from the core, which gathers the task's own and its comments' —
    /// there is no attach-to-task endpoint anywhere.
    /// </summary>
    [Fact]
    public async Task The_files_on_a_task_are_listed_with_their_sizes()
    {
        var core = OpenedTask().AnswerOk("attachments", new
        {
            files = new[]
            {
                new
                {
                    id = "f1",
                    name = "itinerary.pdf",
                    size = 2048,
                    mimeType = "application/pdf",
                    isCached = false,
                    path = "C:/cache/f1.pdf",
                },
            },
        });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.LoadAttachmentsAsync();

        Assert.Single(view.Attachments);
        Assert.Equal("itinerary.pdf", view.Attachments[0].Name);
        Assert.Equal("2 KB", view.Attachments[0].SizeLabel);
        Assert.False(view.Attachments[0].IsCached);
    }

    /// <summary>
    /// A download answers with a path, not with bytes: what somebody does with an attachment is
    /// open it in the program that reads that kind of file.
    /// </summary>
    [Fact]
    public async Task Downloading_answers_with_where_the_file_landed()
    {
        var core = OpenedTask()
            .AnswerOk("downloadAttachment", new { path = "C:/cache/f1.pdf" })
            .AnswerOk("attachments", new { files = Array.Empty<object>() });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        var path = await view.DownloadAsync("f1");

        Assert.Equal("C:/cache/f1.pdf", path);
    }

    /// <summary>
    /// Attaching needs a connection, unlike every other write here — the journal holds JSON, and a
    /// queued photo would be megabytes nobody else could see. So it reports rather than pretends.
    /// </summary>
    [Fact]
    public async Task An_attachment_that_could_not_be_uploaded_is_reported()
    {
        var core = OpenedTask()
            .AnswerFailure("attachFile", AstridFailureKind.Offline, "no network");
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.False(await view.AttachAsync("C:/photos/map.png"));
    }

    /// <summary>
    /// The choices carry the instant each one means, so the shell never subtracts an hour from a
    /// date — the arithmetic that goes wrong across a daylight-saving boundary.
    /// </summary>
    [Fact]
    public async Task The_reminder_picker_writes_the_instant_the_core_worked_out()
    {
        var core = OpenedTask()
            .AnswerOk("reminderOptions", new
            {
                reminderTime = (string?)null,
                picks = new[]
                {
                    new { titleKey = "reminder.none", reminderTime = (string?)null, isSelected = true },
                    new
                    {
                        titleKey = "reminder.hour_before",
                        reminderTime = (string?)"2026-09-20T08:00:00+00:00",
                        isSelected = false,
                    },
                },
            })
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");
        await view.LoadReminderPicksAsync();

        Assert.Equal(2, view.ReminderPicks.Count);
        Assert.False(view.HasReminder);

        await view.SetReminderAsync(view.ReminderPicks[1].ReminderTime);

        var update = core.Sent.First(sent => sent.Contains("updateTask"));
        // The offset's "+" comes back escaped by the JSON writer, so this looks for the instant
        // rather than the exact spelling of the separator.
        Assert.Contains("reminderTime", update);
        Assert.Contains("2026-09-20T08:00:00", update);
    }

    /// <summary>Clearing sends null, because absent means "leave it alone".</summary>
    [Fact]
    public async Task Choosing_no_reminder_clears_it()
    {
        var core = OpenedTask().AnswerOk("updateTask").AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.SetReminderAsync(null);

        Assert.Contains("\"reminderTime\":null", core.Sent.First(sent => sent.Contains("updateTask")));
    }

    /// <summary>
    /// The presets come from the core, marked. A shell that decided which row was selected would
    /// be the fourth place to decide it.
    /// </summary>
    [Fact]
    public async Task The_repeat_picker_is_loaded_when_it_is_opened()
    {
        var core = OpenedTask().AnswerOk("repeatOptions", new
        {
            repeating = "weekly",
            repeatFrom = "DUE_DATE",
            presets = new[]
            {
                new { value = "never", titleKey = "repeat.never", isSelected = false },
                new { value = "weekly", titleKey = "repeat.weekly", isSelected = true },
            },
            summary = new[] { new { key = "repeat.weekly" }, new { key = "repeat.from_due_date" } },
        });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.LoadRepeatAsync();

        Assert.Equal(2, view.RepeatPresets.Count);
        Assert.True(view.RepeatPresets[1].IsSelected);
        Assert.True(view.RepeatsFromDueDate);
        Assert.True(view.IsRepeating);
    }

    /// <summary>
    /// A preset clears any custom pattern with it: a daily task carrying a leftover custom rule
    /// repeats one way and reads another.
    /// </summary>
    [Fact]
    public async Task Choosing_a_preset_clears_the_custom_pattern()
    {
        var core = OpenedTask().AnswerOk("updateTask").AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.True(await view.SetRepeatAsync("daily"));

        var update = core.Sent.First(sent => sent.Contains("updateTask"));
        Assert.Contains("\"repeating\":\"daily\"", update);
        Assert.Contains("\"repeatingData\":null", update);
    }

    /// <summary>
    /// "Custom" is not a repeat until a pattern has been built. Writing it with nothing behind it
    /// leaves a task repeating on a rule nobody can read.
    /// </summary>
    [Fact]
    public async Task Choosing_custom_writes_nothing_on_its_own()
    {
        var core = OpenedTask();
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.False(await view.SetRepeatAsync("custom"));

        Assert.DoesNotContain("updateTask", core.SentKinds());
    }

    [Fact]
    public async Task A_custom_pattern_is_stored_with_the_preset_that_names_it()
    {
        var core = OpenedTask().AnswerOk("updateTask").AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.SetCustomRepeatAsync("weeks", 2, ["monday", "wednesday"]);

        var update = core.Sent.First(sent => sent.Contains("updateTask"));
        Assert.Contains("\"repeating\":\"custom\"", update);
        Assert.Contains("\"unit\":\"weeks\"", update);
        Assert.Contains("\"interval\":2", update);
        Assert.Contains("monday", update);
    }

    /// <summary>
    /// The fields a pattern does not need are left out. The column is free-form JSON, and a
    /// monthly pattern carrying weekdays from a previous edit means something different to
    /// whichever client reads it next.
    /// </summary>
    [Fact]
    public async Task A_pattern_carries_only_the_fields_its_unit_needs()
    {
        var core = OpenedTask().AnswerOk("updateTask").AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.SetCustomRepeatAsync("months", 1, ["monday"], "never", 10, "2027-01-01T00:00:00Z");

        var update = core.Sent.First(sent => sent.Contains("updateTask"));
        Assert.DoesNotContain("weekdays", update);
        Assert.DoesNotContain("endAfterOccurrences", update);
        Assert.DoesNotContain("endUntilDate", update);
    }

    /// <summary>An interval of zero is not a repeat, it is a loop.</summary>
    [Fact]
    public async Task An_interval_below_one_is_pulled_back_up()
    {
        var core = OpenedTask().AnswerOk("updateTask").AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.SetCustomRepeatAsync("days", 0);

        Assert.Contains("\"interval\":1", core.Sent.First(sent => sent.Contains("updateTask")));
    }

    /// <summary>
    /// The picker is asked for as it opens rather than carried with the task: the answer depends on
    /// the account's agents and on every list the task is on.
    /// </summary>
    [Fact]
    public async Task The_assignee_picker_is_loaded_when_it_is_opened()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new { isAllDay = true, dates = Array.Empty<object>(), times = Array.Empty<object>() })
            .AnswerOk("refreshComments")
            .AnswerOk("assigneeOptions", new
            {
                assigneeId = (string?)null,
                options = new[]
                {
                    new { userId = (string?)null, name = (string?)null, initials = "", isCurrentUser = false, isAgent = false },
                    new { userId = (string?)"me", name = (string?)"Jon", initials = "JO", isCurrentUser = true, isAgent = false },
                },
            });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.LoadAssigneesAsync();

        Assert.Equal(2, view.Assignees.Count);
        // Unassigned first, and it carries no word — the shell names it from its resources.
        Assert.Null(view.Assignees[0].UserId);
        Assert.Equal("assignee.unassigned", view.Assignees[0].TitleKey);
        Assert.Equal("Jon", view.Assignees[1].TitleKey);
    }

    [Fact]
    public async Task Assigning_writes_the_chosen_person()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new { isAllDay = true, dates = Array.Empty<object>(), times = Array.Empty<object>() })
            .AnswerOk("refreshComments")
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        Assert.True(await view.AssignAsync("u1"));

        var update = core.Sent.First(sent => sent.Contains("updateTask"));
        Assert.Contains("\"assigneeId\":\"u1\"", update);
    }

    /// <summary>
    /// Clearing has to send null rather than leave the field out: absent means "leave alone", and a
    /// picker that cannot express "no one" cannot take a task off somebody.
    /// </summary>
    [Fact]
    public async Task Choosing_no_one_clears_the_assignee_rather_than_saying_nothing()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new { isAllDay = true, dates = Array.Empty<object>(), times = Array.Empty<object>() })
            .AnswerOk("refreshComments")
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail());
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.AssignAsync(null);

        var update = core.Sent.First(sent => sent.Contains("updateTask"));
        Assert.Contains("\"assigneeId\":null", update);
    }

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
        // The screen and the quick date choices, both cache reads, and then the comment thread
        // from the server — sync does not pull threads, so one is fetched when it is opened.
        Assert.Equal(["taskDetail", "dueDateOptions", "refreshComments"], core.SentKinds());
    }

    /// <summary>
    /// A thread that cannot be fetched leaves the cached one on screen. That is the right thing to
    /// be looking at offline, and an error banner over a working screen is noise.
    /// </summary>
    [Fact]
    public async Task A_comment_refresh_that_fails_leaves_the_cached_thread_alone()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(comments: ["asked Sam"]))
            .AnswerFailure("refreshComments", AstridFailureKind.Offline, "no network");
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.Equal("asked Sam", view.Comments[0].Content);
        Assert.Null(view.ErrorMessage);
    }

    /// <summary>And one that succeeds replaces it from the answer, without re-reading the screen.</summary>
    [Fact]
    public async Task A_comment_refresh_updates_the_thread_from_its_own_answer()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail(comments: ["stale"]))
            .AnswerOk("refreshComments", new object[]
            {
                new { id = "c1", content = "fresh", createdAt = "2026-09-07T12:00:00Z" },
            });
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.Equal("fresh", view.Comments[0].Content);
        Assert.Equal(1, core.SentKinds().Count(kind => kind == "taskDetail"));
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
    /// The quick choices arrive with the instant each one means, so the shell never computes a
    /// date. Every part of that arithmetic is decided once, in the core, for all three clients.
    /// </summary>
    [Fact]
    public async Task The_due_picks_carry_the_instants_they_mean()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new
            {
                isAllDay = true,
                dueDateTime = "2026-09-07T00:00:00Z",
                dates = new object[]
                {
                    new { titleKey = "picker.no_due_date", dueDateTime = (string?)null, isSelected = false },
                    new { titleKey = "picker.today", dueDateTime = "2026-09-07T00:00:00Z", isSelected = true },
                },
                times = new object[]
                {
                    new { titleKey = "picker.morning", hour = 9, dueDateTime = "2026-09-07T09:00:00Z", isSelected = false },
                },
            });
        var view = new TaskDetailViewModel(core);

        await view.OpenAsync("t1");

        Assert.Equal("picker.no_due_date", view.DatePicks[0].TitleKey);
        Assert.Null(view.DatePicks[0].DueDateTime);
        Assert.True(view.DatePicks[1].IsSelected);
        Assert.Equal("2026-09-07T09:00:00Z", view.TimePicks[0].DueDateTime);
        Assert.True(view.IsAllDay);
    }

    /// <summary>Choosing "No due date" sends an explicit null, which is what clears the field.</summary>
    [Fact]
    public async Task Choosing_no_due_date_clears_it()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new
            {
                isAllDay = true,
                dates = new object[]
                {
                    new { titleKey = "picker.no_due_date", dueDateTime = (string?)null, isSelected = false },
                },
                times = Array.Empty<object>(),
            })
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new { isAllDay = true, dates = Array.Empty<object>(), times = Array.Empty<object>() });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.TakeDuePickAsync(view.DatePicks[0]);

        var sent = core.Sent.Last(json => json.Contains("updateTask", StringComparison.Ordinal));
        Assert.Contains("\"dueDateTime\":null", sent, StringComparison.Ordinal);
    }

    /// <summary>Choosing a time makes the task timed rather than all-day.</summary>
    [Fact]
    public async Task Choosing_a_time_makes_the_task_timed()
    {
        var core = new FakeCore()
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new
            {
                isAllDay = true,
                dates = Array.Empty<object>(),
                times = new object[]
                {
                    new { titleKey = "picker.morning", hour = 9, dueDateTime = "2026-09-07T09:00:00Z", isSelected = false },
                },
            })
            .AnswerOk("updateTask")
            .AnswerOk("taskDetail", Detail())
            .AnswerOk("dueDateOptions", new { isAllDay = false, dates = Array.Empty<object>(), times = Array.Empty<object>() });
        var view = new TaskDetailViewModel(core);
        await view.OpenAsync("t1");

        await view.TakeDuePickAsync(view.TimePicks[0]);

        var sent = core.Sent.Last(json => json.Contains("updateTask", StringComparison.Ordinal));
        Assert.Contains("\"isAllDay\":false", sent, StringComparison.Ordinal);
        Assert.Contains("2026-09-07T09:00:00Z", sent, StringComparison.Ordinal);
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
