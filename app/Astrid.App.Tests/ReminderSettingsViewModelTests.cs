using Astrid.App.ViewModels;
using Xunit;
using static Astrid.App.Tests.SettingsFixtures;

namespace Astrid.App.Tests;

/// <summary>The Reminders page: how somebody wants to be reminded.</summary>
public sealed class ReminderSettingsViewModelTests
{
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
        var settings = new SettingsViewModel(core);
        var view = settings.Reminders;
        await settings.LoadAsync();

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
        var settings = new SettingsViewModel(core);
        var view = settings.Reminders;
        await settings.LoadAsync();

        await view.SetQuietHoursAsync("22:00", "08:00");
        Assert.True(view.QuietHoursEnabled);
        Assert.Equal("22:00", view.QuietHoursStart);

        await view.SetQuietHoursAsync(null, null);
        Assert.False(view.QuietHoursEnabled);

        var cleared = core.Sent.Last(sent => sent.Contains("updateReminderSettings"));
        Assert.Contains("\"quietHoursStart\":null", cleared);
        Assert.Contains("\"quietHoursEnd\":null", cleared);
    }
}
