using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The mark does what the core said it does — all three answers, not two (task 3c0dfb73).
/// </summary>
/// <remarks>
/// <para>
/// The shell read <c>action</c> in exactly one place, <c>IsCopyOnly</c>, and completed on
/// everything else. So a board card finished its task on the click that was meant to set its
/// column, somebody else's task finished on a stray click with nothing on the row to say it had
/// (AITD-375), and a row nobody owned finished instead of first asking whose it is (AITD-382).
/// </para>
/// <para>
/// The assertion that matters in each case is a NEGATIVE one: no <c>completeTask</c> was sent at
/// all. A picker that opens and also completes would pass a test that only looked at the outcome.
/// </para>
/// </remarks>
public sealed class LeadingPickerTests
{
    private static object Row(string id, string title, string action, string leadingKind,
        bool pickerShowsProjectState = false, bool completed = false, string? assigneeId = null,
        int priority = 0, bool isRepeating = false) => new
        {
            id,
            title,
            completed,
            priority,
            due = new { key = "none" },
            isOverdue = false,
            leading = assigneeId is null
                ? new { kind = leadingKind, userId = (string?)null }
                : new { kind = leadingKind, userId = (string?)assigneeId },
            action,
            pickerShowsProjectState,
            depth = 0,
            isPending = false,
            isPrivate = false,
            isRepeating,
            hasDescription = false,
            commentCount = 0,
            attachmentCount = 0,
            subtaskCount = 0,
            listChips = Array.Empty<object>(),
            statusRole = (string?)null,
        };

    private static object Rows(params object[] rows) =>
        new { total = rows.Length, offset = 0, rows };

    /// <summary>
    /// An unassigned list row opens the picker and writes nothing (AITD-382).
    /// </summary>
    [Fact]
    public async Task An_unassigned_row_does_not_complete_on_click_task_3c0dfb73()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Rows(Row("t0", "Prune the roses", "openPicker", "unassigned")))
            .AnswerOk("assigneeOptions", new
            {
                assigneeId = (string?)null,
                options = new[]
                {
                    new { userId = (string?)null, name = (string?)null, initials = "U" },
                    new { userId = (string?)"me", name = (string?)"Jon", initials = "J" },
                },
            });
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Garden");

        var outcome = await view.ActivateLeadingAsync("t0");

        Assert.Equal(LeadingOutcome.OpenPicker, outcome);
        Assert.DoesNotContain(core.Sent, json => json.Contains("\"kind\":\"completeTask\""));
        Assert.Equal("t0", view.LeadingPicker.TaskId);
        // Who it could go to, offered; the board state is not, because the core did not say so.
        Assert.Equal(2, view.LeadingPicker.Assignees.Count);
        Assert.False(view.LeadingPicker.ShowsProjectState);
        Assert.Empty(view.LeadingPicker.StatusChoices);
        Assert.DoesNotContain(core.Sent, json => json.Contains("\"kind\":\"taskStatusOptions\""));
    }

    /// <summary>
    /// Somebody else's row opens the picker, and that picker offers the column — a task being
    /// triaged rather than worked is exactly when its column matters.
    /// </summary>
    [Fact]
    public async Task Someone_elses_row_opens_a_picker_with_the_board_state_task_3c0dfb73()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Rows(Row("t1", "Plant tulips", "openPicker", "avatar",
                pickerShowsProjectState: true, assigneeId: "them")))
            .AnswerOk("assigneeOptions", new
            {
                assigneeId = (string?)"them",
                options = new[] { new { userId = (string?)"me", name = (string?)"Jon", initials = "J" } },
            })
            .AnswerOk("taskStatusOptions", new
            {
                columns = new[]
                {
                    new { id = "ready", name = "Ready", kind = "status", isCurrent = true },
                    new { id = "doing", name = "Doing", kind = "status", isCurrent = false },
                },
            });
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Garden");

        Assert.Equal(LeadingOutcome.OpenPicker, await view.ActivateLeadingAsync("t1"));

        Assert.DoesNotContain(core.Sent, json => json.Contains("\"kind\":\"completeTask\""));
        Assert.True(view.LeadingPicker.ShowsProjectState);
        Assert.Equal(["Ready", "Doing"], view.LeadingPicker.StatusChoices.Select(c => c.Name));

        // And a pick from it is the ordinary write, through the Outbox like every other edit.
        core.AnswerOk("setTaskStatus").AnswerOk("rowsForList", Rows());
        Assert.True(await view.LeadingPicker.PickStatusAsync("doing"));
        Assert.Contains(core.Sent, json => json.Contains("\"kind\":\"setTaskStatus\"")
                                           && json.Contains("\"columnId\":\"doing\""));
    }

    /// <summary>
    /// A board card's mark never completes — in either display mode, even for the reader's own
    /// task. The trapdoor tasks <c>9be8cb1b</c> and <c>f9d7ed42</c> closed on Apple.
    /// </summary>
    [Fact]
    public async Task A_board_cards_mark_does_not_complete_on_click_task_3c0dfb73()
    {
        var core = new FakeCore()
            .AnswerOk("board", new
            {
                projectId = "p1",
                columns = new[]
                {
                    new
                    {
                        id = "ready", name = "Ready", description = "", kind = "status", total = 1,
                        cards = new[]
                        {
                            Row("c0", "Water roses", "openPicker", "checkbox",
                                pickerShowsProjectState: true, assigneeId: "me"),
                        },
                    },
                },
            })
            .AnswerOk("assigneeOptions", new
            {
                assigneeId = (string?)"me",
                options = new[] { new { userId = (string?)"me", name = (string?)"Jon", initials = "J" } },
            })
            .AnswerOk("taskStatusOptions", new
            {
                columns = new[] { new { id = "doing", name = "Doing", kind = "status", isCurrent = false } },
            });
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");

        Assert.Equal(LeadingOutcome.OpenPicker, await view.ActivateLeadingAsync("c0"));

        Assert.DoesNotContain(core.Sent, json => json.Contains("\"kind\":\"completeTask\""));
        Assert.Equal("c0", view.LeadingPicker.TaskId);
        Assert.True(view.LeadingPicker.ShowsProjectState);
    }

    /// <summary>
    /// What still completes: the reader's own task on a list row, whose control IS a checkbox.
    /// Closing a trapdoor is not the same as nailing the door shut.
    /// </summary>
    [Fact]
    public async Task My_own_list_row_still_completes_task_3c0dfb73()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Rows(Row("t2", "Water roses", "complete", "checkbox",
                assigneeId: "me")))
            .AnswerOk("completeTask")
            .AnswerOk("rowsForList", Rows());
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Garden");

        Assert.Equal(LeadingOutcome.Completed, await view.ActivateLeadingAsync("t2"));

        Assert.Contains(core.Sent, json => json.Contains("\"kind\":\"completeTask\"")
                                           && json.Contains("\"completed\":true"));
        Assert.Null(view.LeadingPicker.TaskId);
    }

    /// <summary>And a copy-only row still copies (task f6bc59e8), which was the one answer read.</summary>
    [Fact]
    public async Task A_copy_only_row_still_copies_task_3c0dfb73()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Rows(Row("t3", "Bake bread", "copy", "copy")))
            .AnswerOk("copyTask", new { id = "t9", title = "Bake bread" });
        var view = new TaskListViewModel(core);
        await view.OpenAsync("pub", "Recipes");

        Assert.Equal(LeadingOutcome.Copied, await view.ActivateLeadingAsync("t3"));

        Assert.Contains(core.Sent, json => json.Contains("\"kind\":\"copyTask\""));
        Assert.DoesNotContain(core.Sent, json => json.Contains("\"kind\":\"completeTask\""));
    }

    /// <summary>
    /// A screen reader is told what the control does, not what the other controls do.
    /// </summary>
    /// <remarks>
    /// <c>AutomationProperties.Name</c> on both marks is <c>LeadingActionName</c>, and that said
    /// "Complete Plant tulips" for a control that opens a picker — the same trapdoor as the click,
    /// only harder to notice.
    /// </remarks>
    [Fact]
    public async Task The_marks_name_stops_saying_complete_for_a_picker_task_3c0dfb73()
    {
        var core = new FakeCore().AnswerOk("rowsForList", Rows(
            Row("t4", "Plant tulips", "openPicker", "avatar", assigneeId: "them"),
            Row("t5", "Water roses", "complete", "checkbox", assigneeId: "me"),
            Row("t6", "Bake bread", "copy", "copy")));
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Garden");

        Assert.Equal("Options for Plant tulips", view.Rows[0].LeadingActionName);
        Assert.True(view.Rows[0].OpensPicker);
        Assert.Equal("Complete Water roses", view.Rows[1].LeadingActionName);
        Assert.False(view.Rows[1].OpensPicker);
        Assert.Equal("Copy Bake bread", view.Rows[2].LeadingActionName);
    }

    /// <summary>
    /// A task on no board answers <c>taskStatusOptions</c> with a refusal, and that is not an
    /// error to put in front of the reader: the picker simply has no state row, and who and
    /// priority still work.
    /// </summary>
    [Fact]
    public async Task A_task_on_no_board_still_gets_a_picker_task_3c0dfb73()
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Rows(Row("t7", "Plant tulips", "openPicker", "avatar",
                pickerShowsProjectState: true, assigneeId: "them")))
            .AnswerOk("assigneeOptions", new
            {
                assigneeId = (string?)"them",
                options = new[] { new { userId = (string?)"me", name = (string?)"Jon", initials = "J" } },
            })
            .AnswerFailure("taskStatusOptions", AstridFailureKind.NotFound, "no board");
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Garden");

        Assert.Equal(LeadingOutcome.OpenPicker, await view.ActivateLeadingAsync("t7"));

        Assert.Single(view.LeadingPicker.Assignees);
        Assert.Empty(view.LeadingPicker.StatusChoices);
        Assert.Null(view.ErrorMessage);
    }

    /// <summary>A row that has gone writes nothing. Completing on a miss is how this began.</summary>
    [Fact]
    public async Task A_mark_on_a_row_that_has_gone_writes_nothing_task_3c0dfb73()
    {
        var core = new FakeCore().AnswerOk("rowsForList", Rows());
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Garden");

        Assert.Equal(LeadingOutcome.Gone, await view.ActivateLeadingAsync("vanished"));

        Assert.DoesNotContain(core.Sent, json => json.Contains("\"kind\":\"completeTask\""));
    }

    /// <summary>
    /// The picker ends in "Mark as done", or "Mark as to-do" on a finished task, beside the task's
    /// own checkbox: its priority colour, its repeat arrows, ticked when done (task 8cf2d10d,
    /// ADEV-146/148). The words are web's registry keys, not a Windows-invented pair.
    /// </summary>
    [Theory]
    [InlineData(false, "tasks.markAsDone")]
    [InlineData(true, "tasks.markAsTodo")]
    public async Task The_picker_ends_in_mark_as_done_beside_the_tasks_own_checkbox_task_8cf2d10d(
        bool completed, string key)
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Rows(Row("t8", "Water roses", "openPicker", "checkbox",
                completed: completed, assigneeId: "them", priority: 3, isRepeating: true)))
            .AnswerOk("assigneeOptions", new
            {
                assigneeId = (string?)"them",
                options = new[] { new { userId = (string?)"me", name = (string?)"Jon", initials = "J" } },
            });
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Garden");

        Assert.Equal(LeadingOutcome.OpenPicker, await view.ActivateLeadingAsync("t8"));

        Assert.Equal(key, view.LeadingPicker.CompletionKey);
        Assert.Equal(completed, view.LeadingPicker.Completed);
        Assert.Equal(3, view.LeadingPicker.Priority);
        Assert.True(view.LeadingPicker.IsRepeating);
    }

    /// <summary>A row carrying the core's <c>completionNeedsConfirmation</c> and its holder.</summary>
    private static object AskingRow(string id, bool asks, string? holderName, bool completed = false)
        => new
        {
            id,
            title = "Water roses",
            completed,
            priority = 0,
            due = new { key = "none" },
            isOverdue = false,
            leading = new { kind = holderName is null ? "unassigned" : "avatar", userId = holderName is null ? null : "them" },
            action = "openPicker",
            completionNeedsConfirmation = asks,
            assignee = holderName is null ? null : new { id = "them", name = holderName },
            depth = 0,
            listChips = Array.Empty<object>(),
        };

    private static async Task<(FakeCore Core, TaskListViewModel View)> OpenPickerOn(object row)
    {
        var core = new FakeCore()
            .AnswerOk("rowsForList", Rows(row))
            .AnswerOk("assigneeOptions", new
            {
                assigneeId = (string?)null,
                options = new[] { new { userId = (string?)"me", name = (string?)"Jon", initials = "J" } },
            })
            .AnswerOk("completeTask", new { });
        var view = new TaskListViewModel(core);
        await view.OpenAsync("l1", "Garden");
        Assert.Equal(LeadingOutcome.OpenPicker, await view.ActivateLeadingAsync("t9"));
        return (core, view);
    }

    /// <summary>
    /// Someone else's task: the picker's completion row becomes the question — "Assigned to Ana",
    /// then Cancel / Complete — and only Complete writes (task d02d76e4, web ADEV-71). The flag is
    /// the core's <c>completionNeedsConfirmation</c>, read off the row, never worked out here.
    /// </summary>
    [Fact]
    public async Task Completing_someone_elses_task_asks_first_task_d02d76e4()
    {
        var (core, view) = await OpenPickerOn(AskingRow("t9", asks: true, holderName: "Ana"));
        var picker = view.LeadingPicker;

        Assert.True(picker.AsksBeforeCompleting);
        Assert.Equal("tasks.confirmCompleteAssigned", picker.HolderLineKey);
        Assert.Equal("Ana", picker.HolderName);
        Assert.DoesNotContain(core.Sent, json => json.Contains("\"kind\":\"completeTask\""));

        Assert.True(await picker.CompleteAsync());
        Assert.Contains(core.Sent, json => json.Contains("\"kind\":\"completeTask\""));
    }

    /// <summary>Nobody's task asks too, and its holder line says nobody holds it (task d02d76e4).</summary>
    [Fact]
    public async Task Completing_an_unassigned_task_asks_first_task_d02d76e4()
    {
        var (_, view) = await OpenPickerOn(AskingRow("t9", asks: true, holderName: null));

        Assert.True(view.LeadingPicker.AsksBeforeCompleting);
        Assert.Equal("tasks.confirmCompleteUnassigned", view.LeadingPicker.HolderLineKey);
        Assert.Null(view.LeadingPicker.HolderName);
    }

    /// <summary>
    /// When the core says not to ask — reopening a finished task, or the reader's own — the picker
    /// keeps its plain "Mark as done" row (task d02d76e4).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_picker_does_not_ask_when_the_core_says_not_to_task_d02d76e4(bool completed)
    {
        var (_, view) = await OpenPickerOn(
            AskingRow("t9", asks: false, holderName: "Ana", completed: completed));

        Assert.False(view.LeadingPicker.AsksBeforeCompleting);
    }
}
