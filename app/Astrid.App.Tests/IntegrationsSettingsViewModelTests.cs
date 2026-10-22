using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>The Integrations page: how Google lists get linked.</summary>
public sealed class IntegrationsSettingsViewModelTests
{
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
        var view = new SettingsViewModel(core).Integrations;

        await view.LoadGoogleSyncModeAsync();

        Assert.Equal("all_bidirectional", view.GoogleSyncMode);
    }

    /// <summary>Manual until the account says otherwise, since the all-lists modes make lists.</summary>
    [Fact]
    public void The_google_sync_mode_starts_manual()
    {
        Assert.Equal("manual", new SettingsViewModel(new FakeCore()).Integrations.GoogleSyncMode);
    }

    // ── The account panel (task e32f1322) ────────────────────────────────────────────────────

    /// <summary>
    /// Task e32f1322. A section per provider: which account it is connected as, and both ends of
    /// every list it mirrors. Which account and not just whether — somebody with a work Google
    /// account and a personal one cannot tell from a tick which of them their lists are going into.
    /// </summary>
    [Fact]
    public async Task The_account_panel_reads_both_providers_and_both_ends_of_every_link()
    {
        var core = Panel(
            Google("jon@gmail.com",
                new { id = "link-1", listId = "l1", listName = "Work", containerId = "tl-1", containerName = "Work (Google)" }),
            GitHub("jonparis"));
        var view = new SettingsViewModel(core).Integrations;

        Assert.True(await view.LoadExternalAccountsAsync());

        Assert.True(view.GoogleConnected);
        Assert.Equal("jon@gmail.com", view.GoogleConnectedAs);
        var link = Assert.Single(view.GoogleLinks);
        Assert.Equal("link-1", link.Id);
        Assert.Equal("l1", link.ListId);
        Assert.Equal("Work", link.ListLabel);
        Assert.Equal("Work (Google)", link.ContainerLabel);

        Assert.True(view.GitHubConnected);
        Assert.Equal("jonparis", view.GitHubConnectedAs);
        Assert.Empty(view.GitHubLinks);
        Assert.True(view.HasNoGitHubLinks);
    }

    /// <summary>
    /// Task e32f1322. The core answers no <c>listName</c> for a link naming a list this device has
    /// not cached, and the row shows the link rather than hiding it — the name fills in on the next
    /// refresh. A panel that drops links it cannot name is a panel that lies about what is mirrored.
    /// </summary>
    [Fact]
    public async Task A_link_naming_a_list_this_device_has_not_cached_still_draws()
    {
        var core = Panel(
            Google("jon@gmail.com", new { id = "link-2", listId = "l9", containerId = "tl-9" }),
            GitHub(null));
        var view = new SettingsViewModel(core).Integrations;

        await view.LoadExternalAccountsAsync();

        var link = Assert.Single(view.GoogleLinks);
        Assert.Null(link.ListName);
        Assert.Equal("l9", link.ListLabel);
        Assert.Equal("tl-9", link.ContainerLabel);
    }

    /// <summary>
    /// Task e32f1322. "New List" binds a NEW Google list to an Astrid one, so what it can be
    /// pressed for is the lists Google has not got yet. Offering an already-linked list would be
    /// offering to make a second Google list for it.
    /// </summary>
    [Fact]
    public async Task New_list_is_offered_only_for_the_lists_google_has_no_link_for()
    {
        var core = Panel(
                Google("jon@gmail.com",
                    new { id = "link-1", listId = "l1", listName = "Work", containerId = "tl-1" }),
                GitHub(null))
            .AnswerOk("lists", new object[]
            {
                new { id = "l1", name = "Work" },
                new { id = "l2", name = "Garden" },
                // A board column is a state, not a place, and is never somewhere to mirror.
                new { id = "l3", name = "Doing", listType = "status" },
            });
        var view = new SettingsViewModel(core).Integrations;

        await view.LoadExternalAccountsAsync();

        var offered = Assert.Single(view.LinkableLists);
        Assert.Equal("l2", offered.Id);
        Assert.Equal("Garden", offered.Name);
    }

    /// <summary>
    /// Task e32f1322. The button is one gesture over one command, and the panel it leaves behind is
    /// the one the core now has — read back rather than guessed at, since the core may have adopted
    /// an existing unlinked Google list of the same name instead of making one.
    /// </summary>
    [Fact]
    public async Task New_list_sends_the_chosen_list_and_reads_the_panel_back()
    {
        var core = Panel(Google("jon@gmail.com"), GitHub(null))
            .AnswerOk("lists", new object[] { new { id = "l2", name = "Garden" } })
            .AnswerOk("createAndLinkGoogleList")
            .Answer("externalAccounts", Panelled(
                Google("jon@gmail.com",
                    new { id = "link-9", listId = "l2", listName = "Garden", containerId = "tl-9", containerName = "Garden" }),
                GitHub(null)))
            .AnswerOk("lists", Array.Empty<object>());
        var view = new SettingsViewModel(core).Integrations;
        await view.LoadExternalAccountsAsync();
        Assert.False(view.CanCreateGoogleList, "nothing chosen yet");

        view.NewGoogleListTarget = view.LinkableLists[0];
        Assert.True(view.CanCreateGoogleList);
        Assert.True(await view.CreateGoogleListAsync());

        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"createAndLinkGoogleList\"")
            && json.Contains("\"listId\":\"l2\"")
            && json.Contains("\"name\":\"Garden\""));
        Assert.Equal("l2", Assert.Single(view.GoogleLinks).ListId);
        // Linked now, so it is no longer something to make a Google list for.
        Assert.Empty(view.LinkableLists);
        Assert.Null(view.NewGoogleListTarget);
        Assert.False(view.CanCreateGoogleList);
    }

    /// <summary>
    /// Task e32f1322. The panel is the server's, so offline it is absent — and absent with a reason
    /// on screen, not an empty section that reads as "nothing is linked".
    /// </summary>
    [Fact]
    public async Task A_panel_the_core_would_not_answer_is_worded_not_silent()
    {
        var core = new FakeCore()
            .AnswerFailure("externalAccounts", AstridFailureKind.Offline, "no network");
        var view = new SettingsViewModel(core).Integrations;
        Assert.False(view.HasNoGoogleLinks, "not asked yet is not none");

        Assert.False(await view.LoadExternalAccountsAsync());

        Assert.True(view.HasExternalAccountsUnavailable);
        Assert.Contains("connection", view.ExternalAccountsUnavailable);
        Assert.False(view.HasNoGoogleLinks);
        Assert.Empty(view.LinkableLists);
    }

    // ── The script the panel is read from ────────────────────────────────────────────────────

    private static FakeCore Panel(object google, object github) =>
        new FakeCore().Answer("externalAccounts", Panelled(google, github));

    private static string Panelled(object google, object github) =>
        System.Text.Json.JsonSerializer.Serialize(
            new { ok = true, value = new { providers = new[] { google, github } } },
            CommandJson.Options);

    private static object Google(string? connectedAs, params object[] links) =>
        new { provider = "google_tasks", connected = true, connectedAs, links };

    /// <summary>GitHub is <c>git_hub</c> here: the core's serde name, not the wire's.</summary>
    private static object GitHub(string? connectedAs, params object[] links) =>
        new { provider = "git_hub", connected = connectedAs is not null, connectedAs, links };
}
