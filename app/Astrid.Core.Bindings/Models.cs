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

/// <summary>What the Outbox is holding, for the "not synced yet" indicator.</summary>
public sealed record OutboxStats
{
    [JsonPropertyName("pending")] public int Pending { get; init; }

    [JsonPropertyName("running")] public int Running { get; init; }

    [JsonPropertyName("failed")] public int Failed { get; init; }

    [JsonPropertyName("hasUnsentWork")] public bool HasUnsentWork { get; init; }
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
