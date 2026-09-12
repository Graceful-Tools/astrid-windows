namespace Astrid.App.Tests;

/// <summary>
/// The account answers the settings tests feed the fake core: the same shapes
/// <c>settings</c>, <c>refreshSettings</c> and the writes that answer with the account use.
/// </summary>
internal static class SettingsFixtures
{
    public static object Account(bool push = true, bool email = true, int offset = 15,
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

    public static object AccountOf(object user) => new
    {
        user,
        reminderSettings = new { enablePushReminders = true, enableEmailReminders = true },
        offsets = Array.Empty<object>(),
        timezone = "+00:00",
    };

    public static object WithSmartTasks(string offset, string time, string layout, bool email = true) => new
    {
        user = new { id = "me", name = "Jon", email = "jon@x.io" },
        reminderSettings = new { enablePushReminders = true, enableEmailReminders = true },
        offsets = Array.Empty<object>(),
        timezone = "+00:00",
        smartTasks = new
        {
            emailToTaskEnabled = email,
            defaultTaskDueOffset = offset,
            defaultDueTime = time,
            taskDisplayMode = layout,
            subtaskDisplay = "indented",
            smartTaskCreationEnabled = true,
        },
        dueOffsetChoices = new[]
        {
            new { value = "none", titleKey = "smart.offset.none" },
            new { value = "1_day", titleKey = "smart.offset.1_day" },
            new { value = "3_days", titleKey = "smart.offset.3_days" },
            new { value = "1_week", titleKey = "smart.offset.1_week" },
        },
        dueTimeChoices = new[]
        {
            new { value = "09:00", titleKey = "smart.time.09_00" },
            new { value = "17:00", titleKey = "smart.time.17_00" },
        },
        layoutChoices = new[]
        {
            new { value = "list", titleKey = "smart.layout.list" },
            new { value = "project", titleKey = "smart.layout.project" },
        },
    };

    public static object WithAppearance(bool parsing, string subtasks) => new
    {
        user = new { id = "me", name = "Jon", email = "jon@x.io" },
        reminderSettings = new { enablePushReminders = true, enableEmailReminders = true },
        offsets = Array.Empty<object>(),
        timezone = "+00:00",
        smartTasks = new
        {
            emailToTaskEnabled = true,
            defaultTaskDueOffset = "1_week",
            defaultDueTime = "17:00",
            taskDisplayMode = "list",
            subtaskDisplay = subtasks,
            smartTaskCreationEnabled = parsing,
        },
        dueOffsetChoices = Array.Empty<object>(),
        dueTimeChoices = Array.Empty<object>(),
        layoutChoices = Array.Empty<object>(),
        subtaskChoices = new[]
        {
            new { value = "indented", titleKey = "smart.subtasks.indented" },
            new { value = "under_parent", titleKey = "smart.subtasks.under_parent" },
        },
    };
}
