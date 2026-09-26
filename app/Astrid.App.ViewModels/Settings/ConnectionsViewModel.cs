using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The Connections page (astrid-web #285): everything that can act as this account, grouped the
/// way the web groups it, with a way to stop each one — and the developer's half, making and
/// editing an OAuth client by hand (the Apple clients' AITD-419).
/// </summary>
/// <remarks>
/// <para>
/// Thin on purpose, as the Apple <c>ConnectionsModel</c> is: which rows group under which heading,
/// which look unused, which can be edited and what a draft may say are the core's answers. What
/// lives here is the round trip — a revoke is optimistic and puts its own row back if refused, the
/// editor asks the core after every edit, a minted secret is held only as long as the screen.
/// </para>
/// <para>
/// A secret written to disk is one the account cannot rotate by revoking, because nobody knows it
/// is there. So the plaintext lives in memory, until <see cref="ForgetMintedCredentials"/>.
/// </para>
/// </remarks>
public sealed class ConnectionsViewModel : ObservableObject
{
    private readonly SettingsSession _session;
    private bool _isLoading;
    private bool _loaded;
    private int _reviewCount;
    private bool _requiresWebSession;
    private MintedClient? _mintedClient;
    private bool _isEditorOpen;
    private string? _editingClientId;
    private string _draftName = string.Empty;
    private string _draftDescription = string.Empty;
    private string _draftRedirectUriText = string.Empty;
    private IReadOnlyList<string> _draftGrantTypes = ["client_credentials"];
    private DraftProblem? _draftProblem;
    private bool _canSend;
    private bool _isSaving;

    public ConnectionsViewModel(SettingsSession session)
    {
        _session = session;
    }

    // ── The list ─────────────────────────────────────────────────────────────────────────────

    /// <summary>The rows, as sections in the core's display order, empty ones omitted.</summary>
    public ObservableCollection<ConnectionSection> Sections { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    /// <summary>Whether the page has been read at least once and holds nothing at all.</summary>
    public bool IsEmpty => _loaded && !IsLoading && Sections.Count == 0;

    /// <summary>How many rows look unused — the line at the top, before any row is read.</summary>
    public int ReviewCount
    {
        get => _reviewCount;
        private set
        {
            if (Set(ref _reviewCount, value))
            {
                Raise(nameof(HasReviews));
            }
        }
    }

    public bool HasReviews => ReviewCount > 0;

    /// <summary>
    /// The server refused because the write needs an interactive session. This client sends its
    /// session cookie so it normally does not happen; when it does, the page says "do this on the
    /// web" rather than failing blankly.
    /// </summary>
    public bool RequiresWebSession
    {
        get => _requiresWebSession;
        private set => Set(ref _requiresWebSession, value);
    }

    /// <summary>
    /// Read what is connected.
    /// </summary>
    /// <remarks>
    /// Reads only. Opening the page must not mint anything: a screen that created a credential by
    /// being looked at is a screen that fills an account with credentials nobody is holding.
    /// </remarks>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var response = await _session.Core.CallAsync(Commands.Connections(), cancellationToken);
            if (!response.Ok)
            {
                _session.ErrorMessage = response.IsStillPending
                    ? "Reading connections needs a connection."
                    : response.Error?.Message;
                return;
            }
            var panel = response.Read<ConnectionsPanel>();
            Sections.Clear();
            foreach (var section in panel?.Sections ?? [])
            {
                Sections.Add(section);
            }
            ReviewCount = panel?.ReviewCount ?? 0;
            _loaded = true;
            _session.ErrorMessage = null;
        }
        finally
        {
            IsLoading = false;
            Raise(nameof(IsEmpty));
        }
    }

    /// <summary>
    /// Stop one connection. Optimistic: the row disappears at once and comes back, in place, if
    /// the server refuses.
    /// </summary>
    /// <remarks>
    /// Only <em>this</em> row comes back. Revokes can overlap, and a refresh can land mid-request,
    /// so restoring a snapshot of the whole list would resurrect a row another revoke had just
    /// removed for good.
    /// </remarks>
    public async Task<bool> RevokeAsync(Connection connection, CancellationToken cancellationToken = default)
    {
        if (!connection.Revocable)
        {
            return false;
        }
        var (section, index) = Locate(connection);
        if (section is null)
        {
            return false;
        }
        RequiresWebSession = false;
        Remove(section, index);
        var response = await _session.Core.CallAsync(
            Commands.RevokeConnection(connection.Kind, connection.Id), cancellationToken);
        if (response.Ok)
        {
            // Recomputed from the rows in hand, so revoking one takes it out of the count without
            // another round trip.
            ReviewCount = Sections.Sum(s => s.Rows.Count(row => row.Review is not null));
            if (MintedClient is not null && connection.Detail?.ClientId == MintedClient.ClientId)
            {
                // The pair just made is the one revoked: the secret on screen is now useless.
                MintedClient = null;
            }
            _session.ErrorMessage = null;
            return true;
        }
        Restore(section, index, connection);
        RequiresWebSession = response.Error?.Status == 403;
        _session.ErrorMessage = response.IsStillPending
            ? "Revoking needs a connection."
            : response.Error?.Message;
        return false;
    }

    private (ConnectionSection? Section, int Index) Locate(Connection connection)
    {
        foreach (var section in Sections)
        {
            var index = section.Rows.ToList().FindIndex(row => row.Kind == connection.Kind && row.Id == connection.Id);
            if (index >= 0)
            {
                return (section, index);
            }
        }
        return (null, -1);
    }

    /// <summary>Take a row out of its section; a section left empty goes too, as the core omits one.</summary>
    private void Remove(ConnectionSection section, int index)
    {
        var rows = section.Rows.ToList();
        rows.RemoveAt(index);
        ReplaceSection(section, rows);
    }

    /// <summary>Put a refused row back where it was — or into a section of its own if that went.</summary>
    private void Restore(ConnectionSection section, int index, Connection connection)
    {
        var live = Sections.FirstOrDefault(s => s.TitleKey == section.TitleKey);
        var rows = live?.Rows.ToList() ?? [];
        rows.Insert(Math.Min(index, rows.Count), connection);
        if (live is null)
        {
            Sections.Add(section with { Rows = rows });
            Raise(nameof(IsEmpty));
            return;
        }
        ReplaceSection(live, rows);
    }

    private void ReplaceSection(ConnectionSection section, List<Connection> rows)
    {
        var at = Sections.IndexOf(section);
        if (at < 0)
        {
            return;
        }
        if (rows.Count == 0)
        {
            Sections.RemoveAt(at);
        }
        else
        {
            Sections[at] = section with { Rows = rows };
        }
        Raise(nameof(IsEmpty));
    }

    // ── A minted secret, shown once ──────────────────────────────────────────────────────────

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

    /// <summary>
    /// Forget the plaintext.
    /// </summary>
    /// <remarks>
    /// Called when the settings panel closes. It lives for as long as the screen showing it and no
    /// longer — that is the whole of its storage policy.
    /// </remarks>
    public void ForgetMintedCredentials()
    {
        MintedClient = null;
    }

    // ── The editor (AITD-419) ────────────────────────────────────────────────────────────────

    /// <summary>Whether the new/edit form is open.</summary>
    public bool IsEditorOpen
    {
        get => _isEditorOpen;
        private set => Set(ref _isEditorOpen, value);
    }

    /// <summary>The client being changed, or null when making one.</summary>
    public string? EditingClientId
    {
        get => _editingClientId;
        private set
        {
            if (Set(ref _editingClientId, value))
            {
                Raise(nameof(IsEditing));
                Raise(nameof(IsCreating));
            }
        }
    }

    public bool IsEditing => EditingClientId is not null;

    /// <summary>
    /// Making one, so the name, description, scopes and grants are open. An edit changes the
    /// redirect URIs only, as the web's dialog does, so the two consoles agree on what an edit
    /// means.
    /// </summary>
    public bool IsCreating => EditingClientId is null;

    public string DraftName
    {
        get => _draftName;
        set => Set(ref _draftName, value);
    }

    public string DraftDescription
    {
        get => _draftDescription;
        set => Set(ref _draftDescription, value);
    }

    /// <summary>One URI per line, as typed — blank lines are what typing looks like.</summary>
    public string DraftRedirectUriText
    {
        get => _draftRedirectUriText;
        set => Set(ref _draftRedirectUriText, value);
    }

    /// <summary>The scopes the draft asks for. Which exist is the core's list.</summary>
    public ObservableCollection<string> DraftScopes { get; } = [];

    /// <summary>
    /// The scopes a picker may offer, from the core, each saying whether the draft asks for it.
    /// Rebuilt after every check, so a box shows the set that resulted rather than the one clicked.
    /// </summary>
    public ObservableCollection<ScopeChoice> ScopeChoices { get; } = [];

    /// <summary>The three grants, each saying whether the draft carries it — after the pairing.</summary>
    public ObservableCollection<GrantChoice> GrantChoices { get; } = [];

    private static readonly (string Grant, string LabelKey, string HintKey)[] Grants =
    [
        ("client_credentials", "connections.grant.client_credentials", "connections.grant.client_credentials_hint"),
        ("authorization_code", "connections.grant.authorization_code", "connections.grant.authorization_code_hint"),
        ("refresh_token", "connections.grant.refresh_token", "connections.grant.refresh_token_hint"),
    ];

    /// <summary>The grants as the core settled them after the last check, in wire order.</summary>
    public IReadOnlyList<string> DraftGrantTypes
    {
        get => _draftGrantTypes;
        private set => Set(ref _draftGrantTypes, value);
    }

    /// <summary>The first thing to fix, or null. The view words it in the reader's language.</summary>
    public DraftProblem? DraftProblem
    {
        get => _draftProblem;
        private set
        {
            if (Set(ref _draftProblem, value))
            {
                Raise(nameof(HasDraftProblem));
            }
        }
    }

    public bool HasDraftProblem => DraftProblem is not null;

    /// <summary>Whether the draft can go to the server, as the core last judged it.</summary>
    public bool CanSend
    {
        get => _canSend;
        private set => Set(ref _canSend, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set => Set(ref _isSaving, value);
    }

    /// <summary>Open the form to make a client: one grant, the one a script needs.</summary>
    public async Task BeginCreateAsync(CancellationToken cancellationToken = default)
    {
        EditingClientId = null;
        DraftName = string.Empty;
        DraftDescription = string.Empty;
        DraftRedirectUriText = string.Empty;
        DraftScopes.Clear();
        DraftGrantTypes = ["client_credentials"];
        IsEditorOpen = true;
        await CheckDraftAsync(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Open the form on a client that exists. It has to be fetched: the list knows its id but not
    /// its redirect URIs.
    /// </summary>
    public async Task<bool> BeginEditAsync(string clientId, CancellationToken cancellationToken = default)
    {
        RequiresWebSession = false;
        var response = await _session.Core.CallAsync(Commands.LoadOAuthClient(clientId), cancellationToken);
        if (!response.Ok || response.Read<OAuthClientSummary>() is not { } client)
        {
            _session.ErrorMessage = response.IsStillPending
                ? "Editing a client needs a connection."
                : response.Error?.Message;
            return false;
        }
        EditingClientId = client.ClientId;
        DraftName = client.Name;
        DraftDescription = client.Description ?? string.Empty;
        DraftRedirectUriText = string.Join(Environment.NewLine, client.RedirectUris);
        DraftScopes.Clear();
        foreach (var scope in client.Scopes)
        {
            DraftScopes.Add(scope);
        }
        DraftGrantTypes = client.GrantTypes;
        IsEditorOpen = true;
        await CheckDraftAsync(cancellationToken: cancellationToken);
        return true;
    }

    public void CancelEditor()
    {
        IsEditorOpen = false;
        EditingClientId = null;
        DraftProblem = null;
    }

    /// <summary>Tick or untick one scope, then ask the core again.</summary>
    public async Task SetScopeAsync(string scope, bool on, CancellationToken cancellationToken = default)
    {
        if (on && !DraftScopes.Contains(scope))
        {
            DraftScopes.Add(scope);
        }
        else if (!on)
        {
            DraftScopes.Remove(scope);
        }
        await CheckDraftAsync(cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Flip one grant. The core applies the pairing — <c>refresh_token</c> and
    /// <c>authorization_code</c> travel together, and the last grant cannot go — and answers with
    /// the set that results.
    /// </summary>
    public Task ToggleGrantAsync(string grant, CancellationToken cancellationToken = default) =>
        CheckDraftAsync(grant, cancellationToken);

    /// <summary>Ask the core what the draft is worth as it stands.</summary>
    public async Task CheckDraftAsync(string? toggleGrant = null, CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(
            Commands.CheckOAuthClientDraft(Draft(), toggleGrant), cancellationToken);
        if (!response.Ok || response.Read<OAuthClientDraftCheck>() is not { } check)
        {
            return;
        }
        DraftGrantTypes = check.GrantTypes;
        DraftProblem = check.Problem;
        CanSend = check.CanSend;
        GrantChoices.Clear();
        foreach (var (grant, labelKey, hintKey) in Grants)
        {
            GrantChoices.Add(new GrantChoice(grant, labelKey, hintKey, check.GrantTypes.Contains(grant)));
        }
        ScopeChoices.Clear();
        foreach (var scope in check.Scopes)
        {
            ScopeChoices.Add(new ScopeChoice(scope, DraftScopes.Contains(scope)));
        }
    }

    /// <summary>
    /// Send the draft. A create holds the secret open rather than closing — closing on top of it
    /// would lose the only copy there will ever be; an edit has nothing to show and closes.
    /// </summary>
    /// <returns>True when the form should close.</returns>
    public async Task<bool> SaveAsync(CancellationToken cancellationToken = default)
    {
        await CheckDraftAsync(cancellationToken: cancellationToken);
        if (!CanSend)
        {
            // A draft the core can reject is one the server never sees.
            return false;
        }
        IsSaving = true;
        RequiresWebSession = false;
        try
        {
            var command = EditingClientId is { } clientId
                ? Commands.UpdateOAuthClient(clientId, Draft())
                : Commands.CreateOAuthClient(Draft());
            var response = await _session.Core.CallAsync(command, cancellationToken);
            if (!response.Ok)
            {
                RequiresWebSession = response.Error?.Status == 403;
                _session.ErrorMessage = response.IsStillPending
                    ? "Saving a client needs a connection."
                    : response.Error?.Message;
                return false;
            }
            _session.ErrorMessage = null;
            if (EditingClientId is null)
            {
                MintedClient = response.Read<MintedClient>();
                IsEditorOpen = false;
                await LoadAsync(cancellationToken);
                return false;
            }
            IsEditorOpen = false;
            EditingClientId = null;
            await LoadAsync(cancellationToken);
            return true;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private OAuthClientDraft Draft() => new()

    {
        Name = DraftName,
        Description = DraftDescription,
        Scopes = DraftScopes.ToList(),
        GrantTypes = DraftGrantTypes,
        RedirectUriText = DraftRedirectUriText,
    };
}

/// <summary>One grant as a checkbox draws it: the wire name, the words, and whether it is on.</summary>
public sealed record GrantChoice(string Grant, string LabelKey, string HintKey, bool IsOn);

/// <summary>One scope as a checkbox draws it.</summary>
public sealed record ScopeChoice(string Scope, bool IsOn);
