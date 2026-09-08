using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The account screen: who is signed in, and how they want to be reminded.
/// </summary>
public sealed class SettingsViewModelTests
{
    private static object Account(bool push = true, bool email = true, int offset = 15,
        string? quietStart = null) => new
    {
        user = new { id = "me", name = "Jon", email = "jon@example.test" },
        reminderSettings = new
        {
            enablePushReminders = push,
            enableEmailReminders = email,
            defaultReminderTime = offset,
            enableDailyDigest = false,
            dailyDigestTime = "09:00",
            quietHoursStart = quietStart,
            quietHoursEnd = quietStart is null ? null : "08:00",
        },
        offsets = new[]
        {
            new { titleKey = "reminder.at_due_time", minutes = 0 },
            new { titleKey = "reminder.15_minutes_before", minutes = 15 },
        },
        timezone = "+00:00",
    };

    /// <summary>
    /// The cache first, the server second: an account screen that opens onto a spinner when the
    /// answer is already on this machine is the same mistake as a task list that does.
    /// </summary>
    [Fact]
    public async Task Loading_reads_the_cache_before_it_asks_the_server()
    {
        var core = new FakeCore()
            .AnswerOk("settings", Account())
            .AnswerOk("refreshSettings", Account());
        var view = new SettingsViewModel(core);

        await view.LoadAsync();

        Assert.Equal(["settings", "refreshSettings"], core.SentKinds());
        Assert.Equal("Jon", view.DisplayName);
        Assert.Equal("jon@example.test", view.Email);
        Assert.True(view.PushEnabled);
        Assert.Equal(2, view.Offsets.Count);
    }

    /// <summary>
    /// One toggle at a time. The core merges, so a screen sending a single field must not clear
    /// everything else — possibly set on another client.
    /// </summary>
    [Fact]
    public async Task Changing_one_setting_sends_only_that_one()
    {
        var core = new FakeCore()
            .AnswerOk("settings", Account())
            .AnswerOk("refreshSettings", Account())
            .AnswerOk("updateReminderSettings", Account(push: false));
        var view = new SettingsViewModel(core);
        await view.LoadAsync();

        Assert.True(await view.SetAsync("enablePushReminders", false));

        var update = core.Sent.First(sent => sent.Contains("updateReminderSettings"));
        Assert.Contains("\"enablePushReminders\":false", update);
        Assert.DoesNotContain("enableEmailReminders", update);
        Assert.False(view.PushEnabled);
    }

    /// <summary>
    /// Quiet hours travel as a pair: the server reads their absence as "none", and a window with
    /// only one end is something nothing can act on.
    /// </summary>
    [Fact]
    public async Task Quiet_hours_are_set_and_cleared_at_both_ends()
    {
        var core = new FakeCore()
            .AnswerOk("settings", Account())
            .AnswerOk("refreshSettings", Account())
            .AnswerOk("updateReminderSettings", Account(quietStart: "22:00"))
            .AnswerOk("updateReminderSettings", Account());
        var view = new SettingsViewModel(core);
        await view.LoadAsync();

        await view.SetQuietHoursAsync("22:00", "08:00");
        Assert.True(view.QuietHoursEnabled);

        await view.SetQuietHoursAsync(null, null);
        Assert.False(view.QuietHoursEnabled);

        var cleared = core.Sent.Last(sent => sent.Contains("updateReminderSettings"));
        Assert.Contains("\"quietHoursStart\":null", cleared);
        Assert.Contains("\"quietHoursEnd\":null", cleared);
    }

    /// <summary>
    /// Offline, what is on screen came from the cache and is still what this account last chose,
    /// so there is nothing to report.
    /// </summary>
    [Fact]
    public async Task Failing_to_catch_up_while_offline_says_nothing()
    {
        var core = new FakeCore()
            .AnswerOk("settings", Account())
            .AnswerFailure("refreshSettings", AstridFailureKind.Offline, "no network");
        var view = new SettingsViewModel(core);

        await view.LoadAsync();

        Assert.Null(view.ErrorMessage);
        Assert.Equal("Jon", view.DisplayName);
    }
}
