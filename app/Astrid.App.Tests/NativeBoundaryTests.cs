using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The one place C# and Rust are tested together.
/// </summary>
/// <remarks>
/// <para>
/// Everything else on this side runs against <see cref="FakeCore"/>, which is the right trade for
/// view-model behaviour. But the boundary itself — marshalling, callback lifetimes, who frees
/// which string — cannot be tested with a fake, and it is where the failures are unforgiving: a
/// wrong lifetime is a process that vanishes with no stack, on somebody else's machine, once every
/// few hundred launches.
/// </para>
/// <para>
/// These use an in-memory cache and never touch the network, so they are as fast and as
/// deterministic as the fake ones.
/// </para>
/// </remarks>
public sealed class NativeBoundaryTests
{
    private static AstridClient Start() => AstridClient.Start(":memory:");

    [Fact]
    public void The_core_reports_its_version()
    {
        Assert.False(string.IsNullOrWhiteSpace(AstridClient.Version));
    }

    [Fact]
    public async Task A_command_crosses_the_boundary_and_the_answer_comes_back()
    {
        using var client = Start();

        var created = await client.CallAsync(Commands.CreateTask("Buy milk"));

        Assert.True(created.Ok);
        Assert.Equal("Buy milk", created.Value.GetProperty("title").GetString());
    }

    /// <summary>
    /// A title with an accent in it has to survive. The default marshalling on Windows is ANSI,
    /// and getting this wrong corrupts text silently, only for the people whose language needs it.
    /// </summary>
    [Fact]
    public async Task Text_crosses_as_utf8_in_both_directions()
    {
        using var client = Start();
        const string title = "Acheter du café — 日本語 — emoji 🫖";

        var created = await client.CallAsync(Commands.CreateTask(title));

        Assert.Equal(title, created.Value.GetProperty("title").GetString());
    }

    /// <summary>
    /// The whole offline story in one test, across the real boundary: the task exists before
    /// anything is sent, and the Outbox says so.
    /// </summary>
    [Fact]
    public async Task A_task_created_offline_is_there_and_is_queued()
    {
        using var client = Start();
        var list = await client.CallAsync(Commands.CreateList("Home"));
        var listId = list.Value.GetProperty("id").GetString()!;

        await client.CallAsync(Commands.CreateTask("Buy milk", [listId]));

        var rows = (await client.CallAsync(Commands.RowsForList(listId))).Read<RowWindow>();
        Assert.NotNull(rows);
        Assert.Equal(1, rows!.Total);
        Assert.Equal("Buy milk", rows.Rows[0].Title);
        Assert.True(rows.Rows[0].IsPending);

        var stats = (await client.CallAsync(Commands.OutboxStats())).Read<OutboxStats>();
        Assert.True(stats!.HasUnsentWork);
    }

    /// <summary>
    /// Rule 2 of docs/ASTRID.md §0, reaching all the way from a C# call site: the shell cannot
    /// complete a task by updating it, because that path skips the repeat rollover.
    /// </summary>
    [Fact]
    public async Task The_shell_cannot_complete_a_task_by_updating_it()
    {
        using var client = Start();
        var created = await client.CallAsync(Commands.CreateTask("Water plants"));
        var taskId = created.Value.GetProperty("id").GetString()!;

        var refused = await client.CallAsync(Commands.UpdateTask(taskId,
            new Dictionary<string, object?> { ["completed"] = true }));

        Assert.False(refused.Ok);
        Assert.Equal(AstridFailureKind.BadRequest, refused.Error!.Kind);
        Assert.Contains("completeTask", refused.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>Many calls in flight at once, each getting its own answer back.</summary>
    [Fact]
    public async Task Concurrent_calls_do_not_cross_their_answers()
    {
        using var client = Start();
        var list = await client.CallAsync(Commands.CreateList("Home"));
        var listId = list.Value.GetProperty("id").GetString()!;

        var creations = Enumerable.Range(0, 50)
            .Select(index => client.CallAsync(Commands.CreateTask($"Task {index}", [listId])))
            .ToArray();
        var answers = await Task.WhenAll(creations);

        var titles = answers.Select(answer => answer.Value.GetProperty("title").GetString()).ToHashSet();
        Assert.Equal(50, titles.Count);
        Assert.All(answers, answer => Assert.True(answer.Ok));
    }

    [Fact]
    public async Task A_command_the_core_cannot_read_answers_rather_than_hanging()
    {
        using var client = Start();

        var answer = await client.CallAsync("{\"kind\":\"somethingLater\"}");

        Assert.False(answer.Ok);
        Assert.Equal(AstridFailureKind.BadRequest, answer.Error!.Kind);
    }

    [Fact]
    public void A_cache_path_that_cannot_be_opened_reports_why()
    {
        var exception = Assert.Throws<AstridStartupException>(
            () => AstridClient.Start(Path.Combine("Z:", "no", "such", "place", "astrid.db")));
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    /// <summary>
    /// Starting and stopping repeatedly is what a window being opened and closed looks like. A
    /// lifetime bug here shows up as a crash on the third or fourth time, not the first.
    /// </summary>
    [Fact]
    public async Task Starting_and_stopping_repeatedly_is_safe()
    {
        for (var round = 0; round < 5; round++)
        {
            using var client = Start();
            client.Subscribe();
            var answer = await client.CallAsync(Commands.Lists());
            Assert.True(answer.Ok);
        }
    }

    /// <summary>Using a client after it has been stopped is a mistake with a name, not a crash.</summary>
    [Fact]
    public async Task Using_a_stopped_client_says_so()
    {
        var client = Start();
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.CallAsync(Commands.Lists()));
    }
}
