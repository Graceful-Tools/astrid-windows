using Astrid.App.ViewModels;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>The API access page: an MCP token and client credentials, minted here and shown once.</summary>
public sealed class ApiAccessViewModelTests
{
    /// <summary>
    /// The point of the panel: a client that is already signed in mints its own credential rather
    /// than sending somebody to a browser to do what the client is authorised for.
    /// </summary>
    [Fact]
    public async Task A_token_is_minted_and_held_for_the_screen_to_show()
    {
        var core = new FakeCore().AnswerOk("createMcpToken", new { token = "mcp_live_abc" });
        var view = new SettingsViewModel(core).ApiAccess;

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
        var view = new SettingsViewModel(core).ApiAccess;
        view.NewClientName = "CI";

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
        var view = new SettingsViewModel(core).ApiAccess;
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
        var view = new SettingsViewModel(core).ApiAccess;
        view.NewClientName = "Windows fixall";

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
        var view = new SettingsViewModel(new FakeCore()).ApiAccess;

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
        var view = new SettingsViewModel(core).ApiAccess;
        view.NewClientName = "CI";
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
        var view = new SettingsViewModel(core).ApiAccess;

        await view.LoadApiAccessAsync();

        Assert.DoesNotContain("createMcpToken", core.SentKinds());
        Assert.DoesNotContain("createOAuthClient", core.SentKinds());
    }
}
