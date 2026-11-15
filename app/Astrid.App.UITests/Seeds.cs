using Microsoft.Data.Sqlite;

namespace Astrid.App.UITests;

/// <summary>
/// Rows for the cache that the app cannot make offline.
/// </summary>
/// <remarks>
/// Written the way the core reads them: every table keeps the object as JSON in its
/// <c>json</c> column and a few fields beside it for the indexes, and the JSON is the wire shape
/// with every field optional. So a project, a list on it and a task in the list are three short
/// documents and one membership row.
/// </remarks>
internal static class Seeds
{
    internal const string BoardListName = "Launch";
    internal const string BoardCardTitle = "Write the announcement";

    /// <summary>
    /// A list that has a board, with one card in its Inbox. The columns are the three every
    /// board shares, which the core draws without any server configuration.
    /// </summary>
    internal static void ListWithBoard(string cachePath)
    {
        using var connection = new SqliteConnection($"Data Source={cachePath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();

        Execute(connection,
            "INSERT OR REPLACE INTO projects (id, name, json) VALUES ('p1', $name, $json)",
            ("$name", BoardListName),
            ("$json", $$"""{"id":"p1","name":"{{BoardListName}}"}"""));
        Execute(connection,
            "INSERT OR REPLACE INTO lists (id, name, project_id, json) VALUES ('l1', $name, 'p1', $json)",
            ("$name", BoardListName),
            ("$json", $$"""{"id":"l1","name":"{{BoardListName}}","projectId":"p1"}"""));
        Execute(connection,
            "INSERT OR REPLACE INTO tasks (id, title, json) VALUES ('t1', $title, $json)",
            ("$title", BoardCardTitle),
            ("$json", $$"""{"id":"t1","title":"{{BoardCardTitle}}","lists":[{"id":"l1","name":"{{BoardListName}}"}]}"""));
        Execute(connection,
            "INSERT OR REPLACE INTO task_lists_membership (task_id, list_id) VALUES ('t1', 'l1')");

        transaction.Commit();
    }

    internal const string MineCardTitle = "Draft the invitations";
    internal const string TheirsCardTitle = "Book the room";
    internal const string RepeatingCardTitle = "Water the plants";

    /// <summary>
    /// A board whose cards cover every mark this harness can produce (task e17707a2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// PRODUCT_CONTRACT.md §4 gives three answers to "whose task is this?": mine, with a checkbox;
    /// somebody else's, as their initial in a priority-coloured square; nobody's, as the unassigned
    /// mark in the same square. This seeds the two SQUARE answers, at two priorities each, plus the
    /// repeating square — which is built out of controls rather than drawn as one image, and so had
    /// nowhere to say "repeats" until task <c>fa63a5dd</c>.
    /// </para>
    /// <para>
    /// All three, which needs a signed-in user: the credential file the launcher writes carries
    /// only a cookie, so <c>account.current-user</c> has to be in the cache's metadata or the app
    /// is signed in as nobody and every card comes back a square however it is assigned. That row
    /// is what makes the fourth card the reader's own, and so a checkbox.
    /// </para>
    /// </remarks>
    internal static void BoardWithEveryMark(string cachePath)
    {
        using var connection = new SqliteConnection($"Data Source={cachePath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();

        Execute(connection,
            "INSERT OR REPLACE INTO projects (id, name, json) VALUES ('p1', $name, $json)",
            ("$name", BoardListName),
            ("$json", $$"""{"id":"p1","name":"{{BoardListName}}"}"""));
        Execute(connection,
            "INSERT OR REPLACE INTO lists (id, name, project_id, json) VALUES ('l1', $name, 'p1', $json)",
            ("$name", BoardListName),
            ("$json", $$"""{"id":"l1","name":"{{BoardListName}}","projectId":"p1"}"""));

        // Without this the app is signed in as nobody, and no card can be the reader's however it
        // is assigned — the credential file the launcher writes carries only a cookie.
        Execute(connection,
            "INSERT OR REPLACE INTO metadata (key, value) VALUES ('account.current-user', $json)",
            ("$json", $$"""{"id":"{{OwnerId}}","name":"Smoke Tester","email":"smoke@example.com"}"""));

        // Nobody's: no assignee at all, which is what a card typed into a column of a list whose
        // defaultAssigneeId is "unassigned" becomes.
        AddBoardCard(connection, "t1", BoardCardTitle, assignee: null, priority: 0, repeating: false);
        // Somebody else's: their initial in the square.
        AddBoardCard(connection, "t2", TheirsCardTitle, assignee: "Dana", priority: 2, repeating: false);
        // Somebody else's AND repeating: the arrows are the square's border.
        AddBoardCard(connection, "t3", RepeatingCardTitle, assignee: "Dana", priority: 1, repeating: true);
        // Mine: a checkbox, which is a different control from the other three rather than the same
        // square in another colour. Priority 3, so the first card (priority 0) and this one sit at
        // both ends of the range whose default sentinel caused the original blank.
        AddBoardCard(connection, "t4", MineCardTitle, assignee: OwnerId, priority: 3, repeating: false);

        transaction.Commit();
    }

    /// <summary>One card on the board's list, with whatever makes its mark.</summary>
    private static void AddBoardCard(
        SqliteConnection connection, string id, string title, string? assignee, int priority, bool repeating)
    {
        // Built by concatenation rather than as a raw string: these are fragments of JSON made of
        // little but quotes, and a raw string holding them needs more quote characters in its
        // fence than it does content.
        var quote = "\"";
        // assigneeId AND assignee: the first is what the core compares against the signed-in user
        // to decide whose task this is, the second is what a square draws an initial from.
        var name = assignee == OwnerId ? "Smoke Tester" : assignee;
        var who = assignee is null
            ? string.Empty
            : ",\"assigneeId\":" + quote + assignee + quote
                + ",\"assignee\":{\"id\":" + quote + assignee + quote
                + ",\"name\":" + quote + name + quote + "}";
        var repeat = repeating ? ",\"repeating\":\"weekly\"" : string.Empty;
        var json = "{\"id\":" + quote + id + quote
            + ",\"title\":" + quote + title + quote
            + ",\"priority\":" + priority
            + who + repeat
            + ",\"lists\":[{\"id\":\"l1\",\"name\":" + quote + BoardListName + quote + "}]}";
        Execute(connection,
            "INSERT OR REPLACE INTO tasks (id, title, json) VALUES ($id, $title, $json)",
            ("$id", id),
            ("$title", title),
            ("$json", json));
        Execute(connection,
            "INSERT OR REPLACE INTO task_lists_membership (task_id, list_id) VALUES ($id, 'l1')",
            ("$id", id));
    }

    internal const string HandSortedListName = "Errands";
    internal const string FirstErrand = "Post the parcel";
    internal const string SecondErrand = "Return the library books";

    /// <summary>
    /// A list sorted by hand, with two tasks and no arrangement yet — so they draw newest first,
    /// the second errand above the first, and a drag can put them the other way round.
    /// </summary>
    internal static void ListSortedByHand(string cachePath)
    {
        using var connection = new SqliteConnection($"Data Source={cachePath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();

        Execute(connection,
            "INSERT OR REPLACE INTO lists (id, name, json) VALUES ('l2', $name, $json)",
            ("$name", HandSortedListName),
            ("$json", $$"""{"id":"l2","name":"{{HandSortedListName}}","sortBy":"manual"}"""));
        Execute(connection,
            "INSERT OR REPLACE INTO tasks (id, title, created_at, json) VALUES ('e1', $title, '2026-01-01T00:00:00Z', $json)",
            ("$title", FirstErrand),
            ("$json", $$"""{"id":"e1","title":"{{FirstErrand}}","createdAt":"2026-01-01T00:00:00Z","lists":[{"id":"l2","name":"{{HandSortedListName}}"}]}"""));
        Execute(connection,
            "INSERT OR REPLACE INTO tasks (id, title, created_at, json) VALUES ('e2', $title, '2026-01-02T00:00:00Z', $json)",
            ("$title", SecondErrand),
            ("$json", $$"""{"id":"e2","title":"{{SecondErrand}}","createdAt":"2026-01-02T00:00:00Z","lists":[{"id":"l2","name":"{{HandSortedListName}}"}]}"""));
        Execute(connection,
            "INSERT OR REPLACE INTO task_lists_membership (task_id, list_id) VALUES ('e1', 'l2')");
        Execute(connection,
            "INSERT OR REPLACE INTO task_lists_membership (task_id, list_id) VALUES ('e2', 'l2')");

        transaction.Commit();
    }

    internal const string PriorityListName = "Priorities";

    /// <summary>The four titles, indexed by the priority each one carries.</summary>
    internal static readonly string[] PriorityTitles =
    [
        "Unmarked errand",
        "Low errand",
        "Medium errand",
        "High errand",
    ];

    private const string OwnerId = "u-smoke";

    /// <summary>
    /// A list with one task at each of the four priorities, in a chosen appearance (task 82e4ad32).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <paramref name="assigned"/> chooses which of the two marks the rows wear, because
    /// <c>LeadingControl::for_task</c> decides it from the assignee: the signed-in user's task
    /// leads with the checkbox, and a task belonging to nobody leads with the square instead. Both
    /// are drawn from the same four priority colours and neither can stand in for the other — the
    /// checkbox is one piece of art in the control's foreground, the square an outline with a glyph
    /// inside it — so each is seeded and read.
    /// </para>
    /// <para>
    /// Unassigned was unseedable until task f1a8ff9f: the square was filled with the priority
    /// colour, no priority painted it transparent, and the glyph on top was a literal white, so an
    /// unassigned row with no priority drew nothing at all and a quarter of the thing under test
    /// was invisible. The square is a priority outline now, as astrid-web has always drawn it, and
    /// every priority including 0 is a colour.
    /// </para>
    /// <para>
    /// Who is signed in is <c>account.current-user</c> in the cache's metadata, which is where
    /// <c>AccountService</c> reads it from; the credential file the launcher writes only carries a
    /// cookie, so without this row the app is signed in as nobody. The appearance is
    /// <c>theme.mode</c> in the same table — a per-machine setting the core keeps, not a shell one.
    /// </para>
    /// <para>
    /// Named rather than numbered, because the test finds each row by the title a screen reader
    /// would read and then looks to its left for the mark. The order they draw in does not matter
    /// for that, so nothing here arranges them.
    /// </para>
    /// </remarks>
    internal static Action<string> ListWithEveryPriority(string theme, bool assigned = true) => cachePath =>
    {
        using var connection = new SqliteConnection($"Data Source={cachePath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();

        Execute(connection,
            "INSERT OR REPLACE INTO metadata (key, value) VALUES ('account.current-user', $json)",
            ("$json", $$"""{"id":"{{OwnerId}}","name":"Smoke Tester","email":"smoke@example.com"}"""));
        Execute(connection,
            "INSERT OR REPLACE INTO metadata (key, value) VALUES ('theme.mode', $theme)",
            ("$theme", theme));

        Execute(connection,
            "INSERT OR REPLACE INTO lists (id, name, json) VALUES ('l3', $name, $json)",
            ("$name", PriorityListName),
            ("$json", $$"""{"id":"l3","name":"{{PriorityListName}}"}"""));
        for (var priority = 0; priority < PriorityTitles.Length; priority++)
        {
            var id = $"p{priority}";
            var title = PriorityTitles[priority];
            var owner = assigned
                ? $$"""
                    "assigneeId":"{{OwnerId}}","assignee":{"id":"{{OwnerId}}","name":"Smoke Tester"},
                    """
                : string.Empty;
            Execute(connection,
                "INSERT OR REPLACE INTO tasks (id, title, json) VALUES ($id, $title, $json)",
                ("$id", id),
                ("$title", title),
                ("$json", $$"""
                    {"id":"{{id}}","title":"{{title}}","priority":{{priority}},{{owner}}"lists":[{"id":"l3","name":"{{PriorityListName}}"}]}
                    """));
            Execute(connection,
                "INSERT OR REPLACE INTO task_lists_membership (task_id, list_id) VALUES ($id, 'l3')",
                ("$id", id));
        }

        // A conversation on the list, so the chat pane has bubbles to draw rather than "no
        // conversation yet" (task 770625f8). One message from the reader and one from somebody
        // else: the two grounds are different brushes, and the bug was that BOTH came out of the
        // light dictionary however dark the window was.
        Execute(connection,
            "INSERT OR REPLACE INTO chat_channels (id, list_id, json) VALUES ('c1', 'l3', $json)",
            ("$json", """{"id":"c1","listId":"l3"}"""));
        Execute(connection,
            "INSERT OR REPLACE INTO users (id, json) VALUES ('u-other', $json)",
            ("$json", """{"id":"u-other","name":"Ada"}"""));
        var said = new[]
        {
            ("m1", OwnerId, "Smoke Tester", MineSaid, "2026-11-14T10:00:00.000Z"),
            ("m2", "u-other", "Ada", TheirsSaid, "2026-11-14T10:01:00.000Z"),
        };
        foreach (var (id, author, name, content, at) in said)
        {
            Execute(connection,
                "INSERT OR REPLACE INTO chat_messages (id, channel_id, author_id, created_at, json)"
                + " VALUES ($id, 'c1', $author, $at, $json)",
                ("$id", id),
                ("$author", author),
                ("$at", at),
                ("$json", $$"""
                    {"id":"{{id}}","channelId":"c1","authorId":"{{author}}","author":{"id":"{{author}}","name":"{{name}}"},"content":"{{content}}","createdAt":"{{at}}"}
                    """));
        }

        transaction.Commit();
    };

    /// <summary>What the reader said, and what somebody else said — the two bubble grounds.</summary>
    internal const string MineSaid = "Mine reads on both";

    /// <summary>The other half of the pair.</summary>
    internal const string TheirsSaid = "And so does theirs";

    // ── The project board's swimlanes (task f8b844dd) ───────────────────────────────────────────

    internal const string ProjectName = "Launch";
    internal const string FirstRowName = "Web";
    internal const string SecondRowName = "iOS";
    internal const string OffBoardListName = "Groceries";
    internal const string FirstRowCardTitle = "Ship the landing page";
    internal const string SecondRowCardTitle = "Submit the build";

    /// <summary>
    /// A project with TWO lists on it, each holding a card, and one list on no project.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two lists is the whole point: a project board with one row looks exactly like the
    /// single-list board, so a one-row seed would pass whether the rows were being drawn or not.
    /// Each card is in a different list, so a row drawing the wrong list's cards is visible.
    /// </para>
    /// <para>
    /// Nothing is seeded for <c>project_board_rows</c>, deliberately. Offline the core's read
    /// falls back to the project's own lists with <c>fromCache</c> set — which is the
    /// <em>nothing saved</em> half of the distinction this board turns on, and the half a seeded
    /// row would hide.
    /// </para>
    /// <para>
    /// The third list belongs to no project, so it is a candidate for "Add list" and never a row.
    /// It is also what a list dragged from the sidebar onto the board would be.
    /// </para>
    /// </remarks>
    internal static void ProjectWithTwoLists(string cachePath)
    {
        using var connection = new SqliteConnection($"Data Source={cachePath}");
        connection.Open();
        using var transaction = connection.BeginTransaction();

        Execute(connection,
            "INSERT OR REPLACE INTO projects (id, name, json) VALUES ('p1', $name, $json)",
            ("$name", ProjectName),
            ("$json", $$"""{"id":"p1","name":"{{ProjectName}}"}"""));

        void List(string id, string name, string? projectId)
        {
            // NULL rather than an empty string for a list on no project: the column is what the
            // store's own queries read, and "" is a project id as far as a comparison is
            // concerned — it would make this list a member of a project nobody has.
            if (projectId is null)
            {
                Execute(connection,
                    "INSERT OR REPLACE INTO lists (id, name, project_id, json) VALUES ($id, $name, NULL, $json)",
                    ("$id", id),
                    ("$name", name),
                    ("$json", $$"""{"id":"{{id}}","name":"{{name}}"}"""));
                return;
            }
            Execute(connection,
                "INSERT OR REPLACE INTO lists (id, name, project_id, json) VALUES ($id, $name, $project, $json)",
                ("$id", id),
                ("$name", name),
                ("$project", projectId),
                ("$json", $$"""{"id":"{{id}}","name":"{{name}}","projectId":"{{projectId}}"}"""));
        }

        void Card(string id, string title, string listId, string listName)
        {
            Execute(connection,
                "INSERT OR REPLACE INTO tasks (id, title, json) VALUES ($id, $title, $json)",
                ("$id", id),
                ("$title", title),
                ("$json", $$"""{"id":"{{id}}","title":"{{title}}","lists":[{"id":"{{listId}}","name":"{{listName}}"}]}"""));
            Execute(connection,
                "INSERT OR REPLACE INTO task_lists_membership (task_id, list_id) VALUES ($task, $list)",
                ("$task", id),
                ("$list", listId));
        }

        List("lweb", FirstRowName, "p1");
        List("lios", SecondRowName, "p1");
        List("lshop", OffBoardListName, null);
        Card("tweb", FirstRowCardTitle, "lweb", FirstRowName);
        Card("tios", SecondRowCardTitle, "lios", SecondRowName);

        transaction.Commit();
    }

    private static void Execute(SqliteConnection connection, string sql, params (string Name, string Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        command.ExecuteNonQuery();
    }
}
