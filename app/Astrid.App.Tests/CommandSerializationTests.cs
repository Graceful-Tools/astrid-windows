using System.Text.Json;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// What a command looks like on the wire.
/// </summary>
/// <remarks>
/// These would be pedantic if the core were forgiving, and it is not: a field it does not
/// recognise is ignored and a field that is absent means "leave it alone". Both failures are
/// silent — a button that does nothing, or an edit that half-applies — so the shapes are pinned
/// here rather than discovered in use.
/// </remarks>
public sealed class CommandSerializationTests
{
    private static string Json(object command) =>
        JsonSerializer.Serialize(command, CommandJson.Options);

    /// <summary>
    /// The one that would be silent and expensive: <c>WhenWritingNull</c> is set on the serializer,
    /// and if it applied to dictionary entries then "clear the due date" would serialise to an
    /// empty change set and do nothing at all.
    /// </summary>
    [Fact]
    public void Clearing_a_field_sends_an_explicit_null()
    {
        var json = Json(Commands.UpdateTask("t1",
            new Dictionary<string, object?> { ["dueDateTime"] = null }));

        Assert.Contains("\"dueDateTime\":null", json, StringComparison.Ordinal);
    }

    /// <summary>And a field that was not touched is not in the payload at all.</summary>
    [Fact]
    public void An_untouched_field_is_absent_rather_than_null()
    {
        var json = Json(Commands.UpdateTask("t1",
            new Dictionary<string, object?> { ["title"] = "Buy oat milk" }));

        Assert.DoesNotContain("dueDateTime", json, StringComparison.Ordinal);
        Assert.Contains("\"title\":\"Buy oat milk\"", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// The core reads commands with camelCase field names. A call site that serialised with the
    /// default naming would send <c>ListId</c> and get "bad request" back with nothing to explain
    /// it.
    /// </summary>
    /// <summary>A reorder carries the rows' ids as an array, in order (task 7883f710).</summary>
    [Fact]
    public void A_manual_order_is_an_array_of_ids_task_7883f710()
    {
        var json = Json(Commands.SetManualOrder("l1", ["t2", "t1"]));

        Assert.Contains("\"kind\":\"setManualOrder\"", json, StringComparison.Ordinal);
        Assert.Contains("\"listId\":\"l1\"", json, StringComparison.Ordinal);
        Assert.Contains("\"order\":[\"t2\",\"t1\"]", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// A <c>stream</c> change carries which edge it is, and the shell has to read it (task
    /// ef92df55). <c>Parse</c> only ever looked for ids, so both edges arrived identical and the
    /// window could not have told them apart even with an arm for them.
    /// </summary>
    [Fact]
    public void A_stream_change_carries_which_edge_it_is_task_ef92df55()
    {
        Assert.True(ChangeNotification.Parse("{\"change\":\"stream\",\"live\":true}").Live);
        Assert.False(ChangeNotification.Parse("{\"change\":\"stream\",\"live\":false}").Live);
        // Every other change says nothing about the stream, which is not the same as "down".
        Assert.Null(ChangeNotification.Parse("{\"change\":\"task\",\"id\":\"t1\"}").Live);
    }

    /// <summary>
    /// Starting the stream over is a command the core has and the bindings did not (task ef92df55).
    /// </summary>
    [Fact]
    public void Reconnecting_the_stream_is_a_bare_command_task_ef92df55()
    {
        Assert.Contains("\"kind\":\"reconnectStream\"", Json(Commands.ReconnectStream()),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Asking whether the stream is live is a command the core has and the bindings did not (task
    /// 1e4c959e) — the edges are only half the answer, and a stream that never connects has no
    /// edge to send.
    /// </summary>
    [Fact]
    public void Asking_whether_the_stream_is_live_is_a_bare_command_task_1e4c959e()
    {
        Assert.Contains("\"kind\":\"streamState\"", Json(Commands.StreamState()),
            StringComparison.Ordinal);
        Assert.True(AstridResponse.Parse("{\"ok\":true,\"value\":{\"live\":true}}")
            .Read<StreamState>()!.Live);
        Assert.False(AstridResponse.Parse("{\"ok\":true,\"value\":{\"live\":false}}")
            .Read<StreamState>()!.Live);
    }

    /// <summary>
    /// Sending a refused write again is a command the core has and the bindings did not (task
    /// 84e077ca) — which is what made a dead-lettered write a dead end on this client.
    /// </summary>
    [Fact]
    public void Retrying_refused_writes_is_a_bare_command_task_84e077ca()
    {
        Assert.Contains("\"kind\":\"retryDeadLetters\"", Json(Commands.RetryDeadLetters()),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>outboxStats</c> carries the writes the server refused as well as the counts, and the
    /// shell has to read both (task 84e077ca). The core gained the list in <c>c0ec031</c>;
    /// <c>failed</c> is what <c>hasUnsentWork</c> deliberately leaves out.
    /// </summary>
    [Fact]
    public void The_outbox_answer_carries_the_writes_that_were_refused_task_84e077ca()
    {
        var stats = AstridResponse.Parse(
                "{\"ok\":true,\"value\":{\"pending\":0,\"running\":0,\"failed\":2,"
                + "\"hasUnsentWork\":false,\"deadLetters\":["
                + "{\"kind\":\"updateTask\",\"error\":\"403 forbidden\"},"
                + "{\"kind\":\"createTask\",\"error\":null}]}}")
            .Read<OutboxStats>();

        Assert.NotNull(stats);
        Assert.Equal(2, stats!.Failed);
        Assert.False(stats.HasUnsentWork);
        Assert.Equal(2, stats.DeadLetters.Count);
        Assert.Equal("updateTask", stats.DeadLetters[0].Kind);
        Assert.Equal("403 forbidden", stats.DeadLetters[0].Error);
        // The core sends `last_error` as it has it, and it can be absent.
        Assert.Null(stats.DeadLetters[1].Error);
    }

    [Fact]
    public void Fields_are_camel_case()
    {
        Assert.Contains("\"listId\":\"l1\"", Json(Commands.List("l1")), StringComparison.Ordinal);
        Assert.Contains("\"taskId\":\"t1\"", Json(Commands.Task("t1")), StringComparison.Ordinal);
        Assert.Contains("\"callbackUrl\":", Json(Commands.CompleteSignIn("astrid://x")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_request_carries_its_window()
    {
        var json = Json(Commands.RowsForList("l1", offset: 40, limit: 200));

        Assert.Contains("\"offset\":40", json, StringComparison.Ordinal);
        Assert.Contains("\"limit\":200", json, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creating a task with no lists sends an empty array rather than omitting the field, because
    /// "no list" is a real answer — an inbox task — and not the absence of one.
    /// </summary>
    [Fact]
    public void Creating_a_task_with_no_list_says_so()
    {
        Assert.Contains("\"listIds\":[]", Json(Commands.CreateTask("Buy milk")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Every_command_names_its_kind()
    {
        foreach (var command in new[]
        {
            Commands.Lists(),
            Commands.OutboxStats(),
            Commands.Sync(),
            Commands.IsSignedIn(),
            Commands.BeginSignIn(),
            Commands.ResolveShortcut("x", hasSelection: true),
        })
        {
            using var document = JsonDocument.Parse(Json(command));
            Assert.True(document.RootElement.TryGetProperty("kind", out var kind));
            Assert.False(string.IsNullOrEmpty(kind.GetString()));
        }
    }
}
