using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The Connections page (astrid-web #285): what can act as the account, revocable here, and the
/// developer's console for an OAuth client made by hand (AITD-419). Twin of the Apple clients'
/// <c>ConnectionsTests</c> and <c>OAuthClientEditorTests</c> for the round trip; the rules
/// themselves are the core's and are tested there.
/// </summary>
public sealed class ConnectionsViewModelTests
{
    private static object Row(string id, string kind, string category, string? owner, bool revocable = true,
        string manageIn = "connections", object? review = null, object? detail = null, string? editableClientId = null) => new
        {
            id,
            kind,
            category,
            owner,
            name = id,
            actsAs = (string?)null,
            scopes = new[] { "tasks:read" },
            createdAt = "2026-05-01T10:00:00Z",
            lastUsedAt = (string?)null,
            expiresAt = (string?)null,
            status = "active",
            revocable,
            manageIn,
            detail,
            editableClientId,
            review,
        };

    private static object Panel(params object[] sections) => new
    {
        connections = Array.Empty<object>(),
        sections,
        reviewCount = sections
            .Cast<dynamic>()
            .Sum(section => ((object[])section.rows).Count(row => ((dynamic)row).review is not null)),
    };

    private static object Section(string titleKey, params object[] rows) => new
    {
        titleKey,
        showsOwnerBadges = true,
        rows,
    };

    private static object Apps(params object[] rows) => Section("connections.category.app", rows);

    private static readonly object Idle = new { reason = "idle", days = 97 };

    /// <summary>
    /// The page is the core's sections, in its order, and the count of rows that look unused
    /// comes with them — one judgement, drawn twice.
    /// </summary>
    [Fact]
    public async Task Loading_draws_the_core_s_sections_and_its_review_count()
    {
        var core = new FakeCore().AnswerOk("connections", Panel(
            Apps(Row("c1", "oauthClient", "app", "you", review: Idle), Row("dcr-1", "authorizedApp", "app", "thirdParty")),
            Section("connections.category.token", Row("tok-1", "accessToken", "token", null, manageIn: "agents"))));
        var view = new SettingsViewModel(core).Connections;

        await view.LoadAsync();

        Assert.Equal(2, view.Sections.Count);
        Assert.Equal("connections.category.app", view.Sections[0].TitleKey);
        Assert.Equal(new[] { "c1", "dcr-1" }, view.Sections[0].Rows.Select(row => row.Id));
        Assert.Equal(1, view.ReviewCount);
        Assert.True(view.HasReviews);
        Assert.False(view.IsEmpty);
        Assert.False(view.IsLoading);
    }

    /// <summary>Nothing connected is a state the page exists to show, not a failure.</summary>
    [Fact]
    public async Task Nothing_connected_reads_as_empty_not_as_an_error()
    {
        var core = new FakeCore().AnswerOk("connections", Panel());
        var settings = new SettingsViewModel(core);

        await settings.Connections.LoadAsync();

        Assert.True(settings.Connections.IsEmpty);
        Assert.Null(settings.ErrorMessage);
    }

    /// <summary>
    /// Opening the page must not mint anything. A screen that created a credential by being
    /// looked at is a screen that fills an account with credentials nobody is holding.
    /// </summary>
    [Fact]
    public async Task Opening_the_page_reads_and_mints_nothing()
    {
        var core = new FakeCore().AnswerOk("connections", Panel());
        var view = new SettingsViewModel(core).Connections;

        await view.LoadAsync();

        Assert.Equal(["connections"], core.SentKinds());
    }

    /// <summary>
    /// A revoke is optimistic — the row goes at once — and names the row's kind and id together,
    /// since ids repeat across kinds. The review count follows from the rows in hand.
    /// </summary>
    [Fact]
    public async Task Revoking_drops_the_row_at_once_and_sends_its_kind_and_id()
    {
        var core = new FakeCore()
            .AnswerOk("connections", Panel(Apps(
                Row("c1", "oauthClient", "app", "you", review: Idle),
                Row("dcr-1", "authorizedApp", "app", "thirdParty"))))
            .AnswerOk("revokeConnection");
        var view = new SettingsViewModel(core).Connections;
        await view.LoadAsync();
        var script = view.Sections[0].Rows[0];

        Assert.True(await view.RevokeAsync(script));

        Assert.Equal(new[] { "dcr-1" }, view.Sections[0].Rows.Select(row => row.Id));
        Assert.Equal(0, view.ReviewCount);
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"revokeConnection\"")
            && json.Contains("\"connectionKind\":\"oauthClient\"")
            && json.Contains("\"id\":\"c1\""));
    }

    /// <summary>A refused revoke puts its own row back, in place — not a snapshot of the whole list.</summary>
    [Fact]
    public async Task A_refused_revoke_puts_the_row_back_where_it_was()
    {
        var core = new FakeCore()
            .AnswerOk("connections", Panel(Apps(
                Row("c1", "oauthClient", "app", "you"),
                Row("dcr-1", "authorizedApp", "app", "thirdParty"),
                Row("agent-1", "customAgent", "app", "agent", manageIn: "agents"))))
            .AnswerFailure("revokeConnection", AstridFailureKind.BadRequest, "no");
        var settings = new SettingsViewModel(core);
        var view = settings.Connections;
        await view.LoadAsync();
        var claude = view.Sections[0].Rows[1];

        Assert.False(await view.RevokeAsync(claude));

        Assert.Equal(new[] { "c1", "dcr-1", "agent-1" }, view.Sections[0].Rows.Select(row => row.Id));
        Assert.Equal("no", settings.ErrorMessage);
        Assert.False(view.RequiresWebSession);
    }

    /// <summary>
    /// The server revokes only for an interactive session. This client sends its cookie so it
    /// normally succeeds; a 403 reads as "do this on the web", not as a bare error.
    /// </summary>
    [Fact]
    public async Task A_session_only_refusal_points_at_the_web()
    {
        var core = new FakeCore()
            .AnswerOk("connections", Panel(Apps(Row("c1", "oauthClient", "app", "you"))))
            .Answer("revokeConnection",
                "{\"ok\":false,\"error\":{\"kind\":\"refused\",\"message\":\"Revoking a connection requires an interactive session\",\"status\":403}}");
        var view = new SettingsViewModel(core).Connections;
        await view.LoadAsync();

        Assert.False(await view.RevokeAsync(view.Sections[0].Rows[0]));

        Assert.True(view.RequiresWebSession);
        Assert.Single(view.Sections[0].Rows);
    }

    /// <summary>A row the core says cannot be revoked never reaches the server.</summary>
    [Fact]
    public async Task An_unrevocable_row_never_reaches_the_server()
    {
        var core = new FakeCore().AnswerOk("connections", Panel(
            Section("connections.category.unknown", Row("future-1", "unknown", "unknown", null, revocable: false))));
        var view = new SettingsViewModel(core).Connections;
        await view.LoadAsync();

        Assert.False(await view.RevokeAsync(view.Sections[0].Rows[0]));

        Assert.DoesNotContain("revokeConnection", core.SentKinds());
    }

    /// <summary>A revoked section left empty goes, as the core omits an empty one.</summary>
    [Fact]
    public async Task Revoking_the_last_row_of_a_section_takes_the_section_with_it()
    {
        var core = new FakeCore()
            .AnswerOk("connections", Panel(
                Apps(Row("c1", "oauthClient", "app", "you")),
                Section("connections.category.webhook", Row("webhook", "webhook", "webhook", null, manageIn: "agents"))))
            .AnswerOk("revokeConnection");
        var view = new SettingsViewModel(core).Connections;
        await view.LoadAsync();

        Assert.True(await view.RevokeAsync(view.Sections[1].Rows[0]));

        Assert.Single(view.Sections);
        Assert.Equal("connections.category.app", view.Sections[0].TitleKey);
    }

    // ── The editor (AITD-419) ────────────────────────────────────────────────────────────────

    private static object Check(bool canSend, object? problem = null, string[]? grantTypes = null) => new
    {
        problem,
        canSend,
        grantTypes = grantTypes ?? new[] { "client_credentials" },
        redirectUris = Array.Empty<string>(),
        scopes = new[] { "tasks:read", "tasks:write", "lists:read" },
    };

    /// <summary>
    /// Opening the form asks the core what an empty draft is worth, and the boxes are drawn from
    /// its answer: the grants it settled and the scopes it offers, each saying whether it is on.
    /// </summary>
    [Fact]
    public async Task Opening_the_form_asks_the_core_and_draws_its_choices()
    {
        var core = new FakeCore().AnswerOk("checkOAuthClientDraft",
            Check(false, problem: new { key = "nameMissing" }));
        var view = new SettingsViewModel(core).Connections;

        await view.BeginCreateAsync();

        Assert.True(view.IsEditorOpen);
        Assert.True(view.IsCreating);
        Assert.Equal("nameMissing", view.DraftProblem?.Key);
        Assert.False(view.CanSend);
        Assert.Equal(new[] { "client_credentials", "authorization_code", "refresh_token" },
            view.GrantChoices.Select(choice => choice.Grant));
        Assert.Equal(new[] { true, false, false }, view.GrantChoices.Select(choice => choice.IsOn));
        Assert.Equal(3, view.ScopeChoices.Count);
        Assert.All(view.ScopeChoices, choice => Assert.False(choice.IsOn));
    }

    /// <summary>
    /// A grant click goes to the core, which applies the pairing and answers with the set; the
    /// boxes show the set that resulted, not the one clicked.
    /// </summary>
    [Fact]
    public async Task Toggling_a_grant_sends_it_to_the_core_and_adopts_the_paired_answer()
    {
        var core = new FakeCore()
            .AnswerOk("checkOAuthClientDraft", Check(false, problem: new { key = "nameMissing" }))
            .AnswerOk("checkOAuthClientDraft", Check(false, problem: new { key = "redirectRequired" },
                grantTypes: ["client_credentials", "authorization_code", "refresh_token"]));
        var view = new SettingsViewModel(core).Connections;
        await view.BeginCreateAsync();

        await view.ToggleGrantAsync("authorization_code");

        Assert.Contains(core.Sent, json => json.Contains("\"toggleGrant\":\"authorization_code\""));
        Assert.Equal(new[] { true, true, true }, view.GrantChoices.Select(choice => choice.IsOn));
        Assert.Equal("redirectRequired", view.DraftProblem?.Key);
    }

    /// <summary>A ticked scope rides in the next check, and the boxes say so.</summary>
    [Fact]
    public async Task Ticking_a_scope_carries_it_into_the_draft()
    {
        var core = new FakeCore()
            .AnswerOk("checkOAuthClientDraft", Check(false))
            .AnswerOk("checkOAuthClientDraft", Check(false));
        var view = new SettingsViewModel(core).Connections;
        await view.BeginCreateAsync();

        await view.SetScopeAsync("tasks:read", true);

        Assert.Contains(core.Sent, json => json.Contains("\"scopes\":[\"tasks:read\"]"));
        Assert.True(view.ScopeChoices.Single(choice => choice.Scope == "tasks:read").IsOn);
    }

    /// <summary>
    /// The secret exists in plaintext exactly once, in the creation answer. A create holds it
    /// open rather than closing — closing on top of it would lose the only copy there will ever
    /// be — and the list is read again so the new row appears.
    /// </summary>
    [Fact]
    public async Task Creating_holds_the_secret_open_and_reloads_the_list()
    {
        var core = new FakeCore()
            .AnswerOk("checkOAuthClientDraft", Check(true))
            .AnswerOk("checkOAuthClientDraft", Check(true))
            .AnswerOk("createOAuthClient", new { clientId = "astrid_client_abc", clientSecret = "shh", name = "My script" })
            .AnswerOk("connections", Panel(Apps(Row("c1", "oauthClient", "app", "you"))));
        var view = new SettingsViewModel(core).Connections;
        await view.BeginCreateAsync();
        view.DraftName = "My script";

        var shouldClose = await view.SaveAsync();

        Assert.False(shouldClose, "closing on top of the secret would lose the only copy");
        Assert.Equal("shh", view.MintedClient?.ClientSecret);
        Assert.True(view.HasMintedClient);
        Assert.False(view.IsEditorOpen);
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"createOAuthClient\"") && json.Contains("\"name\":\"My script\""));
        Assert.Equal("connections", core.SentKinds().Last());
    }

    /// <summary>A draft the core can reject is one the server never sees.</summary>
    [Fact]
    public async Task An_invalid_draft_never_reaches_the_server()
    {
        var core = new FakeCore()
            .AnswerOk("checkOAuthClientDraft", Check(false, problem: new { key = "nameMissing" }))
            .AnswerOk("checkOAuthClientDraft", Check(false, problem: new { key = "nameMissing" }));
        var view = new SettingsViewModel(core).Connections;
        await view.BeginCreateAsync();

        Assert.False(await view.SaveAsync());

        Assert.DoesNotContain("createOAuthClient", core.SentKinds());
        Assert.Equal("nameMissing", view.DraftProblem?.Key);
    }

    /// <summary>
    /// An edit loads the client — the list knows its id but not its redirect URIs — writes back
    /// through the update command, mints nothing, and closes: there is no secret to show.
    /// </summary>
    [Fact]
    public async Task Editing_loads_the_client_and_writes_back_its_redirect_uris()
    {
        var core = new FakeCore()
            .AnswerOk("loadOAuthClient", new
            {
                clientId = "astrid_client_abc",
                name = "Browser app",
                description = "Nightly",
                redirectUris = new[] { "https://old.test/cb" },
                grantTypes = new[] { "authorization_code", "refresh_token" },
                scopes = new[] { "tasks:read" },
                isActive = true,
            })
            .AnswerOk("checkOAuthClientDraft", Check(true, grantTypes: ["authorization_code", "refresh_token"]))
            .AnswerOk("checkOAuthClientDraft", Check(true, grantTypes: ["authorization_code", "refresh_token"]))
            .AnswerOk("updateOAuthClient", new { clientId = "astrid_client_abc", name = "Browser app" })
            .AnswerOk("connections", Panel());
        var view = new SettingsViewModel(core).Connections;

        Assert.True(await view.BeginEditAsync("astrid_client_abc"));
        Assert.True(view.IsEditing);
        Assert.Equal("Browser app", view.DraftName);
        Assert.Equal("https://old.test/cb", view.DraftRedirectUriText);
        Assert.Equal(new[] { false, true, true }, view.GrantChoices.Select(choice => choice.IsOn));

        view.DraftRedirectUriText = "https://new.test/cb";
        var shouldClose = await view.SaveAsync();

        Assert.True(shouldClose, "an edit has nothing to show afterwards");
        Assert.Null(view.MintedClient);
        Assert.False(view.IsEditorOpen);
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"updateOAuthClient\"")
            && json.Contains("\"clientId\":\"astrid_client_abc\"")
            && json.Contains("\"redirectUriText\":\"https://new.test/cb\""));
    }

    /// <summary>
    /// A minted credential lives for as long as the screen showing it. That is the whole of its
    /// storage policy, and it only holds if something actually forgets.
    /// </summary>
    [Fact]
    public async Task Closing_the_panel_forgets_the_secret()
    {
        var core = new FakeCore()
            .AnswerOk("checkOAuthClientDraft", Check(true))
            .AnswerOk("checkOAuthClientDraft", Check(true))
            .AnswerOk("createOAuthClient", new { clientId = "astrid_client_abc", clientSecret = "shh", name = "CI" })
            .AnswerOk("connections", Panel());
        var view = new SettingsViewModel(core).Connections;
        await view.BeginCreateAsync();
        view.DraftName = "CI";
        await view.SaveAsync();
        Assert.True(view.HasMintedClient);

        view.ForgetMintedCredentials();

        Assert.Null(view.MintedClient);
    }

    /// <summary>Revoking the pair just made leaves its secret on screen pointing at nothing.</summary>
    [Fact]
    public async Task Revoking_the_pair_just_made_takes_its_secret_off_screen()
    {
        var core = new FakeCore()
            .AnswerOk("checkOAuthClientDraft", Check(true))
            .AnswerOk("checkOAuthClientDraft", Check(true))
            .AnswerOk("createOAuthClient", new { clientId = "astrid_client_abc", clientSecret = "shh", name = "CI" })
            .AnswerOk("connections", Panel(Apps(
                Row("c1", "oauthClient", "app", "you", detail: new { clientId = "astrid_client_abc" }))))
            .AnswerOk("revokeConnection");
        var view = new SettingsViewModel(core).Connections;
        await view.BeginCreateAsync();
        view.DraftName = "CI";
        await view.SaveAsync();

        Assert.True(await view.RevokeAsync(view.Sections[0].Rows[0]));

        Assert.Null(view.MintedClient);
    }

    // ── The agents page mints the webhook server's pair ──────────────────────────────────────

    /// <summary>
    /// The webhook server needs a client-credentials pair as well as its signing secret. The
    /// preset request carries only the preset and the agent: scopes and grant types are the
    /// server's decision. Shown once, held until the screen closes.
    /// </summary>
    [Fact]
    public async Task The_agents_page_mints_the_webhook_server_s_pair_from_the_preset()
    {
        var core = new FakeCore()
            .AnswerOk("mintTransportCredentials", new { clientId = "astrid_client_minted", clientSecret = "shh", name = "" });
        var agents = new SettingsViewModel(core).Agents;

        Assert.True(await agents.MintWebhookCredentialsAsync());

        Assert.Equal("astrid_client_minted", agents.WebhookCredentials?.ClientId);
        Assert.True(agents.HasWebhookCredentials);
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"mintTransportCredentials\"")
            && json.Contains("\"preset\":\"webhookServer\"")
            && json.Contains("\"agent\":\"claude\""));

        agents.ForgetWebhookCredentials();
        Assert.False(agents.HasWebhookCredentials);
    }
}
