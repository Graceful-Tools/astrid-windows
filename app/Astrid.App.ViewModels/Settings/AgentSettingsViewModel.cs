using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The AI agents page: the Agent Hub's modes and credentials, Copilot, the webhook, and the
/// account's own agents.
/// </summary>
/// <remarks>
/// Everything here is online-only, like everything that is not a task: a key or a webhook queued
/// on this machine would be a secret sitting in a write journal for no good reason.
/// </remarks>
public sealed class AgentSettingsViewModel : ObservableObject
{
    private readonly SettingsSession _session;
    private bool _copilotConnected;
    private WebhookSettings _webhook = new();
    private string? _webhookUrl;
    private string? _newWebhookSecret;
    private string? _webhookTestResult;

    public AgentSettingsViewModel(SettingsSession session)
    {
        _session = session;
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
        var response = await _session.Core.CallAsync(Commands.Agents(), cancellationToken);
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
        var response = await _session.Core.CallAsync(
            connect ? Commands.ConnectCopilot() : Commands.DisconnectCopilot(), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.Error?.Message;
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
        var response = await _session.Core.CallAsync(
            Commands.SetAgentMode(agent, mode), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.Error?.Message;
            return false;
        }
        await LoadAgentsAsync(cancellationToken);
        return true;
    }

    /// <summary>Store a key for one service.</summary>
    public async Task<bool> SaveCredentialAsync(string serviceId, string key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }
        var response = await _session.Core.CallAsync(
            Commands.SaveAgentCredential(serviceId, key.Trim()), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.IsStillPending
                ? "Saving a key needs a connection."
                : response.Error?.Message;
            return false;
        }
        await LoadAgentsAsync(cancellationToken);
        return true;
    }

    // ── The webhook, and the account's own agents ────────────────────────────────────────────

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
        var response = await _session.Core.CallAsync(Commands.WebhookSettings(), cancellationToken);
        if (response.Ok && response.Read<WebhookSettings>() is { } settings)
        {
            _webhookUrl = null;
            Webhook = settings;
        }

        var agents = await _session.Core.CallAsync(Commands.CustomAgents(), cancellationToken);
        if (agents.Ok)
        {
            CustomAgents.Clear();
            foreach (var agent in agents.ReadArray<CustomAgent>())
            {
                CustomAgents.Add(agent);
            }
        }
    }

    /// <summary>Save where deliveries go.</summary>
    public async Task<bool> SaveWebhookAsync(bool regenerateSecret = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(WebhookUrl))
        {
            return false;
        }
        var response = await _session.Core.CallAsync(
            Commands.SaveWebhook(WebhookUrl.Trim(), Webhook.Enabled, Webhook.Events,
                Webhook.Agents, regenerateSecret),
            cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.IsStillPending
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
        var response = await _session.Core.CallAsync(Commands.DeleteWebhook(), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.Error?.Message;
            return false;
        }
        NewWebhookSecret = null;
        await LoadWebhookAsync(cancellationToken);
        return true;
    }

    /// <summary>Fire a test delivery.</summary>
    public async Task TestWebhookAsync(CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(Commands.TestWebhook(), cancellationToken);
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
        var response = await _session.Core.CallAsync(
            Commands.RegisterCustomAgent(name.Trim()), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.Error?.Message;
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
        var response = await _session.Core.CallAsync(
            Commands.DeleteCustomAgent(agentId), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.Error?.Message;
            return false;
        }
        await LoadWebhookAsync(cancellationToken);
        return true;
    }
}
