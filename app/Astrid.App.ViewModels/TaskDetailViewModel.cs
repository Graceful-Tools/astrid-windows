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
    private string _commentDraft = string.Empty;
    private bool _repeatsFromDueDate;
    private bool _hasReminder;
    private TimerState _timer = new();

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

    /// <summary>
    /// What the timer is doing.
    /// </summary>
    /// <remarks>
    /// The section shows while a timer runs and a task with recorded time keeps its caption, so
    /// hiding the section never hides the data — the rule the Mac settled on.
    /// </remarks>
    public TimerState Timer
    {
        get => _timer;
        private set
        {
            if (Set(ref _timer, value))
            {
                Raise(nameof(IsTiming));
                Raise(nameof(HasLoggedTime));
            }
        }
    }

    public bool IsTiming => Timer.IsRunning;

    public bool HasLoggedTime => !Timer.IsRunning && Timer.LoggedMinutes > 0;

    /// <summary>Start or stop timing the open task.</summary>
    public async Task<bool> SetTimingAsync(bool running,
        CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return false;
        }
        var response = await _core.CallAsync(
            running ? Commands.StartTimer(TaskId) : Commands.StopTimer(TaskId), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        var state = response.Read<TimerState>();
        if (state is not null)
        {
            Timer = state;
        }
        return true;
    }

    /// <summary>The files on this task — its own, and its comments'.</summary>
    public ObservableCollection<AttachmentSummary> Attachments { get; } = [];

    /// <summary>When to be reminded, loaded when the picker opens.</summary>
    public ObservableCollection<ReminderPick> ReminderPicks { get; } = [];

    /// <summary>The repeat presets, loaded when the picker opens.</summary>
    public ObservableCollection<RepeatPreset> RepeatPresets { get; } = [];

    /// <summary>
    /// How the open task's repeat reads, in parts. The shell turns these into words.
    /// </summary>
    /// <remarks>
    /// A custom repeat cannot describe itself in a chip — "Custom" says nothing — so the detail
    /// gives it its own row, worded by the same function the picker uses.
    /// </remarks>
    public ObservableCollection<SummaryPart> RepeatSummary { get; } = [];

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

    private IReadOnlyList<MarkdownBlock> _descriptionBlocks = [];
    private bool _isEditingDescription;

    /// <summary>
    /// The description as the core rendered it, block by block (task 11cfaf6d).
    /// </summary>
    /// <remarks>
    /// Beside <see cref="Description"/>, not instead of it: the web draws the rendered form and
    /// edits the text, and so does this. Which markdown means what is the core's decision,
    /// mirrored from the web's own renderer, so the two clients cannot read one description two
    /// ways.
    /// </remarks>
    public IReadOnlyList<MarkdownBlock> DescriptionBlocks
    {
        get => _descriptionBlocks;
        private set
        {
            if (Set(ref _descriptionBlocks, value))
            {
                Raise(nameof(ShowsRenderedDescription));
                Raise(nameof(ShowsDescriptionEditor));
            }
        }
    }

    /// <summary>Whether the description is open for typing rather than drawn.</summary>
    public bool IsEditingDescription
    {
        get => _isEditingDescription;
        private set
        {
            if (Set(ref _isEditingDescription, value))
            {
                Raise(nameof(ShowsRenderedDescription));
                Raise(nameof(ShowsDescriptionEditor));
            }
        }
    }

    /// <summary>
    /// The rendered description is on screen: there is one, and nobody is editing it.
    /// </summary>
    public bool ShowsRenderedDescription => !IsEditingDescription && DescriptionBlocks.Count > 0;

    /// <summary>
    /// The plain editor is on screen: the description is being edited, or there is none yet to
    /// draw — an empty one shows the box with its placeholder, as the web shows its prompt.
    /// </summary>
    public bool ShowsDescriptionEditor => !ShowsRenderedDescription;

    /// <summary>The rendered description was clicked: open it for typing.</summary>
    public void BeginEditingDescription() => IsEditingDescription = true;

    public int Priority
    {
        get => _priority;
        private set
        {
            Set(ref _priority, value);
            Raise(nameof(CheckboxAsset));
        }
    }

    public bool Completed
    {
        get => _completed;
        private set
        {
            Set(ref _completed, value);
            Raise(nameof(CheckboxAsset));
        }
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
        if (TaskId != taskId)
        {
            // A different task: whatever was being typed into the last one's description is not
            // being typed into this one's.
            IsEditingDescription = false;
        }
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
        await EnsureInlineFilesAsync(cancellationToken);
    }

    /// <summary>True while the pass below is running, so its own refresh does not restart it.</summary>
    private bool _fetchingInlineFiles;

    /// <summary>
    /// Fetch the pictures a comment would draw but has no bytes for.
    /// </summary>
    /// <remarks>
    /// The core says where bytes already are without touching the network, which covers anything
    /// this device attached. A picture posted from another client is on the server and nowhere
    /// else, so without this it draws as a chip until somebody clicks it — a thumbnail that
    /// appears for your own half of the thread and never for theirs.
    /// <para>
    /// Bounded twice over: only files the core said would be drawn, and only the ones whose bytes
    /// are missing. The refresh at the end rebuilds the rows now the paths resolve, and the guard
    /// is what stops that refresh starting the pass again.
    /// </para>
    /// </remarks>
    private async Task EnsureInlineFilesAsync(CancellationToken cancellationToken)
    {
        if (_fetchingInlineFiles || TaskId is null)
        {
            return;
        }
        var missing = Comments
            .SelectMany(comment => comment.Files)
            .Where(file => file.RendersInline && string.IsNullOrEmpty(file.LocalPath))
            .Select(file => file.Id)
            .Distinct()
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        _fetchingInlineFiles = true;
        try
        {
            foreach (var fileId in missing)
            {
                // Failures are ignored on purpose: offline, or a file somebody deleted, leaves the
                // chip on screen, which is what it looked like a moment ago anyway.
                await _core.CallAsync(
                    Commands.DownloadAttachment(TaskId, fileId), cancellationToken);
            }
            await RefreshCommentsAsync(cancellationToken);
        }
        finally
        {
            _fetchingInlineFiles = false;
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

    /// <summary>Save the description, and go back to drawing it.</summary>
    /// <remarks>
    /// Editing ends whether or not the save reached the server: offline, the write is in the
    /// Outbox and the reload draws what was typed, which is what "saved" means here.
    /// </remarks>
    public async Task<bool> SaveDescriptionAsync(string description, CancellationToken cancellationToken = default)
    {
        var saved = await UpdateAsync(new Dictionary<string, object?> { ["description"] = description },
            cancellationToken);
        IsEditingDescription = false;
        return saved;
    }

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
    /// <summary>
    /// Set the due date to a day chosen from a calendar.
    /// </summary>
    /// <remarks>
    /// Two steps because the first is a question only the core can answer: what that day means for
    /// this task. An all-day task takes the day; a timed one keeps its time of day, because
    /// picking "the 14th" on something due at 17:00 means the 14th at 17:00. The shell computes
    /// neither.
    /// </remarks>
    public async Task<bool> SetDueDayAsync(DateTimeOffset day,
        CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return false;
        }
        var response = await _core.CallAsync(
            Commands.DueDateOnDay(TaskId, day.ToString("yyyy-MM-dd")), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        var picked = response.Read<DuePick>();
        return picked is not null
            && await SetDueDateAsync(picked.DueDateTime, IsAllDay, cancellationToken);
    }

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

    /// <summary>Whether the open task counts from its due date rather than from completion.</summary>
    public bool RepeatsFromDueDate
    {
        get => _repeatsFromDueDate;
        private set => Set(ref _repeatsFromDueDate, value);
    }

    /// <summary>Whether the open task repeats at all. What draws the glyph on the row.</summary>
    public bool IsRepeating => RepeatSummary.Count > 0;

    /// <summary>
    /// The mark this task wears, as an image path.
    /// </summary>
    /// <remarks>
    /// The same one the row shows, so opening a task does not change how its priority reads. See
    /// <see cref="Astrid.Core.Bindings.TaskRow.CheckboxAsset"/> for why it is one image rather than
    /// three overlaid controls.
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

    /// <summary>Fetch the files on the open task.</summary>
    public async Task LoadAttachmentsAsync(CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return;
        }
        var response = await _core.CallAsync(Commands.Attachments(TaskId), cancellationToken);
        if (!Handle(response))
        {
            return;
        }
        var files = response.Read<Astrid.Core.Bindings.Attachments>();
        if (files is not null)
        {
            Replace(Attachments, files.Files);
        }
    }

    /// <summary>
    /// Fetch a file and answer with where it landed.
    /// </summary>
    /// <remarks>
    /// The path rather than the bytes: what somebody wants to do with an attachment is open it in
    /// the program that reads that kind of file, and carrying a photo across the boundary as JSON
    /// to hand it back again would be work nobody asked for.
    /// </remarks>
    public async Task<string?> DownloadAsync(string fileId,
        CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return null;
        }
        var response = await _core.CallAsync(
            Commands.DownloadAttachment(TaskId, fileId), cancellationToken);
        if (!Handle(response))
        {
            return null;
        }
        await LoadAttachmentsAsync(cancellationToken);
        return response.Read<DownloadedFile>()?.Path;
    }

    /// <summary>
    /// Ask what a paste means.
    /// </summary>
    /// <remarks>
    /// Reading the clipboard is the window's job — it is a platform thing. Which of the things on
    /// it was meant, and what to call a screenshot that has no name, are rules with tests in
    /// <c>astrid_core::paste</c>, so they are asked for rather than repeated here.
    /// </remarks>
    public async Task<PasteDecision> DecidePasteAsync(IReadOnlyList<string> files, bool hasImage,
        bool hasText, CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.ClipboardPaste(files, hasImage ? "png" : null, hasText), cancellationToken);
        if (!response.Ok || !response.Value.TryGetProperty("action", out var action))
        {
            return new PasteDecision("text", [], null);
        }
        var chosen = response.Value.TryGetProperty("files", out var listed)
            ? listed.EnumerateArray()
                .Select(item => item.GetString() ?? string.Empty)
                .Where(path => path.Length > 0)
                .ToList()
            : [];
        var name = response.Value.TryGetProperty("name", out var named) ? named.GetString() : null;
        return new PasteDecision(action.GetString() ?? "text", chosen, name);
    }

    /// <summary>
    /// Attach what a paste decided to attach.
    /// </summary>
    /// <remarks>
    /// The decision is the core's — see <c>astrid_core::paste</c> — so this asks first and then
    /// does what it is told. Every file goes through the same path the Attach button uses: there
    /// is one pipeline, and paste is a source feeding it.
    /// </remarks>
    public async Task<int> AttachPastedAsync(IReadOnlyList<string> paths,
        CancellationToken cancellationToken = default)
    {
        var attached = 0;
        foreach (var path in paths)
        {
            if (await AttachAsync(path, cancellationToken: cancellationToken))
            {
                attached++;
            }
        }
        return attached;
    }

    /// <summary>Attach a file from this machine.</summary>
    public async Task<bool> AttachAsync(string path, string? content = null,
        CancellationToken cancellationToken = default)
    {
        if (TaskId is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }
        var response = await _core.CallAsync(
            Commands.AttachFile(TaskId, path, content), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        await ReloadAsync(cancellationToken);
        await LoadAttachmentsAsync(cancellationToken);
        return true;
    }

    /// <summary>Whether the open task has a reminder set.</summary>
    public bool HasReminder
    {
        get => _hasReminder;
        private set => Set(ref _hasReminder, value);
    }

    /// <summary>Fetch when the open task could be reminded.</summary>
    public async Task LoadReminderPicksAsync(CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return;
        }
        var response = await _core.CallAsync(Commands.ReminderOptions(TaskId), cancellationToken);
        if (!Handle(response))
        {
            return;
        }
        var options = response.Read<ReminderOptions>();
        if (options is null)
        {
            return;
        }
        HasReminder = options.ReminderTime is not null;
        Replace(ReminderPicks, options.Picks);
    }

    /// <summary>Set or clear the reminder.</summary>
    /// <remarks>
    /// The instant comes from the core with the choice, so the shell never subtracts an hour from
    /// a date itself — which is the arithmetic that goes wrong across a daylight-saving boundary.
    /// </remarks>
    public Task<bool> SetReminderAsync(string? reminderTime,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(new Dictionary<string, object?> { ["reminderTime"] = reminderTime },
            cancellationToken);

    /// <summary>Fetch the repeat presets for the open task.</summary>
    public async Task LoadRepeatAsync(CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return;
        }
        var response = await _core.CallAsync(Commands.RepeatOptions(TaskId), cancellationToken);
        if (!Handle(response))
        {
            return;
        }
        var choices = response.Read<RepeatChoices>();
        if (choices is null)
        {
            return;
        }
        RepeatsFromDueDate = choices.RepeatFrom == "DUE_DATE";
        Replace(RepeatPresets, choices.Presets);
        Replace(RepeatSummary, choices.Summary);
        Raise(nameof(IsRepeating));
        Raise(nameof(CheckboxAsset));
    }

    /// <summary>Choose one of the presets, or stop repeating.</summary>
    /// <remarks>
    /// Choosing "custom" from the preset list is not a write: there is nothing to store until a
    /// pattern has been built, and writing <c>custom</c> with no pattern is what leaves a task
    /// repeating on a rule nobody can read.
    /// </remarks>
    public Task<bool> SetRepeatAsync(string value, CancellationToken cancellationToken = default)
    {
        if (value == "custom")
        {
            return Task.FromResult(false);
        }
        return UpdateAsync(new Dictionary<string, object?>
        {
            ["repeating"] = value,
            // Clearing the pattern with the preset: a daily task carrying a leftover custom rule
            // is a task that repeats one way and reads another.
            ["repeatingData"] = null,
        }, cancellationToken);
    }

    /// <summary>Count the next occurrence from the due date, or from when it was finished.</summary>
    public Task<bool> SetRepeatFromAsync(bool fromDueDate,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(new Dictionary<string, object?>
        {
            ["repeatFrom"] = fromDueDate ? "DUE_DATE" : "COMPLETION_DATE",
        }, cancellationToken);

    /// <summary>Store a custom pattern.</summary>
    /// <remarks>
    /// The fields the pattern does not need are left out rather than sent as nulls — the server's
    /// column is free-form JSON, and a weekly pattern carrying a stale <c>monthDay</c> is a rule
    /// that means something different to whichever client reads it next.
    /// </remarks>
    public Task<bool> SetCustomRepeatAsync(string unit, int interval,
        IReadOnlyList<string>? weekdays = null, string? endCondition = null,
        int? endAfterOccurrences = null, string? endUntilDate = null,
        CancellationToken cancellationToken = default)
    {
        var pattern = new Dictionary<string, object?>
        {
            ["type"] = "custom",
            ["unit"] = unit,
            ["interval"] = Math.Max(1, interval),
        };
        if (unit == "weeks" && weekdays is { Count: > 0 })
        {
            pattern["weekdays"] = weekdays;
        }
        if (endCondition is not null)
        {
            pattern["endCondition"] = endCondition;
            if (endCondition == "after_occurrences" && endAfterOccurrences is > 0)
            {
                pattern["endAfterOccurrences"] = endAfterOccurrences;
            }
            if (endCondition == "until_date" && endUntilDate is not null)
            {
                pattern["endUntilDate"] = endUntilDate;
            }
        }
        return UpdateAsync(new Dictionary<string, object?>
        {
            ["repeating"] = "custom",
            ["repeatingData"] = pattern,
        }, cancellationToken);
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

    /// <summary>
    /// What is typed into the comment box but not yet posted.
    /// </summary>
    /// <remarks>
    /// Held here rather than read off the control at send time so that the Send button and the
    /// Return key agree about whether there is anything to send — an offered Send that does
    /// nothing when clicked is worse than no Send at all.
    /// </remarks>
    public string CommentDraft
    {
        get => _commentDraft;
        set
        {
            Set(ref _commentDraft, value);
            Raise(nameof(CanSendComment));
        }
    }

    /// <summary>Whether there is anything to post. The predicate the send path guards on.</summary>
    public bool CanSendComment => CommentDraft.Trim().Length > 0;

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
            DescriptionBlocks = Read<MarkdownBlock>(value, "descriptionBlocks");
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
        // The repeat comes with the screen rather than with the picker: the row has to say how the
        // task repeats before anybody opens anything.
        Replace(RepeatSummary, Read<SummaryPart>(value, "repeatSummary"));
        Raise(nameof(IsRepeating));
        Raise(nameof(CheckboxAsset));
        Timer = value.TryGetProperty("timer", out var timer)
            ? timer.Deserialize<TimerState>(CommandJson.Options) ?? new TimerState()
            : new TimerState();
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

    [System.Text.Json.Serialization.JsonPropertyName("authorName")]
    public string? AuthorName { get; init; }

    /// <summary>True while this comment exists only on this device.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("isPending")]
    public bool IsPending { get; init; }

    /// <summary>
    /// Whether to draw a text bubble at all.
    /// </summary>
    /// <remarks>
    /// A file posted without a caption has no text, and an empty bubble beside a picture reads as
    /// a failed post. The core decides it — see <c>astrid_core::rows::comment</c>.
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("showsText")]
    public bool ShowsText { get; init; }

    /// <summary>
    /// The files this comment carries.
    /// </summary>
    /// <remarks>
    /// Attachments reach a task through comments, so a file somebody attached hangs off the
    /// comment rather than off the task. Drawing only the text is what made attaching look broken.
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("files")]
    public IReadOnlyList<CommentFile> Files { get; init; } = [];

    /// <summary>
    /// Which side of the thread this bubble sits on.
    /// </summary>
    /// <remarks>
    /// The core decides it against the signed-in user — see <c>astrid_core::rows::comment</c>. A
    /// thread drawn all on one side says the other person never replied.
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("isMine")]
    public bool IsMine { get; init; }

    /// <summary>Nobody wrote it; the server did. A centred note rather than either voice.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("isSystem")]
    public bool IsSystem { get; init; }

    /// <summary>A bubble is anything that is not a system note.</summary>
    public bool IsBubble => !IsSystem;

    public bool HasFiles => Files.Count > 0;

    public override string ToString() => Content;
}

/// <summary>What a paste should do, as the core decided it.</summary>
/// <param name="Action"><c>files</c>, <c>image</c>, or <c>text</c> for one to leave alone.</param>
/// <param name="Files">The files to attach, already capped and in order.</param>
/// <param name="Name">What to call the clipboard's picture, when there is one.</param>
public sealed record PasteDecision(string Action, IReadOnlyList<string> Files, string? Name);

/// <summary>One file on a comment.</summary>
public sealed record CommentFile
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("size")]
    public long Size { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("mimeType")]
    public string MimeType { get; init; } = string.Empty;

    /// <summary>Whether it is drawn where it sits, or offered as something to open.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("rendersInline")]
    public bool RendersInline { get; init; }

    /// <summary>
    /// Where the bytes are on this machine, when they are already here.
    /// </summary>
    /// <remarks>
    /// Null is not an error — it means "not fetched yet". The core reports it without touching the
    /// network, so a picture this device attached draws from the copy the Outbox already wrote
    /// rather than being fetched back from the server it has not reached yet.
    /// </remarks>
    [System.Text.Json.Serialization.JsonPropertyName("localPath")]
    public string? LocalPath { get; init; }

    /// <summary>Whether there is a picture to draw right now, as opposed to a chip.</summary>
    public bool ShowsThumbnail => RendersInline && !string.IsNullOrEmpty(LocalPath);

    /// <summary>The chip is what is drawn when there is no picture in hand.</summary>
    public bool ShowsChip => !ShowsThumbnail;

    /// <summary>The size as a person reads it.</summary>
    public string SizeLabel => Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024} KB",
        _ => $"{Size / (1024 * 1024)} MB",
    };

    public override string ToString() => Name;
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
