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
