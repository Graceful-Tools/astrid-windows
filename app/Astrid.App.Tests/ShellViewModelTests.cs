using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

public sealed class ShellViewModelTests
{
    /// <summary>Runs posted work inline, standing in for the UI thread.</summary>
    private static readonly Action<Func<Task>> RunInline = work => work().GetAwaiter().GetResult();

    private static object Lists(params (string Id, string Name, bool Favorite)[] lists) =>
        lists.Select(list => new
        {
            id = list.Id,
            name = list.Name,
            isFavorite = list.Favorite,
        }).ToArray();

    private static object EmptyWindow() => new { total = 0, offset = 0, rows = Array.Empty<object>() };

    private static FakeCore StartedCore(params (string Id, string Name, bool Favorite)[] lists) =>
        new FakeCore()
            .AnswerOk("lists", Lists(lists))
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("outboxStats", new { pending = 0, running = 0, failed = 0, hasUnsentWork = false })
            .AnswerOk("sync", new { fetched = false });

    /// <summary>
    /// The first paint comes from the cache and owes nothing to the network — which is the whole
    /// feel of the app, and is why the order of these four calls is a test rather than a comment.
    /// </summary>
    [Fact]
    public async Task Starting_draws_from_the_cache_before_it_syncs()
    {
        var core = StartedCore(("l1", "Home", true));
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        var kinds = core.SentKinds().ToList();
        Assert.Equal("lists", kinds[0]);
        Assert.Equal("rowsForList", kinds[1]);
        Assert.Contains("sync", kinds);
        Assert.True(kinds.IndexOf("rowsForList") < kinds.IndexOf("sync"));
    }

    [Fact]
    public async Task The_first_list_is_opened_without_being_asked()
    {
        var core = StartedCore(("l1", "Home", false), ("l2", "Work", false));
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.Equal("Home", shell.Tasks.ListName);
    }

    /// <summary>Favourites first, in their order.</summary>
    [Fact]
    public async Task The_sidebar_puts_favourites_above_the_rest()
    {
        var core = StartedCore(("l1", "Work", false), ("l2", "Home", true));
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.Single(shell.Sidebar.Favorites);
        Assert.Equal("Home", shell.Sidebar.Favorites[0].Name);
        Assert.Single(shell.Sidebar.Lists);
        Assert.Equal("Work", shell.Sidebar.Lists[0].Name);
    }

    /// <summary>A board column is a state, not a place to file a task.</summary>
    [Fact]
    public async Task Board_columns_never_appear_in_the_sidebar()
    {
        var core = new FakeCore()
            .AnswerOk("lists", new object[]
            {
                new { id = "l1", name = "Home" },
                new { id = "s1", name = "Doing", listType = "status" },
            })
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("outboxStats", new { hasUnsentWork = false })
            .AnswerOk("sync", new { fetched = false });
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.Single(shell.Sidebar.Lists);
        Assert.Equal("Home", shell.Sidebar.Lists[0].Name);
    }

    /// <summary>
    /// A sync that could not reach the server is a state, not a failure. The app carries on with
    /// what it has and says so quietly.
    /// </summary>
    [Fact]
    public async Task A_sync_that_could_not_reach_the_server_says_offline_and_carries_on()
    {
        var core = StartedCore(("l1", "Home", false));
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.Equal("offline", shell.StatusMessage);
        Assert.False(shell.NeedsSignIn);
    }

    [Fact]
    public async Task An_expired_session_during_sync_asks_for_a_sign_in()
    {
        var core = new FakeCore()
            .AnswerOk("lists", Lists(("l1", "Home", false)))
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("outboxStats", new { hasUnsentWork = false })
            .AnswerFailure("sync", AstridFailureKind.Unauthorized);
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.True(shell.NeedsSignIn);
    }

    [Fact]
    public async Task Unsent_work_is_visible_to_the_window()
    {
        var core = new FakeCore()
            .AnswerOk("lists", Lists(("l1", "Home", false)))
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("outboxStats", new { pending = 3, hasUnsentWork = true })
            .AnswerOk("sync", new { fetched = false })
            .AnswerOk("outboxStats", new { pending = 3, hasUnsentWork = true });
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.True(shell.HasUnsentWork);
    }

    /// <summary>
    /// A notification refreshes what it names and nothing else. The stream can deliver several a
    /// second while a colleague works in the same list.
    /// </summary>
    [Fact]
    public async Task A_task_notification_refreshes_the_list_and_not_the_sidebar()
    {
        var core = StartedCore(("l1", "Home", false));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();

        core.AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("outboxStats", new { hasUnsentWork = false });
        var before = core.SentKinds().Count(kind => kind == "lists");

        core.Notify("task", "t1");

        Assert.Equal(before, core.SentKinds().Count(kind => kind == "lists"));
        Assert.Contains("rowsForList", core.SentKinds());
    }

    [Fact]
    public async Task A_list_notification_refreshes_the_sidebar()
    {
        var core = StartedCore(("l1", "Home", false));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();

        core.AnswerOk("lists", Lists(("l1", "Home", false), ("l2", "Work", false)));
        core.Notify("list", "l2");

        Assert.Equal(2, shell.Sidebar.Lists.Count + shell.Sidebar.Favorites.Count);
    }

    /// <summary>
    /// A notification of a kind this build draws nothing for costs nothing. The next sync carries
    /// whatever it was about.
    /// </summary>
    [Fact]
    public async Task A_notification_this_build_does_not_know_is_ignored()
    {
        var core = StartedCore(("l1", "Home", false));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        var before = core.Sent.Count;

        core.Notify("somethingLater", "x");

        Assert.Equal(before, core.Sent.Count);
    }

    /// <summary>
    /// The core outlives a window being closed. A handler left on a disposed view model keeps it
    /// alive and then touches a UI that is gone.
    /// </summary>
    [Fact]
    public async Task Disposing_stops_listening()
    {
        var core = StartedCore(("l1", "Home", false));
        var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        shell.Dispose();

        var before = core.Sent.Count;
        core.Notify("task", "t1");

        Assert.Equal(before, core.Sent.Count);
    }
}
