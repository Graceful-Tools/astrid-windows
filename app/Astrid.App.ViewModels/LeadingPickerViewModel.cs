using System.Collections.ObjectModel;
using System.Text.Json;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// What a tap on a row's or a card's mark turned out to be.
/// </summary>
/// <remarks>
/// The core's three answers, relayed rather than decided. It is an answer rather than a void so a
/// test can see which one was taken without a window — which is the whole point of moving the
/// branch out of the two code-behinds (task 3c0dfb73).
/// </remarks>
public enum LeadingOutcome
{
    /// <summary>There is no such row any more. Nothing was written.</summary>
    Gone,

    /// <summary>The task was completed, or un-completed.</summary>
    Completed,

    /// <summary>A task in a public list the reader cannot edit was copied (task f6bc59e8).</summary>
    Copied,

    /// <summary>
    /// The core said to ask rather than to finish. Nothing has been written; the view opens the
    /// picker.
    /// </summary>
    OpenPicker,
}

/// <summary>
/// The picker a leading mark opens: priority, who, and — when the core says so — the board column.
/// </summary>
/// <remarks>
/// <para>
/// Shared by the list and the board, because it is one control: a board card and a list row both
/// reach it through <see cref="LeadingOutcome.OpenPicker"/>, and a picker that differed between
/// them would be two answers to one rule.
/// </para>
/// <para>
/// It decides nothing. <see cref="ShowsProjectState"/> is the row's
/// <see cref="TaskRow.PickerShowsProjectState"/> — <c>LeadingControl::picker_shows_project_state</c>
/// as the core answered it — and the options are <c>assigneeOptions</c> and
/// <c>taskStatusOptions</c>. The picks go back as the ordinary writes, so they travel through the
/// Outbox like every other edit.
/// </para>
/// </remarks>
public sealed class LeadingPickerViewModel : ObservableObject
{
    private readonly IAstridCore _core;
    private readonly Func<CancellationToken, Task> _refresh;
    private string? _taskId;
    private string _title = string.Empty;
    private int _priority;
    private bool _showsProjectState;
    private bool _completed;
    private bool _isRepeating;
    private bool _isLoading;

    /// <param name="core">The boundary. Every option and every pick is a command.</param>
    /// <param name="refresh">
    /// Re-read whatever the mark was on. A pick can move a task out of the list or into another
    /// column, and only the surface that opened the picker knows how to redraw itself.
    /// </param>
    public LeadingPickerViewModel(IAstridCore core, Func<CancellationToken, Task> refresh)
    {
        _core = core;
        _refresh = refresh;
    }

    /// <summary>The task the picker is open on, or null when it is closed.</summary>
    public string? TaskId
    {
        get => _taskId;
        private set => Set(ref _taskId, value);
    }

    /// <summary>What the picker is about, for its heading and its accessibility name.</summary>
    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    /// <summary>The priority as it stands, so the picker can mark the current one.</summary>
    public int Priority
    {
        get => _priority;
        private set => Set(ref _priority, value);
    }

    /// <summary>Whether the board column is one of the things this picker offers.</summary>
    public bool ShowsProjectState
    {
        get => _showsProjectState;
        private set => Set(ref _showsProjectState, value);
    }

    /// <summary>Whether the task is already done, which is what the completion row says.</summary>
    public bool Completed
    {
        get => _completed;
        private set
        {
            if (Set(ref _completed, value))
            {
                Raise(nameof(CompletionKey));
            }
        }
    }

    /// <summary>Whether the task repeats, so the completion row's checkbox carries the arrows.</summary>
    public bool IsRepeating
    {
        get => _isRepeating;
        private set => Set(ref _isRepeating, value);
    }

    /// <summary>
    /// The completion row's word: "Mark as done", or "Mark as to-do" on a finished task
    /// (task 8cf2d10d, ADEV-146). Web's registry keys, so every client says the same thing.
    /// </summary>
    public string CompletionKey => Completed ? "tasks.markAsTodo" : "tasks.markAsDone";

    /// <summary>True while the options are being fetched.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    /// <summary>Who this task can go to, unassigned first — <c>rows::assignee</c>'s order.</summary>
    public ObservableCollection<AssigneeOption> Assignees { get; } = [];

    /// <summary>
    /// The board's columns, when <see cref="ShowsProjectState"/>. Empty otherwise, and empty for a
    /// task on no board at all.
    /// </summary>
    public ObservableCollection<StatusChoice> StatusChoices { get; } = [];

    /// <summary>
    /// Open the picker on a row, and fetch what it offers.
    /// </summary>
    /// <remarks>
    /// Asked on opening rather than carried with the row, as the detail's assignee list is: the
    /// answer depends on the account's agents and on the members of every list the task is on, and
    /// none of that is worth holding for a popover nobody has opened.
    /// </remarks>
    public async Task OpenAsync(TaskRow row, CancellationToken cancellationToken = default)
    {
        TaskId = row.Id;
        Title = row.Title;
        Priority = row.Priority;
        Completed = row.Completed;
        IsRepeating = row.IsRepeating;
        ShowsProjectState = row.PickerShowsProjectState;
        Assignees.Clear();
        StatusChoices.Clear();
        IsLoading = true;
        try
        {
            var people = await _core.CallAsync(Commands.AssigneeOptions(row.Id), cancellationToken);
            if (people.Ok && people.Read<AssigneeChoices>() is { } choices)
            {
                foreach (var option in choices.Options)
                {
                    Assignees.Add(option);
                }
            }

            if (!ShowsProjectState)
            {
                return;
            }
            var columns = await _core
                .CallAsync(Commands.TaskStatusOptions(row.Id), cancellationToken);
            if (!columns.Ok)
            {
                // A task on no board answers with a refusal, and that is not an error to report:
                // the picker simply has no state row. Who and priority still work.
                return;
            }
            foreach (var column in Read<StatusChoice>(columns.Value, "columns"))
            {
                StatusChoices.Add(column);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Forget the open task. Called when the popover closes.</summary>
    public void Close()
    {
        TaskId = null;
        Assignees.Clear();
        StatusChoices.Clear();
    }

    /// <summary>Set the priority of the task the picker is open on.</summary>
    public Task<bool> PickPriorityAsync(int priority, CancellationToken cancellationToken = default)
    {
        Priority = priority;
        return WriteAsync(
            taskId => Commands.UpdateTask(taskId, new Dictionary<string, object?>
            {
                ["priority"] = priority,
            }),
            cancellationToken);
    }

    /// <summary>
    /// Give the task to somebody, or to nobody.
    /// </summary>
    /// <remarks>
    /// Nobody goes as <c>"unassigned"</c>, not as a null, for the reason quick-add sends it that
    /// way: a null reads as "unchanged" to the update, so choosing nobody would do nothing.
    /// </remarks>
    public Task<bool> PickAssigneeAsync(string? userId, CancellationToken cancellationToken = default)
        => WriteAsync(
            taskId => Commands.UpdateTask(taskId, new Dictionary<string, object?>
            {
                ["assigneeId"] = userId ?? "unassigned",
            }),
            cancellationToken);

    /// <summary>Move the task into a board column.</summary>
    public Task<bool> PickStatusAsync(string columnId, CancellationToken cancellationToken = default)
        => WriteAsync(taskId => Commands.SetTaskStatus(taskId, columnId), cancellationToken);

    /// <summary>
    /// Finish the task from inside the picker — the one place a click may still complete it.
    /// </summary>
    /// <remarks>
    /// Always <c>completeTask</c>, never an update carrying the flag: a repeating task rolls
    /// forward rather than finishing, and only that path does it.
    /// </remarks>
    public Task<bool> CompleteAsync(CancellationToken cancellationToken = default)
    {
        var completed = !Completed;
        return WriteAsync(taskId => Commands.CompleteTask(taskId, completed), cancellationToken);
    }

    private async Task<bool> WriteAsync(Func<string, object> command,
        CancellationToken cancellationToken)
    {
        if (TaskId is not { Length: > 0 } taskId)
        {
            return false;
        }
        var response = await _core.CallAsync(command(taskId), cancellationToken);
        // Offline is not a failure: the write is in the Outbox, and the surface behind the picker
        // is re-read either way so it shows what the core now holds.
        await _refresh(cancellationToken);
        return response.Ok || response.IsStillPending;
    }

    private static List<T> Read<T>(JsonElement value, string name) =>
        value.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Array
            ? element.Deserialize<List<T>>(CommandJson.Options) ?? []
            : [];
}
