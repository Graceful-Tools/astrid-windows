using System.Text.Json;
using System.Text.Json.Serialization;

namespace Astrid.Core.Bindings;

/// <summary>
/// The shapes a screen reads out of a response.
/// </summary>
/// <remarks>
/// Only what the shell draws. The core's models are wider — a task has thirty fields — and
/// mirroring them here would be a second definition to keep in step for no gain: what a row shows
/// is decided in <c>astrid_core::rows</c>, and this is that decision arriving.
/// </remarks>
public sealed record TaskRow
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;

    [JsonPropertyName("completed")] public bool Completed { get; init; }

    [JsonPropertyName("priority")] public int Priority { get; init; }

    [JsonPropertyName("due")] public DueLabel Due { get; init; } = new();

    [JsonPropertyName("isOverdue")] public bool IsOverdue { get; init; }

    [JsonPropertyName("leading")] public LeadingControl Leading { get; init; } = new();

    /// <summary>What clicking the leading control does: <c>complete</c> or <c>openPicker</c>.</summary>
    [JsonPropertyName("action")] public string Action { get; init; } = "complete";

    /// <summary>How far to indent, from the subtask chain.</summary>
    [JsonPropertyName("depth")] public int Depth { get; init; }

    /// <summary>True while this task exists only on this device.</summary>
    [JsonPropertyName("isPending")] public bool IsPending { get; init; }

    [JsonPropertyName("isPrivate")] public bool IsPrivate { get; init; }

    [JsonPropertyName("isRepeating")] public bool IsRepeating { get; init; }

    [JsonPropertyName("hasDescription")] public bool HasDescription { get; init; }

    [JsonPropertyName("commentCount")] public int CommentCount { get; init; }

    [JsonPropertyName("attachmentCount")] public int AttachmentCount { get; init; }

    [JsonPropertyName("subtaskCount")] public int SubtaskCount { get; init; }

    [JsonPropertyName("listChips")] public IReadOnlyList<ListChip> ListChips { get; init; } = [];

    [JsonPropertyName("assignee")] public UserSummary? Assignee { get; init; }

    [JsonPropertyName("statusRole")] public string? StatusRole { get; init; }

    /// <summary>What a screen reader should call the checkbox on this row.</summary>
    public string CompleteActionName => $"Complete {Title}";

    /// <summary>
    /// The mark this row wears, as an image path.
    /// </summary>
    /// <remarks>
    /// The same PNGs astrid-web draws (its <c>TaskCheckbox</c>), carried into this repo so a
    /// priority reads identically on both. One image says three things at once — the colour is the
    /// priority, the ring is whether it repeats, the tick is whether it is done — which is why this
    /// is a file name rather than three overlaid controls.
    ///
    /// The priority is clamped rather than trusted: it arrives from the server, and a value this
    /// build has not seen would otherwise ask for an image that does not exist and draw nothing at
    /// all, losing the control a person taps to finish the task.
    /// </remarks>
    public string CheckboxAsset
    {
        get
        {
            var priority = Priority is >= 0 and <= 3 ? Priority : 0;
            var repeat = IsRepeating ? "_repeat" : string.Empty;
            var done = Completed ? "_checked" : string.Empty;
            return $"ms-appx:///Assets/Checkboxes/check_box{repeat}{done}_{priority}.png";
        }
    }

    /// <summary>
    /// Whether there is a second line to draw at all.
    /// </summary>
    /// <remarks>
    /// The web only draws it when there is a due date, a list or a label. An empty second line
    /// still takes its gap, which is what turns a tidy list into a loose one.
    /// </remarks>
    public bool HasSecondLine => Due.HasDate || ListChips.Count > 0 || IsPending;

    /// <summary>What a screen reader should call the delete button on this row.</summary>
    public string DeleteActionName => $"Delete {Title}";

    /// <summary>
    /// The title.
    /// </summary>
    /// <remarks>
    /// Overridden because a <c>ListView</c> names its container from the item's <c>ToString()</c>,
    /// and a record's generated one prints every field — so Narrator reads out an id, a colour and
    /// eight nulls before reaching the task. Setting <c>AutomationProperties.Name</c> inside the
    /// template does not help: the name that is read belongs to the container, not to its content.
    /// </remarks>
    public override string ToString() => Title;
}

/// <summary>
/// What a due date says, as a key the shell resolves against its own resources.
/// </summary>
/// <remarks>
/// The core does not return English — see <c>astrid_core::rows</c>. <c>Key</c> is one of
/// <c>none</c>, <c>yesterday</c>, <c>today</c>, <c>tomorrow</c> or <c>on</c>; the last carries a
/// date and, for a timed task, a time.
/// </remarks>
public sealed record DueLabel
{
    [JsonPropertyName("key")] public string Key { get; init; } = "none";

    [JsonPropertyName("date")] public string? Date { get; init; }

    [JsonPropertyName("time")] public string? Time { get; init; }

    public bool HasDate => Key != "none";
}

/// <summary>What the control at the leading edge of a row shows.</summary>
public sealed record LeadingControl
{
    /// <summary><c>checkbox</c>, <c>avatar</c> or <c>unassigned</c>.</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = "unassigned";

    [JsonPropertyName("userId")] public string? UserId { get; init; }
}

/// <summary>A list, drawn as a chip on a row.</summary>
public sealed record ListChip
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    [JsonPropertyName("color")] public string Color { get; init; } = "#3b82f6";
}

/// <summary>One list as the detail's list editor draws it (task d3f3b111).</summary>
public sealed record ListPick
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    [JsonPropertyName("color")] public string Color { get; init; } = "#3b82f6";

    /// <summary>What a screen reader should call the × on this chip.</summary>
    public string RemoveActionName => $"Remove from {Name}";
}

/// <summary>What the list editor shows for one task and one search.</summary>
public sealed record ListPicks
{
    /// <summary>The lists the task is in.</summary>
    [JsonPropertyName("selected")] public IReadOnlyList<ListPick> Selected { get; init; } = [];

    /// <summary>The lists it could be added to that match the search.</summary>
    [JsonPropertyName("options")] public IReadOnlyList<ListPick> Options { get; init; } = [];

    /// <summary>The name to offer creating, when what was typed is not a list yet.</summary>
    [JsonPropertyName("createName")] public string? CreateName { get; init; }
}

/// <summary>Enough of a person to draw them.</summary>
public sealed record UserSummary
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("email")] public string? Email { get; init; }

    [JsonPropertyName("image")] public string? Image { get; init; }

    /// <summary>
    /// What to show where a name goes. Never empty: somebody we hold only an id for still has to
    /// render as something.
    /// </summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name) ? Name!
        : !string.IsNullOrWhiteSpace(Email) ? Email!
        : Id;
}

/// <summary>A window of rows, with the total behind it.</summary>
public sealed record RowWindow
{
    /// <summary>
    /// How many rows there are to scroll through, after filtering. What a virtualised list sizes
    /// its scrollbar from — not the number of tasks in the account.
    /// </summary>
    [JsonPropertyName("total")] public int Total { get; init; }

    [JsonPropertyName("offset")] public int Offset { get; init; }

    [JsonPropertyName("rows")] public IReadOnlyList<TaskRow> Rows { get; init; } = [];
}

/// <summary>A list, as the sidebar needs it.</summary>
public sealed record ListSummary
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    [JsonPropertyName("color")] public string? Color { get; init; }

    [JsonPropertyName("isFavorite")] public bool? IsFavorite { get; init; }

    [JsonPropertyName("favoriteOrder")] public int? FavoriteOrder { get; init; }

    [JsonPropertyName("listType")] public string? ListType { get; init; }

    [JsonPropertyName("projectId")] public string? ProjectId { get; init; }

    [JsonPropertyName("taskCount")] public int? TaskCount { get; init; }

    /// <summary>
    /// True for a board column, which is a state rather than a place. Never offered as somewhere
    /// to file a task.
    /// </summary>
    public bool IsStatusList => ListType == "status";

    public string DisplayColor => string.IsNullOrWhiteSpace(Color) ? "#3b82f6" : Color!;

    /// <summary>The name. See <see cref="TaskRow.ToString"/> for why this is overridden.</summary>
    public override string ToString() => Name;
}

/// <summary>One quick due-date or time choice.</summary>
public sealed record DuePick
{
    /// <summary>A resource key. The core never returns words — see <c>astrid_core::rows</c>.</summary>
    [JsonPropertyName("titleKey")] public string TitleKey { get; init; } = string.Empty;

    /// <summary>The instant this choice means, or null for "no due date".</summary>
    [JsonPropertyName("dueDateTime")] public string? DueDateTime { get; init; }

    [JsonPropertyName("hour")] public int? Hour { get; init; }

    /// <summary>Whether the task is already set to this.</summary>
    [JsonPropertyName("isSelected")] public bool IsSelected { get; init; }
}

/// <summary>The quick choices for one task.</summary>
public sealed record DueDateOptions
{
    [JsonPropertyName("isAllDay")] public bool IsAllDay { get; init; }

    [JsonPropertyName("dueDateTime")] public string? DueDateTime { get; init; }

    [JsonPropertyName("dates")] public IReadOnlyList<DuePick> Dates { get; init; } = [];

    [JsonPropertyName("times")] public IReadOnlyList<DuePick> Times { get; init; } = [];
}

/// <summary>How this account wants to be reminded. The server's fields, unchanged.</summary>
public sealed record ReminderSettings
{
    [JsonPropertyName("enablePushReminders")] public bool EnablePushReminders { get; init; }

    [JsonPropertyName("enableEmailReminders")] public bool EnableEmailReminders { get; init; }

    /// <summary>Minutes before a task is due. Zero means at the time it is due.</summary>
    [JsonPropertyName("defaultReminderTime")] public int DefaultReminderTime { get; init; }

    [JsonPropertyName("enableDailyDigest")] public bool EnableDailyDigest { get; init; }

    /// <summary><c>HH:MM</c>.</summary>
    [JsonPropertyName("dailyDigestTime")] public string? DailyDigestTime { get; init; }

    [JsonPropertyName("dailyDigestTimezone")] public string? DailyDigestTimezone { get; init; }

    /// <summary>Null when there are no quiet hours at all.</summary>
    [JsonPropertyName("quietHoursStart")] public string? QuietHoursStart { get; init; }

    [JsonPropertyName("quietHoursEnd")] public string? QuietHoursEnd { get; init; }
}

/// <summary>One AI agent, and how it is set to run.</summary>
public sealed record AgentSummary
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")] public string? Description { get; init; }

    /// <summary>
    /// Filled in from the modes map, which the server sends separately.
    /// </summary>
    /// <remarks>
    /// <c>api</c> means Astrid runs it; <c>polling</c> and <c>webhook</c> mean the account's own
    /// agent does and needs a credential; <c>off</c> means it does not run.
    /// </remarks>
    public string Mode { get; init; } = "off";

    /// <summary>Whether this mode needs a credential of the account's own.</summary>
    public bool NeedsOwnCredential => Mode is "polling" or "webhook";

    public override string ToString() => Name;
}

/// <summary>A service whose credential the account can hold. Never the credential itself.</summary>
public sealed record AgentCredential
{
    [JsonPropertyName("serviceId")] public string ServiceId { get; init; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    /// <summary>Whether a key is stored. The server does not answer with the key.</summary>
    [JsonPropertyName("configured")] public bool Configured { get; init; }

    public override string ToString() => Name.Length > 0 ? Name : ServiceId;
}

/// <summary>The Agent Hub.</summary>
public sealed record AgentHub
{
    [JsonPropertyName("agents")] public IReadOnlyList<AgentSummary> Agents { get; init; } = [];

    /// <summary>Agent id to mode, which the server sends beside the agents rather than on them.</summary>
    [JsonPropertyName("modes")]
    public IReadOnlyDictionary<string, string> Modes { get; init; } =
        new Dictionary<string, string>();

    [JsonPropertyName("credentials")]
    public IReadOnlyList<AgentCredential> Credentials { get; init; } = [];

    /// <summary>Copilot's status, which a deployment can be without entirely.</summary>
    [JsonPropertyName("copilot")] public CopilotStatus Copilot { get; init; } = new();
}

/// <summary>
/// One client-credentials pair this account has registered.
/// </summary>
/// <remarks>
/// No secret: the server returns it once at creation and stores a hash. A field that was sometimes
/// a secret and sometimes null is a field somebody will try to read.
/// </remarks>
public sealed record OAuthClientRow
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The public half.
    /// </summary>
    /// <remarks>
    /// Safe to show, the half somebody has to copy again later, and what a revoke addresses — the
    /// route matches <c>clientId</c> and nothing else.
    /// </remarks>
    [JsonPropertyName("clientId")] public string ClientId { get; init; } = string.Empty;

    /// <summary>What it may do. "A pair for CI" and "a pair that can delete every list" look the
    /// same without it.</summary>
    [JsonPropertyName("scopes")] public IReadOnlyList<string> Scopes { get; init; } = [];

    [JsonPropertyName("createdAt")] public string? CreatedAt { get; init; }

    /// <summary>A revoked pair stays in the list saying so, rather than vanishing.</summary>
    [JsonPropertyName("isActive")] public bool IsActive { get; init; } = true;

    /// <summary>The scopes as one line, for the row under the name.</summary>
    public string ScopeLabel => Scopes.Count == 0 ? string.Empty : string.Join(", ", Scopes);

    public override string ToString() => Name.Length > 0 ? Name : ClientId;
}

/// <summary>The API-access panel: what is registered, before anything new is made.</summary>
public sealed record ApiAccessPanel
{
    [JsonPropertyName("clients")] public IReadOnlyList<OAuthClientRow> Clients { get; init; } = [];
}

/// <summary>
/// A credential the server has just made, in the one form it will ever be readable.
/// </summary>
/// <remarks>
/// Never written to disk. The account cannot rotate a secret it does not know is there.
/// </remarks>
public sealed record MintedClient
{
    [JsonPropertyName("clientId")] public string ClientId { get; init; } = string.Empty;

    [JsonPropertyName("clientSecret")] public string ClientSecret { get; init; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
}

/// <summary>An MCP token, plaintext, as minted.</summary>
public sealed record MintedToken
{
    [JsonPropertyName("token")] public string Token { get; init; } = string.Empty;
}

/// <summary>Where an account's own agent is told about work.</summary>
public sealed record WebhookSettings
{
    [JsonPropertyName("configured")] public bool Configured { get; init; }

    [JsonPropertyName("enabled")] public bool Enabled { get; init; }

    [JsonPropertyName("webhookUrl")] public string WebhookUrl { get; init; } = string.Empty;

    /// <summary>Whether a signing secret exists. Never the secret.</summary>
    /// <remarks>
    /// It signs every delivery, so a server that echoed it would let any reader forge events into
    /// somebody's agent. Reporting that it exists is all a screen needs.
    /// </remarks>
    [JsonPropertyName("hasSecret")] public bool HasSecret { get; init; }

    [JsonPropertyName("events")] public IReadOnlyList<string> Events { get; init; } = [];

    [JsonPropertyName("agents")] public IReadOnlyList<string> Agents { get; init; } = [];

    /// <summary>What a picker is built from, whether or not anything is configured.</summary>
    [JsonPropertyName("availableEvents")]
    public IReadOnlyList<string> AvailableEvents { get; init; } = [];

    [JsonPropertyName("availableAgents")]
    public IReadOnlyList<string> AvailableAgents { get; init; } = [];

    [JsonPropertyName("failureCount")] public int FailureCount { get; init; }
}

/// <summary>One agent this account registered of its own.</summary>
public sealed record CustomAgent
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    [JsonPropertyName("email")] public string Email { get; init; } = string.Empty;

    public override string ToString() => Name.Length > 0 ? Name : Email;
}

/// <summary>Whether the account's Copilot integration is connected.</summary>
public sealed record CopilotStatus
{
    [JsonPropertyName("connected")] public bool Connected { get; init; }
}

/// <summary>A container on the other side: a Google task list, or a repository.</summary>
public sealed record ExternalContainer
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    /// <summary>What a picker shows. See <see cref="TaskRow.ToString"/> for why.</summary>
    public override string ToString() => Name;
}

/// <summary>One list mirrored to one container.</summary>
public sealed record ExternalLink
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("astridListId")] public string AstridListId { get; init; } = string.Empty;

    [JsonPropertyName("remoteContainerId")]
    public string RemoteContainerId { get; init; } = string.Empty;
}

/// <summary>One provider's part of a list's external-sync panel.</summary>
public sealed record ExternalProvider
{
    /// <summary><c>google_tasks</c> or <c>git_hub</c>, as the core spells them.</summary>
    [JsonPropertyName("provider")] public string Provider { get; init; } = string.Empty;

    [JsonPropertyName("connected")] public bool Connected { get; init; }

    /// <summary>Empty until the provider is connected: asking before then can only 401.</summary>
    [JsonPropertyName("containers")]
    public IReadOnlyList<ExternalContainer> Containers { get; init; } = [];

    /// <summary>Null when this list is not mirrored anywhere on this provider.</summary>
    [JsonPropertyName("link")] public ExternalLink? Link { get; init; }

    /// <summary>What to call it on screen.</summary>
    public string Name => Provider == "git_hub" ? "GitHub" : "Google Tasks";

    public bool IsLinked => Link is not null;
}

/// <summary>A list's external sync.</summary>
public sealed record ExternalSync
{
    [JsonPropertyName("listId")] public string ListId { get; init; } = string.Empty;

    [JsonPropertyName("providers")]
    public IReadOnlyList<ExternalProvider> Providers { get; init; } = [];
}

/// <summary>One row of the command palette.</summary>
public sealed record PaletteRow
{
    /// <summary><c>command</c>, <c>list</c> or <c>task</c> — what pressing it does.</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;

    /// <summary>A list id, a task id, or the action name a command dispatches under.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;

    /// <summary>The key that would do it, for a command.</summary>
    [JsonPropertyName("keys")] public string? Keys { get; init; }

    /// <summary>Which list a task is in, so two of the same name are told apart.</summary>
    [JsonPropertyName("subtitle")] public string? Subtitle { get; init; }

    /// <summary>What the row shows on its right: the key, or the list.</summary>
    public string Trailing => Keys ?? Subtitle ?? string.Empty;

    /// <summary>
    /// What a screen reader reads.
    /// </summary>
    /// <remarks>
    /// A record's generated ToString prints every field — "PaletteRow { Kind = command, Id = … }" —
    /// and that is exactly what a list item announces when nothing else names it.
    /// </remarks>
    public override string ToString() =>
        Trailing.Length > 0 ? $"{Title}, {Trailing}" : Title;
}

/// <summary>What the palette found.</summary>
public sealed record Palette
{
    [JsonPropertyName("rows")] public IReadOnlyList<PaletteRow> Rows { get; init; } = [];
}

/// <summary>The three numbers on a profile.</summary>
public sealed record ProfileStats
{
    [JsonPropertyName("completed")] public int Completed { get; init; }

    [JsonPropertyName("inspired")] public int Inspired { get; init; }

    [JsonPropertyName("supported")] public int Supported { get; init; }
}

/// <summary>One choice for the default reminder offset.</summary>
public sealed record ReminderOffset
{
    [JsonPropertyName("titleKey")] public string TitleKey { get; init; } = string.Empty;

    [JsonPropertyName("minutes")] public int Minutes { get; init; }
}

/// <summary>The account screen.</summary>
public sealed record AccountSettings
{
    [JsonPropertyName("user")] public UserSummary? User { get; init; }

    [JsonPropertyName("reminderSettings")]
    public ReminderSettings ReminderSettings { get; init; } = new();

    /// <summary>
    /// The offsets a new task's reminder can default to — the same list the per-task picker uses,
    /// so "15 minutes before" means one thing in this app rather than two.
    /// </summary>
    [JsonPropertyName("offsets")] public IReadOnlyList<ReminderOffset> Offsets { get; init; } = [];

    [JsonPropertyName("timezone")] public string? Timezone { get; init; }
}

/// <summary>What a task's timer is doing.</summary>
public sealed record TimerState
{
    [JsonPropertyName("isRunning")] public bool IsRunning { get; init; }

    /// <summary>
    /// When the running session started.
    /// </summary>
    /// <remarks>
    /// The start rather than the elapsed time, so a screen counting up does the counting and the
    /// core is not asked for a number that is stale the moment it is answered.
    /// </remarks>
    [JsonPropertyName("startedAt")] public string? StartedAt { get; init; }

    /// <summary>Minutes recorded on the task before this session.</summary>
    [JsonPropertyName("loggedMinutes")] public long LoggedMinutes { get; init; }

    /// <summary>What the last session recorded — the caption a task keeps.</summary>
    [JsonPropertyName("lastValue")] public string? LastValue { get; init; }
}

/// <summary>A file on a task.</summary>
public sealed record AttachmentSummary
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    [JsonPropertyName("size")] public long Size { get; init; }

    [JsonPropertyName("mimeType")] public string MimeType { get; init; } = string.Empty;

    /// <summary>Whether the bytes are already on this machine.</summary>
    [JsonPropertyName("isCached")] public bool IsCached { get; init; }

    /// <summary>Where the bytes are, or would be.</summary>
    [JsonPropertyName("path")] public string Path { get; init; } = string.Empty;

    /// <summary>The size, in the units somebody reads.</summary>
    public string SizeLabel => Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024} KB",
        _ => $"{Size / (1024 * 1024)} MB",
    };
}

/// <summary>The files on a task.</summary>
public sealed record Attachments
{
    [JsonPropertyName("files")]
    public IReadOnlyList<AttachmentSummary> Files { get; init; } = [];
}

/// <summary>Where a downloaded file landed.</summary>
public sealed record DownloadedFile
{
    [JsonPropertyName("path")] public string Path { get; init; } = string.Empty;
}

/// <summary>One choice in a filter group.</summary>
public sealed record FilterPick
{
    /// <summary>
    /// The field this choice writes.
    /// </summary>
    /// <remarks>
    /// On the pick as well as on the group, because a radio button is drawn one at a time and one
    /// that does not know its group clears the wrong one.
    /// </remarks>
    [JsonPropertyName("field")] public string Field { get; init; } = string.Empty;

    /// <summary>The value to write back, exactly as the core's rules match it.</summary>
    [JsonPropertyName("value")] public string Value { get; init; } = string.Empty;

    [JsonPropertyName("titleKey")] public string TitleKey { get; init; } = string.Empty;

    [JsonPropertyName("isSelected")] public bool IsSelected { get; init; }
}

/// <summary>One filter: the field it writes, and the choices for it.</summary>
public sealed record FilterGroup
{
    /// <summary>The field on the list, as the API spells it.</summary>
    [JsonPropertyName("field")] public string Field { get; init; } = string.Empty;

    [JsonPropertyName("titleKey")] public string TitleKey { get; init; } = string.Empty;

    [JsonPropertyName("picks")] public IReadOnlyList<FilterPick> Picks { get; init; } = [];
}

/// <summary>What a list is filtered and sorted by.</summary>
public sealed record FilterOptions
{
    [JsonPropertyName("listId")] public string ListId { get; init; } = string.Empty;

    /// <summary>Whether anything is narrowing what the list shows.</summary>
    [JsonPropertyName("isFiltered")] public bool IsFiltered { get; init; }

    [JsonPropertyName("groups")] public IReadOnlyList<FilterGroup> Groups { get; init; } = [];
}

/// <summary>One message in a list's conversation.</summary>
public sealed record MessageRow
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("content")] public string Content { get; init; } = string.Empty;

    /// <summary>Null for a message the server wrote rather than a person.</summary>
    [JsonPropertyName("authorName")] public string? AuthorName { get; init; }

    [JsonPropertyName("initials")] public string Initials { get; init; } = string.Empty;

    [JsonPropertyName("image")] public string? Image { get; init; }

    /// <summary>Whether this account wrote it. Which side the bubble sits on.</summary>
    [JsonPropertyName("isMine")] public bool IsMine { get; init; }

    /// <summary>Still in the Outbox. Not an error.</summary>
    [JsonPropertyName("isPending")] public bool IsPending { get; init; }

    /// <summary>Written by the server — "Dana joined the list" — rather than by somebody.</summary>
    [JsonPropertyName("isSystem")] public bool IsSystem { get; init; }

    [JsonPropertyName("createdAt")] public string? CreatedAt { get; init; }

}

/// <summary>A list's conversation.</summary>
public sealed record ChatPanel
{
    /// <summary>Null when this deployment has no channel for the list.</summary>
    [JsonPropertyName("channelId")] public string? ChannelId { get; init; }

    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("messages")] public IReadOnlyList<MessageRow> Messages { get; init; } = [];
}

/// <summary>Somebody a list is shared with.</summary>
public sealed record ListMember
{
    [JsonPropertyName("userId")] public string UserId { get; init; } = string.Empty;

    /// <summary><c>owner</c>, <c>admin</c>, <c>member</c>, <c>viewer</c> — never compared here.</summary>
    [JsonPropertyName("role")] public string Role { get; init; } = string.Empty;

    [JsonPropertyName("user")] public UserSummary? User { get; init; }

    /// <summary>
    /// What to show for this person. Never a bare id: a raw UUID where a name goes reads as a bug,
    /// and it was one on the Mac.
    /// </summary>
    public string DisplayName => User?.DisplayName ?? UserId;

    /// <summary>The resource key for this role. The words live in the shell.</summary>
    public string RoleKey => Role.Length == 0 ? string.Empty : $"role.{Role.ToLowerInvariant()}";
}

/// <summary>One list's settings, and who it is shared with.</summary>
public sealed record ListSettings
{
    [JsonPropertyName("listId")] public string ListId { get; init; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    [JsonPropertyName("ownerId")] public string? OwnerId { get; init; }

    /// <summary>The colour every screen draws the list in (task 53780e75).</summary>
    [JsonPropertyName("color")] public string Color { get; init; } = "#3b82f6";

    /// <summary>The web's palette, offered as swatches.</summary>
    [JsonPropertyName("colorChoices")] public IReadOnlyList<string> ColorChoices { get; init; } = [];

    /// <summary><c>PRIVATE</c>, <c>SHARED</c> or <c>PUBLIC</c>; null when the server never said.</summary>
    [JsonPropertyName("privacy")] public string? Privacy { get; init; }

    [JsonPropertyName("isFavorite")] public bool IsFavorite { get; init; }

    [JsonPropertyName("canManageMembers")] public bool CanManageMembers { get; init; }

    [JsonPropertyName("canManageList")] public bool CanManageList { get; init; }

    [JsonPropertyName("canDeleteList")] public bool CanDeleteList { get; init; }

    [JsonPropertyName("canLeave")] public bool CanLeave { get; init; }

    [JsonPropertyName("currentUserId")] public string? CurrentUserId { get; init; }

    [JsonPropertyName("members")] public IReadOnlyList<ListMember> Members { get; init; } = [];
}

/// <summary>One column of a project board, and the cards in it.</summary>
public sealed record BoardColumn
{
    /// <summary>A status role, or one of the two virtual ids for Inbox and Done.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;

    /// <summary><c>inbox</c>, <c>status</c> or <c>done</c>.</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;

    /// <summary>How many cards the column holds, not how many crossed the boundary.</summary>
    [JsonPropertyName("total")] public int Total { get; init; }

    [JsonPropertyName("cards")] public IReadOnlyList<TaskRow> Cards { get; init; } = [];

}

/// <summary>A project board.</summary>
public sealed record Board
{
    /// <summary>Null when the list belongs to no board.</summary>
    [JsonPropertyName("projectId")] public string? ProjectId { get; init; }

    [JsonPropertyName("columns")] public IReadOnlyList<BoardColumn> Columns { get; init; } = [];
}

/// <summary>One choice in the reminder picker, and the instant it means.</summary>
public sealed record ReminderPick
{
    [JsonPropertyName("titleKey")] public string TitleKey { get; init; } = string.Empty;

    /// <summary>The instant to store, or null for "no reminder".</summary>
    [JsonPropertyName("reminderTime")] public string? ReminderTime { get; init; }

    [JsonPropertyName("isSelected")] public bool IsSelected { get; init; }
}

/// <summary>The reminder picker's choices, and the one the task holds.</summary>
public sealed record ReminderOptions
{
    [JsonPropertyName("reminderTime")] public string? ReminderTime { get; init; }

    [JsonPropertyName("picks")] public IReadOnlyList<ReminderPick> Picks { get; init; } = [];
}

/// <summary>A task asking to be remembered.</summary>
public sealed record Reminder
{
    [JsonPropertyName("taskId")] public string TaskId { get; init; } = string.Empty;

    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;

    /// <summary>
    /// When the reminder was for — not "now", so a banner that waited can say what it waited for.
    /// </summary>
    [JsonPropertyName("reminderTime")] public string ReminderTime { get; init; } = string.Empty;

    [JsonPropertyName("dueDateTime")] public string? DueDateTime { get; init; }
}

/// <summary>What is outstanding.</summary>
public sealed record RemindersDue
{
    [JsonPropertyName("reminders")] public IReadOnlyList<Reminder> Reminders { get; init; } = [];
}

/// <summary>One fragment of a repeat's description: a key, and what it interpolates.</summary>
public sealed record SummaryPart
{
    /// <summary>A resource key. The core never returns words.</summary>
    [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;

    /// <summary>The number the key's plural form and its placeholder refer to.</summary>
    [JsonPropertyName("count")] public long? Count { get; init; }

    /// <summary>Weekdays as the wire spells them, for the shell to translate itself.</summary>
    [JsonPropertyName("values")] public IReadOnlyList<string> Values { get; init; } = [];

    [JsonPropertyName("date")] public string? Date { get; init; }
}

/// <summary>One choice in the repeat picker.</summary>
public sealed record RepeatPreset
{
    /// <summary>What to write back, as the wire spells it: <c>never</c>, <c>daily</c>, …</summary>
    [JsonPropertyName("value")] public string Value { get; init; } = string.Empty;

    [JsonPropertyName("titleKey")] public string TitleKey { get; init; } = string.Empty;

    [JsonPropertyName("isSelected")] public bool IsSelected { get; init; }
}

/// <summary>The repeat picker's rows, and how the current repeat reads.</summary>
public sealed record RepeatChoices
{
    [JsonPropertyName("repeating")] public string? Repeating { get; init; }

    [JsonPropertyName("repeatFrom")] public string? RepeatFrom { get; init; }

    [JsonPropertyName("presets")] public IReadOnlyList<RepeatPreset> Presets { get; init; } = [];

    [JsonPropertyName("summary")] public IReadOnlyList<SummaryPart> Summary { get; init; } = [];
}

/// <summary>One row of the assignee picker. A null <see cref="UserId"/> is "no one".</summary>
public sealed record AssigneeOption
{
    /// <summary>Null for the unassigned row.</summary>
    [JsonPropertyName("userId")] public string? UserId { get; init; }

    /// <summary>Null for the unassigned row, which the shell names from its resources.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }

    [JsonPropertyName("initials")] public string Initials { get; init; } = string.Empty;

    [JsonPropertyName("image")] public string? Image { get; init; }

    [JsonPropertyName("isCurrentUser")] public bool IsCurrentUser { get; init; }

    [JsonPropertyName("isAgent")] public bool IsAgent { get; init; }

    /// <summary>
    /// What to put on the row: the person's name, or the key the unassigned word lives under.
    /// </summary>
    /// <remarks>
    /// <c>assignee.unassigned</c> is the key both Apple clients use. The Mac passed its own English
    /// "No one" to the localiser as a key, no such key existed, and all twelve translations fell
    /// back to English — user-facing strings are a cross-platform contract, not a per-platform
    /// choice.
    /// </remarks>
    [JsonIgnore] public string TitleKey => Name ?? "assignee.unassigned";

    /// <summary>Its own glyph: "unassigned" is a state, not an empty person.</summary>
    [JsonIgnore] public string Glyph => UserId is null ? "\u2014" : Initials;
}

/// <summary>The picker's rows, and which of them the task currently holds.</summary>
public sealed record AssigneeChoices
{
    [JsonPropertyName("assigneeId")] public string? AssigneeId { get; init; }

    [JsonPropertyName("options")] public IReadOnlyList<AssigneeOption> Options { get; init; } = [];
}

/// <summary>What the Outbox is holding, for the "not synced yet" indicator.</summary>
public sealed record OutboxStats
{
    [JsonPropertyName("pending")] public int Pending { get; init; }

    [JsonPropertyName("running")] public int Running { get; init; }

    [JsonPropertyName("failed")] public int Failed { get; init; }

    [JsonPropertyName("hasUnsentWork")] public bool HasUnsentWork { get; init; }
}

/// <summary>
/// One block of a rendered description, comment or message, as <c>astrid_core::markdown</c>
/// renders it (task 11cfaf6d).
/// </summary>
/// <remarks>
/// One flat record for every kind rather than a class per kind: the shell switches on
/// <see cref="Kind"/> and reads the fields that kind carries — <c>paragraph</c> and
/// <c>heading</c> their <see cref="Inlines"/>, <c>code</c> its <see cref="Text"/>, <c>list</c>
/// its <see cref="Items"/>, <c>quote</c> its <see cref="Blocks"/>, <c>table</c> its
/// <see cref="Header"/> and <see cref="Rows"/>. A <c>rule</c> carries nothing.
/// </remarks>
public sealed record MarkdownBlock
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("inlines")] public IReadOnlyList<MarkdownInline> Inlines { get; init; } = [];

    /// <summary>A heading's level, 1 through 6.</summary>
    [JsonPropertyName("level")] public int Level { get; init; }

    /// <summary>A code block's language, when the fence named one.</summary>
    [JsonPropertyName("language")] public string? Language { get; init; }

    /// <summary>A code block's text.</summary>
    [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;

    [JsonPropertyName("ordered")] public bool Ordered { get; init; }

    /// <summary>The first number of an ordered list.</summary>
    [JsonPropertyName("start")] public long Start { get; init; } = 1;

    [JsonPropertyName("items")] public IReadOnlyList<MarkdownListItem> Items { get; init; } = [];

    /// <summary>A quote's own blocks.</summary>
    [JsonPropertyName("blocks")] public IReadOnlyList<MarkdownBlock> Blocks { get; init; } = [];

    /// <summary>A table's column alignments: <c>left</c>, <c>center</c>, <c>right</c> or <c>none</c>.</summary>
    [JsonPropertyName("alignments")] public IReadOnlyList<string> Alignments { get; init; } = [];

    [JsonPropertyName("header")] public IReadOnlyList<IReadOnlyList<MarkdownInline>> Header { get; init; } = [];

    [JsonPropertyName("rows")] public IReadOnlyList<IReadOnlyList<IReadOnlyList<MarkdownInline>>> Rows { get; init; } = [];
}

/// <summary>One item of a rendered list.</summary>
public sealed record MarkdownListItem
{
    /// <summary>Set for a task-list item; null for an ordinary one.</summary>
    [JsonPropertyName("checked")] public bool? Checked { get; init; }

    [JsonPropertyName("blocks")] public IReadOnlyList<MarkdownBlock> Blocks { get; init; } = [];
}

/// <summary>One run of a rendered block: text in a style, a reference pill, or a line break.</summary>
public sealed record MarkdownInline
{
    /// <summary><c>text</c>, <c>reference</c> or <c>lineBreak</c>.</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;

    [JsonPropertyName("bold")] public bool Bold { get; init; }

    [JsonPropertyName("italic")] public bool Italic { get; init; }

    [JsonPropertyName("strike")] public bool Strike { get; init; }

    /// <summary>An inline code span.</summary>
    [JsonPropertyName("code")] public bool Code { get; init; }

    /// <summary>Where the run goes when clicked, when the core kept an address for it.</summary>
    [JsonPropertyName("link")] public string? Link { get; init; }

    /// <summary>A pill's kind: <c>user</c>, <c>list</c> or <c>task</c>.</summary>
    [JsonPropertyName("reference")] public string? Reference { get; init; }

    /// <summary>A pill's label — the name as it was typed.</summary>
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;

    /// <summary>A pill's id.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
}

/// <summary>Reading a response into one of the shapes above.</summary>
public static class ResponseReader
{
    public static IReadOnlyList<T> ReadArray<T>(this AstridResponse response)
    {
        if (!response.Ok || response.Value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return response.Value.Deserialize<List<T>>(CommandJson.Options) ?? [];
    }

    public static T? Read<T>(this AstridResponse response) where T : class
        => response.Ok ? response.ValueAs<T>() : null;
}
