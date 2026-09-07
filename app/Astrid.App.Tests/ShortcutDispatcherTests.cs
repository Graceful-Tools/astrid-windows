using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The shell's half of the keyboard: what it does once the core has said what a key means.
/// </summary>
/// <remarks>
/// The meaning itself — and the guard about when a key may fire — is a cross-platform contract
/// tested in <c>astrid_core::keyboard</c> against a fixture generated from web. These check the
/// dispatch: that the right thing happens, that it happens to the selected row, and that a key the
/// scheme owns is never left to fall through to the list underneath.
/// </remarks>
public sealed class ShortcutDispatcherTests
{
    private static readonly Action<Func<Task>> RunInline = work => work().GetAwaiter().GetResult();

    private static object Window(params string[] titles) => new
    {
        total = titles.Length,
        offset = 0,
        rows = titles.Select((title, index) => new
        {
            id = $"t{index}",
            title,
            completed = false,
            priority = 0,
            due = new { key = "none" },
            leading = new { kind = "unassigned" },
            action = "complete",
            listChips = Array.Empty<object>(),
        }).ToArray(),
    };

    private static async Task<(FakeCore Core, ShellViewModel Shell, ShortcutDispatcher Keys)> Started(
        params string[] titles)
    {
        var core = new FakeCore()
            .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false })
            .AnswerOk("lists", new object[] { new { id = "l1", name = "Home" } })
            .AnswerOk("rowsForList", Window(titles))
            .AnswerOk("outboxStats", new { hasUnsentWork = false })
            .AnswerOk("sync", new { fetched = false });
        var shell = new ShellViewModel(core, RunInline);
        await shell.StartAsync();
        return (core, shell, new ShortcutDispatcher(core, shell));
    }

    [Fact]
    public async Task Moving_down_selects_the_first_row_when_nothing_is_selected()
    {
        var (core, shell, keys) = await Started("one", "two");
        core.AnswerOk("resolveShortcut", new { action = "selectNext" });

        Assert.True(await keys.HandleAsync("ArrowDown", isTextFieldFocused: false, isModalPresented: false));
        Assert.Equal("one", shell.Tasks.Selected?.Title);
    }

    /// <summary>
    /// A list that jumps from the last row to the first when somebody holds a direction key reads
    /// as a bug, whatever the intent.
    /// </summary>
    [Fact]
    public async Task The_selection_stops_at_the_ends_rather_than_wrapping()
    {
        var (core, shell, keys) = await Started("one", "two");
        for (var press = 0; press < 4; press++)
        {
            core.AnswerOk("resolveShortcut", new { action = "selectNext" });
            await keys.HandleAsync("ArrowDown", false, false);
        }
        Assert.Equal("two", shell.Tasks.Selected?.Title);

        for (var press = 0; press < 4; press++)
        {
            core.AnswerOk("resolveShortcut", new { action = "selectPrevious" });
            await keys.HandleAsync("ArrowUp", false, false);
        }
        Assert.Equal("one", shell.Tasks.Selected?.Title);
    }

    /// <summary>Completing from the keyboard uses the command that rolls repeating tasks over.</summary>
    [Fact]
    public async Task Completing_from_the_keyboard_acts_on_the_selected_row()
    {
        var (core, shell, keys) = await Started("one", "two");
        shell.Tasks.Selected = shell.Tasks.Rows[1];
        core.AnswerOk("resolveShortcut", new { action = "completeTask" })
            .AnswerOk("completeTask")
            .AnswerOk("rowsForList", Window("one", "two"));

        await keys.HandleAsync("x", false, false);

        var sent = core.Sent.Last(json => json.Contains("completeTask", StringComparison.Ordinal));
        Assert.Contains("\"taskId\":\"t1\"", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("updateTask", core.SentKinds());
    }

    [Fact]
    public async Task Setting_a_priority_sends_the_number_the_server_stores()
    {
        var (core, shell, keys) = await Started("one");
        shell.Tasks.Selected = shell.Tasks.Rows[0];
        core.AnswerOk("resolveShortcut", new { action = "priorityHigh" })
            .AnswerOk("updateTask")
            .AnswerOk("rowsForList", Window("one"));

        await keys.HandleAsync("3", false, false);

        var sent = core.Sent.Last(json => json.Contains("updateTask", StringComparison.Ordinal));
        Assert.Contains("\"priority\":3", sent, StringComparison.Ordinal);
    }

    /// <summary>
    /// Clearing a due date sends an explicit null. An absent field would leave the date exactly
    /// where it was, and the key would appear to do nothing.
    /// </summary>
    [Fact]
    public async Task Removing_a_due_date_sends_a_null()
    {
        var (core, shell, keys) = await Started("one");
        shell.Tasks.Selected = shell.Tasks.Rows[0];
        core.AnswerOk("resolveShortcut", new { action = "removeDueDate" })
            .AnswerOk("updateTask")
            .AnswerOk("rowsForList", Window("one"));

        await keys.HandleAsync("r", false, false);

        var sent = core.Sent.Last(json => json.Contains("updateTask", StringComparison.Ordinal));
        Assert.Contains("\"dueDateTime\":null", sent, StringComparison.Ordinal);
    }

    /// <summary>
    /// The core says a key is not allowed to fire — somebody is typing — and the dispatcher does
    /// nothing and says so, leaving the keystroke to the text box.
    /// </summary>
    [Fact]
    public async Task A_key_the_core_suppresses_is_left_alone()
    {
        var (core, shell, keys) = await Started("one");
        shell.Tasks.Selected = shell.Tasks.Rows[0];
        core.AnswerOk("resolveShortcut", new { action = (string?)null });

        Assert.False(await keys.HandleAsync("x", isTextFieldFocused: true, isModalPresented: false));
        Assert.DoesNotContain("completeTask", core.SentKinds());
    }

    /// <summary>
    /// A shortcut the scheme owns but this build has no behaviour for is swallowed. Letting it
    /// fall through would make a key that is supposed to do one thing do something else.
    /// </summary>
    [Fact]
    public async Task A_shortcut_with_no_behaviour_yet_is_still_consumed()
    {
        var (core, _, keys) = await Started("one");
        core.AnswerOk("resolveShortcut", new { action = "cycleFilters" });

        Assert.True(await keys.HandleAsync("f", false, false));
    }

    /// <summary>Actions the window owns are asked for, not done here.</summary>
    [Fact]
    public async Task The_window_is_asked_for_what_only_it_can_do()
    {
        var (core, _, keys) = await Started("one");
        core.AnswerOk("resolveShortcut", new { action = "newTask" });
        string? asked = null;
        keys.ShellActionRequested += action => asked = action;

        Assert.True(await keys.HandleAsync("n", false, false));
        Assert.Equal("newTask", asked);
    }

    /// <summary>
    /// A selection that vanished whenever a colleague edited something in the same list would make
    /// the keyboard unusable.
    /// </summary>
    [Fact]
    public async Task The_selection_survives_a_refresh()
    {
        var (core, shell, _) = await Started("one", "two");
        shell.Tasks.Selected = shell.Tasks.Rows[1];

        core.AnswerOk("rowsForList", Window("one", "two renamed"));
        await shell.Tasks.RefreshAsync();

        Assert.Equal("t1", shell.Tasks.Selected?.Id);
        Assert.Equal("two renamed", shell.Tasks.Selected?.Title);
    }
}
