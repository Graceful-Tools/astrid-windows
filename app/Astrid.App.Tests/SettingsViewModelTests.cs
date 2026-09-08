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
            .AnswerOk("refreshSettings", Account())
            .AnswerOk("profileStats", new { completed = 1, inspired = 2, supported = 3 });
        var view = new SettingsViewModel(core);

        await view.LoadAsync();

        // The cache, then the server, then the numbers — which need to know who is signed in, so
        // they come after the account rather than beside it.
        Assert.Equal(["settings", "refreshSettings", "profileStats"], core.SentKinds());
        Assert.Equal("Jon", view.DisplayName);
        Assert.Equal("jon@example.test", view.Email);
        Assert.True(view.PushEnabled);
        Assert.Equal(2, view.Offsets.Count);
    }

    /// <summary>
    /// The mode arrives in a map beside the agents rather than on them, and joining the two in one
    /// place keeps every control that shows an agent from doing it again.
    /// </summary>
    [Fact]
    public async Task An_agents_mode_is_joined_from_the_map_beside_it()
    {
        var core = new FakeCore().AnswerOk("agents", new
        {
            agents = new[]
            {
                new { id = "astrid", name = "Astrid", description = (string?)"The one that answers" },
                new { id = "claude", name = "Claude", description = (string?)null },
            },
            modes = new Dictionary<string, string> { ["astrid"] = "api", ["claude"] = "webhook" },
            credentials = new[]
            {
                new { serviceId = "openai", name = "OpenAI", configured = true },
                new { serviceId = "anthropic", name = "Anthropic", configured = false },
            },
        });
        var view = new SettingsViewModel(core);

        await view.LoadAgentsAsync();

        Assert.Equal("api", view.Agents[0].Mode);
        Assert.False(view.Agents[0].NeedsOwnCredential);
        Assert.Equal("webhook", view.Agents[1].Mode);
        Assert.True(view.Agents[1].NeedsOwnCredential);
        Assert.True(view.Credentials[0].Configured);
    }

    /// <summary>An agent the modes map does not mention is off, not unknown.</summary>
    [Fact]
    public async Task An_agent_with_no_mode_is_off()
    {
        var core = new FakeCore().AnswerOk("agents", new
        {
            agents = new[] { new { id = "astrid", name = "Astrid" } },
            modes = new Dictionary<string, string>(),
            credentials = Array.Empty<object>(),
        });
        var view = new SettingsViewModel(core);

        await view.LoadAgentsAsync();

        Assert.Equal("off", view.Agents[0].Mode);
    }

    /// <summary>
    /// The mode belongs to the account, so the screen reads it back rather than assuming its own
    /// last answer — somebody who chose "every list" on another machine sees that here.
    /// </summary>
    [Fact]
    public async Task The_google_sync_mode_is_read_from_the_account()
    {
        var core = new FakeCore().AnswerOk("googleSyncMode", new
        {
            mode = "all_bidirectional",
            suffix = "(G)",
        });
        var view = new SettingsViewModel(core);

        await view.LoadGoogleSyncModeAsync();

        Assert.Equal("all_bidirectional", view.GoogleSyncMode);
    }

    /// <summary>Manual until the account says otherwise, since the all-lists modes make lists.</summary>
    [Fact]
    public void The_google_sync_mode_starts_manual()
    {
        Assert.Equal("manual", new SettingsViewModel(new FakeCore()).GoogleSyncMode);
    }

    /// <summary>A blank box is not a key.</summary>
    [Fact]
    public async Task An_empty_key_is_not_sent()
    {
        var core = new FakeCore();
        var view = new SettingsViewModel(core);

        Assert.False(await view.SaveCredentialAsync("openai", "   "));
        Assert.Empty(core.Sent);
    }

    /// <summary>
    /// The numbers come from the server, and a screen without them is not a broken screen — three
    /// missing statistics are not worth a message beside somebody's own name.
    /// </summary>
    [Fact]
    public async Task The_profile_numbers_are_loaded_but_never_insisted_on()
    {
        var core = new FakeCore()
            .AnswerOk("settings", Account())
            .AnswerOk("refreshSettings", Account())
            .AnswerFailure("profileStats", AstridFailureKind.Offline, "no network");
        var view = new SettingsViewModel(core);

        await view.LoadAsync();

        Assert.Null(view.ErrorMessage);
        Assert.Equal(0, view.Stats.Completed);
    }

    [Fact]
    public async Task An_export_says_where_it_was_written()
    {
        var core = new FakeCore()
            .AnswerOk("settings", Account())
            .AnswerOk("refreshSettings", Account())
            .AnswerOk("profileStats", new { completed = 12, inspired = 3, supported = 5 })
            .AnswerOk("exportAccount", new { path = "C:/exports/astrid.json", bytes = 2048 });
        var view = new SettingsViewModel(core);
        await view.LoadAsync();

        Assert.Equal(12, view.Stats.Completed);
        Assert.True(await view.ExportAsync("json", "C:/exports/astrid.json"));
        Assert.Equal("C:/exports/astrid.json", view.LastExportPath);
    }

    /// <summary>An export is a fetch, so offline it did not happen and says so.</summary>
    [Fact]
    public async Task An_export_offline_reports_rather_than_pretending()
    {
        var core = new FakeCore()
            .AnswerOk("settings", Account())
            .AnswerOk("refreshSettings", Account())
            .AnswerOk("profileStats", new { completed = 0, inspired = 0, supported = 0 })
            .AnswerFailure("exportAccount", AstridFailureKind.Offline, "no network");
        var view = new SettingsViewModel(core);
        await view.LoadAsync();

        Assert.False(await view.ExportAsync("json", "C:/exports/astrid.json"));
        Assert.NotNull(view.ErrorMessage);
        Assert.Null(view.LastExportPath);
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
