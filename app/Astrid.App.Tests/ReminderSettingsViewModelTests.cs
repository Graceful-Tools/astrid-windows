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
    /// The calendar feed (task 28c5c6a9): the two fields and the address are read with the
    /// account, each field is written on its own, and the address is offered only while sync is
    /// on and something is included.
    /// </summary>
    [Fact]
    public async Task The_calendar_feed_is_read_written_one_field_at_a_time_and_offered_when_on_task_28c5c6a9()
    {
        object Account(bool enabled, string type) => new
        {
            user = new { id = "me", name = "Jon", email = "jon@x.io" },
            reminderSettings = new
            {
                enablePushReminders = true, enableEmailReminders = true,
                enableCalendarSync = enabled, calendarSyncType = type,
            },
            offsets = Array.Empty<object>(),
            timezone = "+00:00",
            calendarFeedUrl = "https://astrid.cc/api/calendar/tasks.ics",
        };
        var core = new FakeCore()
            .AnswerOk("settings", Account(true, "with_due_times"))
            .AnswerOk("refreshSettings", Account(true, "with_due_times"))
            .AnswerOk("updateReminderSettings", Account(true, "none"))
            .AnswerOk("updateReminderSettings", Account(false, "none"));
        var settings = new SettingsViewModel(core);
        var view = settings.Reminders;
        await settings.LoadAsync();

        Assert.True(view.CalendarSyncEnabled);
        Assert.Equal("with_due_times", view.CalendarSyncType);
        Assert.Equal("calendar.with_due_times", view.SelectedCalendarSyncChoice?.TitleKey);
        Assert.Equal("calendar.with_due_times_desc", view.CalendarSyncDescriptionKey);
        Assert.Equal("https://astrid.cc/api/calendar/tasks.ics", view.CalendarFeedUrl);
        Assert.True(view.HasCalendarFeed);

        Assert.True(await view.SetCalendarSyncTypeAsync("none"));
        var typed = core.Sent.Last(sent => sent.Contains("updateReminderSettings"));
        Assert.Contains("\"calendarSyncType\":\"none\"", typed);
        Assert.DoesNotContain("enableCalendarSync", typed);
        Assert.False(view.HasCalendarFeed, "nothing included, nothing to subscribe to");

        Assert.True(await view.SetCalendarSyncAsync(false));
        Assert.Contains("\"enableCalendarSync\":false", core.Sent.Last(sent => sent.Contains("updateReminderSettings")));
        Assert.False(view.CalendarSyncEnabled);
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
