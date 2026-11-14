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
    /// The tasks belong to the signed-in user, which is what makes each row's mark the checkbox —
    /// <c>LeadingControl::for_task</c> gives an unassigned task the square instead, and the square
    /// deliberately draws nothing at all at priority 0, so three quarters of the thing under test
    /// would be invisible.
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
    internal static Action<string> ListWithEveryPriority(string theme) => cachePath =>
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
            Execute(connection,
                "INSERT OR REPLACE INTO tasks (id, title, json) VALUES ($id, $title, $json)",
                ("$id", id),
                ("$title", title),
                ("$json", $$"""
                    {"id":"{{id}}","title":"{{title}}","priority":{{priority}},"assigneeId":"{{OwnerId}}","assignee":{"id":"{{OwnerId}}","name":"Smoke Tester"},"lists":[{"id":"l3","name":"{{PriorityListName}}"}]}
                    """));
            Execute(connection,
                "INSERT OR REPLACE INTO task_lists_membership (task_id, list_id) VALUES ($id, 'l3')",
                ("$id", id));
        }

        transaction.Commit();
    };

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
