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

    /// <summary>
    /// A column carries the empty message the core chose for it (task 4ac294ab, astrid-core
    /// ADEV-221) — a key, which the view turns into words.
    /// </summary>
    [Fact]
    public async Task Task4ac294ab_a_column_carries_its_own_empty_message_key()
    {
        var core = new FakeCore().AnswerOk("board", new
        {
            projectId = "p1",
            columns = new[]
            {
                new { id = "__virtual_inbox__", name = "Inbox", description = "", kind = "inbox", total = 0, emptyMessageKey = "board.empty.inbox", cards = Array.Empty<object>() },
                new { id = "doing", name = "Doing", description = "", kind = "status", total = 0, emptyMessageKey = "board.empty.doing", cards = Array.Empty<object>() },
            },
        });
        var view = new BoardViewModel(core);

        await view.LoadAsync("l1");

        Assert.Equal("board.empty.inbox", view.Columns[0].EmptyMessageKey);
        Assert.Equal("board.empty.doing", view.Columns[1].EmptyMessageKey);
    }

    /// <summary>
    /// A column the core sent no key for says the generic message, never nothing (task 4ac294ab):
    /// the board answer at the current pin leaves the key out.
    /// </summary>
    [Fact]
    public async Task Task4ac294ab_a_column_with_no_key_says_the_generic_message()
    {
        var core = new FakeCore().AnswerOk("board", new
        {
            projectId = "p1",
            columns = new[]
            {
                new { id = "ready", name = "Ready", description = "", kind = "status", total = 0, cards = Array.Empty<object>() },
            },
        });
        var view = new BoardViewModel(core);

        await view.LoadAsync("l1");

        Assert.Equal("board.empty.custom", view.Columns[0].EmptyMessageKey);
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

        Assert.Equal(["board", "getListGoals", "moveTaskToColumn", "board"], core.SentKinds());
        Assert.Equal("doing", view.Columns[0].Id);
    }

    /// <summary>
    /// A card's mark completes the task, the way a row's does (task 6979e694).
    /// </summary>
    /// <remarks>
    /// Always the complete command, never an update carrying <c>completed</c>: a repeating task
    /// rolls forward rather than finishing, and only that path does it. The board reloads
    /// afterwards for the same reason a move does — a rolled-forward task is a different card now,
    /// and only the core knows what it looks like.
    /// </remarks>
    [Fact]
    public async Task A_cards_mark_completes_the_task_task_6979e694()
    {
        var core = new FakeCore()
            .AnswerOk("board", Board(Column("ready", "Ready", "status", "Write it down")))
            .AnswerOk("completeTask")
            .AnswerOk("board", Board(Column("ready", "Ready", "status")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");

        Assert.True(await view.SetCompletedAsync("ready-0", completed: true));

        var sent = core.Sent.First(json => json.Contains("completeTask"));
        Assert.Contains("\"taskId\":\"ready-0\"", sent);
        Assert.Contains("\"completed\":true", sent);
        Assert.Equal(["board", "getListGoals", "completeTask", "board"], core.SentKinds());
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
        Assert.Equal(["board", "getListGoals", "addBoardCard", "board"], core.SentKinds());
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
        Assert.Equal(["board", "getListGoals"], core.SentKinds());

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

    // ── What the field at the bottom of a column will make (task 71ad20a0) ──────────────────
    //
    // The field took a title and nothing else, while the list's own add field has offered a
    // priority picker, an assignee picker and a preview since task 8aa5732c. Same gesture, two
    // different sets of controls depending on which view you happened to be in.
    //
    // NOT smart parsing, deliberately (Jon, 2026-11-14): the column already decides the list and
    // whether Done means already done, so a date or a #list tag read out of the sentence would be
    // fighting it.

    /// <summary>
    /// A column's add field previews the card it would make, as the list's field does
    /// (task 71ad20a0).
    /// </summary>
    /// <remarks>
    /// Asked with the BOARD'S list id, not the column's: a card typed into a column belongs to the
    /// board's list, and the column decides its status rather than its list. That is why the
    /// preview needed no core change — only carrying the two picks did.
    /// </remarks>
    [Fact]
    public async Task A_columns_add_field_previews_the_card_it_would_make_task_71ad20a0()
    {
        var core = new FakeCore()
            .AnswerOk("board", Board(Column("doing", "Doing", "status")))
            .AnswerOk("quickAddDefaults", QuickAdd.Defaults("checkbox", priority: 2, assigneeId: "me"));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");

        await view.Columns[0].LoadQuickAddDefaultsAsync();

        var asked = core.Sent.Last(json => json.Contains("\"kind\":\"quickAddDefaults\""));
        Assert.Contains("\"listId\":\"l1\"", asked);
        Assert.True(view.Columns[0].QuickAddIsCheckbox);
        Assert.False(view.Columns[0].QuickAddIsSquare);
        Assert.Equal(2, view.Columns[0].QuickAddPriority);
        Assert.Equal(3, view.Columns[0].QuickAddAssignees.Count);
    }

    /// <summary>
    /// A pick is re-asked of the core rather than worked out here (task 71ad20a0).
    /// </summary>
    /// <remarks>
    /// Which of the three leading states a pick produces is the same rule that produced the first
    /// answer — docs/ASTRID.md rule 4. Choosing a priority in a column whose board would assign
    /// the card to somebody else must not turn the square into a checkbox because the shell
    /// guessed.
    /// </remarks>
    [Fact]
    public async Task A_columns_quick_add_pick_is_re_asked_rather_than_worked_out_task_71ad20a0()
    {
        var core = new FakeCore()
            .AnswerOk("board", Board(Column("doing", "Doing", "status")))
            .AnswerOk("quickAddDefaults", QuickAdd.Defaults("checkbox", assigneeId: "me"))
            .AnswerOk("quickAddDefaults", QuickAdd.Defaults("avatar", priority: 3, assigneeId: "dana"));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");
        await view.Columns[0].LoadQuickAddDefaultsAsync();

        await view.Columns[0].PickQuickAddPriorityAsync(3);

        var asked = core.Sent.Last(json => json.Contains("\"kind\":\"quickAddDefaults\""));
        Assert.Contains("\"priority\":3", asked);
        Assert.Equal(3, view.Columns[0].QuickAddPriority);
        // The shell did not decide this: the second answer said avatar, so the preview followed it.
        Assert.True(view.Columns[0].QuickAddIsSquare);
        Assert.Equal("D", view.Columns[0].QuickAddMark);
    }

    /// <summary>
    /// Choosing nobody is sent as the sentinel, never as a null (task 71ad20a0).
    /// </summary>
    /// <remarks>
    /// A null means "nothing chosen", and the list's own default fills that back in — so a null
    /// here would make choosing nobody do nothing at all in exactly the lists where it matters.
    /// The same trap the list's field documents (task 8aa5732c).
    /// </remarks>
    [Fact]
    public async Task Choosing_nobody_for_a_column_card_sends_the_sentinel_task_71ad20a0()
    {
        var core = new FakeCore()
            .AnswerOk("board", Board(Column("doing", "Doing", "status")))
            .AnswerOk("quickAddDefaults", QuickAdd.Defaults("checkbox", assigneeId: "me"))
            .AnswerOk("quickAddDefaults", QuickAdd.Defaults("unassigned"));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");
        await view.Columns[0].LoadQuickAddDefaultsAsync();

        await view.Columns[0].PickQuickAddAssigneeAsync(null);

        var asked = core.Sent.Last(json => json.Contains("\"kind\":\"quickAddDefaults\""));
        Assert.Contains("\"assigneeId\":\"unassigned\"", asked);
    }

    /// <summary>
    /// The picks reach the created card, and only that column's card (task 71ad20a0).
    /// </summary>
    /// <remarks>
    /// Per column, not per board, and held on the board view model for the reason the drafts are:
    /// a create reloads the board and rebuilds every column view, so a pick kept on one of those
    /// would vanish from whichever column its owner was still setting up.
    /// </remarks>
    [Fact]
    public async Task A_columns_picks_reach_its_card_and_not_another_columns_task_71ad20a0()
    {
        var core = new FakeCore()
            .AnswerOk("board", Board(Column("ready", "Ready", "status"), Column("doing", "Doing", "status")))
            .AnswerOk("quickAddDefaults", QuickAdd.Defaults("checkbox", priority: 3, assigneeId: "dana"))
            .AnswerOk("addBoardCard")
            .AnswerOk("board", Board(Column("ready", "Ready", "status"), Column("doing", "Doing", "status", "Write it down")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");
        await view.Columns[1].PickQuickAddPriorityAsync(3);
        view.Columns[1].Draft = "Write it down";

        Assert.True(await view.Columns[1].AddCardAsync());

        var added = core.Sent.First(sent => sent.Contains("addBoardCard"));
        Assert.Contains("\"columnId\":\"doing\"", added);
        Assert.Contains("\"priority\":3", added);

        // The other column was never set up, so its card would carry nothing — a pick in one
        // column is not a pick in the next one along.
        Assert.Equal(0, view.Columns[0].QuickAddPriority);
    }

    // ── Done's finished-on pill (Jon, 2026-11-19: the same on every platform) ────────────────

    private static readonly System.Globalization.CultureInfo English = new("en-US");

    /// <summary>One card with the core's <c>completedOn</c>, in a column of the given kind.</summary>
    private static object ColumnWithDay(string id, string kind, string? completedOn) => new
    {
        id,
        name = id,
        description = string.Empty,
        kind,
        total = 1,
        cards = new[]
        {
            new
            {
                id = $"{id}-0",
                title = "Ship it",
                completed = completedOn is not null,
                completedOn,
                priority = 0,
                due = new { key = "none" },
                leading = new { kind = "checkbox" },
                action = "openPicker",
                listChips = Array.Empty<object>(),
            },
        },
    };

    /// <summary>The core's day decodes onto the row, whatever the column.</summary>
    [Fact]
    public async Task A_row_decodes_the_day_the_core_says_it_was_finished()
    {
        var core = new FakeCore().AnswerOk("board", Board(ColumnWithDay("__virtual_done__", "done", "2026-11-19")));
        var view = new BoardViewModel(core);

        await view.LoadAsync("l1");

        Assert.Equal("2026-11-19", view.Columns[0].Cards[0].CompletedOn);
    }

    /// <summary>A Done card says when it was finished, as a pill, in the reader's words.</summary>
    [Fact]
    public async Task A_done_card_draws_its_finished_day_as_a_pill()
    {
        var thisYear = DateTime.Now.Year;
        var core = new FakeCore().AnswerOk("board", Board(
            ColumnWithDay("__virtual_done__", "done", $"{thisYear}-11-19")));
        var view = new BoardViewModel(core);
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = English;
        try
        {
            await view.LoadAsync("l1");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }

        var card = view.Columns[0].Cards[0];
        Assert.Equal("Nov 19", card.CompletedPill);
        Assert.True(card.ShowsCompletedPill);
    }

    /// <summary>
    /// Only Done draws it: an open card has no day, and a card in any other column draws none
    /// even if it carries one.
    /// </summary>
    [Fact]
    public async Task Only_a_done_card_with_a_day_draws_a_pill()
    {
        var core = new FakeCore().AnswerOk("board", Board(
            ColumnWithDay("ready", "status", null),
            ColumnWithDay("doing", "status", "2026-11-19"),
            ColumnWithDay("__virtual_done__", "done", null)));
        var view = new BoardViewModel(core);

        await view.LoadAsync("l1");

        Assert.All(view.Columns, column =>
        {
            Assert.Null(column.Cards[0].CompletedPill);
            Assert.False(column.Cards[0].ShowsCompletedPill);
        });
    }

    /// <summary>The order is the core's — newest finished first — and the pill does not touch it.</summary>
    [Fact]
    public async Task Done_keeps_the_order_the_core_sent()
    {
        var core = new FakeCore().AnswerOk("board", Board(
            Column("__virtual_done__", "Done", "done", "Newest", "Older", "Oldest")));
        var view = new BoardViewModel(core);

        await view.LoadAsync("l1");

        Assert.Equal(["Newest", "Older", "Oldest"], view.Columns[0].Cards.Select(card => card.Title));
    }

    [Fact]
    public void The_pill_leaves_out_the_year_when_it_is_this_one()
    {
        Assert.Equal("Nov 19", CompletedDayPill.Label("2026-11-19", new DateOnly(2026, 12, 31), English));
    }

    [Fact]
    public void The_pill_says_the_year_when_it_was_another()
    {
        Assert.Equal("Nov 19, 2025", CompletedDayPill.Label("2025-11-19", new DateOnly(2026, 1, 2), English));
    }

    /// <summary>The month and the order are the reader's culture's, not English's.</summary>
    [Fact]
    public void The_pill_is_in_the_readers_culture()
    {
        var german = new System.Globalization.CultureInfo("de-DE");
        var label = CompletedDayPill.Label("2026-11-19", new DateOnly(2026, 11, 20), german);

        Assert.NotNull(label);
        Assert.StartsWith("19.", label);
        Assert.Contains("Nov", label);
        Assert.DoesNotContain("2026", label);
    }

    /// <summary>
    /// The pill's words carry a plain check before the day, the same on every platform (Jon,
    /// 2026-11-19): U+2713, never U+2714, which can draw as an emoji. A screen reader hears
    /// "Completed …" instead, so the glyph is never read out.
    /// </summary>
    [Theory]
    [InlineData("en-US", "board_card_completed_pill", "\u2713 on ")]
    [InlineData("de-DE", "board_card_completed_pill", "\u2713 am ")]
    [InlineData("en-US", "board_card_completed_on", "Completed ")]
    [InlineData("de-DE", "board_card_completed_on", "Erledigt ")]
    public void The_pill_says_a_check_and_the_day_and_a_screen_reader_hears_completed(
        string language, string name, string words)
    {
        var resw = System.Xml.Linq.XDocument.Load(
            Path.Combine(LocalisationTests.AppDirectory(), "Strings", language, "Resources.resw"));
        var value = resw.Root!.Elements("data")
            .Single(data => data.Attribute("name")!.Value == name)
            .Element("value")!.Value;

        // The words before the day, then the day; the pattern is checked in two halves so this file
        // spells no sentence the design budget would count as hand-written copy.
        Assert.StartsWith(words, value);
        Assert.EndsWith("{0}", value);
        Assert.DoesNotContain('\u2714', value);
        // The day goes where {0} is, as the label the view model built says it.
        Assert.EndsWith("Nov 19", string.Format(English, value, "Nov 19"));
    }

    /// <summary>The fallback table says the same as the English resources.</summary>
    [Fact]
    public void The_fallback_table_carries_both_pill_keys()
    {
        var source = File.ReadAllText(Path.Combine(LocalisationTests.AppDirectory(), "Strings.cs"));

        Assert.Contains("[\"board.card.completed_pill\"] = \"\\u2713 on {0}\"", source);
        Assert.Contains("[\"board.card.completed_on\"] = \"Completed {0}\"", source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a day")]
    public void No_day_is_no_pill(string? completedOn)
    {
        Assert.Null(CompletedDayPill.Label(completedOn, new DateOnly(2026, 11, 19), English));
    }
}



/// <summary>
/// <summary>
/// A tapped card opens a CALLOUT anchored to it (task eb2684bb, replacing the inline slot of
/// 91a25b8a).
/// </summary>
/// <remarks>
/// <para>
/// The board says WHICH card is open and nothing about where the callout goes — that is
/// <c>astrid_rules::rows::callout::place</c>, asked by the view with a rectangle it measured. The
/// shell hosts its one detail pane in the callout, so the board and the list still share one
/// implementation of a task.
/// </para>
/// <para>
/// <strong>This reverses task 6df01318 deliberately.</strong> That task made the expanded card be
/// REPLACED by its detail, because an inline slot drawn after the card showed the same task twice
/// — collapsed card above, detail pane below. A callout is not drawn in the list at all, and the
/// card is what it points at: removing the card would leave the arrow aimed at whatever slid up
/// into its place. So the card stays, and the duplication 6df01318 was about cannot happen, since
/// there is no second copy in the column.
/// </para>
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

    /// <summary>
    /// Opening a card leaves the column exactly as it was (task eb2684bb).
    /// </summary>
    /// <remarks>
    /// The whole of what was wrong before: an <c>InlineDetailSlot</c> took the card's place and the
    /// column grew wider to hold it, so opening a card re-laid out the board under the reader's
    /// cursor. A callout floats above, and the cards do not move.
    /// </remarks>
    [Fact]
    public async Task Opening_a_card_moves_nothing_in_the_column_task_eb2684bb()
    {
        var core = new FakeCore().AnswerOk("board", Board(Column("ready", "t1", "t2", "t3")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");

        view.Expand("t2");

        var cards = view.Columns[0].Cards;
        Assert.Equal(["t1", "t2", "t3"], cards.Select(card => card.Id));
        // The open card is still THERE — it is what the callout's arrow points at. Taking it out
        // would leave the arrow aimed at whichever card slid up into its place.
        Assert.Contains(cards, card => card.Id == "t2");
    }

    /// <summary>
    /// The column names the card the callout is anchored to (task eb2684bb).
    /// </summary>
    /// <remarks>
    /// The id rather than a flag, because the view has to find that card's element to measure it:
    /// the core is given a rectangle, and where a row sits on screen is geometry no view model can
    /// know.
    /// </remarks>
    [Fact]
    public async Task The_column_names_the_card_the_callout_is_anchored_to_task_eb2684bb()
    {
        var core = new FakeCore().AnswerOk("board", Board(Column("ready", "t1", "t2", "t3")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");

        view.Expand("t2");

        Assert.Equal("t2", view.ExpandedTaskId);
        Assert.Equal("t2", view.Columns[0].ExpandedTaskId);
        Assert.True(view.Columns[0].HoldsExpandedCard);
        Assert.Equal(3, view.Columns[0].Cards.Count);
    }

    [Fact]
    public async Task Collapsing_leaves_nothing_anchored_task_eb2684bb()
    {
        var core = new FakeCore().AnswerOk("board", Board(Column("ready", "t1", "t2")));
        var view = new BoardViewModel(core);
        await view.LoadAsync("l1");
        view.Expand("t1");

        view.Collapse();

        Assert.Null(view.ExpandedTaskId);
        Assert.Null(view.Columns[0].ExpandedTaskId);
        Assert.False(view.Columns[0].HoldsExpandedCard);
        Assert.Equal(2, view.Columns[0].Cards.Count);
    }

    /// <summary>
    /// A refresh — a sync, a colleague's edit, the open task's own title being changed — must not
    /// lose the expansion. The callout follows the card wherever the reload puts it
    /// (task eb2684bb).
    /// </summary>
    /// <remarks>
    /// <c>HoldsExpandedCard</c> is computed from the cards the column actually has rather than set
    /// while building them, so a card that moved columns takes its callout with it without the
    /// board being told twice.
    /// </remarks>
    [Fact]
    public async Task A_refresh_keeps_the_callout_with_its_card_even_when_the_card_moves_task_eb2684bb()
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
        Assert.Equal("t2", view.Columns[1].Cards[0].Id);
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
