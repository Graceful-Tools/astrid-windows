using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// Waiting means waiting on a date or on a task, and nothing else (task 7462618e).
/// </summary>
/// <remarks>
/// <para>
/// Decided by Jon on 2026-11-12 and implemented in <c>astrid_rules::waiting</c>: a move into
/// Waiting that names no condition is refused, nothing is written, and the refusal carries
/// <c>code: "WAITING_NEEDS_CONDITION"</c>. These tests hold the shell's half of that — it reads the
/// code, asks for a condition, and sends the same move again carrying the answer.
/// </para>
/// <para>
/// None of them assert what counts as a condition. A future date counts, an incomplete blocker
/// counts, a date in the past does not, and all three are the core's decision; a shell that
/// re-checked them would be a second implementation of the rule, which is how the clients came to
/// disagree about repeats.
/// </para>
/// </remarks>
public sealed class WaitConditionTests
{
    /// <summary>The refusal, exactly as <c>Failure::waiting_needs_condition()</c> serialises it.</summary>
    private const string NeedsCondition =
        """{"ok":false,"error":{"kind":"refused","message":"waiting on what?","code":"WAITING_NEEDS_CONDITION"}}""";

    private static object Column(string id, string name, string kind, params string[] cards) => new
    {
        id,
        name,
        description = string.Empty,
        kind,
        total = cards.Length,
        cards = cards.Select((title, index) => new
        {
            id = $"{id}-{index}",
            title,
            completed = false,
            priority = 0,
            due = new { key = "none" },
            isOverdue = false,
            leading = new { kind = "unassigned" },
            action = "openPicker",
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

    private static object Board() => new
    {
        projectId = "p1",
        columns = new[]
        {
            Column("ready", "Ready", "status", "Write it down"),
            Column("waiting", "Waiting", "status"),
        },
    };

    private static FakeCore BoardCore() => new FakeCore().AnswerOk("board", Board());

    /// <summary>
    /// The envelope's <c>code</c> reaches C#.
    /// </summary>
    /// <remarks>
    /// The root of the whole task: <see cref="AstridFailure"/> read <c>kind</c>, <c>message</c>,
    /// <c>status</c> and <c>id</c> but not <c>code</c>, so this refusal arrived indistinguishable
    /// from "not yours" and every shell drew it as a flat error line.
    /// </remarks>
    [Fact]
    public void A_refusal_carries_its_code_task_7462618e()
    {
        var response = AstridResponse.Parse(NeedsCondition);

        Assert.False(response.Ok);
        Assert.Equal(AstridFailureKind.Refused, response.Error?.Kind);
        Assert.Equal("WAITING_NEEDS_CONDITION", response.Error?.Code);
        Assert.True(response.NeedsWaitCondition);
    }

    /// <summary>A refusal with no code is still a refusal, and asks for nothing.</summary>
    [Fact]
    public void A_refusal_without_a_code_is_not_a_wait_prompt_task_7462618e()
    {
        var response = AstridResponse.Parse(
            """{"ok":false,"error":{"kind":"refused","message":"not yours"}}""");

        Assert.Null(response.Error?.Code);
        Assert.False(response.NeedsWaitCondition);
    }

    /// <summary>
    /// A move that names no condition sends no wait keys at all.
    /// </summary>
    /// <remarks>
    /// The four keys are <c>#[serde(default)]</c> in core, so omitting them is how every existing
    /// call keeps working. Asserting the absence rather than trusting it: a <c>keepAssignee: false</c>
    /// written out on every move would be a behaviour change smuggled in as a serialisation detail.
    /// </remarks>
    [Fact]
    public async Task An_ordinary_move_sends_no_wait_keys_task_7462618e()
    {
        var core = BoardCore().AnswerOk("moveTaskToColumn").AnswerOk("board", Board());
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");

        Assert.True(await view.MoveAsync("ready-0", "waiting"));

        var move = core.Sent.First(sent => sent.Contains("moveTaskToColumn"));
        Assert.DoesNotContain("dueDateTime", move);
        Assert.DoesNotContain("isAllDay", move);
        Assert.DoesNotContain("waitOnTaskId", move);
        Assert.DoesNotContain("keepAssignee", move);
    }

    /// <summary>
    /// A move refused for want of a condition asks for one instead of showing an error.
    /// </summary>
    /// <remarks>
    /// <see cref="BoardViewModel.ErrorMessage"/> stays null: there is nothing wrong, and a red line
    /// under a question the user has not been asked yet is the bug this task was filed about.
    /// </remarks>
    [Fact]
    public async Task A_move_into_waiting_with_no_condition_asks_for_one_task_7462618e()
    {
        var core = BoardCore().Answer("moveTaskToColumn", NeedsCondition).AnswerOk("board", Board());
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");

        Assert.False(await view.MoveAsync("ready-0", "waiting"));

        Assert.Null(view.ErrorMessage);
        Assert.NotNull(view.WaitPrompt);
        Assert.Equal("waiting", view.WaitPrompt!.ColumnId);
        Assert.Equal("Waiting", view.WaitPrompt.ColumnName);
        Assert.Equal("ready-0", view.WaitPrompt.TaskId);
    }

    /// <summary>Answering with a date sends the same move again, carrying it.</summary>
    [Fact]
    public async Task Answering_with_a_date_sends_the_move_again_task_7462618e()
    {
        var core = BoardCore()
            .Answer("moveTaskToColumn", NeedsCondition)
            .AnswerOk("board", Board())
            .AnswerOk("moveTaskToColumn")
            .AnswerOk("board", Board());
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");
        await view.MoveAsync("ready-0", "waiting");

        Assert.True(await view.ResolveWaitAsync(
            new WaitCondition(DueDateTime: "2026-12-01T00:00:00Z", IsAllDay: true)));

        var retry = core.Sent.Last(sent => sent.Contains("moveTaskToColumn"));
        Assert.Contains("\"taskId\":\"ready-0\"", retry);
        Assert.Contains("\"columnId\":\"waiting\"", retry);
        Assert.Contains("\"dueDateTime\":\"2026-12-01T00:00:00Z\"", retry);
        Assert.Contains("\"isAllDay\":true", retry);
        Assert.DoesNotContain("waitOnTaskId", retry);
        Assert.Null(view.WaitPrompt);
    }

    /// <summary>Answering with a task sends <c>waitOnTaskId</c>, and keeps the assignee if asked.</summary>
    [Fact]
    public async Task Answering_with_a_task_sends_the_blocker_and_keep_assigned_task_7462618e()
    {
        var core = BoardCore()
            .Answer("moveTaskToColumn", NeedsCondition)
            .AnswerOk("board", Board())
            .AnswerOk("moveTaskToColumn")
            .AnswerOk("board", Board());
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");
        await view.MoveAsync("ready-0", "waiting");

        Assert.True(await view.ResolveWaitAsync(
            new WaitCondition(WaitOnTaskId: "blocker-1", KeepAssignee: true)));

        var retry = core.Sent.Last(sent => sent.Contains("moveTaskToColumn"));
        Assert.Contains("\"waitOnTaskId\":\"blocker-1\"", retry);
        Assert.Contains("\"keepAssignee\":true", retry);
        Assert.DoesNotContain("dueDateTime", retry);
    }

    /// <summary>
    /// Cancelling leaves the task where it was, and sends nothing.
    /// </summary>
    /// <remarks>
    /// There is nothing to undo: core refuses before it writes, so the card never left its column.
    /// The test holds that the shell does not invent a compensating move.
    /// </remarks>
    [Fact]
    public async Task Cancelling_the_prompt_writes_nothing_task_7462618e()
    {
        var core = BoardCore().Answer("moveTaskToColumn", NeedsCondition).AnswerOk("board", Board());
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");
        await view.MoveAsync("ready-0", "waiting");
        var before = core.Sent.Count;

        view.CancelWaitPrompt();

        Assert.Null(view.WaitPrompt);
        Assert.Equal(before, core.Sent.Count);
        Assert.Null(view.ErrorMessage);
    }

    /// <summary>Answering with nothing at all does not re-send a move core would refuse again.</summary>
    [Fact]
    public async Task An_empty_answer_is_not_sent_task_7462618e()
    {
        var core = BoardCore().Answer("moveTaskToColumn", NeedsCondition).AnswerOk("board", Board());
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");
        await view.MoveAsync("ready-0", "waiting");
        var before = core.Sent.Count;

        Assert.False(await view.ResolveWaitAsync(new WaitCondition()));

        Assert.Equal(before, core.Sent.Count);
        Assert.NotNull(view.WaitPrompt);
    }

    /// <summary>
    /// A card typed straight into Waiting asks too, and the retry still carries the title.
    /// </summary>
    /// <remarks>
    /// <c>addBoardCard</c> takes the same four keys, and for the same reason: a card born in Waiting
    /// is a task entering Waiting. The draft is cleared before the write (the Mac's
    /// <c>addCard(to:)</c>), so the retry has to carry the title it captured rather than re-read the
    /// field — which is why the prompt holds it.
    /// </remarks>
    [Fact]
    public async Task A_card_typed_into_waiting_asks_for_a_condition_task_7462618e()
    {
        var core = BoardCore().Answer("addBoardCard", NeedsCondition).AnswerOk("board", Board())
            .AnswerOk("addBoardCard").AnswerOk("board", Board());
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");
        var waiting = view.Columns.Single(column => column.Id == "waiting");
        waiting.Draft = "Chase the invoice";

        Assert.False(await waiting.AddCardAsync());

        Assert.NotNull(view.WaitPrompt);
        Assert.Equal("Chase the invoice", view.WaitPrompt!.Title);
        Assert.Null(view.WaitPrompt.TaskId);

        Assert.True(await view.ResolveWaitAsync(new WaitCondition(WaitOnTaskId: "blocker-1")));
        var retry = core.Sent.Last(sent => sent.Contains("addBoardCard"));
        Assert.Contains("\"title\":\"Chase the invoice\"", retry);
        Assert.Contains("\"waitOnTaskId\":\"blocker-1\"", retry);
    }
}
