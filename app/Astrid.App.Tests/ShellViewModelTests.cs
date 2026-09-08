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

    /// <summary>
    /// A core that is signed in with these lists.
    /// </summary>
    /// <remarks>
    /// The session answer comes first because the window asks for it first: what to show depends
    /// on it, and loading a sidebar behind a sign-in screen would show the previous user's lists.
    /// </remarks>
    private static FakeCore StartedCore(params (string Id, string Name, bool Favorite)[] lists) =>
        new FakeCore()
            .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false })
            .AnswerOk("lists", Lists(lists))
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("outboxStats", new { pending = 0, running = 0, failed = 0, hasUnsentWork = false })
            .AnswerOk("sync", new { fetched = false });

    /// <summary>
    /// Once, and then never again on this machine. A tour that came back every launch would be
    /// the first thing anybody turned off.
    /// </summary>
    [Fact]
    public async Task The_tour_is_shown_once_and_remembered()
    {
        var core = StartedCore()
            .AnswerOk("hasSeenTour", new { seen = false })
            .AnswerOk("tourSeen");
        using var shell = new ShellViewModel(core, RunInline);

        await shell.MaybeShowTourAsync();
        Assert.True(shell.IsTourOpen);

        await shell.DismissTourAsync();
        Assert.False(shell.IsTourOpen);
        Assert.Contains("tourSeen", core.SentKinds());
    }

    [Fact]
    public async Task A_machine_that_has_seen_the_tour_is_not_shown_it()
    {
        var core = StartedCore().AnswerOk("hasSeenTour", new { seen = true });
        using var shell = new ShellViewModel(core, RunInline);

        await shell.MaybeShowTourAsync();

        Assert.False(shell.IsTourOpen);
    }

    private static object PaletteRows() => new
    {
        rows = new object[]
        {
            new { kind = "command", id = "newTask", title = "New task", keys = "n" },
            new { kind = "list", id = "l1", title = "Home" },
            new { kind = "task", id = "t1", title = "Buy milk", subtitle = "Home" },
        },
    };

    /// <summary>
    /// Opening the palette fills it, because a blank box teaches nobody what it can do.
    /// </summary>
    [Fact]
    public async Task Opening_the_palette_shows_something_before_anything_is_typed()
    {
        var core = StartedCore(("l1", "Home", false)).AnswerOk("palette", PaletteRows());
        using var shell = new ShellViewModel(core, RunInline);

        await shell.ShowPaletteAsync(true);

        Assert.True(shell.IsPaletteOpen);
        Assert.Equal(3, shell.PaletteRows.Count);
        Assert.Contains("\"query\":\"\"", core.Sent.First(sent => sent.Contains("palette")));
    }

    /// <summary>
    /// A command row is handed to whoever carries out the keyboard's actions rather than run here:
    /// two implementations of "new task" is how they come to differ.
    /// </summary>
    [Fact]
    public async Task Choosing_a_command_is_handed_to_the_shortcut_dispatcher()
    {
        var core = StartedCore().AnswerOk("palette", PaletteRows());
        using var shell = new ShellViewModel(core, RunInline);
        await shell.ShowPaletteAsync(true);
        string? requested = null;
        shell.PaletteCommandRequested += action => requested = action;

        await shell.RunPaletteRowAsync(shell.PaletteRows[0]);

        Assert.Equal("newTask", requested);
        Assert.False(shell.IsPaletteOpen);
    }

    [Fact]
    public async Task Choosing_a_list_opens_it_and_the_sidebar_follows()
    {
        var core = StartedCore(("l1", "Home", false), ("l2", "Work", false))
            .AnswerOk("palette", new
            {
                rows = new object[] { new { kind = "list", id = "l2", title = "Work" } },
            })
            .AnswerOk("rowsForList", EmptyWindow());
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        await shell.ShowPaletteAsync(true);

        await shell.RunPaletteRowAsync(shell.PaletteRows[0]);

        Assert.Equal("l2", shell.Tasks.ListId);
        Assert.Equal("l2", shell.Sidebar.Selected?.Id);
    }

    /// <summary>
    /// A reminder reaches whoever draws banners, and nothing marks itself shown on the way — a
    /// banner that failed to appear is still owed.
    /// </summary>
    [Fact]
    public async Task A_reminder_coming_due_is_handed_to_the_shell()
    {
        var core = StartedCore().AnswerOk("remindersDue", new
        {
            reminders = new[]
            {
                new { taskId = "t1", title = "Call the vet", reminderTime = "2026-09-07T11:59:00Z" },
            },
        });
        using var shell = new ShellViewModel(core, RunInline);
        var heard = new List<Reminder>();
        shell.RemindersDue += reminders => heard.AddRange(reminders);

        await shell.RaiseRemindersAsync();

        Assert.Single(heard);
        Assert.Equal("Call the vet", heard[0].Title);
        Assert.DoesNotContain("reminderShown", core.SentKinds());
    }

    /// <summary>Nothing due, nothing raised. An empty banner is worse than none.</summary>
    [Fact]
    public async Task Nothing_due_raises_nothing()
    {
        var core = StartedCore().AnswerOk("remindersDue", new { reminders = Array.Empty<object>() });
        using var shell = new ShellViewModel(core, RunInline);
        var heard = 0;
        shell.RemindersDue += _ => heard++;

        await shell.RaiseRemindersAsync();

        Assert.Equal(0, heard);
    }

    /// <summary>
    /// Completing from a banner goes through the completion command like everywhere else, so a
    /// repeating task rolls forward instead of finishing. A banner is not a special case.
    /// </summary>
    [Fact]
    public async Task Completing_from_a_reminder_rolls_repeating_tasks_over()
    {
        var core = StartedCore()
            .AnswerOk("completeTask", new { id = "t1" })
            .AnswerOk("rowsForList", EmptyWindow());
        using var shell = new ShellViewModel(core, RunInline);

        await shell.CompleteFromReminderAsync("t1");

        Assert.Contains("completeTask", core.SentKinds());
        Assert.DoesNotContain("updateTask", core.SentKinds());
    }

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
        Assert.Equal("isSignedIn", kinds[0]);
        Assert.Equal("lists", kinds[1]);
        Assert.Equal("rowsForList", kinds[2]);
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
            .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false })
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
            .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false })
            .AnswerOk("lists", Lists(("l1", "Home", false)))
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("outboxStats", new { hasUnsentWork = false })
            .AnswerFailure("sync", AstridFailureKind.Unauthorized)
            // The session has gone, so the window asks again and goes back to the sign-in screen.
            .AnswerOk("isSignedIn", new { signedIn = false, waitingForCallback = false });
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.True(shell.NeedsSignIn);
    }

    [Fact]
    public async Task Unsent_work_is_visible_to_the_window()
    {
        var core = new FakeCore()
            .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false })
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
