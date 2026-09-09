using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// What the board view does — and, as everywhere else here, what it declines to decide.
/// </summary>
public sealed class BoardViewModelTests
{
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

    private static object Board(params object[] columns) => new
    {
        projectId = "p1",
        columns,
    };

    [Fact]
    public async Task Opening_a_board_shows_the_columns_the_core_returned()
    {
        var core = new FakeCore().AnswerOk("board", Board(
            Column("__virtual_inbox__", "Inbox", "inbox", "Write it down"),
            Column("ready", "Ready", "status"),
            Column("__virtual_done__", "Done", "done")));
        var view = new BoardViewModel(core);

        await view.LoadAsync("l1");

        Assert.True(view.HasBoard);
        Assert.Equal(3, view.Columns.Count);
        Assert.Equal("Write it down", view.Columns[0].Cards[0].Title);
    }

    /// <summary>
    /// The header says how many cards the column holds, not how many crossed the boundary — the
    /// same distinction the list's total makes.
    /// </summary>
    [Fact]
    public async Task A_column_heading_counts_every_card_not_the_ones_carried()
    {
        var core = new FakeCore().AnswerOk("board", new
        {
            projectId = "p1",
            columns = new[]
            {
                new { id = "ready", name = "Ready", description = "", kind = "status", total = 120, cards = Array.Empty<object>() },
            },
        });
        var view = new BoardViewModel(core);

        await view.LoadAsync("l1");

        // The words are the shell's; what the view model carries is the count itself.
        Assert.Equal(120, view.Columns[0].Total);
        Assert.Empty(view.Columns[0].Cards);
    }

    /// <summary>A list that belongs to no board is not an error; there is simply nothing to draw.</summary>
    [Fact]
    public async Task A_list_with_no_board_shows_nothing_and_says_nothing()
    {
        var core = new FakeCore().AnswerOk("board", new
        {
            projectId = (string?)null,
            columns = Array.Empty<object>(),
        });
        var view = new BoardViewModel(core);

        await view.LoadAsync("l1");

        Assert.False(view.HasBoard);
        Assert.Empty(view.Columns);
        Assert.Null(view.ErrorMessage);
    }

    /// <summary>
    /// A move reloads the board rather than moving the card itself: dropping a repeating card on
    /// Done rolls it forward and leaves it where it was, and a view that moved the card would have
    /// to know that.
    /// </summary>
    [Fact]
    public async Task Moving_a_card_reloads_the_board()
    {
        var core = new FakeCore()
            .AnswerOk("board", Board(Column("ready", "Ready", "status", "Write it down")))
            .AnswerOk("moveTaskToColumn")
            .AnswerOk("board", Board(Column("doing", "Doing", "status", "Write it down")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");

        Assert.True(await view.MoveAsync("ready-0", "doing"));

        Assert.Equal(["board", "moveTaskToColumn", "board"], core.SentKinds());
        Assert.Equal("doing", view.Columns[0].Id);
    }

    /// <summary>
    /// Offline is not an error. The move is in the Outbox, the cache already shows it, and a red
    /// message here is how a working offline app comes to look broken.
    /// </summary>
    [Fact]
    public async Task A_move_that_has_not_reached_the_server_is_not_reported_as_a_failure()
    {
        var core = new FakeCore()
            .AnswerOk("board", Board(Column("ready", "Ready", "status", "Write it down")))
            .AnswerFailure("moveTaskToColumn", AstridFailureKind.Offline, "no network")
            .AnswerOk("board", Board(Column("ready", "Ready", "status", "Write it down")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");

        await view.MoveAsync("ready-0", "doing");

        Assert.Null(view.ErrorMessage);
    }

    [Fact]
    public async Task A_refusal_is_reported()
    {
        var core = new FakeCore()
            .AnswerOk("board", Board(Column("ready", "Ready", "status", "Write it down")))
            .AnswerFailure("moveTaskToColumn", AstridFailureKind.Refused, "not yours")
            .AnswerOk("board", Board(Column("ready", "Ready", "status", "Write it down")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");

        await view.MoveAsync("ready-0", "doing");

        Assert.Equal("not yours", view.ErrorMessage);
    }

    /// <summary>The move carries the list the board was opened from, so it resolves the right board.</summary>
    [Fact]
    public async Task A_move_says_which_board_it_came_from()
    {
        var core = new FakeCore()
            .AnswerOk("board", Board(Column("ready", "Ready", "status", "Write it down")))
            .AnswerOk("moveTaskToColumn")
            .AnswerOk("board", Board());
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");

        await view.MoveAsync("ready-0", "doing");

        var move = core.Sent.First(sent => sent.Contains("moveTaskToColumn"));
        Assert.Contains("\"listId\":\"l1\"", move);
        Assert.Contains("\"columnId\":\"doing\"", move);
    }
}


/// <summary>
/// A tapped card expands in place, as astrid-web's board does (task 91a25b8a).
/// </summary>
/// <remarks>
/// The board says WHERE the detail goes — a slot after the expanded card — and nothing about what
/// the detail contains. The shell hosts its one detail pane in that slot, so the board and the
/// list share one implementation of a task.
/// </remarks>
public sealed class BoardExpansionTests
{
    private static object Column(string id, params string[] cards) => new
    {
        id,
        name = id,
        description = string.Empty,
        kind = "status",
        total = cards.Length,
        cards = cards.Select(title => new
        {
            id = title,
            title,
            completed = false,
            priority = 0,
            due = new { key = "none" },
            leading = new { kind = "unassigned" },
            action = "openPicker",
        }).ToArray(),
    };

    private static object Board(params object[] columns) => new { projectId = "p1", columns };

    [Fact]
    public async Task Expanding_a_card_puts_the_detail_slot_right_after_it_task_91a25b8a()
    {
        var core = new FakeCore().AnswerOk("board", Board(Column("ready", "t1", "t2", "t3")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");

        view.Expand("t2");

        Assert.Equal("t2", view.ExpandedTaskId);
        var items = view.Columns[0].Items;
        Assert.Equal(4, items.Count);
        Assert.Equal("t2", Assert.IsType<TaskRow>(items[1]).Id);
        Assert.Equal("t2", Assert.IsType<InlineDetailSlot>(items[2]).TaskId);
        Assert.True(view.Columns[0].HoldsExpandedCard);
        // The cards themselves are untouched: the slot is a place, not a card.
        Assert.Equal(3, view.Columns[0].Cards.Count);
    }

    [Fact]
    public async Task Collapsing_removes_the_slot()
    {
        var core = new FakeCore().AnswerOk("board", Board(Column("ready", "t1", "t2")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");
        view.Expand("t1");

        view.Collapse();

        Assert.Null(view.ExpandedTaskId);
        Assert.Equal(2, view.Columns[0].Items.Count);
        Assert.DoesNotContain(view.Columns[0].Items, item => item is InlineDetailSlot);
        Assert.False(view.Columns[0].HoldsExpandedCard);
    }

    /// <summary>
    /// A refresh — a sync, a colleague's edit, the expanded task's own title being changed — must
    /// not lose the expansion. The slot follows the card wherever the reload puts it.
    /// </summary>
    [Fact]
    public async Task A_refresh_keeps_the_slot_with_its_card_even_when_the_card_moves()
    {
        var core = new FakeCore()
            .AnswerOk("board", Board(Column("ready", "t1", "t2"), Column("doing")))
            .AnswerOk("board", Board(Column("ready", "t1"), Column("doing", "t2")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");
        view.Expand("t2");

        await view.RefreshAsync();

        Assert.Equal("t2", view.ExpandedTaskId);
        Assert.False(view.Columns[0].HoldsExpandedCard);
        Assert.True(view.Columns[1].HoldsExpandedCard);
        Assert.Equal("t2", Assert.IsType<InlineDetailSlot>(view.Columns[1].Items[1]).TaskId);
    }

    /// <summary>A card that is no longer on the board has nothing to expand, as on the web.</summary>
    [Fact]
    public async Task A_card_that_left_the_board_collapses()
    {
        var core = new FakeCore()
            .AnswerOk("board", Board(Column("ready", "t1", "t2")))
            .AnswerOk("board", Board(Column("ready", "t1")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");
        view.Expand("t2");

        await view.RefreshAsync();

        Assert.Null(view.ExpandedTaskId);
    }
}
