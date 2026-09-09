using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// Sharing a list: who is on it, and what this account may do about it.
/// </summary>
public sealed class ListSettingsViewModelTests
{
    private static object Settings(bool canManage = true, bool canLeave = false,
        bool canDelete = true) => new
    {
        listId = "l1",
        name = "Work",
        ownerId = "me",
        canManageMembers = canManage,
        canManageList = canManage,
        canDeleteList = canDelete,
        canLeave,
        currentUserId = "me",
        members = new[]
        {
            new { userId = "me", role = "owner", user = new { id = "me", name = "Jon" } },
            new { userId = "dana", role = "member", user = new { id = "dana", name = "Dana" } },
        },
    };

    /// <summary>
    /// Colour, favourite and privacy have controls (task 53780e75): each is read from the
    /// settings and each writes through the core, the colour and privacy as list edits and the
    /// favourite as its own command, since it is this account's rather than the list's.
    /// </summary>
    [Fact]
    public async Task Colour_favourite_and_privacy_are_read_and_written_task_53780e75()
    {
        var core = new FakeCore()
            .AnswerOk("listMembers", new
            {
                listId = "l1",
                name = "Work",
                color = "#ef4444",
                colorChoices = new[] { "#ef4444", "#22c55e", "#3b82f6" },
                privacy = "SHARED",
                isFavorite = false,
                canManageList = true,
                members = Array.Empty<object>(),
            })
            .AnswerOk("updateList")
            .AnswerOk("setListFavorite")
            .AnswerOk("updateList");
        var view = new ListSettingsViewModel(core);

        await view.LoadAsync("l1");
        Assert.Equal("#ef4444", view.Color);
        Assert.Equal(3, view.ColorChoices.Count);
        Assert.True(view.ColorChoices[0].IsSelected);
        Assert.False(view.ColorChoices[1].IsSelected);
        Assert.True(view.IsShared);
        Assert.False(view.IsFavorite);

        Assert.True(await view.SetColorAsync("#22c55e"));
        Assert.Contains("\"color\":\"#22c55e\"", core.Sent.First(sent => sent.Contains("updateList")));
        Assert.Equal("#22c55e", view.Color);
        Assert.True(view.ColorChoices[1].IsSelected, "the swatch follows the colour");
        Assert.False(view.ColorChoices[0].IsSelected);

        Assert.True(await view.SetFavoriteAsync(true));
        var favourite = core.Sent.First(sent => sent.Contains("setListFavorite"));
        Assert.Contains("\"favorite\":true", favourite);
        Assert.True(view.IsFavorite);

        Assert.True(await view.SetPrivacyAsync("PUBLIC"));
        Assert.Contains("\"privacy\":\"PUBLIC\"", core.Sent.Last(sent => sent.Contains("updateList")));
        Assert.True(view.IsPublic);
        Assert.False(view.IsShared);
    }

    /// <summary>
    /// The agent that picks up a list's tasks and the repository it commits to are offered from
    /// what the account can use, and each choice writes its own field (task f44b4a0c).
    /// </summary>
    [Fact]
    public async Task The_list_s_agent_and_repository_are_offered_and_written_task_f44b4a0c()
    {
        var core = new FakeCore()
            .AnswerOk("listMembers", new
            {
                listId = "l1",
                name = "Work",
                canManageList = true,
                defaultAgentId = (string?)null,
                githubRepositoryId = (string?)null,
                members = Array.Empty<object>(),
            })
            .AnswerOk("listAgentOptions", new
            {
                defaultAgentId = (string?)null,
                githubRepositoryId = (string?)null,
                agents = new[] { new { id = "ai-agent-claude", name = "Claude Agent" } },
                repositories = new[] { new { fullName = "Graceful-Tools/astrid-windows", name = "astrid-windows" } },
                githubConnected = true,
            })
            .AnswerOk("updateList")
            .AnswerOk("updateList");
        var view = new ListSettingsViewModel(core);
        await view.LoadAsync("l1");

        await view.LoadAgentOptionsAsync();

        Assert.Equal(2, view.AgentChoices.Count);
        Assert.Equal("defaults.account_agent", view.SelectedAgent?.TitleKey);
        Assert.Equal("Claude Agent", view.AgentChoices[1].Text);
        Assert.Equal(2, view.RepositoryChoices.Count);
        Assert.Equal("defaults.no_repository", view.SelectedRepository?.TitleKey);
        Assert.False(view.NeedsGithub);

        Assert.True(await view.ChooseDefaultAsync(view.AgentChoices[1]));
        Assert.Contains("\"defaultAgentId\":\"ai-agent-claude\"", core.Sent.First(sent => sent.Contains("updateList")));
        Assert.Equal("ai-agent-claude", view.DefaultAgentId);
        Assert.Equal("ai-agent-claude", view.SelectedAgent?.Value);

        Assert.True(await view.ChooseDefaultAsync(view.RepositoryChoices[1]));
        Assert.Contains("\"githubRepositoryId\":\"Graceful-Tools/astrid-windows\"", core.Sent.Last(sent => sent.Contains("updateList")));
        Assert.Equal("Graceful-Tools/astrid-windows", view.SelectedRepository?.Value);
    }

    /// <summary>Without GitHub there are no repositories to offer, and the screen can say why.</summary>
    [Fact]
    public async Task Without_github_the_repository_choice_says_to_connect_it()
    {
        var core = new FakeCore()
            .AnswerOk("listMembers", Settings())
            .AnswerOk("listAgentOptions", new
            {
                defaultAgentId = (string?)null,
                githubRepositoryId = "Graceful-Tools/astrid-web",
                agents = Array.Empty<object>(),
                repositories = Array.Empty<object>(),
                githubConnected = false,
            });
        var view = new ListSettingsViewModel(core);
        await view.LoadAsync("l1");

        await view.LoadAgentOptionsAsync();

        Assert.True(view.NeedsGithub);
        // The repository set on the web is still shown as set, not silently read as none.
        Assert.Equal("Graceful-Tools/astrid-web", view.SelectedRepository?.Value);
    }

    /// <summary>
    /// A list with a board offers its columns, and each change goes through the core and comes
    /// back re-read (task e5214fba).
    /// </summary>
    [Fact]
    public async Task Board_columns_are_listed_and_changed_from_the_settings_task_e5214fba()
    {
        object Settings(params object[] statuses) => new
        {
            listId = "l1",
            name = "Work",
            projectId = "p1",
            canManageList = true,
            statuses,
            members = Array.Empty<object>(),
        };
        var ready = new { id = "ready", name = "Ready", isDefault = true };
        var review = new { id = "custom-review", name = "Review", isDefault = false };
        var renamed = new { id = "custom-review", name = "In review", isDefault = false };
        var core = new FakeCore()
            .AnswerOk("listMembers", Settings(ready))
            .AnswerOk("addBoardStatus", new { state = review })
            .AnswerOk("listMembers", Settings(ready, review))
            .AnswerOk("renameBoardStatus", new { state = renamed })
            .AnswerOk("listMembers", Settings(ready, renamed))
            .AnswerOk("reorderBoardStatus", new { state = renamed })
            .AnswerOk("listMembers", Settings(ready, renamed))
            .AnswerOk("removeBoardStatus", new { state = renamed })
            .AnswerOk("listMembers", Settings(ready));
        var view = new ListSettingsViewModel(core);

        await view.LoadAsync("l1");
        Assert.True(view.HasBoard);
        Assert.Single(view.Statuses);
        Assert.True(view.Statuses[0].IsDefault);

        Assert.True(await view.AddStatusAsync(" Review "));
        Assert.Contains("\"name\":\"Review\"", core.Sent.First(sent => sent.Contains("addBoardStatus")));
        Assert.Equal(2, view.Statuses.Count);
        Assert.True(view.Statuses[1].IsCustom);

        Assert.True(await view.RenameStatusAsync("custom-review", "In review"));
        Assert.Contains("\"role\":\"custom-review\"", core.Sent.First(sent => sent.Contains("renameBoardStatus")));
        Assert.Equal("In review", view.Statuses[1].Name);

        Assert.True(await view.MoveStatusAsync("custom-review", "up"));
        Assert.Contains("\"direction\":\"up\"", core.Sent.First(sent => sent.Contains("reorderBoardStatus")));

        Assert.True(await view.RemoveStatusAsync("custom-review"));
        Assert.Single(view.Statuses);

        // The same name again is not a rename, and an empty one is not an add.
        Assert.False(await view.RenameStatusAsync("ready", "Ready"));
        Assert.False(await view.AddStatusAsync("   "));
    }

    /// <summary>A refusal from the core — the web's own message — is shown, not swallowed.</summary>
    [Fact]
    public async Task A_refused_column_name_is_reported_in_the_web_s_words()
    {
        var core = new FakeCore()
            .AnswerOk("listMembers", new { listId = "l1", name = "Work", projectId = "p1", statuses = Array.Empty<object>(), members = Array.Empty<object>() })
            .AnswerFailure("addBoardStatus", AstridFailureKind.BadRequest, "A status with that name already exists");
        var view = new ListSettingsViewModel(core);
        await view.LoadAsync("l1");

        Assert.False(await view.AddStatusAsync("Review"));

        Assert.Equal("A status with that name already exists", view.ErrorMessage);
    }

    /// <summary>A list with no board offers no columns.</summary>
    [Fact]
    public async Task A_list_without_a_board_has_no_columns_to_manage()
    {
        var core = new FakeCore().AnswerOk("listMembers", Settings());
        var view = new ListSettingsViewModel(core);

        await view.LoadAsync("l1");

        Assert.False(view.HasBoard);
        Assert.Empty(view.Statuses);
    }

    /// <summary>
    /// The defaults for new tasks are read from the settings and each choice writes its own field
    /// (task c4102c67); choosing no When also resets the repeat, as the web does.
    /// </summary>
    [Fact]
    public async Task Defaults_for_new_tasks_are_offered_and_written_task_c4102c67()
    {
        var core = new FakeCore()
            .AnswerOk("listMembers", new
            {
                listId = "l1",
                name = "Work",
                canManageList = true,
                defaults = new { assigneeId = "dana", priority = 3, repeating = "weekly", dueDate = "tomorrow", dueTime = "17:00" },
                members = new[]
                {
                    new { userId = "me", role = "owner", user = new { id = "me", name = "Jon" } },
                    new { userId = "dana", role = "member", user = new { id = "dana", name = "Dana" } },
                },
            })
            .AnswerOk("updateList")
            .AnswerOk("updateList")
            .AnswerOk("updateList");
        var view = new ListSettingsViewModel(core);

        await view.LoadAsync("l1");

        Assert.Equal("3", view.SelectedDefaultPriority?.Value);
        Assert.Equal("Dana", view.SelectedDefaultAssignee?.Text);
        Assert.Equal(4, view.DefaultAssigneeChoices.Count); // creator, unassigned, and the two members
        Assert.Equal("defaults.task_creator", view.DefaultAssigneeChoices[0].TitleKey);
        Assert.Equal("weekly", view.SelectedDefaultRepeat?.Value);
        Assert.Equal("tomorrow", view.SelectedDefaultWhen?.Value);
        Assert.Equal("17:00", view.SelectedDefaultTime?.Value);
        Assert.Equal("defaults.all_day", view.DefaultTimeChoices[0].TitleKey);

        Assert.True(await view.ChooseDefaultAsync(view.DefaultPriorityChoices[1]));
        Assert.Contains("\"defaultPriority\":1", core.Sent.First(sent => sent.Contains("updateList")));
        Assert.Equal("1", view.SelectedDefaultPriority?.Value);

        var unassigned = view.DefaultAssigneeChoices.First(choice => choice.Value == "unassigned");
        Assert.True(await view.ChooseDefaultAsync(unassigned));
        Assert.Contains("\"defaultAssigneeId\":\"unassigned\"", core.Sent[2]);

        var none = view.DefaultWhenChoices.First(choice => choice.Value == "none");
        Assert.True(await view.ChooseDefaultAsync(none));
        Assert.Contains("\"defaultDueDate\":\"none\"", core.Sent[3]);
        Assert.Contains("\"defaultRepeating\":\"never\"", core.Sent[3]);
        Assert.Equal("never", view.SelectedDefaultRepeat?.Value);

        // The chosen one writes nothing.
        Assert.False(await view.ChooseDefaultAsync(view.SelectedDefaultWhen!));
        Assert.Equal(4, core.Sent.Count);
    }

    /// <summary>A time set on the web that is not on the hour is still shown, not rounded away.</summary>
    [Fact]
    public async Task A_stored_time_off_the_hour_is_still_offered()
    {
        var core = new FakeCore().AnswerOk("listMembers", new
        {
            listId = "l1",
            name = "Work",
            defaults = new { assigneeId = (string?)null, priority = 0, repeating = "never", dueDate = "today", dueTime = "09:15" },
            members = Array.Empty<object>(),
        });
        var view = new ListSettingsViewModel(core);

        await view.LoadAsync("l1");

        Assert.Equal("09:15", view.SelectedDefaultTime?.Value);
        Assert.Equal("defaults.task_creator", view.SelectedDefaultAssignee?.TitleKey);
    }

    /// <summary>Choosing what is already chosen writes nothing.</summary>
    [Fact]
    public async Task Re_choosing_the_current_colour_favourite_or_privacy_writes_nothing()
    {
        var core = new FakeCore().AnswerOk("listMembers", new
        {
            listId = "l1",
            name = "Work",
            color = "#ef4444",
            colorChoices = new[] { "#ef4444" },
            privacy = "PRIVATE",
            isFavorite = true,
            members = Array.Empty<object>(),
        });
        var view = new ListSettingsViewModel(core);
        await view.LoadAsync("l1");

        Assert.False(await view.SetColorAsync("#ef4444"));
        Assert.False(await view.SetFavoriteAsync(true));
        Assert.False(await view.SetPrivacyAsync("PRIVATE"));

        Assert.Equal(["listMembers"], core.SentKinds());
    }

    [Fact]
    public async Task Loading_shows_the_members_and_what_this_account_may_do()
    {
        var core = new FakeCore().AnswerOk("listMembers", Settings());
        var view = new ListSettingsViewModel(core);

        await view.LoadAsync("l1");

        Assert.Equal("Work", view.Name);
        Assert.Equal(2, view.Members.Count);
        Assert.Equal("Dana", view.Members[1].DisplayName);
        Assert.Equal("role.member", view.Members[1].RoleKey);
        Assert.True(view.CanManageMembers);
    }

    /// <summary>
    /// A member whose user record never arrived is still a person on screen. The Mac put a raw
    /// UUID where a name goes, which reads as a bug because it is one.
    /// </summary>
    [Fact]
    public async Task A_member_with_no_profile_still_shows_something()
    {
        var core = new FakeCore().AnswerOk("listMembers", new
        {
            listId = "l1",
            name = "Work",
            members = new[] { new { userId = "u1", role = "member" } },
        });
        var view = new ListSettingsViewModel(core);

        await view.LoadAsync("l1");

        Assert.Equal("u1", view.Members[0].DisplayName);
    }

    private static object External(bool googleConnected, string? linkedTo = null) => new
    {
        listId = "l1",
        providers = new object[]
        {
            new
            {
                provider = "google_tasks",
                connected = googleConnected,
                containers = googleConnected
                    ? new object[] { new { id = "g1", name = "Groceries" } }
                    : Array.Empty<object>(),
                link = linkedTo is null
                    ? null
                    : (object)new { id = "link-1", astridListId = "l1", remoteContainerId = linkedTo },
            },
            new { provider = "git_hub", connected = false, containers = Array.Empty<object>(), link = (object?)null },
        },
    };

    /// <summary>
    /// A provider that is not connected offers nothing to mirror to — asking for somebody's task
    /// lists before they have said yes can only 401.
    /// </summary>
    [Fact]
    public async Task An_unconnected_provider_offers_no_containers()
    {
        var core = new FakeCore()
            .AnswerOk("listMembers", Settings())
            .AnswerOk("externalSync", External(googleConnected: false));
        var view = new ListSettingsViewModel(core);
        await view.LoadAsync("l1");

        await view.LoadExternalAsync();

        Assert.Equal(2, view.Providers.Count);
        Assert.False(view.Providers[0].Connected);
        Assert.Empty(view.Providers[0].Containers);
        Assert.False(view.Providers[0].IsLinked);
        Assert.Equal("Google Tasks", view.Providers[0].Name);
        Assert.Equal("GitHub", view.Providers[1].Name);
    }

    [Fact]
    public async Task Mirroring_a_list_links_it_and_reloads()
    {
        var core = new FakeCore()
            .AnswerOk("listMembers", Settings())
            .AnswerOk("externalSync", External(googleConnected: true))
            .AnswerOk("linkList")
            .AnswerOk("externalSync", External(googleConnected: true, linkedTo: "g1"));
        var view = new ListSettingsViewModel(core);
        await view.LoadAsync("l1");
        await view.LoadExternalAsync();

        Assert.True(await view.SetLinkAsync("google_tasks", "g1", null));

        var link = core.Sent.First(sent => sent.Contains("linkList"));
        Assert.Contains("\"containerId\":\"g1\"", link);
        Assert.True(view.Providers[0].IsLinked);
    }

    /// <summary>Choosing nothing unlinks, which is a different command.</summary>
    [Fact]
    public async Task Clearing_the_mirror_unlinks()
    {
        var core = new FakeCore()
            .AnswerOk("listMembers", Settings())
            .AnswerOk("externalSync", External(googleConnected: true, linkedTo: "g1"))
            .AnswerOk("unlinkList")
            .AnswerOk("externalSync", External(googleConnected: true));
        var view = new ListSettingsViewModel(core);
        await view.LoadAsync("l1");
        await view.LoadExternalAsync();

        await view.SetLinkAsync("google_tasks", null, "link-1");

        Assert.Contains("unlinkList", core.SentKinds());
        Assert.False(view.Providers[0].IsLinked);
    }

    [Fact]
    public async Task Inviting_reloads_who_is_on_the_list()
    {
        var core = new FakeCore()
            .AnswerOk("listMembers", Settings())
            .AnswerOk("inviteToList")
            .AnswerOk("listMembers", Settings());
        var view = new ListSettingsViewModel(core);
        await view.LoadAsync("l1");

        Assert.True(await view.InviteAsync("sam@example.test"));

        var invite = core.Sent.First(sent => sent.Contains("inviteToList"));
        Assert.Contains("sam@example.test", invite);
        Assert.Equal(["listMembers", "inviteToList", "listMembers"], core.SentKinds());
    }

    /// <summary>A blank box is not an invitation.</summary>
    [Fact]
    public async Task An_empty_email_invites_nobody()
    {
        var core = new FakeCore();
        var view = new ListSettingsViewModel(core);

        Assert.False(await view.InviteAsync("   "));
        Assert.Empty(core.Sent);
    }

    /// <summary>
    /// Membership does not go through the Outbox — an optimistic member row would be
    /// indistinguishable from a real one to every permission check that read it — so an offline
    /// invitation genuinely did not happen, and says so rather than looking sent.
    /// </summary>
    [Fact]
    public async Task An_offline_invitation_says_it_did_not_happen()
    {
        var core = new FakeCore()
            .AnswerOk("listMembers", Settings())
            .AnswerFailure("inviteToList", AstridFailureKind.Offline, "no network");
        var view = new ListSettingsViewModel(core);
        await view.LoadAsync("l1");

        Assert.False(await view.InviteAsync("sam@example.test"));

        Assert.NotNull(view.ErrorMessage);
    }

    /// <summary>
    /// An expired session belongs on the sign-in screen, not in a red line beside an empty member
    /// list. Membership is usually the first thing to notice, being the only screen that reaches
    /// the network on its own.
    /// </summary>
    [Fact]
    public async Task An_expired_session_asks_for_a_sign_in_rather_than_showing_an_error()
    {
        var core = new FakeCore().AnswerFailure("listMembers", AstridFailureKind.Unauthorized);
        var view = new ListSettingsViewModel(core);

        await view.LoadAsync("l1");

        Assert.True(view.NeedsSignIn);
        Assert.Null(view.ErrorMessage);
    }

    [Fact]
    public async Task Removing_somebody_reloads_the_list()
    {
        var core = new FakeCore()
            .AnswerOk("listMembers", Settings())
            .AnswerOk("removeMember")
            .AnswerOk("listMembers", Settings());
        var view = new ListSettingsViewModel(core);
        await view.LoadAsync("l1");

        Assert.True(await view.RemoveAsync("dana"));

        Assert.Contains("removeMember", core.SentKinds());
    }

    /// <summary>Renaming is an ordinary edit, so it goes through the Outbox and works offline.</summary>
    [Fact]
    public async Task Renaming_writes_the_new_name()
    {
        var core = new FakeCore().AnswerOk("listMembers", Settings()).AnswerOk("updateList");
        var view = new ListSettingsViewModel(core);
        await view.LoadAsync("l1");

        Assert.True(await view.RenameAsync("Work things"));

        Assert.Contains("\"name\":\"Work things\"", core.Sent.First(sent => sent.Contains("updateList")));
        Assert.Equal("Work things", view.Name);
    }

    /// <summary>The same name is not a change, and a blank one is not a name.</summary>
    [Fact]
    public async Task A_rename_that_changes_nothing_writes_nothing()
    {
        var core = new FakeCore().AnswerOk("listMembers", Settings());
        var view = new ListSettingsViewModel(core);
        await view.LoadAsync("l1");

        Assert.False(await view.RenameAsync("Work"));
        Assert.False(await view.RenameAsync("  "));

        Assert.DoesNotContain("updateList", core.SentKinds());
    }
}
