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

    private static object AccountOf(object user) => new
    {
        user,
        reminderSettings = new { enablePushReminders = true, enableEmailReminders = true },
        offsets = Array.Empty<object>(),
        timezone = "+00:00",
    };

    private static object WithSmartTasks(string offset, string time, string layout, bool email = true) => new
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

    /// <summary>
    /// The Contacts page lists what the server holds, with a name when there is one and the
    /// address when there is not, and clearing empties it; offline says so (task 438494c7).
    /// </summary>
    [Fact]
    public async Task Contacts_are_listed_and_cleared_and_offline_says_so_task_438494c7()
    {
        var core = new FakeCore()
            .AnswerOk("contacts", new
            {
                total = 2,
                contacts = new[]
                {
                    new { id = "c1", email = "ann@x.io", name = (string?)"Ann" },
                    new { id = "c2", email = "bo@x.io", name = (string?)null },
                },
            })
            .AnswerOk("clearContacts", new { deleted = 2 })
            .AnswerFailure("contacts", AstridFailureKind.Offline, "no network");
        var view = new SettingsViewModel(core);
        Assert.False(view.ContactsAreEmpty, "not asked yet is not empty");

        Assert.True(await view.LoadContactsAsync());
        Assert.Equal(2, view.ContactsTotal);
        Assert.True(view.HasContacts);
        Assert.Equal("Ann", view.Contacts[0].Label);
        Assert.Equal("bo@x.io", view.Contacts[1].Label);

        Assert.True(await view.ClearContactsAsync());
        Assert.Empty(view.Contacts);
        Assert.True(view.ContactsAreEmpty);

        Assert.False(await view.LoadContactsAsync());
        Assert.Equal("Contacts need a connection.", view.ErrorMessage);
    }

    private static object WithAppearance(bool parsing, string subtasks) => new
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

    /// <summary>
    /// Appearance reads whether smart parsing is on and where subtasks go, writes each as one
    /// field, and announces a subtask change the way it announces a layout change — the rows are
    /// different now (task 6ac2639a).
    /// </summary>
    [Fact]
    public async Task Appearance_reads_smart_parsing_and_subtasks_and_a_subtask_change_redraws_rows_task_6ac2639a()
    {
        var core = new FakeCore()
            .AnswerOk("settings", WithAppearance(true, "indented"))
            .AnswerOk("updateSmartTaskSettings", WithAppearance(false, "indented"))
            .AnswerOk("updateSmartTaskSettings", WithAppearance(false, "under_parent"));
        var view = new SettingsViewModel(core);
        var redraws = 0;
        view.DisplayModeChanged += () => redraws++;

        await view.LoadAsync();
        Assert.True(view.SmartParsingEnabled);
        Assert.Equal(2, view.SubtaskChoices.Count);
        Assert.Equal("smart.subtasks.indented", view.SelectedSubtaskDisplay?.TitleKey);

        Assert.True(await view.SetSmartTaskAsync("smartTaskCreationEnabled", false));
        Assert.False(view.SmartParsingEnabled);
        Assert.Equal(0, redraws);
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"updateSmartTaskSettings\"")
            && json.Contains("\"changes\":{\"smartTaskCreationEnabled\":false}"));

        Assert.True(await view.SetSmartTaskAsync("subtaskDisplay", "under_parent"));
        Assert.Equal("under_parent", view.SelectedSubtaskDisplay?.Value);
        Assert.Equal(1, redraws);
    }

    /// <summary>
    /// The Tasks page reads the core's shaped defaults and its choices, lights the current one in
    /// each combo, writes one field per change, and says so when the layout changed — because the
    /// rows have to be redrawn for that one (task c0f3db19).
    /// </summary>
    [Fact]
    public async Task Task_settings_read_the_defaults_write_one_field_and_announce_a_layout_change_task_c0f3db19()
    {
        var core = new FakeCore()
            .AnswerOk("settings", WithSmartTasks("1_week", "17:00", "list"))
            .AnswerOk("updateSmartTaskSettings", WithSmartTasks("3_days", "17:00", "list"))
            .AnswerOk("updateSmartTaskSettings", WithSmartTasks("3_days", "17:00", "project"))
            .AnswerFailure("updateSmartTaskSettings", AstridFailureKind.BadRequest, "Invalid defaultTaskDueOffset value");
        var view = new SettingsViewModel(core);
        var layoutChanges = 0;
        view.DisplayModeChanged += () => layoutChanges++;

        await view.LoadAsync();
        Assert.True(view.EmailToTaskEnabled);
        Assert.Equal(4, view.DueOffsetChoices.Count);
        Assert.Equal("smart.offset.1_week", view.SelectedDueOffset?.TitleKey);
        Assert.Equal("17:00", view.SelectedDueTime?.Value);
        Assert.Equal("list", view.SelectedLayout?.Value);
        Assert.Equal("smart.layout.list_desc", view.LayoutDescriptionKey);

        Assert.True(await view.SetSmartTaskAsync("defaultTaskDueOffset", "3_days"));
        Assert.Equal("3_days", view.SelectedDueOffset?.Value);
        Assert.Equal(0, layoutChanges);
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"updateSmartTaskSettings\"")
            && json.Contains("\"changes\":{\"defaultTaskDueOffset\":\"3_days\"}"));

        Assert.True(await view.SetSmartTaskAsync("taskDisplayMode", "project"));
        Assert.Equal(1, layoutChanges);
        Assert.Equal("project", view.SelectedLayout?.Value);
        Assert.Equal("smart.layout.project_desc", view.LayoutDescriptionKey);

        Assert.False(await view.SetSmartTaskAsync("defaultTaskDueOffset", "2_weeks"));
        Assert.Equal("Invalid defaultTaskDueOffset value", view.ErrorMessage);
        Assert.Equal(1, layoutChanges); // a refused write changes nothing
    }

    /// <summary>
    /// The account's own state reads off the user the core answers with: verification as a key
    /// for the shell to word, the pending address, the dates as days, and the id (task 19fd9289).
    /// </summary>
    [Fact]
    public async Task The_account_page_reads_verification_and_dates_off_the_user_task_19fd9289()
    {
        var core = new FakeCore()
            .AnswerOk("settings", AccountOf(new
            {
                id = "me", name = "Jon", email = "jon@x.io", image = "https://blob.test/me.png",
                verified = false, hasPendingChange = true, pendingEmail = "new@x.io",
                createdAt = "2026-01-02T03:04:05Z", updatedAt = "2026-09-01T00:00:00Z",
            }))
            .AnswerOk("refreshSettings", AccountOf(new
            {
                id = "me", name = "Jon", email = "jon@x.io", image = (string?)null,
                verified = true, verifiedViaOAuth = true, hasPendingChange = false,
                createdAt = "2026-01-02T03:04:05Z", updatedAt = "2026-09-01T00:00:00Z",
            }))
            .AnswerOk("profileStats", new { completed = 1, inspired = 2, supported = 3 });
        var view = new SettingsViewModel(core);

        // The cache first: not verified, with an address waiting.
        await view.LoadAsync();
        // After the refresh: verified through the provider, nothing waiting, no photo.
        Assert.True(view.IsVerified);
        Assert.Equal("account.verified_via_provider", view.VerificationKey);
        Assert.False(view.HasPendingEmail);
        Assert.Null(view.PhotoUrl);
        Assert.Equal("me", view.AccountId);
        Assert.NotEqual(string.Empty, view.CreatedOn);
        Assert.Equal("Jon", view.NameDraft);
        Assert.False(view.CanSaveName, "the name on the account is not a change");

        var cached = new SettingsViewModel(new FakeCore().AnswerOk("settings", AccountOf(new
        {
            id = "me", name = "Jon", email = "jon@x.io",
            verified = false, hasPendingChange = true, pendingEmail = "new@x.io",
        })));
        await cached.LoadAsync();
        Assert.False(cached.IsVerified);
        Assert.Equal("account.not_verified", cached.VerificationKey);
        Assert.Equal("new@x.io", cached.PendingEmail);
        Assert.True(cached.HasPendingEmail);
    }

    /// <summary>
    /// The profile is saved on the button: a changed name goes as one write and the screen
    /// redraws from the answer; a photo goes by path for the core to upload (task 19fd9289).
    /// </summary>
    [Fact]
    public async Task Saving_the_name_or_a_photo_goes_through_one_profile_command_task_19fd9289()
    {
        var core = new FakeCore()
            .AnswerOk("settings", AccountOf(new { id = "me", name = "Jon", email = "jon@x.io" }))
            .AnswerOk("updateProfile", AccountOf(new { id = "me", name = "Jon P", email = "jon@x.io" }))
            .AnswerOk("updateProfile", AccountOf(new
            {
                id = "me", name = "Jon P", email = "jon@x.io", image = "https://blob.test/me.png",
            }));
        var view = new SettingsViewModel(core);
        await view.LoadAsync();

        view.NameDraft = " Jon P ";
        Assert.True(view.CanSaveName);
        Assert.True(await view.SaveNameAsync());
        Assert.Equal("Jon P", view.DisplayName);
        Assert.False(view.CanSaveName, "saved, so nothing left to save");
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"updateProfile\"") && json.Contains("\"name\":\"Jon P\"")
            && !json.Contains("photoPath"));

        Assert.True(await view.SetPhotoAsync(@"C:\Pictures\me.png"));
        Assert.Equal("https://blob.test/me.png", view.PhotoUrl);
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"updateProfile\"") && json.Contains("me.png")
            && !json.Contains("\"name\""));
    }

    /// <summary>
    /// Resend says so when it went, and says why when it did not; deleting is gated on the exact
    /// phrase and reports a refusal rather than pretending (task 19fd9289).
    /// </summary>
    [Fact]
    public async Task Resend_and_delete_report_what_happened_task_19fd9289()
    {
        var core = new FakeCore()
            .AnswerOk("settings", AccountOf(new { id = "me", name = "Jon", email = "jon@x.io", verified = false }))
            .AnswerOk("resendVerification", new { message = "Verification email sent" })
            .AnswerFailure("deleteAccount", AstridFailureKind.Refused, "Account authentication method not found")
            .AnswerOk("deleteAccount");
        var view = new SettingsViewModel(core);
        await view.LoadAsync();

        Assert.True(await view.ResendVerificationAsync());
        Assert.True(view.VerificationSent);

        view.DeleteConfirmation = "delete my account";
        Assert.False(view.CanDeleteAccount, "the phrase is exact, as the server's is");
        Assert.False(await view.DeleteAccountAsync());
        Assert.DoesNotContain(core.SentKinds(), kind => kind == "deleteAccount");

        view.DeleteConfirmation = SettingsViewModel.DeleteConfirmationPhrase;
        Assert.True(view.CanDeleteAccount);
        Assert.False(await view.DeleteAccountAsync());
        Assert.Equal("Account authentication method not found", view.ErrorMessage);

        Assert.True(await view.DeleteAccountAsync());
        Assert.Null(view.ErrorMessage);
        Assert.Equal(string.Empty, view.DeleteConfirmation);
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"deleteAccount\"") && json.Contains("\"confirmation\":\"DELETE MY ACCOUNT\""));
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

    // ── API access ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The point of the panel: a client that is already signed in mints its own credential rather
    /// than sending somebody to a browser to do what the client is authorised for.
    /// </summary>
    [Fact]
    public async Task A_token_is_minted_and_held_for_the_screen_to_show()
    {
        var core = new FakeCore().AnswerOk("createMcpToken", new { token = "mcp_live_abc" });
        var view = new SettingsViewModel(core);

        Assert.True(await view.CreateMcpTokenAsync());

        Assert.Equal("mcp_live_abc", view.McpToken);
        Assert.True(view.HasMcpToken);
    }

    /// <summary>
    /// A minted credential lives for as long as the screen showing it. That is the whole of its
    /// storage policy, and it only holds if something actually forgets.
    /// </summary>
    [Fact]
    public async Task Closing_the_panel_forgets_both_plaintexts()
    {
        var core = new FakeCore()
            .AnswerOk("createMcpToken", new { token = "mcp_live_abc" })
            .AnswerOk("createOAuthClient", new
            {
                clientId = "astrid_client_abc",
                clientSecret = "shown-once",
                name = "CI",
            })
            .AnswerOk("apiAccess", new { clients = Array.Empty<object>() });
        var view = new SettingsViewModel(core) { NewClientName = "CI" };

        await view.CreateMcpTokenAsync();
        await view.CreateOAuthClientAsync();
        Assert.True(view.HasMcpToken);
        Assert.True(view.HasMintedClient);

        view.ForgetMintedCredentials();

        Assert.Null(view.McpToken);
        Assert.Null(view.MintedClient);
    }

    /// <summary>
    /// Revoking leaves the copy on screen dead. Showing it afterwards offers somebody a credential
    /// to paste that has already stopped working.
    /// </summary>
    [Fact]
    public async Task Revoking_clears_the_token_on_screen()
    {
        var core = new FakeCore()
            .AnswerOk("createMcpToken", new { token = "mcp_live_abc" })
            .AnswerOk("revokeMcpTokens");
        var view = new SettingsViewModel(core);
        await view.CreateMcpTokenAsync();

        Assert.True(await view.RevokeMcpTokensAsync());

        Assert.Null(view.McpToken);
    }

    /// <summary>
    /// The secret exists in plaintext exactly once, in the creation answer. Registering and then
    /// reporting only success would leave the pair unusable and unrecoverable.
    /// </summary>
    [Fact]
    public async Task Registering_a_pair_keeps_the_secret_and_reloads_the_list()
    {
        var core = new FakeCore()
            .AnswerOk("createOAuthClient", new
            {
                clientId = "astrid_client_abc",
                clientSecret = "shown-once",
                name = "Windows fixall",
            })
            .AnswerOk("apiAccess", new
            {
                clients = new[]
                {
                    new
                    {
                        clientId = "astrid_client_abc",
                        name = "Windows fixall",
                        scopes = new[] { "tasks:read", "lists:read" },
                        isActive = true,
                    },
                },
            });
        var view = new SettingsViewModel(core) { NewClientName = "Windows fixall" };

        Assert.True(await view.CreateOAuthClientAsync());

        Assert.Equal("shown-once", view.MintedClient?.ClientSecret);
        Assert.Equal("astrid_client_abc", view.MintedClient?.ClientId);
        Assert.Single(view.ApiClients);
        Assert.Equal("tasks:read, lists:read", view.ApiClients[0].ScopeLabel);
        Assert.Equal(string.Empty, view.NewClientName);
    }

    /// <summary>A pair with no name is one nobody can identify later, so the button is off.</summary>
    [Fact]
    public void A_pair_needs_a_name_before_it_can_be_registered()
    {
        var view = new SettingsViewModel(new FakeCore());

        Assert.False(view.CanCreateClient);
        view.NewClientName = "   ";
        Assert.False(view.CanCreateClient);
        view.NewClientName = "CI";
        Assert.True(view.CanCreateClient);
    }

    /// <summary>
    /// Revoking the pair just made leaves its secret on screen pointing at nothing.
    /// </summary>
    [Fact]
    public async Task Revoking_the_pair_just_made_takes_its_secret_off_screen()
    {
        var core = new FakeCore()
            .AnswerOk("createOAuthClient", new
            {
                clientId = "astrid_client_abc",
                clientSecret = "shown-once",
                name = "CI",
            })
            .AnswerOk("apiAccess", new { clients = Array.Empty<object>() })
            .AnswerOk("deleteOAuthClient");
        var view = new SettingsViewModel(core) { NewClientName = "CI" };
        await view.CreateOAuthClientAsync();

        Assert.True(await view.DeleteOAuthClientAsync("astrid_client_abc"));

        Assert.Null(view.MintedClient);
    }

    /// <summary>
    /// Opening the page must not mint anything. A screen that created a credential by being looked
    /// at is a screen that fills an account with credentials nobody is holding.
    /// </summary>
    [Fact]
    public async Task Opening_the_page_reads_and_mints_nothing()
    {
        var core = new FakeCore().AnswerOk("apiAccess", new { clients = Array.Empty<object>() });
        var view = new SettingsViewModel(core);

        await view.LoadApiAccessAsync();

        Assert.DoesNotContain("createMcpToken", core.SentKinds());
        Assert.DoesNotContain("createOAuthClient", core.SentKinds());
    }
}
