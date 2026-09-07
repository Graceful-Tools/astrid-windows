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

    [Fact]
    public async Task Loading_shows_the_members_and_what_this_account_may_do()
    {
        var core = new FakeCore().AnswerOk("listMembers", Settings());
        var view = new ListSettingsViewModel(core);

        await view.LoadAsync("l1");

        Assert.Equal("Work", view.Name);
        Assert.Equal(2, view.Members.Count);
        Assert.Equal("Dana", view.Members[1].DisplayName);
        Assert.Equal("Member", view.Members[1].RoleLabel);
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
