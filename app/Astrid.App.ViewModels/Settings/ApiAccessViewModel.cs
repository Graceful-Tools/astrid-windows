using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The API access page, as astrid-web's own settings page has it: an MCP token and client
/// credentials, minted here and shown once.
/// </summary>
/// <remarks>
/// This client is already signed in, so it can mint its own credentials rather than sending
/// somebody to a browser to do what it is authorised for. Both plaintexts live only as long as the
/// panel: the server stores a hash and shows each once, and a secret written to disk is one the
/// account cannot rotate by revoking, because nobody knows it is there.
/// </remarks>
public sealed class ApiAccessViewModel : ObservableObject
{
    private readonly SettingsSession _session;
    private string? _mcpToken;
    private MintedClient? _mintedClient;
    private string _newClientName = string.Empty;

    public ApiAccessViewModel(SettingsSession session)
    {
        _session = session;
    }

    /// <summary>The client-credentials pairs this account has registered.</summary>
    public ObservableCollection<OAuthClientRow> ApiClients { get; } = [];

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
        var response = await _session.Core.CallAsync(Commands.ApiAccess(), cancellationToken);
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
        var response = await _session.Core.CallAsync(Commands.CreateMcpToken(), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.IsStillPending
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
        var response = await _session.Core.CallAsync(Commands.RevokeMcpTokens(), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.IsStillPending
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
        var response = await _session.Core.CallAsync(
            Commands.CreateOAuthClient(NewClientName.Trim()), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.IsStillPending
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
        var response = await _session.Core.CallAsync(
            Commands.DeleteOAuthClient(clientId), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.IsStillPending
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
}
