using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// One list's settings, and who it is shared with.
/// </summary>
/// <remarks>
/// <para>
/// Membership reaches the network rather than the Outbox, and that is deliberate in the core: an
/// invitation is not a local fact, and an optimistic member row would be indistinguishable from a
/// real one to every permission check that read it afterwards. So these calls can fail while
/// offline, and they say so instead of pretending.
/// </para>
/// <para>
/// What this account may do arrives with the members. Whether somebody can change a role is a
/// permission rule, and a screen that worked it out itself would be the fourth implementation of a
/// rule whose failure mode is a control that 403s — or one quietly missing for somebody who should
/// have it.
/// </para>
/// </remarks>
public sealed class ListSettingsViewModel : ObservableObject
{
    private readonly IAstridCore _core;
    private string _listId = string.Empty;
    private string _name = string.Empty;
    private bool _canManageMembers;
    private bool _canManageList;
    private bool _canDeleteList;
    private bool _canLeave;
    private bool _isLoading;
    private string? _errorMessage;
    private bool _needsSignIn;

    public ListSettingsViewModel(IAstridCore core)
    {
        _core = core;
    }

    /// <summary>Who the list is shared with, as the server has them.</summary>
    public ObservableCollection<ListMember> Members { get; } = [];

    public string ListId
    {
        get => _listId;
        private set => Set(ref _listId, value);
    }

    public string Name
    {
        get => _name;
        private set => Set(ref _name, value);
    }

    public bool CanManageMembers
    {
        get => _canManageMembers;
        private set => Set(ref _canManageMembers, value);
    }

    public bool CanManageList
    {
        get => _canManageList;
        private set => Set(ref _canManageList, value);
    }

    public bool CanDeleteList
    {
        get => _canDeleteList;
        private set => Set(ref _canDeleteList, value);
    }

    /// <summary>Leaving is for a list somebody else owns; an owner leaving would strand it.</summary>
    public bool CanLeave
    {
        get => _canLeave;
        private set => Set(ref _canLeave, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    /// <summary>
    /// The session has expired.
    /// </summary>
    /// <remarks>
    /// Membership is the first thing to notice, because it is the only screen here that reaches
    /// the network on its own. Reporting "the session is not valid" as a red line beside an empty
    /// member list would leave somebody staring at a message instead of a sign-in button.
    /// </remarks>
    public bool NeedsSignIn
    {
        get => _needsSignIn;
        private set => Set(ref _needsSignIn, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    /// <summary>
    /// Where this list is mirrored, and where it could be.
    /// </summary>
    /// <remarks>
    /// Google and GitHub work differently and the panel says so: a GitHub link is synced by a cron
    /// on the server whether or not this app is running, and a Google one is synced by this app
    /// when it runs.
    /// </remarks>
    public ObservableCollection<ExternalProvider> Providers { get; } = [];

    /// <summary>Load where this list is mirrored.</summary>
    public async Task LoadExternalAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ListId))
        {
            return;
        }
        var response = await _core.CallAsync(Commands.ExternalSync(ListId), cancellationToken);
        if (!Handle(response))
        {
            return;
        }
        var sync = response.Read<ExternalSync>();
        if (sync is not null)
        {
            Replace(Providers, sync.Providers);
        }
    }

    /// <summary>Start connecting a provider. Answers with the URL a browser should open.</summary>
    public async Task<string?> ConnectProviderAsync(string provider,
        CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.ConnectProvider(provider), cancellationToken);
        if (!Handle(response))
        {
            return null;
        }
        return response.Value.TryGetProperty("authorizeUrl", out var url) ? url.GetString() : null;
    }

    /// <summary>Mirror this list to a container, or stop.</summary>
    public async Task<bool> SetLinkAsync(string provider, string? containerId, string? linkId,
        CancellationToken cancellationToken = default)
    {
        var response = containerId is null && linkId is not null
            ? await _core.CallAsync(Commands.UnlinkList(provider, linkId), cancellationToken)
            : await _core.CallAsync(
                Commands.LinkList(provider, ListId, containerId ?? string.Empty), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        await LoadExternalAsync(cancellationToken);
        return true;
    }

    /// <summary>Load one list's settings and members.</summary>
    public async Task LoadAsync(string listId, CancellationToken cancellationToken = default)
    {
        ListId = listId;
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var response = await _core.CallAsync(Commands.ListMembers(listId), cancellationToken);
            if (!Handle(response))
            {
                return;
            }
            var settings = response.Read<ListSettings>();
            if (settings is null)
            {
                return;
            }
            Name = settings.Name;
            CanManageMembers = settings.CanManageMembers;
            CanManageList = settings.CanManageList;
            CanDeleteList = settings.CanDeleteList;
            CanLeave = settings.CanLeave;
            Replace(Members, settings.Members);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Invite somebody by email.</summary>
    public async Task<bool> InviteAsync(string email, string role = "member",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }
        var response = await _core.CallAsync(
            Commands.InviteToList(ListId, email.Trim(), role), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        await LoadAsync(ListId, cancellationToken);
        return true;
    }

    public async Task<bool> SetRoleAsync(string userId, string role,
        CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.SetMemberRole(ListId, userId, role), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        await LoadAsync(ListId, cancellationToken);
        return true;
    }

    public async Task<bool> RemoveAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.RemoveMember(ListId, userId), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        await LoadAsync(ListId, cancellationToken);
        return true;
    }

    /// <summary>Leave a list somebody else owns.</summary>
    public async Task<bool> LeaveAsync(CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.LeaveList(ListId), cancellationToken);
        return Handle(response);
    }

    /// <summary>Rename the list. An ordinary edit, so it goes through the Outbox.</summary>
    public async Task<bool> RenameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim() == Name)
        {
            return false;
        }
        var response = await _core.CallAsync(
            Commands.UpdateList(ListId, new Dictionary<string, object?> { ["name"] = name.Trim() }),
            cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        Name = name.Trim();
        return true;
    }

    /// <summary>Private, shared, or public.</summary>
    public async Task<bool> SetPrivacyAsync(string privacy,
        CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.UpdateList(ListId, new Dictionary<string, object?> { ["privacy"] = privacy }),
            cancellationToken);
        return Handle(response);
    }

    /// <summary>
    /// Report what went wrong, except when nothing did.
    /// </summary>
    /// <remarks>
    /// An offline write is in the Outbox and will go, so it is not an error — but membership does
    /// NOT go through the Outbox, so an offline invitation genuinely did not happen and has to say
    /// so. That is why this reports what the core answered rather than deciding for itself.
    /// </remarks>
    private bool Handle(AstridResponse response)
    {
        if (response.Ok)
        {
            ErrorMessage = null;
            return true;
        }
        if (response.NeedsSignIn)
        {
            NeedsSignIn = true;
            ErrorMessage = null;
            return false;
        }
        ErrorMessage = response.IsStillPending
            ? "That needs a connection — nobody has been invited yet."
            : response.Error?.Message;
        return false;
    }

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
