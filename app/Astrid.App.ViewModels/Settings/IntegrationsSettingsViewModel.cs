using System.Collections.ObjectModel;
using System.Text.Json;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The Integrations page: how Google lists get linked, and the account's side of external sync —
/// who each provider is connected as, what it mirrors, and Google's "New List" (task e32f1322).
/// (Copilot is drawn on the same page and read from the Agent Hub, so it lives with
/// <see cref="AgentSettingsViewModel"/>.)
/// </summary>
/// <remarks>
/// Account-wide, not per list: the modes make counterparts for every list on both sides, so the
/// choice lives beside the account rather than in one list's settings.
/// </remarks>
public sealed class IntegrationsSettingsViewModel : ObservableObject
{
    private readonly SettingsSession _session;
    private string _googleSyncMode = "manual";
    private bool _googleConnected;
    private string? _googleConnectedAs;
    private bool _gitHubConnected;
    private string? _gitHubConnectedAs;
    private string? _externalAccountsUnavailable;
    private bool _externalAccountsLoaded;
    private bool _creatingGoogleList;
    private ListSummary? _newGoogleListTarget;

    public IntegrationsSettingsViewModel(SettingsSession session)
    {
        _session = session;
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
        var response = await _session.Core.CallAsync(Commands.GoogleSyncMode(), cancellationToken);
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
        var response = await _session.Core.CallAsync(
            Commands.SetGoogleSyncMode(mode), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.IsStillPending
                ? "Changing how lists link needs a connection."
                : response.Error?.Message;
            return false;
        }
        await LoadGoogleSyncModeAsync(cancellationToken);
        return true;
    }

    // ── The account panel (task e32f1322) ────────────────────────────────────────────────────
    //
    // One command for the whole panel, which is how the core answers it: a panel filled by three
    // round trips is a panel with three chances to be seen half drawn.

    /// <summary>The core's own name for each provider, which is what it answers them under.</summary>
    private const string GoogleProvider = "google_tasks";
    private const string GitHubProvider = "git_hub";

    /// <summary>Every Astrid list Google mirrors, both ends of each named.</summary>
    public ObservableCollection<ExternalAccountLink> GoogleLinks { get; } = [];

    /// <summary>Every Astrid list GitHub mirrors.</summary>
    public ObservableCollection<ExternalAccountLink> GitHubLinks { get; } = [];

    /// <summary>
    /// The lists that could be given a Google list — the account's own, less the ones Google
    /// already has a link for.
    /// </summary>
    /// <remarks>
    /// Offering an already-linked list would be offering to make it a second Google list, which is
    /// not what "New List" means.
    /// </remarks>
    public ObservableCollection<ListSummary> LinkableLists { get; } = [];

    public bool GoogleConnected
    {
        get => _googleConnected;
        private set => Set(ref _googleConnected, value);
    }

    /// <summary>The Google account the lists are mirrored into, when the server names one.</summary>
    /// <remarks>
    /// Which account and not just whether: somebody with a work Google account and a personal one
    /// cannot tell from a tick which of them their lists are going into.
    /// </remarks>
    public string? GoogleConnectedAs
    {
        get => _googleConnectedAs;
        private set => Set(ref _googleConnectedAs, value);
    }

    public bool GitHubConnected
    {
        get => _gitHubConnected;
        private set => Set(ref _gitHubConnected, value);
    }

    public string? GitHubConnectedAs
    {
        get => _gitHubConnectedAs;
        private set => Set(ref _gitHubConnectedAs, value);
    }

    /// <summary>
    /// Why the panel is not there, when it is not: offline, or a refusal. Null when it stands.
    /// </summary>
    public string? ExternalAccountsUnavailable
    {
        get => _externalAccountsUnavailable;
        private set
        {
            if (Set(ref _externalAccountsUnavailable, value))
            {
                Raise(nameof(HasExternalAccountsUnavailable));
            }
        }
    }

    public bool HasExternalAccountsUnavailable => ExternalAccountsUnavailable is not null;

    /// <summary>Asked, answered, and none: the "nothing linked" line, per provider.</summary>
    /// <remarks>
    /// Not yet asked is not none — an empty section drawn before the answer arrives reads as a
    /// statement that nothing is mirrored.
    /// </remarks>
    public bool HasNoGoogleLinks => ExternalAccountsLoaded
        && ExternalAccountsUnavailable is null
        && GoogleLinks.Count == 0;

    public bool HasNoGitHubLinks => ExternalAccountsLoaded
        && ExternalAccountsUnavailable is null
        && GitHubLinks.Count == 0;

    private bool ExternalAccountsLoaded
    {
        get => _externalAccountsLoaded;
        set
        {
            if (Set(ref _externalAccountsLoaded, value))
            {
                Raise(nameof(HasNoGoogleLinks));
                Raise(nameof(HasNoGitHubLinks));
            }
        }
    }

    /// <summary>The list "New List" would make a Google list for; null until one is chosen.</summary>
    public ListSummary? NewGoogleListTarget
    {
        get => _newGoogleListTarget;
        set
        {
            if (Set(ref _newGoogleListTarget, value))
            {
                Raise(nameof(CanCreateGoogleList));
            }
        }
    }

    public bool CanCreateGoogleList => NewGoogleListTarget is not null && !_creatingGoogleList;

    /// <summary>Read the account's side of external sync: who, and what is mirrored.</summary>
    /// <remarks>
    /// Online-only, like the rest of the Integrations page — a refusal is worded rather than drawn
    /// as an empty panel.
    /// </remarks>
    public async Task<bool> LoadExternalAccountsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(Commands.ExternalAccounts(), cancellationToken);
        if (!response.Ok)
        {
            ExternalAccountsUnavailable = response.IsStillPending
                ? "Linked lists need a connection."
                : response.Error?.Message ?? "Linked lists could not be loaded.";
            ExternalAccountsLoaded = true;
            Raise(nameof(HasNoGoogleLinks));
            Raise(nameof(HasNoGitHubLinks));
            return false;
        }
        ExternalAccountsUnavailable = null;

        var providers = Providers(response);
        Show(Find(providers, GoogleProvider), GoogleLinks,
            connected => GoogleConnected = connected, name => GoogleConnectedAs = name);
        Show(Find(providers, GitHubProvider), GitHubLinks,
            connected => GitHubConnected = connected, name => GitHubConnectedAs = name);

        ExternalAccountsLoaded = true;
        Raise(nameof(HasNoGoogleLinks));
        Raise(nameof(HasNoGitHubLinks));
        await LoadLinkableListsAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Google's "New List": make the list on Google and bind it to the chosen Astrid list.
    /// </summary>
    /// <remarks>
    /// One command because it is one gesture, and the core owns the half-done state — a list made
    /// on Google that could not be bound is adopted by the next attempt rather than duplicated, so
    /// pressing this twice is safe. The panel is read back afterwards rather than guessed at, for
    /// the same reason: the core may have adopted an existing Google list of that name.
    /// </remarks>
    public async Task<bool> CreateGoogleListAsync(CancellationToken cancellationToken = default)
    {
        if (NewGoogleListTarget is not { } target)
        {
            return false;
        }
        _creatingGoogleList = true;
        Raise(nameof(CanCreateGoogleList));
        try
        {
            var response = await _session.Core.CallAsync(
                Commands.CreateAndLinkGoogleList(target.Id, target.Name), cancellationToken);
            if (!response.Ok)
            {
                _session.ErrorMessage = response.IsStillPending
                    ? "Making a list on Google needs a connection."
                    : response.Error?.Message;
                return false;
            }
            _session.ErrorMessage = null;
            NewGoogleListTarget = null;
            return await LoadExternalAccountsAsync(cancellationToken);
        }
        finally
        {
            _creatingGoogleList = false;
            Raise(nameof(CanCreateGoogleList));
        }
    }

    /// <summary>The account's lists, less the ones Google has already linked.</summary>
    private async Task LoadLinkableListsAsync(CancellationToken cancellationToken)
    {
        var response = await _session.Core.CallAsync(Commands.Lists(), cancellationToken);
        if (!response.Ok)
        {
            return;
        }
        var linked = GoogleLinks.Select(link => link.ListId).ToHashSet(StringComparer.Ordinal);
        // Places only, as the sidebar reads them: a board column is a state and a label is a chip,
        // and neither is somewhere a Google list could stand for.
        var offered = response.ReadArray<ListSummary>()
            .Where(list => list.IsDomainList && !linked.Contains(list.Id))
            .ToList();
        var chosen = NewGoogleListTarget?.Id;
        LinkableLists.Clear();
        foreach (var list in offered)
        {
            LinkableLists.Add(list);
        }
        // The chosen list survives a refresh only while it is still on offer; one that has just
        // been linked is gone, and a picker left pointing at it would make the button a no-op.
        NewGoogleListTarget = chosen is null
            ? null
            : LinkableLists.FirstOrDefault(list => list.Id == chosen);
    }

    private static IReadOnlyList<ExternalAccountProvider> Providers(AstridResponse response)
    {
        if (!response.Value.TryGetProperty("providers", out var providers))
        {
            return [];
        }
        return providers.Deserialize<List<ExternalAccountProvider>>(CommandJson.Options) ?? [];
    }

    private static ExternalAccountProvider? Find(
        IReadOnlyList<ExternalAccountProvider> providers, string provider) =>
        providers.FirstOrDefault(entry =>
            string.Equals(entry.Provider, provider, StringComparison.Ordinal));

    private static void Show(
        ExternalAccountProvider? provider,
        ObservableCollection<ExternalAccountLink> links,
        Action<bool> setConnected,
        Action<string?> setConnectedAs)
    {
        setConnected(provider?.Connected ?? false);
        setConnectedAs(provider?.ConnectedAs);
        links.Clear();
        foreach (var link in provider?.Links ?? [])
        {
            links.Add(link);
        }
    }
}
