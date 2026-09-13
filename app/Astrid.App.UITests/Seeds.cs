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
