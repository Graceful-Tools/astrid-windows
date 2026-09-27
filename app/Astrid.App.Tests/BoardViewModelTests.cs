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

    /// <summary>
    /// A card typed into a column is created IN that column, in one request (task 95c7a68f).
    /// </summary>
    /// <remarks>
    /// The column id, the board's list and the title — and nothing about roles or completion, which
    /// are the core's to decide. A shell that created the card and then moved it is the Mac's
    /// AITD-328, where the role was lost in between.
    /// </remarks>
    [Fact]
    public async Task Adding_a_card_to_a_column_sends_the_column_and_the_board_task_95c7a68f()
    {
        var core = new FakeCore()
            .AnswerOk("board", Board(Column("doing", "Doing", "status")))
            .AnswerOk("addBoardCard")
            .AnswerOk("board", Board(Column("doing", "Doing", "status", "Write it down")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");
        view.Columns[0].Draft = "  Write it down  ";

        Assert.True(await view.Columns[0].AddCardAsync());

        var added = core.Sent.First(sent => sent.Contains("addBoardCard"));
        Assert.Contains("\"listId\":\"l1\"", added);
        Assert.Contains("\"columnId\":\"doing\"", added);
        Assert.Contains("\"title\":\"Write it down\"", added);
        Assert.DoesNotContain("moveTaskToColumn", string.Join(" ", core.SentKinds()));
        // The board is reloaded, so the card appears without the view placing it itself.
        Assert.Equal(["board", "addBoardCard", "board"], core.SentKinds());
        Assert.Equal("Write it down", view.Columns[0].Cards[0].Title);
    }

    /// <summary>
    /// The field clears on submit, and an empty title does nothing at all (task 95c7a68f).
    /// </summary>
    [Fact]
    public async Task The_add_field_clears_on_submit_and_an_empty_title_does_nothing_task_95c7a68f()
    {
        var core = new FakeCore()
            .AnswerOk("board", Board(Column("doing", "Doing", "status")))
            .AnswerOk("addBoardCard")
            .AnswerOk("board", Board(Column("doing", "Doing", "status", "Write it down")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");

        view.Columns[0].Draft = "   ";
        Assert.False(await view.Columns[0].AddCardAsync());
        Assert.Equal(["board"], core.SentKinds());

        view.Columns[0].Draft = "Write it down";
        await view.Columns[0].AddCardAsync();

        Assert.Equal(string.Empty, view.Columns[0].Draft);
    }

    /// <summary>
    /// A draft typed in one column is not disturbed by another column's create (task 95c7a68f).
    /// </summary>
    /// <remarks>
    /// The reason the drafts live on the board rather than on the column views: a create reloads
    /// the board, which rebuilds every column view, so a draft held on one of those would vanish
    /// from whichever column its owner was still typing in.
    /// </remarks>
    [Fact]
    public async Task A_draft_in_one_column_survives_another_column_s_create_task_95c7a68f()
    {
        var core = new FakeCore()
            .AnswerOk("board", Board(Column("ready", "Ready", "status"), Column("doing", "Doing", "status")))
            .AnswerOk("addBoardCard")
            .AnswerOk("board", Board(Column("ready", "Ready", "status"), Column("doing", "Doing", "status", "Write it down")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");
        view.Columns[0].Draft = "still typing";
        view.Columns[1].Draft = "Write it down";

        await view.Columns[1].AddCardAsync();

        Assert.Equal("still typing", view.Columns[0].Draft);
        Assert.Equal(string.Empty, view.Columns[1].Draft);
    }

    /// <summary>
    /// Offline is not an error here either: the card is in the Outbox and the board already shows
    /// it.
    /// </summary>
    [Fact]
    public async Task A_card_that_has_not_reached_the_server_is_not_reported_as_a_failure_task_95c7a68f()
    {
        var core = new FakeCore()
            .AnswerOk("board", Board(Column("doing", "Doing", "status")))
            .AnswerFailure("addBoardCard", AstridFailureKind.Offline, "no network")
            .AnswerOk("board", Board(Column("doing", "Doing", "status", "Write it down")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");
        view.Columns[0].Draft = "Write it down";

        await view.Columns[0].AddCardAsync();

        Assert.Null(view.ErrorMessage);
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
