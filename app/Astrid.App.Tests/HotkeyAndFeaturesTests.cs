using System.Text.Json;
using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>The rebindable chord, and the flags that gate the board and Google.</summary>
public sealed class HotkeyAndFeaturesTests
{
    private static readonly Action<Func<Task>> RunInline = work => work().GetAwaiter().GetResult();

    [Fact]
    public async Task The_chord_is_read_from_the_core_and_a_change_is_announced()
    {
        var core = new FakeCore()
            .AnswerOk("hotkey", new { chord = "Ctrl+Shift+A", ctrl = true, shift = true, key = "A" })
            .AnswerOk("setHotkey", new { chord = "Ctrl+Alt+Q", ctrl = true, alt = true, key = "Q" });
        var view = new SettingsViewModel(core);
        Hotkey? announced = null;
        view.HotkeyChanged += chord => announced = chord;

        await view.LoadHotkeyAsync();
        Assert.Equal("Ctrl+Shift+A", view.HotkeyChord);

        Assert.True(await view.SetHotkeyAsync("ctrl+alt+q"));
        Assert.Equal("Ctrl+Alt+Q", view.HotkeyChord);
        Assert.Equal("Q", announced?.Key);
        Assert.True(announced?.Alt);
        using var sent = JsonDocument.Parse(core.Sent.Last());
        Assert.Equal("ctrl+alt+q", sent.RootElement.GetProperty("chord").GetString());
    }

    /// <summary>The core judges the chord; its reason reaches the screen and nothing changes.</summary>
    [Fact]
    public async Task A_chord_the_core_refuses_is_reported_and_kept_out()
    {
        var core = new FakeCore()
            .AnswerOk("hotkey", new { chord = "Ctrl+Shift+A", ctrl = true, shift = true, key = "A" })
            .AnswerFailure("setHotkey", AstridFailureKind.BadRequest, "a global shortcut needs Ctrl, Alt or Win");
        var view = new SettingsViewModel(core);
        await view.LoadHotkeyAsync();
        var announced = false;
        view.HotkeyChanged += _ => announced = true;

        Assert.False(await view.SetHotkeyAsync("Shift+A"));

        Assert.Equal("a global shortcut needs Ctrl, Alt or Win", view.ErrorMessage);
        Assert.Equal("Ctrl+Shift+A", view.HotkeyChord);
        Assert.False(announced);
    }

    private static FakeCore StartedCore() => new FakeCore()
        .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false })
        .AnswerOk("lists", new[] { new { id = "l1", name = "Home" } })
        .AnswerOk("rowsForList", new { total = 0, offset = 0, rows = Array.Empty<object>() })
        .AnswerOk("outboxStats", new { hasUnsentWork = false })
        .AnswerOk("notifications", new { unreadCount = 0, notifications = Array.Empty<object>() })
        .AnswerOk("sync", new { fetched = false });

    /// <summary>
    /// A flag the server has not been asked about is not "off": the web only hides what it has
    /// been told to hide, and a board that vanished on every fresh install would be a bug report.
    /// </summary>
    [Fact]
    public async Task Flags_never_fetched_hide_nothing()
    {
        var core = StartedCore().AnswerOk("features", new { projectMode = (bool?)null, googleTasks = (bool?)null });
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.True(shell.ProjectModeEnabled);
        Assert.True(shell.GoogleTasksEnabled);
    }

    [Fact]
    public async Task Flags_the_server_answered_gate_the_board_and_google()
    {
        var core = StartedCore().AnswerOk("features", new { projectMode = false, googleTasks = true });
        using var shell = new ShellViewModel(core, RunInline);

        await shell.StartAsync();

        Assert.False(shell.ProjectModeEnabled);
        Assert.True(shell.GoogleTasksEnabled);
    }

    /// <summary>The three answers to "whose task is this?", as the row draws them.</summary>
    [Fact]
    public void A_row_says_which_leading_control_it_wears()
    {
        var yours = new TaskRow { Leading = new LeadingControl { Kind = "checkbox" } };
        var theirs = new TaskRow { Leading = new LeadingControl { Kind = "avatar", UserId = "u2" }, Assignee = new UserSummary { Id = "u2", Name = "dana" } };
        var nobody = new TaskRow { Leading = new LeadingControl { Kind = "unassigned" } };

        Assert.True(yours.LeadingIsCheckbox);
        Assert.False(yours.LeadingIsSquare);
        Assert.True(theirs.LeadingIsSquare);
        Assert.Equal("D", theirs.AssigneeInitial);
        Assert.True(nobody.LeadingIsSquare);
    }
}
