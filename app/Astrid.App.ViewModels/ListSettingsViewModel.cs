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
    private string _color = "#3b82f6";
    private string? _privacy;
    private bool _isFavorite;
    private string? _projectId;

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

    // ── How the list looks, and who can see it (task 53780e75) ─────────────────────────────

    /// <summary>The colour every screen draws the list in.</summary>
    public string Color
    {
        get => _color;
        private set
        {
            if (Set(ref _color, value))
            {
                RefreshSwatches();
            }
        }
    }

    /// <summary>The web's palette, as swatches with the current one marked.</summary>
    public ObservableCollection<ColorSwatch> ColorChoices { get; } = [];

    /// <summary><c>PRIVATE</c>, <c>SHARED</c> or <c>PUBLIC</c>; null when the server never said.</summary>
    public string? Privacy
    {
        get => _privacy;
        private set
        {
            if (Set(ref _privacy, value))
            {
                Raise(nameof(IsPrivate));
                Raise(nameof(IsShared));
                Raise(nameof(IsPublic));
            }
        }
    }

    public bool IsPrivate => Privacy == "PRIVATE";

    public bool IsShared => Privacy == "SHARED";

    public bool IsPublic => Privacy == "PUBLIC";

    /// <summary>Whether this account keeps the list in its Favourites.</summary>
    public bool IsFavorite
    {
        get => _isFavorite;
        private set => Set(ref _isFavorite, value);
    }

    /// <summary>Give the list a colour. An ordinary edit, through the Outbox.</summary>
    public async Task<bool> SetColorAsync(string color, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(color) || color == Color)
        {
            return false;
        }
        var response = await _core.CallAsync(
            Commands.UpdateList(ListId, new Dictionary<string, object?> { ["color"] = color }),
            cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        Color = color;
        return true;
    }

    /// <summary>
    /// Keep the list in Favourites, or not.
    /// </summary>
    /// <remarks>
    /// Its own command rather than a list update: a favourite is this account's, not the list's,
    /// and the core writes it the way the server stores it.
    /// </remarks>
    public async Task<bool> SetFavoriteAsync(bool favorite, CancellationToken cancellationToken = default)
    {
        if (favorite == IsFavorite)
        {
            return false;
        }
        var response = await _core.CallAsync(Commands.SetListFavorite(ListId, favorite), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        IsFavorite = favorite;
        return true;
    }

    // ── The board's columns (task e5214fba) ────────────────────────────────────────────────
    //
    // Shown only when the list has a board. What each write is allowed to do — the name checks,
    // which columns move or go — is the core's, locked against the web; a refusal comes back as
    // the error message the web would show.

    /// <summary>The board this list belongs to, when it belongs to one.</summary>
    public string? ProjectId
    {
        get => _projectId;
        private set
        {
            if (Set(ref _projectId, value))
            {
                Raise(nameof(HasBoard));
            }
        }
    }

    public bool HasBoard => !string.IsNullOrEmpty(ProjectId);

    /// <summary>The board's editable columns: the three defaults, then its own.</summary>
    public ObservableCollection<BoardStatus> Statuses { get; } = [];

    public Task<bool> AddStatusAsync(string name, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(name)
            ? Task.FromResult(false)
            : ChangeStatusesAsync(Commands.AddBoardStatus(ListId, name.Trim()), cancellationToken);

    public Task<bool> RenameStatusAsync(string role, string name, CancellationToken cancellationToken = default)
    {
        var current = Statuses.FirstOrDefault(status => status.Id == role);
        if (string.IsNullOrWhiteSpace(name) || current is null || current.Name == name.Trim())
        {
            return Task.FromResult(false);
        }
        return ChangeStatusesAsync(Commands.RenameBoardStatus(ListId, role, name.Trim()), cancellationToken);
    }

    /// <param name="direction"><c>up</c> or <c>down</c>.</param>
    public Task<bool> MoveStatusAsync(string role, string direction, CancellationToken cancellationToken = default) =>
        ChangeStatusesAsync(Commands.ReorderBoardStatus(ListId, role, direction), cancellationToken);

    public Task<bool> RemoveStatusAsync(string role, CancellationToken cancellationToken = default) =>
        ChangeStatusesAsync(Commands.RemoveBoardStatus(ListId, role), cancellationToken);

    /// <summary>One column write, then the settings are re-read so the section shows the board as it is.</summary>
    private async Task<bool> ChangeStatusesAsync(object command, CancellationToken cancellationToken)
    {
        var response = await _core.CallAsync(command, cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        await LoadAsync(ListId, cancellationToken);
        return true;
    }

    // ── What a new task starts as (task c4102c67) ──────────────────────────────────────────
    //
    // The web's admin tab has Default Priority, Assignee, Repeating, When and When Time, and its
    // quick-add applies them. The core applies them here too, at the door; this only offers the
    // choices and writes the one picked. Every choice carries either a resource key or a name,
    // never a word of its own.

    private ListDefaults _defaults = new();

    public ObservableCollection<DefaultChoice> DefaultPriorityChoices { get; } = [];
    public ObservableCollection<DefaultChoice> DefaultAssigneeChoices { get; } = [];
    public ObservableCollection<DefaultChoice> DefaultRepeatChoices { get; } = [];
    public ObservableCollection<DefaultChoice> DefaultWhenChoices { get; } = [];
    public ObservableCollection<DefaultChoice> DefaultTimeChoices { get; } = [];

    public DefaultChoice? SelectedDefaultPriority => DefaultPriorityChoices.FirstOrDefault(choice => choice.IsSelected);
    public DefaultChoice? SelectedDefaultAssignee => DefaultAssigneeChoices.FirstOrDefault(choice => choice.IsSelected);
    public DefaultChoice? SelectedDefaultRepeat => DefaultRepeatChoices.FirstOrDefault(choice => choice.IsSelected);
    public DefaultChoice? SelectedDefaultWhen => DefaultWhenChoices.FirstOrDefault(choice => choice.IsSelected);
    public DefaultChoice? SelectedDefaultTime => DefaultTimeChoices.FirstOrDefault(choice => choice.IsSelected);

    /// <summary>The list's defaults, as last loaded.</summary>
    public ListDefaults Defaults => _defaults;

    private static readonly string[] Repeats = ["never", "daily", "weekly", "monthly", "yearly"];
    private static readonly (string Value, string Key)[] Whens =
    [
        ("none", "picker.no_due_date"),
        ("today", "picker.today"),
        ("tomorrow", "picker.tomorrow"),
        ("next_week", "picker.next_week"),
    ];
    private static readonly string[] PriorityKeys = ["priority.none", "priority.low", "priority.medium", "priority.high"];

    private void RefreshDefaultChoices(ListDefaults defaults, IReadOnlyList<ListMember> members)
    {
        _defaults = defaults;

        Replace(DefaultPriorityChoices, PriorityKeys
            .Select((key, level) => new DefaultChoice("priority", level.ToString(), key, null, defaults.Priority == level))
            .ToList());

        var assignees = new List<DefaultChoice>
        {
            new("assignee", null, "defaults.task_creator", null, string.IsNullOrEmpty(defaults.AssigneeId)),
            new("assignee", "unassigned", "assignee.unassigned", null, defaults.AssigneeId == "unassigned"),
        };
        assignees.AddRange(members.Select(member =>
            new DefaultChoice("assignee", member.UserId, null, member.DisplayName, defaults.AssigneeId == member.UserId)));
        Replace(DefaultAssigneeChoices, assignees);

        Replace(DefaultRepeatChoices, Repeats
            .Select(value => new DefaultChoice("repeating", value, $"repeat.{value}", null, defaults.Repeating == value))
            .ToList());

        Replace(DefaultWhenChoices, Whens
            .Select(when => new DefaultChoice("dueDate", when.Value, when.Key, null, defaults.DueDate == when.Value))
            .ToList());

        // All day, then the hours of a working day — and the stored time itself if it is not one
        // of them, so a 09:15 set on the web is shown rather than silently rounded.
        var times = new List<DefaultChoice>
        {
            new("dueTime", null, "defaults.all_day", null, defaults.DueTime is null),
        };
        var hours = Enumerable.Range(6, 17).Select(hour => $"{hour:00}:00").ToList();
        if (defaults.DueTime is { } stored && !hours.Contains(stored))
        {
            hours.Add(stored);
            hours.Sort(StringComparer.Ordinal);
        }
        times.AddRange(hours.Select(time =>
            new DefaultChoice("dueTime", time, null, TimeLabel(time), defaults.DueTime == time)));
        Replace(DefaultTimeChoices, times);

        Raise(nameof(Defaults));
        Raise(nameof(SelectedDefaultPriority));
        Raise(nameof(SelectedDefaultAssignee));
        Raise(nameof(SelectedDefaultRepeat));
        Raise(nameof(SelectedDefaultWhen));
        Raise(nameof(SelectedDefaultTime));
    }

    /// <summary><c>HH:MM</c> in the reader's own clock form.</summary>
    private static string TimeLabel(string time) =>
        TimeSpan.TryParse(time, System.Globalization.CultureInfo.InvariantCulture, out var span)
            ? DateTime.Today.Add(span).ToString("t", System.Globalization.CultureInfo.CurrentCulture)
            : time;

    /// <summary>
    /// A default was chosen. Writes only the field the choice belongs to — except that choosing no
    /// *When* also resets the repeat, as the web does: a task with no date cannot repeat.
    /// </summary>
    public async Task<bool> ChooseDefaultAsync(DefaultChoice choice, CancellationToken cancellationToken = default)
    {
        if (choice.IsSelected)
        {
            return false;
        }
        var changes = new Dictionary<string, object?>();
        switch (choice.Field)
        {
            case "priority":
                changes["defaultPriority"] = int.TryParse(choice.Value, out var level) ? level : 0;
                break;
            case "assignee":
                changes["defaultAssigneeId"] = choice.Value;
                break;
            case "repeating":
                changes["defaultRepeating"] = choice.Value ?? "never";
                break;
            case "dueDate":
                changes["defaultDueDate"] = choice.Value ?? "none";
                if (choice.Value is null or "none")
                {
                    changes["defaultRepeating"] = "never";
                }
                break;
            case "dueTime":
                changes["defaultDueTime"] = choice.Value;
                break;
            default:
                return false;
        }
        var response = await _core.CallAsync(Commands.UpdateList(ListId, changes), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        var updated = _defaults with
        {
            Priority = choice.Field == "priority" && int.TryParse(choice.Value, out var chosen) ? chosen : _defaults.Priority,
            AssigneeId = choice.Field == "assignee" ? choice.Value : _defaults.AssigneeId,
            Repeating = choice.Field == "repeating" ? choice.Value ?? "never"
                : choice.Field == "dueDate" && choice.Value is null or "none" ? "never"
                : _defaults.Repeating,
            DueDate = choice.Field == "dueDate" ? choice.Value ?? "none" : _defaults.DueDate,
            DueTime = choice.Field == "dueTime" ? choice.Value : _defaults.DueTime,
        };
        RefreshDefaultChoices(updated, Members.ToList());
        return true;
    }

    private void RefreshSwatches()
    {
        for (var index = 0; index < ColorChoices.Count; index++)
        {
            var swatch = ColorChoices[index];
            var selected = string.Equals(swatch.Hex, Color, StringComparison.OrdinalIgnoreCase);
            if (swatch.IsSelected != selected)
            {
                ColorChoices[index] = swatch with { IsSelected = selected };
            }
        }
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
            Replace(ColorChoices, settings.ColorChoices
                .Select(hex => new ColorSwatch(hex, string.Equals(hex, settings.Color, StringComparison.OrdinalIgnoreCase)))
                .ToList());
            Color = settings.Color;
            Privacy = settings.Privacy;
            IsFavorite = settings.IsFavorite;
            ProjectId = settings.ProjectId;
            Replace(Statuses, settings.Statuses);
            RefreshDefaultChoices(settings.Defaults, settings.Members);
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
        if (privacy == Privacy)
        {
            return false;
        }
        var response = await _core.CallAsync(
            Commands.UpdateList(ListId, new Dictionary<string, object?> { ["privacy"] = privacy }),
            cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        Privacy = privacy;
        return true;
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

/// <summary>
/// One choice for one of a list's defaults (task c4102c67): which field, what it writes, and
/// how it is named — by a resource key, or by a member's name.
/// </summary>
public sealed record DefaultChoice(string Field, string? Value, string? TitleKey, string? Text, bool IsSelected);

/// <summary>One colour the list could wear, and whether it does.</summary>
public sealed record ColorSwatch(string Hex, bool IsSelected)
{
    /// <summary>What a screen reader should call the swatch.</summary>
    public string ActionName => $"Colour {Hex}";
}
