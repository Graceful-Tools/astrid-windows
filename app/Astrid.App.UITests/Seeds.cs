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

    internal const string HighPriorityCardTitle = "Draft the invitations";
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
    /// <b>The checkbox is deliberately not here, because this harness cannot make one.</b>
    /// <c>AstridApp.WriteSession</c> writes a cookie and no user id, so no seeded task can be the
    /// reader's and every card comes back as a square. Pretending otherwise with a fourth title
    /// called "mine" would be a test that reads as covering three states and covers two.
    /// </para>
    /// <para>
    /// That gap costs little: the checkbox is a single image that draws priority, repeating and
    /// done at once, and <c>PriorityMarkColourTests</c> already reads its pixels on the list side.
    /// The squares are the ones assembled from a Border, a TextBlock and an attached property —
    /// which is exactly where the blank this task is about came from.
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

        // Nobody's: no assignee at all, which is what a card typed into a column of a list whose
        // defaultAssigneeId is "unassigned" becomes.
        AddBoardCard(connection, "t1", BoardCardTitle, assignee: null, priority: 0, repeating: false);
        // Somebody else's: their initial in the square.
        AddBoardCard(connection, "t2", TheirsCardTitle, assignee: "Dana", priority: 2, repeating: false);
        // Somebody else's AND repeating: the arrows are the square's border.
        AddBoardCard(connection, "t3", RepeatingCardTitle, assignee: "Dana", priority: 1, repeating: true);
        // Nobody's again, at the top priority: the square carries priority as its colour, and the
        // first card is priority 0 — the value whose default sentinel caused the original blank.
        // Both ends of that range, so a brush that is never asked for fails here either way.
        AddBoardCard(connection, "t4", HighPriorityCardTitle, assignee: null, priority: 3, repeating: false);

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
        var who = assignee is null
            ? string.Empty
            : ",\"assignee\":{\"id\":" + quote + assignee + quote
                + ",\"name\":" + quote + assignee + quote + "}";
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
