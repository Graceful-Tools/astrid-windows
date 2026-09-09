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
