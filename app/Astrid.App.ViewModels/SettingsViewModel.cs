using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The account: who is signed in, and how they want to be reminded.
/// </summary>
/// <remarks>
/// <para>
/// Drawn from the cache and caught up afterwards, like everything else here. The settings
/// themselves are the server's — push, email, the default reminder offset, a daily digest and
/// quiet hours — and they are shared with every client, which is why nothing about their meaning
/// is decided in this class.
/// </para>
/// <para>
/// A change is written through immediately rather than behind a Save button. There is no draft
/// state to lose, and a settings screen with a Save button is one where somebody flips a toggle,
/// closes the window, and finds nothing changed.
/// </para>
/// </remarks>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly IAstridCore _core;
    private UserSummary? _user;
    private ReminderSettings _reminders = new();
    private bool _isLoading;
    private string? _errorMessage;
    private bool _needsSignIn;
    private ProfileStats _stats = new();
    private bool _copilotConnected;
    private string _googleSyncMode = "manual";
    private string _theme = "ocean";
    private bool? _themeIsDark = false;
    private WebhookSettings _webhook = new();
    private string? _webhookUrl;
    private string? _newWebhookSecret;
    private string? _webhookTestResult;
    private string? _lastExportPath;

    public SettingsViewModel(IAstridCore core)
    {
        _core = core;
    }

    /// <summary>The reminder offsets a new task can default to.</summary>
    public ObservableCollection<ReminderOffset> Offsets { get; } = [];

    public UserSummary? User
    {
        get => _user;
        private set
        {
            if (Set(ref _user, value))
            {
                Raise(nameof(DisplayName));
                Raise(nameof(Email));
            }
        }
    }

    public string DisplayName => User?.DisplayName ?? string.Empty;

    public string Email => User?.Email ?? string.Empty;

    public ReminderSettings Reminders
    {
        get => _reminders;
        private set
        {
            if (Set(ref _reminders, value))
            {
                Raise(nameof(PushEnabled));
                Raise(nameof(EmailEnabled));
                Raise(nameof(DigestEnabled));
                Raise(nameof(QuietHoursEnabled));
            }
        }
    }

    public bool PushEnabled => Reminders.EnablePushReminders;

    public bool EmailEnabled => Reminders.EnableEmailReminders;

    public bool DigestEnabled => Reminders.EnableDailyDigest;

    /// <summary>Quiet hours are on when there is a start to be quiet from.</summary>
    public bool QuietHoursEnabled => !string.IsNullOrEmpty(Reminders.QuietHoursStart);

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    /// <summary>
    /// The session has expired.
    /// </summary>
    /// <remarks>
    /// This screen asks the server for the account, so it notices an expired session as soon as it
    /// opens. "The session is not valid" beside somebody's own name is a worse thing to show than a
    /// sign-in button.
    /// </remarks>
    public bool NeedsSignIn
    {
        get => _needsSignIn;
        private set => Set(ref _needsSignIn, value);
    }

    /// <summary>The AI agents, and the mode each is set to.</summary>
    public ObservableCollection<AgentSummary> Agents { get; } = [];

    /// <summary>Which services have a key stored. Never the keys.</summary>
    public ObservableCollection<AgentCredential> Credentials { get; } = [];

    /// <summary>Load the Agent Hub.</summary>
    /// <remarks>
    /// Separately from the account, because a deployment can be without agents entirely and a
    /// settings screen that failed for that reason would be a screen nobody could use.
    /// </remarks>
    public async Task LoadAgentsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.Agents(), cancellationToken);
        if (!response.Ok)
        {
            return;
        }
        var hub = response.Read<AgentHub>();
        if (hub is null)
        {
            return;
        }
        Agents.Clear();
        foreach (var agent in hub.Agents)
        {
            // The mode arrives in a map beside the agents rather than on them, so it is joined
            // here — one place, rather than in every control that shows an agent.
            Agents.Add(agent with
            {
                Mode = hub.Modes.TryGetValue(agent.Id, out var mode) ? mode : "off",
            });
        }
        CopilotConnected = hub.Copilot.Connected;
        Credentials.Clear();
        foreach (var credential in hub.Credentials)
        {
            Credentials.Add(credential);
        }
    }

    /// <summary>
    /// How Google lists get linked: <c>manual</c>, or one of the three all-lists modes.
    /// </summary>
    public string GoogleSyncMode
    {
        get => _googleSyncMode;
        private set => Set(ref _googleSyncMode, value);
    }

    /// <summary>Read back how this account links Google lists.</summary>
    /// <remarks>
    /// Read rather than remembered: the choice belongs to the account, so a machine that assumed
    /// its own last answer would show the wrong one to somebody who changed it elsewhere.
    /// </remarks>
    public async Task LoadGoogleSyncModeAsync(CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.GoogleSyncMode(), cancellationToken);
        if (!response.Ok)
        {
            return;
        }
        if (response.Value.TryGetProperty("mode", out var mode) && mode.GetString() is { } value)
        {
            GoogleSyncMode = value;
        }
    }

    /// <summary>Choose how Google lists get linked.</summary>
    public async Task<bool> SetGoogleSyncModeAsync(string mode,
        CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.SetGoogleSyncMode(mode), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.IsStillPending
                ? "Changing how lists link needs a connection."
                : response.Error?.Message;
            return false;
        }
        await LoadGoogleSyncModeAsync(cancellationToken);
        return true;
    }

    /// <summary>Where this account's own agent is told about work.</summary>
    public WebhookSettings Webhook
    {
        get => _webhook;
        private set
        {
            Set(ref _webhook, value);
            Raise(nameof(WebhookUrl));
        }
    }

    /// <summary>The URL as it stands, edited before it is saved.</summary>
    public string WebhookUrl
    {
        get => _webhookUrl ?? Webhook.WebhookUrl;
        set => Set(ref _webhookUrl, value);
    }

    /// <summary>
    /// The signing secret, the once the server shows it.
    /// </summary>
    /// <remarks>
    /// It signs every delivery and is never readable again, so it is held here until the screen
    /// that shows it is closed — and never written to the cache, which is a file on disk.
    /// </remarks>
    public string? NewWebhookSecret
    {
        get => _newWebhookSecret;
        private set
        {
            Set(ref _newWebhookSecret, value);
            Raise(nameof(HasNewWebhookSecret));
        }
    }

    /// <summary>Whether there is a secret on screen to be copied.</summary>
    public bool HasNewWebhookSecret => !string.IsNullOrEmpty(NewWebhookSecret);

    /// <summary>What a test delivery did, in the server's own words.</summary>
    public string? WebhookTestResult
    {
        get => _webhookTestResult;
        private set => Set(ref _webhookTestResult, value);
    }

    /// <summary>The agents this account has registered of its own.</summary>
    public ObservableCollection<CustomAgent> CustomAgents { get; } = [];

    /// <summary>Load the webhook settings and the registered agents.</summary>
    /// <remarks>
    /// Both are things a deployment can be without, so a failure here leaves the rest of the hub
    /// drawn rather than turning the screen into an error.
    /// </remarks>
    public async Task LoadWebhookAsync(CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.WebhookSettings(), cancellationToken);
        if (response.Ok && response.Read<WebhookSettings>() is { } settings)
        {
            _webhookUrl = null;
            Webhook = settings;
        }

        var agents = await _core.CallAsync(Commands.CustomAgents(), cancellationToken);
        if (agents.Ok)
        {
            CustomAgents.Clear();
            foreach (var agent in agents.ReadArray<CustomAgent>())
            {
                CustomAgents.Add(agent);
            }
        }
    }

    // ── API access ───────────────────────────────────────────────────────────────────────────

    /// <summary>The client-credentials pairs this account has registered.</summary>
    public ObservableCollection<OAuthClientRow> ApiClients { get; } = [];

    private string? _mcpToken;
    private MintedClient? _mintedClient;
    private string _newClientName = string.Empty;

    /// <summary>
    /// The MCP token, once it has been minted.
    /// </summary>
    /// <remarks>
    /// In memory only, and only until the panel closes. Writing it down would put a credential on
    /// disk that the account cannot rotate by revoking, because nobody would know it was there.
    /// </remarks>
    public string? McpToken
    {
        get => _mcpToken;
        private set
        {
            if (Set(ref _mcpToken, value))
            {
                Raise(nameof(HasMcpToken));
            }
        }
    }

    public bool HasMcpToken => !string.IsNullOrEmpty(McpToken);

    /// <summary>The pair just registered. Plaintext once, in memory only.</summary>
    public MintedClient? MintedClient
    {
        get => _mintedClient;
        private set
        {
            if (Set(ref _mintedClient, value))
            {
                Raise(nameof(HasMintedClient));
            }
        }
    }

    public bool HasMintedClient => MintedClient is not null;

    /// <summary>What to call a new pair. A pair with no name is one nobody can identify later.</summary>
    public string NewClientName
    {
        get => _newClientName;
        set
        {
            if (Set(ref _newClientName, value))
            {
                Raise(nameof(CanCreateClient));
            }
        }
    }

    public bool CanCreateClient => !string.IsNullOrWhiteSpace(NewClientName);

    /// <summary>
    /// Load what is registered.
    /// </summary>
    /// <remarks>
    /// Reads only. Opening the panel must not mint anything: a screen that created a credential
    /// by being looked at is a screen that fills an account with credentials nobody is holding.
    /// </remarks>
    public async Task LoadApiAccessAsync(CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.ApiAccess(), cancellationToken);
        if (!response.Ok)
        {
            return;
        }
        ApiClients.Clear();
        if (response.Read<ApiAccessPanel>() is { } panel)
        {
            foreach (var client in panel.Clients)
            {
                ApiClients.Add(client);
            }
        }
    }

    /// <summary>
    /// Mint an MCP token for this device, or fetch back the one it already has.
    /// </summary>
    /// <remarks>
    /// The server decides which, so pressing this twice is safe and is how somebody who lost their
    /// copy gets it back.
    /// </remarks>
    public async Task<bool> CreateMcpTokenAsync(CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.CreateMcpToken(), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.IsStillPending
                ? "Minting a token needs a connection."
                : response.Error?.Message;
            return false;
        }
        McpToken = response.Read<MintedToken>()?.Token;
        return HasMcpToken;
    }

    /// <summary>Revoke every MCP token, and forget the copy on screen.</summary>
    public async Task<bool> RevokeMcpTokensAsync(CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.RevokeMcpTokens(), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.IsStillPending
                ? "Revoking a token needs a connection."
                : response.Error?.Message;
            return false;
        }
        // The one on screen is now a dead string. Leaving it visible would offer somebody a
        // credential to paste that has already stopped working.
        McpToken = null;
        return true;
    }

    /// <summary>Register a pair, and hold the secret until the panel closes.</summary>
    public async Task<bool> CreateOAuthClientAsync(CancellationToken cancellationToken = default)
    {
        if (!CanCreateClient)
        {
            return false;
        }
        var response = await _core.CallAsync(
            Commands.CreateOAuthClient(NewClientName.Trim()), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.IsStillPending
                ? "Registering a client needs a connection."
                : response.Error?.Message;
            return false;
        }
        MintedClient = response.Read<MintedClient>();
        NewClientName = string.Empty;
        await LoadApiAccessAsync(cancellationToken);
        return HasMintedClient;
    }

    /// <summary>Revoke one pair, addressed by its public half.</summary>
    public async Task<bool> DeleteOAuthClientAsync(string clientId,
        CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.DeleteOAuthClient(clientId), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.IsStillPending
                ? "Revoking a client needs a connection."
                : response.Error?.Message;
            return false;
        }
        // If the pair just made is the one revoked, the secret on screen is now useless.
        if (MintedClient?.ClientId == clientId)
        {
            MintedClient = null;
        }
        await LoadApiAccessAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Forget both plaintexts.
    /// </summary>
    /// <remarks>
    /// Called when the settings panel closes. They live for as long as the screen showing them and
    /// no longer — that is the whole of their storage policy.
    /// </remarks>
    public void ForgetMintedCredentials()
    {
        McpToken = null;
        MintedClient = null;
    }

    /// <summary>Save where deliveries go.</summary>
    public async Task<bool> SaveWebhookAsync(bool regenerateSecret = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WebhookUrl))
        {
            return false;
        }
        var response = await _core.CallAsync(
            Commands.SaveWebhook(WebhookUrl.Trim(), Webhook.Enabled, Webhook.Events,
                Webhook.Agents, regenerateSecret),
            cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.IsStillPending
                ? "Configuring a webhook needs a connection."
                : response.Error?.Message;
            return false;
        }
        // Shown once. Kept in memory only, and only until the screen goes.
        NewWebhookSecret = response.Value.TryGetProperty("secret", out var secret)
            ? secret.GetString()
            : null;
        await LoadWebhookAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteWebhookAsync(CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.DeleteWebhook(), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.Error?.Message;
            return false;
        }
        NewWebhookSecret = null;
        await LoadWebhookAsync(cancellationToken);
        return true;
    }

    /// <summary>Fire a test delivery.</summary>
    public async Task TestWebhookAsync(CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.TestWebhook(), cancellationToken);
        WebhookTestResult = response.Ok
            ? "Sent."
            : response.Error?.Message ?? "It did not go.";
    }

    /// <summary>Register an agent of this account's own.</summary>
    public async Task<string?> RegisterAgentAsync(string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }
        var response = await _core.CallAsync(
            Commands.RegisterCustomAgent(name.Trim()), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.Error?.Message;
            return null;
        }
        await LoadWebhookAsync(cancellationToken);
        // The credentials come back once. Handing them straight back is the only chance anything
        // has to show them.
        return response.Value.TryGetProperty("clientSecret", out var secret)
            ? secret.GetString()
            : null;
    }

    public async Task<bool> DeleteAgentAsync(string agentId,
        CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.DeleteCustomAgent(agentId), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.Error?.Message;
            return false;
        }
        await LoadWebhookAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Which look the app wears: <c>ocean</c>, <c>light</c>, <c>dark</c> or <c>auto</c>.
    /// </summary>
    /// <remarks>
    /// Ocean is the brand look and the default — a light appearance with a cyan surface — so an
    /// app that has never been configured is wearing it. See <c>astrid_core::theme</c>.
    /// </remarks>
    public string Theme
    {
        get => _theme;
        private set
        {
            Set(ref _theme, value);
            Raise(nameof(ThemeChoice));
        }
    }

    /// <summary>What the picker shows. The same list on every client, in the core's order.</summary>
    public ObservableCollection<string> ThemeChoices { get; } = [];

    /// <summary>The chosen entry, for a two-way picker.</summary>
    public string ThemeChoice
    {
        get => Theme;
        set
        {
            if (!string.IsNullOrEmpty(value) && value != Theme)
            {
                _ = SetThemeAsync(value);
            }
        }
    }

    /// <summary>
    /// Whether this look draws dark, or leaves it to the system.
    /// </summary>
    /// <remarks>
    /// Null for <c>auto</c>. The window uses it to decide between an explicit appearance and
    /// following Windows — a shell that guessed would pick one and be wrong half the time.
    /// </remarks>
    public bool? ThemeIsDark
    {
        get => _themeIsDark;
        private set => Set(ref _themeIsDark, value);
    }

    /// <summary>Raised when the look changes, so the window can repaint itself.</summary>
    public event Action? ThemeChanged;

    /// <summary>Read back which look this installation is set to.</summary>
    public async Task LoadThemeAsync(CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.Theme(), cancellationToken);
        if (!response.Ok)
        {
            return;
        }
        Apply(response);
        ThemeChoices.Clear();
        if (response.Value.TryGetProperty("choices", out var choices))
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.GetString() is { } name)
                {
                    ThemeChoices.Add(name);
                }
            }
        }
        ThemeChanged?.Invoke();
    }

    /// <summary>Choose a look.</summary>
    public async Task<bool> SetThemeAsync(string theme,
        CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.SetTheme(theme), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.Error?.Message;
            return false;
        }
        Apply(response);
        ThemeChanged?.Invoke();
        return true;
    }

    private void Apply(AstridResponse response)
    {
        if (response.Value.TryGetProperty("theme", out var theme) && theme.GetString() is { } name)
        {
            Theme = name;
        }
        ThemeIsDark = response.Value.TryGetProperty("isDark", out var dark)
            && dark.ValueKind is System.Text.Json.JsonValueKind.True
                or System.Text.Json.JsonValueKind.False
            ? dark.GetBoolean()
            : null;
    }

    /// <summary>Whether Copilot is connected.</summary>
    public bool CopilotConnected
    {
        get => _copilotConnected;
        private set => Set(ref _copilotConnected, value);
    }

    /// <summary>Start connecting Copilot, or disconnect it.</summary>
    /// <remarks>
    /// Connecting answers with a URL for the browser — the same hand-off as signing in, and for
    /// the same reason. Disconnecting happens here and now.
    /// </remarks>
    public async Task<string?> SetCopilotAsync(bool connect,
        CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            connect ? Commands.ConnectCopilot() : Commands.DisconnectCopilot(), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.Error?.Message;
            return null;
        }
        if (!connect)
        {
            await LoadAgentsAsync(cancellationToken);
            return null;
        }
        return response.Value.TryGetProperty("authorizeUrl", out var url) ? url.GetString() : null;
    }

    /// <summary>Change how one agent runs.</summary>
    public async Task<bool> SetAgentModeAsync(string agent, string mode,
        CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.SetAgentMode(agent, mode), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.Error?.Message;
            return false;
        }
        await LoadAgentsAsync(cancellationToken);
        return true;
    }

    /// <summary>Store a key for one service.</summary>
    /// <remarks>
    /// Needs a connection, like everything else that is not a task: a key queued on this machine
    /// would be a secret sitting in a write journal for no good reason.
    /// </remarks>
    public async Task<bool> SaveCredentialAsync(string serviceId, string key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }
        var response = await _core.CallAsync(
            Commands.SaveAgentCredential(serviceId, key.Trim()), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.IsStillPending
                ? "Saving a key needs a connection."
                : response.Error?.Message;
            return false;
        }
        await LoadAgentsAsync(cancellationToken);
        return true;
    }

    /// <summary>What this account has finished, inspired and supported.</summary>
    /// <remarks>
    /// Fetched rather than counted here: they are about the whole account across every device, and
    /// a client counting its own cache would answer with whatever it happens to have synced.
    /// </remarks>
    public ProfileStats Stats
    {
        get => _stats;
        private set => Set(ref _stats, value);
    }

    /// <summary>Where the last export was written, once one has been.</summary>
    public string? LastExportPath
    {
        get => _lastExportPath;
        private set => Set(ref _lastExportPath, value);
    }

    /// <summary>Write everything this account has to a file.</summary>
    public async Task<bool> ExportAsync(string format, string path,
        CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.ExportAccount(format, path), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.IsStillPending
                ? "An export needs a connection."
                : response.Error?.Message;
            return false;
        }
        ErrorMessage = null;
        LastExportPath = path;
        return true;
    }

    /// <summary>Read the account from the cache, then catch it up.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await ReadAsync(Commands.Settings(), cancellationToken);
        IsLoading = true;
        try
        {
            await ReadAsync(Commands.RefreshSettings(), cancellationToken);
            // After the account, because it needs to know who is signed in — and quietly, because
            // three numbers missing is not worth a message beside somebody's own name.
            var stats = await _core.CallAsync(Commands.ProfileStats(), cancellationToken);
            if (stats.Ok && stats.Read<ProfileStats>() is { } read)
            {
                Stats = read;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Change one setting.</summary>
    /// <remarks>
    /// One field at a time, merged in the core: a screen sending a single toggle must not clear
    /// everything else somebody has set, possibly on another client.
    /// </remarks>
    public Task<bool> SetAsync(string field, object? value,
        CancellationToken cancellationToken = default) =>
        WriteAsync(new Dictionary<string, object?> { [field] = value }, cancellationToken);

    /// <summary>Turn quiet hours on with a window, or off entirely.</summary>
    /// <remarks>
    /// Both ends together, because the server reads their absence as "no quiet hours" — sending
    /// only a start would leave a window with no end, which nothing can act on.
    /// </remarks>
    public Task<bool> SetQuietHoursAsync(string? start, string? end,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new Dictionary<string, object?>
            {
                ["quietHoursStart"] = start,
                ["quietHoursEnd"] = end,
            },
            cancellationToken);

    private async Task<bool> WriteAsync(IReadOnlyDictionary<string, object?> changes,
        CancellationToken cancellationToken)
    {
        var response = await _core.CallAsync(
            Commands.UpdateReminderSettings(changes), cancellationToken);
        return Read(response);
    }

    private async Task ReadAsync(object command, CancellationToken cancellationToken)
    {
        Read(await _core.CallAsync(command, cancellationToken));
    }

    private bool Read(AstridResponse response)
    {
        if (!response.Ok)
        {
            if (response.NeedsSignIn)
            {
                NeedsSignIn = true;
                ErrorMessage = null;
                return false;
            }
            // Offline is not a failure to report here: what is on screen came from the cache and
            // is still what this account last chose.
            ErrorMessage = response.IsStillPending ? null : response.Error?.Message;
            return false;
        }
        ErrorMessage = null;
        var account = response.Read<AccountSettings>();
        if (account is null)
        {
            return false;
        }
        User = account.User;
        Reminders = account.ReminderSettings;
        Offsets.Clear();
        foreach (var offset in account.Offsets)
        {
            Offsets.Add(offset);
        }
        return true;
    }
}
