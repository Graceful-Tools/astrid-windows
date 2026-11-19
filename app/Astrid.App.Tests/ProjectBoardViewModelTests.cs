using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The multi-list project board: each chosen list is a row, and the project's statuses stay the
/// columns (task f8b844dd; core AWTD-1676, web AWTD-1660).
/// </summary>
/// <remarks>
/// <para>
/// As with the single-list board, this view decides nothing. Which lists are rows, which could be
/// added, which column a card is in and whether another row would fit are all
/// <c>astrid_rules::board_rows</c>, locked there against astrid-web's own implementation by the
/// <c>board-rows.json</c> fixture. What is tested here is that the shell asks the right questions
/// and does not quietly answer any of them itself.
/// </para>
/// <para>
/// The distinction these tests exist for most is the one the core takes care to make and a shell
/// is most likely to collapse: nothing saved is not an empty set. Both draw no swimlanes.
/// </para>
/// </remarks>
public sealed class ProjectBoardViewModelTests
{
    private static object Card(string id, string title) => new
    {
        id,
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
    };

    private static object Column(string id, string name, string kind, params string[] cards) => new
    {
        id,
        name,
        description = string.Empty,
        kind,
        total = cards.Length,
        cards = cards.Select((title, index) => Card($"{id}-{index}", title)).ToArray(),
    };

    /// <summary>A row's answer: that list's cards under the project's columns.</summary>
    private static object Row(params object[] columns) => new { projectId = "p1", columns };

    /// <summary>
    /// What the core answers for the rows themselves.
    /// </summary>
    /// <param name="saved">
    /// What the server holds. <c>null</c> is nothing saved — which is not the same answer as an
    /// empty array, and the reason this is written out rather than inferred from
    /// <paramref name="rows"/>.
    /// </param>
    private static object Rows(string[]? saved, string[] rows, string[] candidates,
        bool canAddRow = true, bool fromCache = false) => new
        {
            projectId = "p1",
            savedListIds = saved,
            rowListIds = rows,
            candidateListIds = candidates,
            canAddRow,
            fromCache,
        };

    private static object List(string id, string name, string? projectId = "p1") =>
        new { id, name, projectId };

    /// <summary>The lists the shell resolves row and candidate names against.</summary>
    private static object[] Lists() =>
    [
        List("web", "Web"),
        List("ios", "iOS"),
        List("groceries", "Groceries", projectId: null),
        List("other", "Other", projectId: "p2"),
    ];

    private static FakeCore Core(object rows, params object[] rowAnswers)
    {
        var core = new FakeCore()
            .AnswerOk("lists", Lists())
            .AnswerOk("projectBoardRows", rows);
        foreach (var answer in rowAnswers)
        {
            core.AnswerOk("projectBoardRow", answer);
        }
        return core;
    }

    [Fact]
    public async Task Opening_a_project_board_draws_a_row_per_list_the_core_named()
    {
        var core = Core(
            Rows(saved: ["web", "ios"], rows: ["web", "ios"], candidates: ["groceries"]),
            Row(Column("ready", "Ready", "status", "Ship it")),
            Row(Column("ready", "Ready", "status")));
        var view = new ProjectBoardViewModel(core);

        await view.LoadAsync("p1");

        Assert.Equal(["web", "ios"], view.Rows.Select(row => row.ListId));
        // The row header says the list's name, which the ids alone cannot.
        Assert.Equal(["Web", "iOS"], view.Rows.Select(row => row.Name));
    }

    /// <summary>
    /// The rows are drawn in the order the core gave, not in the order the lists happen to be
    /// cached in: which lists are rows and in what order is <c>board_rows::resolve</c>.
    /// </summary>
    [Fact]
    public async Task The_rows_keep_the_order_the_core_sent_f8b844dd()
    {
        var core = Core(
            Rows(saved: ["ios", "web"], rows: ["ios", "web"], candidates: []),
            Row(Column("ready", "Ready", "status")),
            Row(Column("ready", "Ready", "status")));
        var view = new ProjectBoardViewModel(core);

        await view.LoadAsync("p1");

        Assert.Equal(["ios", "web"], view.Rows.Select(row => row.ListId));
    }

    [Fact]
    public async Task Each_row_draws_the_projects_columns_with_its_own_cards()
    {
        var core = Core(
            Rows(saved: ["web", "ios"], rows: ["web", "ios"], candidates: []),
            Row(
                Column("__virtual_inbox__", "Inbox", "inbox", "Write it down"),
                Column("ready", "Ready", "status"),
                Column("__virtual_done__", "Done", "done")),
            Row(
                Column("__virtual_inbox__", "Inbox", "inbox"),
                Column("ready", "Ready", "status", "Port it"),
                Column("__virtual_done__", "Done", "done")));
        var view = new ProjectBoardViewModel(core);

        await view.LoadAsync("p1");

        Assert.Equal(3, view.Rows[0].Columns.Count);
        Assert.Equal("Write it down", view.Rows[0].Columns[0].Cards[0].Title);
        Assert.Empty(view.Rows[0].Columns[1].Cards);
        Assert.Equal("Port it", view.Rows[1].Columns[1].Cards[0].Title);
        // Every row carries the same columns, so the board has one header strip above the lot.
        Assert.Equal(["Inbox", "Ready", "Done"], view.Columns.Select(column => column.Name));
    }

    /// <summary>
    /// A row asks for its own list, not for the project's: one call per swimlane, each naming the
    /// list whose cards it draws.
    /// </summary>
    [Fact]
    public async Task One_row_is_one_call_naming_that_rows_list_f8b844dd()
    {
        var core = Core(
            Rows(saved: ["web", "ios"], rows: ["web", "ios"], candidates: []),
            Row(Column("ready", "Ready", "status")),
            Row(Column("ready", "Ready", "status")));
        var view = new ProjectBoardViewModel(core);

        await view.LoadAsync("p1");

        var asked = core.Sent.Where(json => json.Contains("\"projectBoardRow\"")).ToList();
        Assert.Equal(2, asked.Count);
        Assert.Contains("\"listId\":\"web\"", asked[0]);
        Assert.Contains("\"listId\":\"ios\"", asked[1]);
        Assert.All(asked, json => Assert.Contains("\"projectId\":\"p1\"", json));
    }

    // ── Nothing saved is not an empty set ───────────────────────────────────────────────────────

    /// <summary>
    /// The trap the core is careful about and a shell is likeliest to collapse: both of these
    /// boards draw no swimlanes, and they are not the same board.
    /// </summary>
    /// <remarks>
    /// Nothing saved means the project's own lists, so a board with nothing saved AND no rows is a
    /// project with no lists in it yet — there is nothing to set up and nothing was removed. An
    /// empty saved set is a board somebody emptied on purpose, and the only thing to say about it
    /// is how to put a list back. A shell that kept only <c>rowListIds</c> would have one notice
    /// for both, which is how an emptied board comes to read as a broken one.
    /// </remarks>
    [Fact]
    public async Task Nothing_saved_and_a_board_emptied_on_purpose_are_different_boards_f8b844dd()
    {
        var untouched = new FakeCore()
            .AnswerOk("lists", Lists())
            .AnswerOk("projectBoardRows",
                Rows(saved: null, rows: [], candidates: ["web", "ios"]));
        var untouchedView = new ProjectBoardViewModel(untouched);

        var emptied = new FakeCore()
            .AnswerOk("lists", Lists())
            .AnswerOk("projectBoardRows",
                Rows(saved: [], rows: [], candidates: ["web", "ios"]));
        var emptiedView = new ProjectBoardViewModel(emptied);

        await untouchedView.LoadAsync("p1");
        await emptiedView.LoadAsync("p1");

        Assert.Empty(untouchedView.Rows);
        Assert.Empty(emptiedView.Rows);
        Assert.False(untouchedView.IsEmptiedOnPurpose);
        Assert.True(emptiedView.IsEmptiedOnPurpose);
        // And the two say different things, which is the point of keeping the distinction at all.
        Assert.NotEqual(untouchedView.ShowsEmptiedNotice, emptiedView.ShowsEmptiedNotice);
    }

    /// <summary>
    /// A board with rows says neither notice, whichever way its rows were arrived at.
    /// </summary>
    [Fact]
    public async Task A_board_with_rows_says_nothing_about_being_empty_f8b844dd()
    {
        var core = Core(
            Rows(saved: null, rows: ["web"], candidates: ["ios"]),
            Row(Column("ready", "Ready", "status")));
        var view = new ProjectBoardViewModel(core);

        await view.LoadAsync("p1");

        Assert.Single(view.Rows);
        Assert.False(view.ShowsEmptiedNotice);
        Assert.False(view.ShowsNoRowsNotice);
    }

    // ── Choosing the rows ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_list_offers_the_candidates_the_core_named_by_name()
    {
        var core = Core(
            Rows(saved: ["web"], rows: ["web"], candidates: ["groceries", "ios", "other"]),
            Row(Column("ready", "Ready", "status")));
        var view = new ProjectBoardViewModel(core);

        await view.LoadAsync("p1");

        // The core ordered them; the shell names them and does not re-sort.
        Assert.Equal(["groceries", "ios", "other"], view.AddListCandidates.Select(list => list.Id));
        Assert.Equal(["Groceries", "iOS", "Other"], view.AddListCandidates.Select(list => list.Name));
        Assert.True(view.CanAddRow);
    }

    /// <summary>
    /// A hundred rows is the core's ceiling, and the shell stops offering rather than offering a
    /// control whose only outcome is the core's refusal.
    /// </summary>
    [Fact]
    public async Task A_full_board_stops_offering_add_list_f8b844dd()
    {
        var core = Core(
            Rows(saved: ["web"], rows: ["web"], candidates: ["ios"], canAddRow: false),
            Row(Column("ready", "Ready", "status")));
        var view = new ProjectBoardViewModel(core);

        await view.LoadAsync("p1");

        Assert.False(view.CanAddRow);
    }

    /// <summary>
    /// Adding a row sends <c>addListId</c> and lets the core do the arithmetic, rather than
    /// reading the set, appending here and writing it back — which would lose a row somebody else
    /// added in between.
    /// </summary>
    [Fact]
    public async Task Adding_a_row_sends_the_one_list_not_the_whole_set_f8b844dd()
    {
        var core = Core(
            Rows(saved: ["web"], rows: ["web"], candidates: ["ios"]),
            Row(Column("ready", "Ready", "status")));
        core.AnswerOk("saveProjectBoardRows",
                Rows(saved: ["web", "ios"], rows: ["web", "ios"], candidates: []))
            .AnswerOk("projectBoardRow", Row(Column("ready", "Ready", "status")))
            .AnswerOk("projectBoardRow", Row(Column("ready", "Ready", "status")));
        var view = new ProjectBoardViewModel(core);
        await view.LoadAsync("p1");

        Assert.True(await view.AddRowAsync("ios"));

        var save = core.Sent.Last(json => json.Contains("\"saveProjectBoardRows\""));
        Assert.Contains("\"addListId\":\"ios\"", save);
        Assert.DoesNotContain("\"listIds\"", save);
        Assert.DoesNotContain("\"removeListId\"", save);
        // The write answers with the new state, and the board is drawn from it.
        Assert.Equal(["web", "ios"], view.Rows.Select(row => row.ListId));
    }

    /// <summary>The cross on a row header, which is a removal and not a save of what is left.</summary>
    [Fact]
    public async Task Removing_a_row_sends_the_one_list_not_the_whole_set_f8b844dd()
    {
        var core = Core(
            Rows(saved: ["web", "ios"], rows: ["web", "ios"], candidates: []),
            Row(Column("ready", "Ready", "status")),
            Row(Column("ready", "Ready", "status")));
        core.AnswerOk("saveProjectBoardRows",
                Rows(saved: ["ios"], rows: ["ios"], candidates: ["web"]))
            .AnswerOk("projectBoardRow", Row(Column("ready", "Ready", "status")));
        var view = new ProjectBoardViewModel(core);
        await view.LoadAsync("p1");

        Assert.True(await view.Rows[0].RemoveAsync());

        var save = core.Sent.Last(json => json.Contains("\"saveProjectBoardRows\""));
        Assert.Contains("\"removeListId\":\"web\"", save);
        Assert.DoesNotContain("\"listIds\"", save);
        Assert.Equal(["ios"], view.Rows.Select(row => row.ListId));
    }

    /// <summary>
    /// Emptying the board on purpose sends <c>[]</c>, not nothing at all.
    /// </summary>
    /// <remarks>
    /// The serializer drops nulls, so a set sent as a null would arrive as a write naming none of
    /// the three things it could do — the core refuses that — and if it did not, "no listIds" is
    /// how this shell would ask for the project's own lists back. The empty array is the write
    /// that means empty.
    /// </remarks>
    [Fact]
    public async Task Emptying_the_board_sends_an_empty_array_rather_than_nothing_f8b844dd()
    {
        var core = Core(
            Rows(saved: ["web"], rows: ["web"], candidates: ["ios"]),
            Row(Column("ready", "Ready", "status")));
        core.AnswerOk("saveProjectBoardRows", Rows(saved: [], rows: [], candidates: ["web", "ios"]));
        var view = new ProjectBoardViewModel(core);
        await view.LoadAsync("p1");

        Assert.True(await view.SetRowsAsync([]));

        var save = core.Sent.Last(json => json.Contains("\"saveProjectBoardRows\""));
        Assert.Contains("\"listIds\":[]", save);
        Assert.Empty(view.Rows);
        // And it is now an emptied board, not an untouched one.
        Assert.True(view.IsEmptiedOnPurpose);
    }

    /// <summary>
    /// The core refuses a save the server would refuse — more than a hundred, or an empty id — and
    /// the shell shows its words rather than a sentence of its own.
    /// </summary>
    [Fact]
    public async Task A_refused_save_is_shown_in_the_cores_words_f8b844dd()
    {
        var core = Core(
            Rows(saved: ["web"], rows: ["web"], candidates: ["ios"]),
            Row(Column("ready", "Ready", "status")));
        core.AnswerFailure("saveProjectBoardRows", AstridFailureKind.BadRequest,
            "listIds must be an array of at most 100 list ids");
        var view = new ProjectBoardViewModel(core);
        await view.LoadAsync("p1");

        Assert.False(await view.AddRowAsync("ios"));

        Assert.Equal("listIds must be an array of at most 100 list ids", view.ErrorMessage);
        // The refused write changed nothing, so the board is as it was.
        Assert.Equal(["web"], view.Rows.Select(row => row.ListId));
    }

    /// <summary>
    /// Saving rows is online-only in the core — it is a view preference the server checks against
    /// what this person can see — so an offline attempt is a failure with words, not a silent
    /// Outbox write the board would then draw as though it had happened.
    /// </summary>
    [Fact]
    public async Task Choosing_rows_offline_says_so_f8b844dd()
    {
        var core = Core(
            Rows(saved: ["web"], rows: ["web"], candidates: ["ios"]),
            Row(Column("ready", "Ready", "status")));
        core.AnswerFailure("saveProjectBoardRows", AstridFailureKind.Offline, "no network");
        var view = new ProjectBoardViewModel(core);
        await view.LoadAsync("p1");

        Assert.False(await view.AddRowAsync("ios"));

        Assert.Equal("no network", view.ErrorMessage);
        Assert.Equal(["web"], view.Rows.Select(row => row.ListId));
    }

    // ── Moving a card ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A card dragged between columns on a row moves against the PROJECT's columns, which is what
    /// the list id in the move is for.
    /// </summary>
    [Fact]
    public async Task A_card_moves_against_the_projects_columns_f8b844dd()
    {
        var core = Core(
            Rows(saved: ["web"], rows: ["web"], candidates: []),
            Row(Column("ready", "Ready", "status", "Ship it")));
        core.AnswerOk("moveTaskToColumn")
            .AnswerOk("projectBoardRows", Rows(saved: ["web"], rows: ["web"], candidates: []))
            .AnswerOk("projectBoardRow", Row(Column("doing", "Doing", "status", "Ship it")));
        var view = new ProjectBoardViewModel(core);
        await view.LoadAsync("p1");

        Assert.True(await view.MoveAsync("ready-0", "doing", "web"));

        var move = core.Sent.Last(json => json.Contains("\"moveTaskToColumn\""));
        Assert.Contains("\"taskId\":\"ready-0\"", move);
        Assert.Contains("\"columnId\":\"doing\"", move);
        Assert.Contains("\"listId\":\"web\"", move);
    }

    /// <summary>
    /// A row whose list belongs to another project — which the core allows, drawing it under THIS
    /// project's columns (D67) — still moves against this project's columns.
    /// </summary>
    /// <remarks>
    /// The core resolves the move's columns from the project of the list id it is given, so
    /// sending the row's own foreign list id would resolve the OTHER project's columns and the
    /// target column would not be among them. The move therefore names a list of this project.
    /// Which list the card is in does not change, and is not what the id is for.
    /// </remarks>
    [Fact]
    public async Task A_row_from_another_project_still_moves_against_this_board_f8b844dd()
    {
        var core = Core(
            Rows(saved: ["other"], rows: ["other"], candidates: []),
            Row(Column("ready", "Ready", "status", "Borrowed")));
        core.AnswerOk("moveTaskToColumn")
            .AnswerOk("projectBoardRows", Rows(saved: ["other"], rows: ["other"], candidates: []))
            .AnswerOk("projectBoardRow", Row(Column("doing", "Doing", "status", "Borrowed")));
        var view = new ProjectBoardViewModel(core);
        await view.LoadAsync("p1");

        Assert.True(await view.MoveAsync("ready-0", "doing", "other"));

        var move = core.Sent.Last(json => json.Contains("\"moveTaskToColumn\""));
        Assert.DoesNotContain("\"listId\":\"other\"", move);
        // "web" is this project's own list, which is what makes the columns resolve to this board's.
        Assert.Contains("\"listId\":\"web\"", move);
    }

    /// <summary>
    /// A move into a column that needs a condition asks for one rather than reporting a failure,
    /// as the single-list board does — nothing was written and the card never left its column.
    /// </summary>
    [Fact]
    public async Task A_move_into_waiting_asks_for_its_condition_f8b844dd()
    {
        var core = Core(
            Rows(saved: ["web"], rows: ["web"], candidates: []),
            Row(Column("waiting", "Waiting", "status", "Ship it")));
        core.Answer("moveTaskToColumn",
            "{\"ok\":false,\"error\":{\"kind\":\"badRequest\",\"code\":\"WAITING_NEEDS_CONDITION\","
            + "\"message\":\"needs a condition\"}}");
        var view = new ProjectBoardViewModel(core);
        await view.LoadAsync("p1");

        Assert.False(await view.MoveAsync("waiting-0", "waiting", "web"));

        Assert.NotNull(view.WaitPrompt);
        Assert.Equal("waiting", view.WaitPrompt!.ColumnId);
        // Named as the board calls it, not as a hardcoded word.
        Assert.Equal("Waiting", view.WaitPrompt.ColumnName);
        Assert.Null(view.ErrorMessage);
    }

    // ── Reading it at all ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A board read offline is the last answer, which is a board worth drawing — the core says so
    /// with <c>fromCache</c> and this keeps the word rather than treating it as a failure.
    /// </summary>
    [Fact]
    public async Task A_board_read_from_the_cache_is_still_a_board_f8b844dd()
    {
        var core = Core(
            Rows(saved: ["web"], rows: ["web"], candidates: [], fromCache: true),
            Row(Column("ready", "Ready", "status", "Ship it")));
        var view = new ProjectBoardViewModel(core);

        await view.LoadAsync("p1");

        Assert.True(view.IsFromCache);
        Assert.Single(view.Rows);
        Assert.Null(view.ErrorMessage);
    }

    /// <summary>A read that failed outright draws no rows and says why.</summary>
    [Fact]
    public async Task A_board_that_cannot_be_read_says_so_and_draws_nothing()
    {
        var core = new FakeCore()
            .AnswerOk("lists", Lists())
            .AnswerFailure("projectBoardRows", AstridFailureKind.Refused, "not yours");
        var view = new ProjectBoardViewModel(core);

        await view.LoadAsync("p1");

        Assert.Empty(view.Rows);
        Assert.Equal("not yours", view.ErrorMessage);
        Assert.False(view.IsLoading);
    }

    /// <summary>
    /// A row whose list is not in the cache is still a row: the core said to draw it, and the id
    /// is a name of sorts. Dropping it would be the shell overruling the rule.
    /// </summary>
    [Fact]
    public async Task A_row_whose_list_is_not_cached_is_still_drawn_f8b844dd()
    {
        var core = Core(
            Rows(saved: ["stranger"], rows: ["stranger"], candidates: []),
            Row(Column("ready", "Ready", "status")));
        var view = new ProjectBoardViewModel(core);

        await view.LoadAsync("p1");

        Assert.Single(view.Rows);
        Assert.Equal("stranger", view.Rows[0].ListId);
        Assert.Equal("stranger", view.Rows[0].Name);
    }

    // ── Done's finished-on pill on a swimlane (Jon, 2026-11-19: the same on every platform) ──

    private static object CardDoneOn(string id, string? completedOn) => new
    {
        id,
        title = "Ship it",
        completed = completedOn is not null,
        completedOn,
        priority = 0,
        due = new { key = "none" },
        leading = new { kind = "checkbox" },
        action = "openPicker",
        listChips = Array.Empty<object>(),
    };

    private static object ColumnOf(string id, string kind, params object[] cards) => new
    {
        id,
        name = id,
        description = string.Empty,
        kind,
        total = cards.Length,
        cards,
    };

    /// <summary>
    /// A swimlane's Done cell draws the same pill the single-list board does; no other cell does,
    /// and an open card in Done has no day to show.
    /// </summary>
    [Fact]
    public async Task A_swimlane_done_card_draws_its_finished_day_as_a_pill()
    {
        var thisYear = DateTime.Now.Year;
        var core = Core(
            Rows(saved: ["web"], rows: ["web"], candidates: []),
            Row(
                ColumnOf("ready", "status", CardDoneOn("r1", $"{thisYear}-11-19")),
                ColumnOf("__virtual_done__", "done",
                    CardDoneOn("d1", $"{thisYear}-11-19"),
                    CardDoneOn("d2", "2025-03-04"),
                    CardDoneOn("d3", null))));
        var view = new ProjectBoardViewModel(core);
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
        try
        {
            await view.LoadAsync("p1");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }

        var ready = view.Rows[0].Columns[0].Cards;
        var done = view.Rows[0].Columns[1].Cards;
        Assert.Null(ready[0].CompletedPill);
        Assert.False(ready[0].ShowsCompletedPill);
        // In the core's order, untouched: the pill is added, nothing is re-sorted.
        Assert.Equal(["d1", "d2", "d3"], done.Select(card => card.Id));
        Assert.Equal("Nov 19", done[0].CompletedPill);
        Assert.Equal("Mar 4, 2025", done[1].CompletedPill);
        Assert.True(done[1].ShowsCompletedPill);
        Assert.Null(done[2].CompletedPill);
        Assert.False(done[2].ShowsCompletedPill);
    }
}
