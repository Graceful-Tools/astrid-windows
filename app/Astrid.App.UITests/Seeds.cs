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
