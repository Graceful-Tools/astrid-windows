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
    /// The same, but the first sync pass reached the server.
    /// </summary>
    /// <remarks>
    /// <c>StartedCore</c>'s pass answers <c>fetched: false</c>, which leaves the window saying it is
    /// offline — and offline outranks the stream being down, so a stream test built on it would be
    /// asserting the precedence rather than the thing it means to test (task ef92df55).
    /// </remarks>
    private static FakeCore OnlineCore(params (string Id, string Name, bool Favorite)[] lists) =>
        new FakeCore()
            .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false })
            .AnswerOk("lists", Lists(lists))
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("outboxStats", new { pending = 0, running = 0, failed = 0, hasUnsentWork = false })
            .AnswerOk("sync", new { fetched = true })
            .AnswerOk("lists", Lists(lists))
            .AnswerOk("rowsForList", EmptyWindow());

    /// <summary>
    /// The same core, with two writes the server refused waiting in the Outbox (task 84e077ca).
    /// </summary>
    /// <remarks>
    /// <c>StartAsync</c> reads the outbox twice — once for the first paint and once after the sync
    /// pass — so this is the second of those reads, and the state the window is left in. Note
    /// <c>hasUnsentWork</c> is false: the core counts a dead-lettered write in <c>failed</c> and
    /// deliberately not in the "still to send" answer, which is the whole reason it was invisible.
    /// </remarks>
    private static FakeCore Refusing(FakeCore core) =>
        core.Answer("outboxStats",
            "{\"ok\":true,\"value\":{\"pending\":0,\"running\":0,\"failed\":2,"
            + "\"hasUnsentWork\":false,\"deadLetters\":["
            + "{\"kind\":\"updateTask\",\"error\":\"403 forbidden\"},"
            + "{\"kind\":\"createTask\",\"error\":\"403 forbidden\"}]}}");

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
        await shell.StartAsync();

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
        await shell.StartAsync();

        await shell.MaybeShowTourAsync();

        Assert.False(shell.IsTourOpen);
    }

    /// <summary>
    /// It landed on top of the sign-in card on a fresh machine — telling somebody about a hotkey
    /// for a window with nothing in it — and marks itself seen when dismissed, so the one moment it
    /// was written for was the one moment it was spent on.
    /// </summary>
    [Fact]
    public async Task The_tour_does_not_open_over_the_sign_in_screen()
    {
        var core = new FakeCore()
            .AnswerOk("isSignedIn", new { signedIn = false, waitingForCallback = false })
            .AnswerOk("hasSeenTour", new { seen = false });
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();

        await shell.MaybeShowTourAsync();

        Assert.False(shell.IsTourOpen);
        Assert.DoesNotContain("hasSeenTour", core.SentKinds());
    }

    /// <summary>Which is when it is worth showing: there is finally something to use it on.</summary>
    [Fact]
    public async Task Signing_in_shows_the_tour_a_fresh_machine_has_not_seen()
    {
        var core = StartedCore(("l1", "Home", false))
            .AnswerOk("completeSignIn", new { signedIn = true })
            .AnswerOk("hasSeenTour", new { seen = false });
        using var shell = new ShellViewModel(core, RunInline);

        await shell.HandleActivationAsync("astrid://auth/callback?token=t");

        Assert.True(shell.IsTourOpen);
    }

    // ── Copying a public list (task f6bc59e8) ─────────────────────────────────────────────────

    /// <summary>Copying a public list reloads the sidebar and opens the copy.</summary>
    [Fact]
    public async Task Copying_a_public_list_opens_the_copy_task_f6bc59e8()
    {
        var core = StartedCore(("l1", "Home", false))
            .AnswerOk("copyList", new { id = "l9", name = "Recipes" })
            .AnswerOk("lists", Lists(("l1", "Home", false), ("l9", "Recipes", false)))
            .AnswerOk("rowsForList", EmptyWindow());
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();

        Assert.True(await shell.CopyPublicListAsync("pub1"));

        Assert.Equal("l9", shell.Tasks.ListId);
        Assert.Equal("Recipes", shell.Tasks.ListName);
        Assert.Contains(shell.Sidebar.Lists, list => list.Id == "l9");
        Assert.Equal("l9", shell.Sidebar.Selected?.Id);
    }

    // ── The open list's picture (task 3a913e52) ───────────────────────────────────────────────

    /// <summary>
    /// Opening a list that has a picture asks the core where to draw it from; opening one without
    /// asks nothing, since the sidebar already says.
    /// </summary>
    [Fact]
    public async Task The_open_lists_picture_is_fetched_only_when_it_has_one_task_3a913e52()
    {
        var core = new FakeCore()
            .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false })
            .AnswerOk("lists", new object[]
            {
                new { id = "l1", name = "Garden", isFavorite = false, imageUrl = "/api/v1/secure-files/f1" },
                new { id = "l2", name = "Work", isFavorite = false },
            })
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("listImage", new { source = "https://astrid.cc/api/v1/secure-files/f1", isLocal = false })
            .AnswerOk("outboxStats", new { pending = 0, running = 0, failed = 0, hasUnsentWork = false })
            .AnswerOk("sync", new { fetched = false })
            .AnswerOk("rowsForList", EmptyWindow());
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();

        Assert.Equal("l1", shell.Tasks.ListId);
        Assert.True(shell.HasListImage);
        Assert.Equal("https://astrid.cc/api/v1/secure-files/f1", shell.ListImageSource);
        var asked = core.Sent.Count(json => json.Contains("\"kind\":\"listImage\""));

        await shell.OpenListAsync("l2", "Work");

        Assert.False(shell.HasListImage);
        Assert.Equal(asked, core.Sent.Count(json => json.Contains("\"kind\":\"listImage\"")));
    }

    // ── Dropping a row on a list (task 27cae198) ─────────────────────────────────────────────

    /// <summary>
    /// A plain drop makes the target the task's only list and redraws the rows and the sidebar;
    /// a Shift drop adds the list instead; a virtual list, or one that is not there, takes nothing.
    /// </summary>
    [Fact]
    public async Task Dropping_a_row_on_a_list_moves_it_and_shift_adds_it_task_27cae198()
    {
        var core = StartedCore(("l1", "Home", false), ("l2", "Work", false))
            .AnswerOk("setTaskLists")
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("lists", Lists(("l1", "Home", false), ("l2", "Work", false)))
            .AnswerOk("addTaskToList")
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("lists", Lists(("l1", "Home", false), ("l2", "Work", false)));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        var sentBefore = core.Sent.Count;

        Assert.True(await shell.DropTaskOnListAsync("t1", "l2", add: false));
        var moved = Assert.Single(core.Sent, json => json.Contains("\"kind\":\"setTaskLists\""));
        Assert.Contains("\"taskId\":\"t1\"", moved);
        Assert.Contains("\"listIds\":[\"l2\"]", moved);
        Assert.Equal(["setTaskLists", "rowsForList", "lists"], core.SentKinds().Skip(sentBefore).Take(3));

        Assert.True(await shell.DropTaskOnListAsync("t1", "l2", add: true));
        var added = Assert.Single(core.Sent, json => json.Contains("\"kind\":\"addTaskToList\""));
        Assert.Contains("\"listId\":\"l2\"", added);

        var sentSoFar = core.Sent.Count;
        Assert.False(await shell.DropTaskOnListAsync("t1", "no-such-list", add: false));
        Assert.Equal(sentSoFar, core.Sent.Count);
    }

    /// <summary>My Tasks and the other views are filters, not places: a drop on one writes nothing.</summary>
    [Fact]
    public async Task A_virtual_list_takes_no_drop_task_27cae198()
    {
        var core = new FakeCore()
            .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false })
            .AnswerOk("lists", new[]
            {
                new { id = "today", name = "Today", isFavorite = false, isVirtual = true },
                new { id = "l1", name = "Home", isFavorite = false, isVirtual = false },
            })
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("outboxStats", new { pending = 0, running = 0, failed = 0, hasUnsentWork = false })
            .AnswerOk("sync", new { fetched = false });
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        var sent = core.Sent.Count;

        Assert.False(await shell.DropTaskOnListAsync("t1", "today", add: false));

        Assert.Equal(sent, core.Sent.Count);
        Assert.Contains(shell.Sidebar.Lists, list => list.Id == "today" && !list.IsDropTarget);
        Assert.Contains(shell.Sidebar.Lists, list => list.Id == "l1" && list.IsDropTarget);
    }

    // ── Tapping a row ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tapping the row whose task is already open closes the pane (task 8ac00791).
    /// </summary>
    /// <remarks>
    /// Selection alone cannot express this: tapping the selected row raises no selection change,
    /// so the pane sat open with no way to dismiss it from the list it came from.
    /// </remarks>
    [Fact]
    public async Task Tapping_the_open_task_closes_it()
    {
        var core = StartedCore(("l1", "Home", false)).AnswerOk("taskDetail", TaskDetail("t1"));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();

        await shell.OpenOrCloseTaskAsync("t1");
        Assert.True(shell.Detail.IsOpen);

        await shell.OpenOrCloseTaskAsync("t1");

        Assert.False(shell.Detail.IsOpen);
    }

    /// <summary>Tapping a DIFFERENT row opens that one rather than closing the pane.</summary>
    [Fact]
    public async Task Tapping_another_task_opens_it_instead_of_closing()
    {
        var core = StartedCore(("l1", "Home", false))
            .AnswerOk("taskDetail", TaskDetail("t1"));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        await shell.OpenOrCloseTaskAsync("t1");

        await shell.OpenOrCloseTaskAsync("t2");

        Assert.True(shell.Detail.IsOpen, "a second task is a different question, not a dismissal");
    }

    /// <summary>With nothing open, a tap opens — there is nothing to toggle off.</summary>
    [Fact]
    public async Task Tapping_with_nothing_open_opens()
    {
        var core = StartedCore(("l1", "Home", false)).AnswerOk("taskDetail", TaskDetail("t1"));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();

        await shell.OpenOrCloseTaskAsync("t1");

        Assert.True(shell.Detail.IsOpen);
    }

    /// <summary>
    /// A refresh hands the list new row objects and re-selects the same task by id. That is not a
    /// new selection, and it must not reopen a pane somebody just tapped closed (task 8ac00791).
    /// </summary>
    [Fact]
    public async Task Reselecting_the_same_row_after_a_refresh_does_not_reopen_a_closed_task_task_8ac00791()
    {
        var core = StartedCore(("l1", "Home", false))
            .AnswerOk("taskDetail", TaskDetail("t1"))
            .AnswerOk("taskDetail", TaskDetail("t2"));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();

        await shell.OpenOrCloseTaskAsync("t1");
        await shell.OpenOrCloseTaskAsync("t1");
        Assert.False(shell.Detail.IsOpen);

        await shell.SelectRowAsync("t1");
        Assert.False(shell.Detail.IsOpen, "the same task re-selected is a refresh, not a choice");

        await shell.SelectRowAsync("t2");
        Assert.True(shell.Detail.IsOpen, "a different task is a choice");
        Assert.Equal("t2", shell.Detail.TaskId);
    }

    /// <summary>
    /// On the board, a tapped card opens its task IN PLACE, and tapping it again closes it
    /// (task 91a25b8a). astrid-web's board expands the card inside its column rather than opening
    /// the side panel, and a person moving between the two should find the same gesture.
    /// </summary>
    [Fact]
    public async Task Tapping_a_card_on_the_board_expands_it_inline_task_91a25b8a()
    {
        var core = StartedCore(("l1", "Home", false))
            .AnswerOk("board", BoardWith("t1", "t2"))
            .AnswerOk("taskDetail", TaskDetail("t1"));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        await shell.ShowBoardAsync(true);

        await shell.ToggleCardAsync("t1");

        Assert.True(shell.Detail.IsOpen);
        Assert.Equal("t1", shell.Detail.TaskId);
        Assert.Equal("t1", shell.Board.ExpandedTaskId);
        Assert.True(shell.ShowsDetailInline);
        Assert.False(shell.ShowsDetailPane, "the side pane stays shut while the card holds the detail");
    }

    [Fact]
    public async Task Tapping_the_expanded_card_collapses_it()
    {
        var core = StartedCore(("l1", "Home", false))
            .AnswerOk("board", BoardWith("t1", "t2"))
            .AnswerOk("taskDetail", TaskDetail("t1"));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        await shell.ShowBoardAsync(true);
        await shell.ToggleCardAsync("t1");

        await shell.ToggleCardAsync("t1");

        Assert.False(shell.Detail.IsOpen);
        Assert.Null(shell.Board.ExpandedTaskId);
    }

    [Fact]
    public async Task Tapping_another_card_moves_the_expansion_to_it()
    {
        var core = StartedCore(("l1", "Home", false))
            .AnswerOk("board", BoardWith("t1", "t2"))
            .AnswerOk("taskDetail", TaskDetail("t1"))
            .AnswerOk("taskDetail", TaskDetail("t2"));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        await shell.ShowBoardAsync(true);
        await shell.ToggleCardAsync("t1");

        await shell.ToggleCardAsync("t2");

        Assert.True(shell.Detail.IsOpen);
        Assert.Equal("t2", shell.Detail.TaskId);
        Assert.Equal("t2", shell.Board.ExpandedTaskId);
    }

    /// <summary>Closing the detail from its own menu collapses the card it was drawn in.</summary>
    [Fact]
    public async Task Closing_the_detail_collapses_the_card()
    {
        var core = StartedCore(("l1", "Home", false))
            .AnswerOk("board", BoardWith("t1"))
            .AnswerOk("taskDetail", TaskDetail("t1"));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        await shell.ShowBoardAsync(true);
        await shell.ToggleCardAsync("t1");

        shell.Detail.Close();

        Assert.Null(shell.Board.ExpandedTaskId);
        Assert.False(shell.ShowsDetailInline);
    }

    /// <summary>
    /// Full screen (task 1927c2e7, PRODUCT_CONTRACT §3) is taken from the side pane and never
    /// offered on a board card, which is deliberately a peek. A card that takes the detail ends
    /// it, so the card never inherits it and the pane does not spring back to it afterwards.
    /// </summary>
    [Fact]
    public async Task Full_screen_is_offered_from_the_side_pane_and_never_on_a_board_card_task_1927c2e7()
    {
        var core = StartedCore(("l1", "Home", false))
            .AnswerOk("taskDetail", TaskDetail("t1"))
            .AnswerOk("board", BoardWith("t1"));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        await shell.OpenOrCloseTaskAsync("t1");

        Assert.False(shell.Detail.IsFullScreen, "off by default");
        Assert.True(shell.CanEnterFullScreen);
        Assert.False(shell.ShowsDetailFullScreen);

        shell.Detail.ToggleFullScreen();

        Assert.True(shell.ShowsDetailFullScreen);
        Assert.True(shell.CanExitFullScreen);
        Assert.False(shell.CanEnterFullScreen);

        // The open task's card expands and takes the detail.
        await shell.ShowBoardAsync(true);

        Assert.True(shell.ShowsDetailInline);
        Assert.False(shell.CanEnterFullScreen, "never offered on a board card");
        Assert.False(shell.CanExitFullScreen);
        Assert.False(shell.ShowsDetailFullScreen);
        Assert.False(shell.Detail.IsFullScreen, "ended when the card took the detail");
    }

    /// <summary>
    /// The settings cover the window (task 3f5834ed): one flag, opened by the account button and
    /// shut by its own arrow; opening shuts the palette, and a sign-in taking the window ends it.
    /// </summary>
    [Fact]
    public async Task Settings_cover_the_window_one_thing_at_a_time_task_3f5834ed()
    {
        var core = StartedCore(("l1", "Home", false));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        await shell.ShowPaletteAsync(true);
        Assert.True(shell.IsPaletteOpen);

        shell.ShowSettings(true);

        Assert.True(shell.IsSettingsOpen);
        Assert.False(shell.IsPaletteOpen, "one thing over the window at a time");

        shell.ShowSettings(false);
        Assert.False(shell.IsSettingsOpen);
    }

    /// <summary>
    /// The empty state is the list's, not the board's: it shows when the list has nothing and
    /// goes away on the board, which draws its own.
    /// </summary>
    [Fact]
    public async Task The_empty_state_shows_off_the_board_only()
    {
        var core = StartedCore(("l1", "Home", false)).AnswerOk("board", BoardWith());
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();

        Assert.True(shell.Tasks.IsEmpty);
        Assert.True(shell.ShowsEmptyList);

        await shell.ShowBoardAsync(true);
        Assert.False(shell.ShowsEmptyList, "the board has its own empty state");

        await shell.ShowBoardAsync(false);
        Assert.True(shell.ShowsEmptyList);
    }

    /// <summary>Off the board, the detail is the side pane it always was.</summary>
    [Fact]
    public async Task In_list_view_the_detail_is_the_side_pane()
    {
        var core = StartedCore(("l1", "Home", false)).AnswerOk("taskDetail", TaskDetail("t1"));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();

        await shell.OpenOrCloseTaskAsync("t1");

        Assert.True(shell.ShowsDetailPane);
        Assert.False(shell.ShowsDetailInline);
    }

    private static object BoardWith(params string[] cards) => new
    {
        projectId = "p1",
        columns = new object[]
        {
            new
            {
                id = "ready",
                name = "Ready",
                description = string.Empty,
                kind = "status",
                total = cards.Length,
                cards = cards.Select(id => new
                {
                    id,
                    title = id,
                    completed = false,
                    priority = 0,
                    due = new { key = "none" },
                    leading = new { kind = "unassigned" },
                    action = "openPicker",
                }).ToArray(),
            },
        },
    };

    private static object TaskDetail(string id) => new
    {
        task = new { id, title = "Buy milk", description = "", completed = false, priority = 0 },
        comments = Array.Empty<object>(),
        subtasks = Array.Empty<object>(),
    };

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
        // My Tasks sits above the lists in the sidebar, so the sidebar asks for it while it is
        // drawing — still before anything reaches the network.
        Assert.Equal("myTasksList", kinds[2]);
        Assert.Equal("rowsForList", kinds[3]);
        Assert.Contains("sync", kinds);
        Assert.True(kinds.IndexOf("rowsForList") < kinds.IndexOf("sync"));
    }

    /// <summary>
    /// My Tasks is what the app opens on. Falling through to the first list would open somebody's
    /// alphabetically-first list instead of the thing they are actually holding.
    /// </summary>
    [Fact]
    public async Task My_tasks_is_what_the_app_opens_on()
    {
        var core = StartedCore(("l1", "Home", false))
            .AnswerOk("myTasksList", new { id = "virtual:my-tasks", name = "My Tasks", isVirtual = true });
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.Equal("virtual:my-tasks", shell.Sidebar.Selected?.Id);
        Assert.Single(shell.Sidebar.MyTasks);
    }

    /// <summary>
    /// A deployment whose core does not answer for My Tasks still has a sidebar, and still opens
    /// something. An empty screen with nothing to explain it is the failure worth avoiding.
    /// </summary>
    [Fact]
    public async Task A_sidebar_without_my_tasks_still_opens_a_list()
    {
        var core = StartedCore(("l1", "Home", false));
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.Empty(shell.Sidebar.MyTasks);
        Assert.Equal("l1", shell.Sidebar.Selected?.Id);
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

    /// <summary>
    /// A board column is a state, not a place to file a task; a label is a tag, drawn on the
    /// tasks that carry it and never offered as somewhere to go (the web's list flavours).
    /// </summary>
    [Fact]
    public async Task Board_columns_and_labels_never_appear_in_the_sidebar()
    {
        var core = new FakeCore()
            .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false })
            .AnswerOk("lists", new object[]
            {
                new { id = "l1", name = "Home" },
                new { id = "s1", name = "Doing", listType = "status" },
                new { id = "b1", name = "Bugs", listType = "label" },
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

        Assert.True(shell.ShowsOffline);
        Assert.False(shell.NeedsSignIn);
    }

    /// <summary>
    /// The stream going down says so, and coming back takes it away (task ef92df55).
    /// </summary>
    /// <remarks>
    /// The core says each edge once. Before this the window looked identical whether events were
    /// flowing or the stream had died twenty minutes ago: <c>ShellViewModel</c>'s change switch
    /// absorbed <c>stream</c> in its default arm.
    /// </remarks>
    [Fact]
    public async Task The_stream_going_down_says_so_and_coming_back_clears_it()
    {
        var core = OnlineCore(("l1", "Home", false));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();

        // Nothing is said until the core says it. The stream starts down and announces itself when
        // it connects, so a window that opened claiming "live updates are off" would be wrong for
        // the second before the first connection, every launch.
        Assert.False(shell.ShowsLiveUpdatesDown);

        core.NotifyStream(live: false);
        Assert.True(shell.ShowsLiveUpdatesDown);

        core.AnswerOk("lists", Lists(("l1", "Home", false)))
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("outboxStats", new { hasUnsentWork = false });
        core.NotifyStream(live: true);
        Assert.False(shell.ShowsLiveUpdatesDown);
    }

    /// <summary>
    /// A stream that never connects says so (task 1e4c959e).
    /// </summary>
    /// <remarks>
    /// The hole <c>ef92df55</c> left. <c>RealtimeSink</c> holds <c>live: AtomicBool::new(false)</c>
    /// and publishes only when the value changes, so a stream that connects goes
    /// <c>false → true</c> and says so, while one that never connects goes <c>false → false</c> and
    /// says nothing at all — leaving the window's optimistic opening value in place forever.
    /// Usually the offline message covers it, but a device that reaches the server perfectly well
    /// while the stream cannot — an SSE-hostile proxy, a corporate filter — is online, syncing, and
    /// silently without live updates. Hence <c>OnlineCore</c>: on the offline fixture this would be
    /// asserting the precedence instead.
    /// </remarks>
    [Fact]
    public async Task A_stream_that_never_connects_says_so_on_open_task_1e4c959e()
    {
        var core = OnlineCore(("l1", "Home", false)).AnswerOk("streamState", new { live = false });
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        // No edge ever arrives — that is the whole case.
        Assert.DoesNotContain("stream", core.SentKinds());
        Assert.True(shell.ShowsLiveUpdatesDown);
    }

    /// <summary>
    /// A stream that is up on open leaves the window saying nothing (task 1e4c959e) — the answer
    /// settles it either way, and the common case must not gain a message it never had.
    /// </summary>
    [Fact]
    public async Task A_stream_that_is_already_up_on_open_says_nothing_task_1e4c959e()
    {
        var core = OnlineCore(("l1", "Home", false)).AnswerOk("streamState", new { live = true });
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.Contains("streamState", core.SentKinds());
        Assert.False(shell.ShowsLiveUpdatesDown);
    }

    /// <summary>
    /// The seed is an opening value, not a second source of truth (task 1e4c959e): an edge after it
    /// still wins, which is what makes the stream connecting a second later take the message away.
    /// </summary>
    [Fact]
    public async Task An_edge_after_the_seed_still_wins_task_1e4c959e()
    {
        var core = OnlineCore(("l1", "Home", false)).AnswerOk("streamState", new { live = false });
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        Assert.True(shell.ShowsLiveUpdatesDown);

        core.AnswerOk("lists", Lists(("l1", "Home", false)))
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("outboxStats", new { hasUnsentWork = false });
        core.NotifyStream(live: true);

        Assert.False(shell.ShowsLiveUpdatesDown);
    }

    /// <summary>
    /// A core that cannot answer leaves the window making no claim (task 1e4c959e). An
    /// unanswerable <c>streamState</c> is not evidence the stream is off, and the optimism the
    /// opening value exists for applies exactly here.
    /// </summary>
    [Fact]
    public async Task A_stream_state_the_core_cannot_answer_accuses_nothing_task_1e4c959e()
    {
        var core = OnlineCore(("l1", "Home", false))
            .AnswerFailure("streamState", AstridFailureKind.BadRequest, "no");
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.False(shell.ShowsLiveUpdatesDown);
    }

    /// <summary>
    /// Offline still wins the slot when the seed says the stream is down (task 1e4c959e). The
    /// precedence <c>ef92df55</c> set is untouched by where the flag's first value comes from.
    /// </summary>
    [Fact]
    public async Task A_seeded_stream_down_still_loses_to_offline_task_1e4c959e()
    {
        // This fixture's sync pass could not reach the server.
        var core = StartedCore(("l1", "Home", false)).AnswerOk("streamState", new { live = false });
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.True(shell.ShowsOffline);
        Assert.False(shell.ShowsLiveUpdatesDown);
    }

    /// <summary>
    /// Reconnecting catches up at once rather than waiting out the sixty-second timer (task
    /// ef92df55). Events missed while the stream was down are not replayed — the core's own comment
    /// calls that timer "a floor for the screen" — so the up edge does the refresh <c>synced</c>
    /// does.
    /// </summary>
    [Fact]
    public async Task The_stream_coming_back_catches_up_without_waiting_for_the_timer()
    {
        var core = OnlineCore(("l1", "Home", false)).AnswerOk("taskDetail", TaskDetail("t1"));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        await shell.OpenTaskAsync("t1");
        core.NotifyStream(live: false);
        var listsBefore = core.SentKinds().Count(kind => kind == "lists");
        var rowsBefore = core.SentKinds().Count(kind => kind == "rowsForList");
        var detailBefore = core.SentKinds().Count(kind => kind == "taskDetail");

        core.AnswerOk("lists", Lists(("l1", "Home", false)))
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("taskDetail", TaskDetail("t1"))
            .AnswerOk("outboxStats", new { hasUnsentWork = false });
        core.NotifyStream(live: true);

        Assert.Equal(listsBefore + 1, core.SentKinds().Count(kind => kind == "lists"));
        Assert.Equal(rowsBefore + 1, core.SentKinds().Count(kind => kind == "rowsForList"));
        // The stream names nothing it missed, so "could not say" applies and the open task reloads.
        Assert.Equal(detailBefore + 1, core.SentKinds().Count(kind => kind == "taskDetail"));
    }

    /// <summary>
    /// Offline and the stream being down are different facts sharing one slot, and offline wins
    /// (task ef92df55). When nothing at all is getting through, "live updates are off" is a second
    /// line saying less than the first.
    /// </summary>
    [Fact]
    public async Task Being_offline_outranks_the_stream_being_down()
    {
        // This fixture's sync pass could not reach the server.
        var core = StartedCore(("l1", "Home", false));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();

        core.NotifyStream(live: false);

        Assert.True(shell.ShowsOffline);
        Assert.False(shell.ShowsLiveUpdatesDown);
    }

    /// <summary>
    /// If the window says the stream is down, it can try again (task ef92df55) — a resume from
    /// sleep is exactly when the core's backoff was chosen for a world that no longer exists.
    /// </summary>
    [Fact]
    public async Task Reconnecting_asks_the_core_to_start_the_stream_over()
    {
        var core = OnlineCore(("l1", "Home", false)).AnswerOk("reconnectStream");
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        core.NotifyStream(live: false);

        await shell.ReconnectStreamAsync();

        Assert.Contains("reconnectStream", core.SentKinds());
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
    /// A write the server refused is visible (task 84e077ca).
    /// </summary>
    /// <remarks>
    /// The window drew one outbox fact — "not synced yet" — and the core's <c>hasUnsentWork</c> is
    /// <c>pending &gt; 0 || running &gt; 0</c>, which stops being true the moment a write is
    /// dead-lettered. So the one state that needs a person was the one state that said nothing.
    /// </remarks>
    [Fact]
    public async Task Writes_the_server_refused_are_visible_to_the_window_task_84e077ca()
    {
        var core = Refusing(OnlineCore(("l1", "Home", false)));
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.Equal(2, shell.RefusedWrites);
        // Not the same fact: nothing is queued, so there is nothing to wait for — only to retry.
        Assert.False(shell.HasUnsentWork);
    }

    /// <summary>
    /// Trying them again sends the command and re-reads the outbox (task 84e077ca).
    /// </summary>
    [Fact]
    public async Task Retrying_refused_writes_sends_them_again_and_re_reads_the_outbox_task_84e077ca()
    {
        var core = Refusing(OnlineCore(("l1", "Home", false)));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        var readsBefore = core.SentKinds().Count(kind => kind == "outboxStats");

        core.AnswerOk("retryDeadLetters", new { revived = 2 })
            // Both went through this time.
            .AnswerOk("outboxStats", new { pending = 0, failed = 0, hasUnsentWork = false });
        await shell.RetryRefusedWritesAsync();

        Assert.Contains("retryDeadLetters", core.SentKinds());
        Assert.Equal(readsBefore + 1, core.SentKinds().Count(kind => kind == "outboxStats"));
        Assert.Equal(2, shell.LastRetryRevived);
        Assert.True(shell.ShowsRetryOutcome);
        Assert.Equal(0, shell.RefusedWrites);
    }

    /// <summary>
    /// A refusal refused again is not a success (task 84e077ca).
    /// </summary>
    /// <remarks>
    /// <c>revived</c> is how many were given another go, not how many got through — the core's own
    /// comment says so. So the outcome is read from the fresh <c>outboxStats</c> beside it, and the
    /// row goes on saying two writes were refused rather than reporting two writes sent.
    /// </remarks>
    [Fact]
    public async Task A_retry_that_is_refused_again_still_says_the_writes_were_refused_task_84e077ca()
    {
        var core = Refusing(OnlineCore(("l1", "Home", false)));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();

        core.AnswerOk("retryDeadLetters", new { revived = 2 })
            // Revived, drained, and refused a second time: back where they started.
            .AnswerOk("outboxStats", new { pending = 0, failed = 2, hasUnsentWork = false });
        await shell.RetryRefusedWritesAsync();

        Assert.Equal(2, shell.RefusedWrites);
        Assert.Equal(2, shell.LastRetryRevived);
    }

    /// <summary>
    /// Nothing to retry says so (task 84e077ca). A button that answers with silence is the thing
    /// this task exists to avoid, and it is reachable: a drain can empty the dead letters between
    /// the row being drawn and the link being pressed.
    /// </summary>
    [Fact]
    public async Task Retrying_with_nothing_refused_says_so_task_84e077ca()
    {
        var core = OnlineCore(("l1", "Home", false));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();

        core.AnswerOk("retryDeadLetters", new { revived = 0 })
            .AnswerOk("outboxStats", new { hasUnsentWork = false });
        await shell.RetryRefusedWritesAsync();

        Assert.True(shell.ShowsRetryOutcome);
        Assert.Equal(0, shell.LastRetryRevived);
    }

    /// <summary>
    /// A core that cannot answer leaves no claim about what happened (task 84e077ca) — the failure
    /// is what the status line is for, and "0 revived" would read as "nothing to retry".
    /// </summary>
    [Fact]
    public async Task A_retry_the_core_refuses_says_nothing_about_what_was_sent_task_84e077ca()
    {
        var core = OnlineCore(("l1", "Home", false));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();

        // What `revive_dead` failing looks like: the core could not write the journal.
        core.AnswerFailure("retryDeadLetters", AstridFailureKind.Cache, "database is locked");
        await shell.RetryRefusedWritesAsync();

        Assert.False(shell.ShowsRetryOutcome);
        Assert.Equal("database is locked", shell.StatusMessage);
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
    /// A background pass that brought something in redraws what is on screen — the list, the
    /// sidebar, and the open task when it is one of the tasks that moved. Before this, a change
    /// that arrived while the live stream was down sat in the cache until the next click.
    /// </summary>
    [Fact]
    public async Task A_synced_notification_refreshes_the_list_the_sidebar_and_the_open_task()
    {
        var core = StartedCore(("l1", "Home", false))
            .AnswerOk("taskDetail", TaskDetail("t1"));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        await shell.OpenTaskAsync("t1");
        var listsBefore = core.SentKinds().Count(kind => kind == "lists");
        var rowsBefore = core.SentKinds().Count(kind => kind == "rowsForList");
        var detailBefore = core.SentKinds().Count(kind => kind == "taskDetail");

        core.AnswerOk("lists", Lists(("l1", "Home", false)))
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("taskDetail", TaskDetail("t1"))
            .AnswerOk("outboxStats", new { hasUnsentWork = false });
        core.NotifySynced("t1", "t7");

        Assert.Equal(listsBefore + 1, core.SentKinds().Count(kind => kind == "lists"));
        Assert.Equal(rowsBefore + 1, core.SentKinds().Count(kind => kind == "rowsForList"));
        Assert.Equal(detailBefore + 1, core.SentKinds().Count(kind => kind == "taskDetail"));
    }

    /// <summary>
    /// An Outbox delivery redraws the screen, as the empty <c>synced</c> it replaced did.
    /// </summary>
    /// <remarks>
    /// The core announced each delivery as a <c>synced</c> naming nothing until AITD-454 gave it
    /// its own word, <c>delivered</c>, carrying what it wrote and the journal's counts. An unknown
    /// change falls through to the silent default here — so without this arm, an offline edit
    /// reaching the server left the list showing the old row and the badge still saying "not synced
    /// yet" until something else happened to refresh (task 6ee938cc).
    /// </remarks>
    [Fact]
    public async Task A_delivered_notification_redraws_and_re_reads_the_outbox_task_6ee938cc()
    {
        var core = StartedCore(("l1", "Home", false))
            .AnswerOk("taskDetail", TaskDetail("t1"));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        await shell.OpenTaskAsync("t1");
        var rowsBefore = core.SentKinds().Count(kind => kind == "rowsForList");
        var detailBefore = core.SentKinds().Count(kind => kind == "taskDetail");
        var outboxBefore = core.SentKinds().Count(kind => kind == "outboxStats");

        core.AnswerOk("lists", Lists(("l1", "Home", false)))
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("taskDetail", TaskDetail("t1"))
            .AnswerOk("outboxStats", new { hasUnsentWork = false });
        core.NotifyDelivered("t1");

        Assert.Equal(rowsBefore + 1, core.SentKinds().Count(kind => kind == "rowsForList"));
        Assert.Equal(detailBefore + 1, core.SentKinds().Count(kind => kind == "taskDetail"));
        Assert.True(core.SentKinds().Count(kind => kind == "outboxStats") > outboxBefore,
            "the delivery changed the journal's counts, so the badge has to be re-read");
    }

    /// <summary>
    /// The open task is reloaded only when the pass names it. Reloading it for every pass that
    /// moved some other task would make it flicker once a minute while a colleague works.
    /// </summary>
    [Fact]
    public async Task A_synced_notification_leaves_an_open_task_it_did_not_touch_alone()
    {
        var core = StartedCore(("l1", "Home", false))
            .AnswerOk("taskDetail", TaskDetail("t1"));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        await shell.OpenTaskAsync("t1");
        var detailBefore = core.SentKinds().Count(kind => kind == "taskDetail");

        core.AnswerOk("lists", Lists(("l1", "Home", false)))
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("outboxStats", new { hasUnsentWork = false });
        core.NotifySynced("t7");

        Assert.Equal(detailBefore, core.SentKinds().Count(kind => kind == "taskDetail"));
    }

    /// <summary>
    /// A pass that could not say what moved — the external mirroring reports counts, not ids —
    /// reloads the open task too. "Could not say" is not "did not".
    /// </summary>
    [Fact]
    public async Task A_synced_notification_naming_nothing_reloads_the_open_task()
    {
        var core = StartedCore(("l1", "Home", false))
            .AnswerOk("taskDetail", TaskDetail("t1"));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        await shell.OpenTaskAsync("t1");
        var detailBefore = core.SentKinds().Count(kind => kind == "taskDetail");

        core.AnswerOk("lists", Lists(("l1", "Home", false)))
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("taskDetail", TaskDetail("t1"))
            .AnswerOk("outboxStats", new { hasUnsentWork = false });
        core.NotifySynced();

        Assert.Equal(detailBefore + 1, core.SentKinds().Count(kind => kind == "taskDetail"));
    }

    /// <summary>
    /// A sync asked for while one is running is still sent. The core decides what to do with
    /// it — a person's pass waits for the slot — and an early return here was the bug that
    /// decision exists to prevent (task 3173727d): a refresh that landed during the background
    /// pass finished having fetched nothing.
    /// </summary>
    [Fact]
    public async Task A_sync_asked_for_during_another_is_still_sent_to_the_core()
    {
        var core = StartedCore(("l1", "Home", false));
        using var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        var before = core.SentKinds().Count(kind => kind == "sync");

        core.AnswerOk("sync", new { fetched = false })
            .AnswerOk("sync", new { fetched = false })
            .AnswerOk("outboxStats", new { hasUnsentWork = false })
            .AnswerOk("outboxStats", new { hasUnsentWork = false })
            .Hold("sync");
        var first = shell.SyncAsync();
        var second = shell.SyncAsync();
        Assert.True(shell.IsSyncing);
        core.Release("sync");
        await Task.WhenAll(first, second);

        Assert.Equal(before + 2, core.SentKinds().Count(kind => kind == "sync"));
        Assert.False(shell.IsSyncing);
    }

    /// <summary>
    /// The bell is read from the cache at start, refreshed after a sync that fetched, and redrawn
    /// when the timer's pass says the inbox moved.
    /// </summary>
    [Fact]
    public async Task The_bell_loads_at_start_refreshes_after_a_sync_and_follows_the_inbox()
    {
        var core = new FakeCore()
            .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false })
            .AnswerOk("lists", Lists(("l1", "Home", false)))
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("outboxStats", new { hasUnsentWork = false })
            .AnswerOk("notifications", new { unreadCount = 1, notifications = Array.Empty<object>() })
            .AnswerOk("sync", new { fetched = true })
            .AnswerOk("lists", Lists(("l1", "Home", false)))
            .AnswerOk("rowsForList", EmptyWindow())
            .AnswerOk("refreshNotifications", new { unreadCount = 3, notifications = Array.Empty<object>() })
            .AnswerOk("outboxStats", new { hasUnsentWork = false });
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.Contains("notifications", core.SentKinds());
        Assert.Contains("refreshNotifications", core.SentKinds());
        Assert.Equal(3, shell.Notifications.UnreadCount);

        core.AnswerOk("notifications", new { unreadCount = 4, notifications = Array.Empty<object>() });
        core.Notify("notifications");

        Assert.Equal(4, shell.Notifications.UnreadCount);
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
