using System.Collections.ObjectModel;
using System.Text.Json;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// One task, open.
/// </summary>
/// <remarks>
/// <para>
/// The screen arrives assembled: <c>taskDetail</c> answers with the task, its comments, its
/// subtasks, its list chips and <b>the order to lay the fields out in</b>. That last one is a
/// product decision shared with web and both Apple clients — "Who, Date, Priority, Lists" — and
/// both Apple platforms got it wrong the same way before it was written down once. This view model
/// reads the order it is given rather than having an opinion.
/// </para>
/// <para>
/// Edits are saved as they are made, not on a Save button. Every write goes to the Outbox, so
/// "saved" and "sent" are already different things and a button that pretended otherwise would be
/// lying about which one it did.
/// </para>
/// </remarks>
public sealed class TaskDetailViewModel : ObservableObject
{
    private readonly IAstridCore _core;
    private string? _taskId;
    private string _title = string.Empty;
    private string _description = string.Empty;
    private int _priority;
    private bool _completed;
    private bool _isOpen;
    private bool _isLoading;
    private string? _errorMessage;
    private DueLabel _due = new();
    private string _priorityGlyph = "○";
    private UserSummary? _assignee;

    public TaskDetailViewModel(IAstridCore core)
    {
        _core = core;
    }

    /// <summary>The quick date choices, with the instant each one means.</summary>
    public ObservableCollection<DuePick> DatePicks { get; } = [];

    /// <summary>The quick time choices.</summary>
    public ObservableCollection<DuePick> TimePicks { get; } = [];

    /// <summary>The order to lay the fields out in, as the core gave it.</summary>
    public ObservableCollection<string> FieldOrder { get; } = [];

    public ObservableCollection<ListChip> ListChips { get; } = [];

    /// <summary>Who this task can be assigned to. Loaded when the picker opens.</summary>
    public ObservableCollection<AssigneeOption> Assignees { get; } = [];

    public ObservableCollection<CommentSummary> Comments { get; } = [];

    public ObservableCollection<SubtaskSummary> Subtasks { get; } = [];

    /// <summary>Whether the detail pane is showing anything.</summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set => Set(ref _isOpen, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    public string? TaskId
    {
        get => _taskId;
        private set => Set(ref _taskId, value);
    }

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public string Description
    {
        get => _description;
        set => Set(ref _description, value);
    }

    public int Priority
    {
        get => _priority;
        private set => Set(ref _priority, value);
    }

    public bool Completed
    {
        get => _completed;
        private set => Set(ref _completed, value);
    }

    public DueLabel Due
    {
        get => _due;
        private set => Set(ref _due, value);
    }

    /// <summary>The mark that stands for this task's priority — the core's, not the shell's.</summary>
    public string PriorityGlyph
    {
        get => _priorityGlyph;
        private set => Set(ref _priorityGlyph, value);
    }

    public UserSummary? Assignee
    {
        get => _assignee;
        private set => Set(ref _assignee, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    /// <summary>Open a task.</summary>
    /// <remarks>
    /// The cached screen first, then a fetch of the comments. Sync pulls tasks and lists but not
    /// comment threads — there are too many and almost all of them are never looked at — so a
    /// thread is fetched when somebody opens it. Cache first means the pane is filled before the
    /// request goes out, rather than blank until it comes back.
    /// </remarks>
    public async Task OpenAsync(string taskId, CancellationToken cancellationToken = default)
    {
        TaskId = taskId;
        IsOpen = true;
        await ReloadAsync(cancellationToken);
        await RefreshCommentsAsync(cancellationToken);
    }

    /// <summary>
    /// Fetch the comment thread from the server.
    /// </summary>
    /// <remarks>
    /// A failure here is not shown. The cached thread is still on screen, which is the right thing
    /// to be looking at offline, and an error banner over a working screen is noise.
    /// </remarks>
    public async Task RefreshCommentsAsync(CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return;
        }
        var response = await _core.CallAsync(Commands.RefreshComments(TaskId), cancellationToken);
        if (response.Ok)
        {
            // The refreshed thread comes back with the answer, so the pane is updated from it
            // directly rather than by re-reading the whole screen.
            Replace(Comments, response.ReadArray<CommentSummary>());
        }
    }

    /// <summary>Close the pane.</summary>
    public void Close()
    {
        IsOpen = false;
        TaskId = null;
        Comments.Clear();
        Subtasks.Clear();
        ListChips.Clear();
        FieldOrder.Clear();
        DatePicks.Clear();
        TimePicks.Clear();
    }

    /// <summary>Re-read the open task. What a change notification for it does.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var response = await _core.CallAsync(Commands.TaskDetail(TaskId), cancellationToken);
            if (!response.Ok)
            {
                // A task that has gone — deleted here or elsewhere — closes the pane rather than
                // leaving a screen showing something that is not there any more.
                if (response.Error?.Kind == AstridFailureKind.NotFound)
                {
                    Close();
                    return;
                }
                ErrorMessage = response.IsStillPending ? null : response.Error?.Message;
                return;
            }

            ErrorMessage = null;
            Read(response.Value);
            // The quick choices depend on the task's own date, so they are re-read with it. Both
            // are cache reads; the alternative is a picker that offers "Today" as unselected on a
            // task somebody just set to today.
            await LoadDuePicksAsync(cancellationToken);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Save the title, if it changed.</summary>
    public Task<bool> SaveTitleAsync(string title, CancellationToken cancellationToken = default)
    {
        var trimmed = title.Trim();
        // An empty title would leave a row nobody can identify. Refusing is kinder than saving it
        // and making the user work out which blank row was theirs.
        return trimmed.Length == 0
            ? Task.FromResult(false)
            : UpdateAsync(new Dictionary<string, object?> { ["title"] = trimmed }, cancellationToken);
    }

    public Task<bool> SaveDescriptionAsync(string description, CancellationToken cancellationToken = default)
        => UpdateAsync(new Dictionary<string, object?> { ["description"] = description },
            cancellationToken);

    public Task<bool> SetPriorityAsync(int priority, CancellationToken cancellationToken = default)
        => UpdateAsync(new Dictionary<string, object?> { ["priority"] = priority }, cancellationToken);

    /// <summary>Set or clear the due date.</summary>
    /// <param name="dueDateTime">An ISO-8601 instant, or null to clear it.</param>
    public Task<bool> SetDueDateAsync(string? dueDateTime, bool isAllDay,
        CancellationToken cancellationToken = default)
        => UpdateAsync(new Dictionary<string, object?>
        {
            // Explicitly null to clear: an absent field would leave the date where it was.
            ["dueDateTime"] = dueDateTime,
            ["isAllDay"] = isAllDay,
        }, cancellationToken);

    /// <summary>
    /// Take a quick choice: a date, or a time of day.
    /// </summary>
    /// <remarks>
    /// A time makes the task timed; clearing the date leaves it all-day, which is the state a task
    /// with no date is in.
    /// </remarks>
    public Task<bool> TakeDuePickAsync(DuePick pick, CancellationToken cancellationToken = default)
        => SetDueDateAsync(pick.DueDateTime, isAllDay: pick.Hour is null && pick.DueDateTime is not null
            ? IsAllDay
            : pick.Hour is null, cancellationToken);

    /// <summary>Whether the open task is all-day. Drawn from the last set of choices read.</summary>
    public bool IsAllDay { get; private set; } = true;

    /// <summary>Fetch the quick choices for the open task.</summary>
    public async Task LoadDuePicksAsync(CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return;
        }
        var response = await _core.CallAsync(Commands.DueDateOptions(TaskId), cancellationToken);
        var options = response.Read<DueDateOptions>();
        if (options is null)
        {
            return;
        }
        IsAllDay = options.IsAllDay;
        Replace(DatePicks, options.Dates);
        Replace(TimePicks, options.Times);
    }

    /// <summary>Fetch who the open task can be assigned to.</summary>
    /// <remarks>
    /// Asked for when the picker opens rather than held with the task: the answer depends on the
    /// account's agents and on the members of every list the task is on, and none of that is worth
    /// carrying around for a screen nobody has opened.
    /// </remarks>
    public async Task LoadAssigneesAsync(CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return;
        }
        var response = await _core.CallAsync(Commands.AssigneeOptions(TaskId), cancellationToken);
        if (!Handle(response))
        {
            return;
        }
        var choices = response.Read<AssigneeChoices>();
        if (choices is not null)
        {
            Replace(Assignees, choices.Options);
        }
    }

    /// <summary>Give the task to someone, or to no one.</summary>
    /// <remarks>
    /// An ordinary update carrying an <c>assigneeId</c> — null clears it, which is why the value is
    /// written rather than omitted. The core's edits distinguish "leave alone" from "clear".
    /// </remarks>
    public Task<bool> AssignAsync(string? userId, CancellationToken cancellationToken = default) =>
        UpdateAsync(new Dictionary<string, object?> { ["assigneeId"] = userId }, cancellationToken);

    /// <summary>
    /// Complete or un-complete the open task.
    /// </summary>
    /// <remarks>
    /// The complete command, never an update carrying a flag: a repeating task rolls forward to its
    /// next occurrence instead of finishing, and only that path does it.
    /// </remarks>
    public async Task<bool> SetCompletedAsync(bool completed, CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return false;
        }
        var response = await _core.CallAsync(Commands.CompleteTask(TaskId, completed), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        await ReloadAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AddCommentAsync(string content, CancellationToken cancellationToken = default)
    {
        var trimmed = content.Trim();
        if (TaskId is null || trimmed.Length == 0)
        {
            return false;
        }
        var response = await _core.CallAsync(Commands.PostComment(TaskId, trimmed), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        await ReloadAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AddSubtaskAsync(string title, CancellationToken cancellationToken = default)
    {
        var trimmed = title.Trim();
        if (TaskId is null || trimmed.Length == 0)
        {
            return false;
        }
        var response = await _core.CallAsync(
            Commands.CreateTask(trimmed, parentTaskId: TaskId), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        await ReloadAsync(cancellationToken);
        return true;
    }

    private async Task<bool> UpdateAsync(IReadOnlyDictionary<string, object?> changes,
        CancellationToken cancellationToken)
    {
        if (TaskId is null)
        {
            return false;
        }
        var response = await _core.CallAsync(Commands.UpdateTask(TaskId, changes), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        await ReloadAsync(cancellationToken);
        return true;
    }

    private bool Handle(AstridResponse response)
    {
        if (response.Ok)
        {
            ErrorMessage = null;
            return true;
        }
        // Offline is not a failure to report: the change is journalled and will go.
        ErrorMessage = response.IsStillPending ? null : response.Error?.Message;
        return false;
    }

    private void Read(JsonElement value)
    {
        if (value.TryGetProperty("task", out var task))
        {
            Title = task.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty;
            Description = task.TryGetProperty("description", out var description)
                ? description.GetString() ?? string.Empty
                : string.Empty;
            Priority = task.TryGetProperty("priority", out var priority) ? priority.GetInt32() : 0;
            Completed = task.TryGetProperty("completed", out var completed) && completed.GetBoolean();
        }

        PriorityGlyph = value.TryGetProperty("priorityGlyph", out var glyph)
            ? glyph.GetString() ?? "○"
            : "○";
        Due = value.TryGetProperty("due", out var due)
            ? due.Deserialize<DueLabel>(CommandJson.Options) ?? new DueLabel()
            : new DueLabel();
        Assignee = value.TryGetProperty("assignee", out var assignee)
                   && assignee.ValueKind != JsonValueKind.Null
            ? assignee.Deserialize<UserSummary>(CommandJson.Options)
            : null;

        ReplaceStrings(FieldOrder, Read<string>(value, "fieldOrder"));
        Replace(ListChips, Read<ListChip>(value, "listChips"));
        Replace(Comments, Read<CommentSummary>(value, "comments"));
        Replace(Subtasks, Read<SubtaskSummary>(value, "subtasks"));
    }

    private static List<T> Read<T>(JsonElement value, string name) =>
        value.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Array
            ? element.Deserialize<List<T>>(CommandJson.Options) ?? []
            : [];

    /// <summary>
    /// Swap in a new set, keeping the collection the UI is bound to.
    /// </summary>
    /// <remarks>
    /// Clearing and re-adding would collapse every expander and lose the caret in a comment being
    /// typed, on every reload — and a reload happens after every edit.
    /// </remarks>
    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        for (var index = 0; index < source.Count; index++)
        {
            if (index < target.Count)
            {
                if (!EqualityComparer<T>.Default.Equals(target[index], source[index]))
                {
                    target[index] = source[index];
                }
            }
            else
            {
                target.Add(source[index]);
            }
        }
        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private static void ReplaceStrings(ObservableCollection<string> target, IReadOnlyList<string> source)
        => Replace(target, source);
}

/// <summary>A comment, as the detail screen shows it.</summary>
public sealed record CommentSummary
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("author")]
    public UserSummary? Author { get; init; }

    /// <summary>True while this comment exists only on this device.</summary>
    public bool IsPending => Id.StartsWith("temp_", StringComparison.Ordinal);

    public override string ToString() => Content;
}

/// <summary>A subtask, as the detail screen lists it.</summary>
public sealed record SubtaskSummary
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("completed")]
    public bool Completed { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("isPending")]
    public bool IsPending { get; init; }

    public override string ToString() => Title;
}
