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
    private bool _isFullScreen;
    private bool _isLoading;
    private string? _errorMessage;
    private DueLabel _due = new();
    private string _priorityGlyph = "○";
    private UserSummary? _assignee;
    private string _commentDraft = string.Empty;
    private bool _repeatsFromDueDate;
    private bool _hasReminder;
    private TimerState _timer = new();
    private bool _isCanceled;
    private bool _isCopyOnly;
    private string? _link;

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
        // The button reads as pressed before the answer (task cdb30d3d); the answer then carries
        // the real state, including what a stopped run was worth.
        Timer = Timer with { IsRunning = running };
        var response = await _core.CallAsync(
            running ? Commands.StartTimer(TaskId) : Commands.StopTimer(TaskId), cancellationToken);
        if (!Handle(response))
        {
            await RevertAsync(cancellationToken);
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

    /// <summary>
    /// Whether the open task has been expanded to fill the window (task 1927c2e7).
    /// </summary>
    /// <remarks>
    /// PRODUCT_CONTRACT §3: an escape hatch for a long description, not a layout — so off by
    /// default, and off again for the next task. Where it may be taken from is the shell's call
    /// (<c>ShellViewModel.CanEnterFullScreen</c>); this only remembers that it was.
    /// </remarks>
    public bool IsFullScreen
    {
        get => _isFullScreen;
        private set => Set(ref _isFullScreen, value);
    }

    public void ToggleFullScreen() => IsFullScreen = !IsFullScreen;

    public void LeaveFullScreen() => IsFullScreen = false;

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

    // ── One editing session at a time (PRODUCT_CONTRACT.md §6, task e71ed760) ──────────────
    //
    // The machine is the core's — astrid_core::editing, locked by contracts/fixtures/editing.json
    // — and is stepped by command; this only does what each answer names. Title and description
    // hold a pending buffer, so they have a commit and a revert. Lists and assignee change on
    // selection in this app — the write goes when the row is picked — so they register no commit
    // handler, but they are on the session: opening either commits an open title or description.

    public const string TitleEditor = "title";
    public const string DescriptionEditor = "description";
    public const string ListsEditor = "lists";
    public const string AssigneeEditor = "assignee";

    private string? _activeEditor;
    private string _savedTitle = string.Empty;
    private string _savedDescription = string.Empty;

    /// <summary>The one open editor, as the core last said; null when none is.</summary>
    public string? ActiveEditor
    {
        get => _activeEditor;
        private set => Set(ref _activeEditor, value);
    }

    /// <summary>
    /// Open an editor. Whatever was open is committed first — by the machine, not by the caller,
    /// which is what makes "one at a time" a property rather than a convention.
    /// </summary>
    public async Task BeginEditingAsync(string editor, CancellationToken cancellationToken = default)
    {
        await StepAsync(Commands.BeginEditing(editor), cancellationToken);
        if (editor == DescriptionEditor)
        {
            // The rendered description gives way to the box.
            IsEditingDescription = true;
        }
    }

    /// <summary>Close an editor, committing it. A stale end — already handed off — commits nothing.</summary>
    public Task EndEditingAsync(string editor, CancellationToken cancellationToken = default) =>
        StepAsync(Commands.EndEditing(editor), cancellationToken);

    /// <summary>Close an editor, reverting it: the only transition that discards.</summary>
    public Task CancelEditingAsync(string editor, CancellationToken cancellationToken = default) =>
        StepAsync(Commands.CancelEditing(editor), cancellationToken);

    /// <summary>Commit whatever is open: navigating away and backgrounding save.</summary>
    public Task CommitAllAsync(CancellationToken cancellationToken = default) =>
        StepAsync(Commands.CommitAllEditing(), cancellationToken);

    private async Task StepAsync(object command, CancellationToken cancellationToken)
    {
        var response = await _core.CallAsync(command, cancellationToken);
        if (!response.Ok || response.Read<EditingTransition>() is not { } transition)
        {
            // A core that cannot be asked leaves the editors as they are: nothing is saved or
            // thrown away on a guess.
            return;
        }
        ActiveEditor = transition.Active;
        if (transition.Commit is { } commit)
        {
            await CommitEditorAsync(commit, cancellationToken);
        }
        if (transition.Cancel is { } cancel)
        {
            RevertEditor(cancel);
        }
    }

    /// <summary>Save what an editor holds. The bound text is the buffer, so this reads it.</summary>
    private Task CommitEditorAsync(string editor, CancellationToken cancellationToken) => editor switch
    {
        TitleEditor => SaveTitleAsync(Title, cancellationToken),
        DescriptionEditor => SaveDescriptionAsync(Description, cancellationToken),
        // Lists and assignee were saved when the row was picked; closing is the whole story.
        _ => Task.CompletedTask,
    };

    /// <summary>Put back what the task had when it was opened, or last reloaded.</summary>
    private void RevertEditor(string editor)
    {
        switch (editor)
        {
            case TitleEditor:
                Title = _savedTitle;
                break;
            case DescriptionEditor:
                Description = _savedDescription;
                IsEditingDescription = false;
                break;
        }
    }

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

    /// <summary>
    /// Closed as anything but done (task 016ce981). The core decides what that means; the chip
    /// beside the title and the menu's closing entry both read it.
    /// </summary>
    public bool IsCanceled
    {
        get => _isCanceled;
        private set
        {
            if (Set(ref _isCanceled, value))
            {
                Raise(nameof(WontDoLabelKey));
            }
        }
    }

    /// <summary>
    /// The menu's closing entry: "Won't do" on a task that is open or finished, "Reopen" on one
    /// closed as won't-do — the web's single entry, flipped by the task's state.
    /// </summary>
    public string WontDoLabelKey => IsCanceled ? "detail.reopen" : "detail.wont_do";

    /// <summary>
    /// The address "Copy link" copies — the one the web's own task links carry. Null until the task
    /// has reached the server, because until then it has no id the server knows.
    /// </summary>
    public string? Link
    {
        get => _link;
        private set
        {
            if (Set(ref _link, value))
            {
                Raise(nameof(CanCopyLink));
            }
        }
    }

    public bool CanCopyLink => !string.IsNullOrEmpty(Link);

    /// <summary>
    /// A task in a public list the reader cannot edit (task f6bc59e8): the header offers a copy
    /// where the checkbox would be, and the title and description are not for editing.
    /// </summary>
    public bool IsCopyOnly
    {
        get => _isCopyOnly;
        private set => Set(ref _isCopyOnly, value);
    }

    /// <summary>Copy the open task to the reader's own tasks. See <c>TaskListViewModel.CopyAsync</c>.</summary>
    public async Task<bool> CopyToMineAsync(CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return false;
        }
        var response = await _core.CallAsync(
            Commands.CopyTask(TaskId, targetListId: null, includeComments: false), cancellationToken);
        return Handle(response);
    }

    /// <summary>The columns the Status submenu offers, filled when the menu opens.</summary>
    public ObservableCollection<StatusChoice> StatusChoices { get; } = [];

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
            // A different task: navigating away saves, so whatever was being typed into the last
            // one is committed before this one is read — and it was being typed into that one's
            // description, not this one's.
            if (ActiveEditor is not null)
            {
                await CommitAllAsync(cancellationToken);
            }
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

    /// <summary>Close the pane, committing whatever was being edited: navigating away saves.</summary>
    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (ActiveEditor is not null)
        {
            await CommitAllAsync(cancellationToken);
        }
        Close();
    }

    /// <summary>
    /// Close the pane without saving — the task is gone, or is being deleted.
    /// </summary>
    /// <remarks>
    /// The session is emptied all the same, without a commit: an editor left open in the core
    /// would be handed to the next task's first editor as the one to save.
    /// </remarks>
    public void Close()
    {
        if (ActiveEditor is { } abandoned)
        {
            ActiveEditor = null;
            _ = _core.CallAsync(Commands.CancelEditing(abandoned));
        }
        IsOpen = false;
        IsFullScreen = false;
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

    /// <summary>
    /// Set the priority. The square lights before the core has answered (task cdb30d3d).
    /// </summary>
    /// <remarks>
    /// Optimistic on purpose: the write is local-first through the Outbox, so the answer is the
    /// cache agreeing a moment later. Showing the old value until then is what made the buttons
    /// feel dead. A refused write reloads, which puts the old value back.
    /// </remarks>
    public async Task<bool> SetPriorityAsync(int priority, CancellationToken cancellationToken = default)
    {
        Priority = priority;
        if (await UpdateAsync(new Dictionary<string, object?> { ["priority"] = priority }, cancellationToken))
        {
            return true;
        }
        await RevertAsync(cancellationToken);
        return false;
    }

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

    // ── The lists a task is in (task d3f3b111) ──────────────────────────────────────────────
    //
    // The web's Lists row is an editor: chips with an × each, a search over the account's lists,
    // and "Create …" when the typed name is nobody's yet. What is offered, and what a list made
    // from here looks like, are the core's rules (`rows::list_picks`); this asks and relays.

    /// <summary>The lists the task is in, as the editor draws them.</summary>
    public ObservableCollection<ListPick> SelectedLists { get; } = [];

    /// <summary>The lists the task could be added to that match <see cref="ListSearch"/>.</summary>
    public ObservableCollection<ListPick> ListOptions { get; } = [];

    private string _listSearch = string.Empty;
    private string? _createListName;

    /// <summary>What has been typed into the editor's search box.</summary>
    public string ListSearch
    {
        get => _listSearch;
        private set => Set(ref _listSearch, value);
    }

    /// <summary>The name the editor offers to create, when the search matches no list.</summary>
    public string? CreateListName
    {
        get => _createListName;
        private set
        {
            if (Set(ref _createListName, value))
            {
                Raise(nameof(CanCreateList));
            }
        }
    }

    public bool CanCreateList => !string.IsNullOrEmpty(CreateListName);

    /// <summary>Fill the editor for what has been typed. Called as the flyout opens and as the box changes.</summary>
    public async Task LoadListPicksAsync(string query, CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return;
        }
        ListSearch = query;
        var response = await _core.CallAsync(Commands.ListPicks(TaskId, query), cancellationToken);
        if (!Handle(response))
        {
            return;
        }
        var picks = response.Read<ListPicks>();
        if (picks is null)
        {
            return;
        }
        Replace(SelectedLists, picks.Selected);
        Replace(ListOptions, picks.Options);
        CreateListName = picks.CreateName;
    }

    /// <summary>Put the task in a list it is not in.</summary>
    public Task<bool> AddToListAsync(string listId, CancellationToken cancellationToken = default) =>
        ChangeListsAsync(Commands.AddTaskToList(TaskId ?? string.Empty, listId), cancellationToken);

    /// <summary>Take the task out of one of its lists.</summary>
    public Task<bool> RemoveFromListAsync(string listId, CancellationToken cancellationToken = default) =>
        ChangeListsAsync(Commands.RemoveTaskFromList(TaskId ?? string.Empty, listId), cancellationToken);

    /// <summary>Create the list the editor offered, and put the task in it.</summary>
    public Task<bool> CreateListAsync(CancellationToken cancellationToken = default) =>
        CreateListName is { } name
            ? ChangeListsAsync(Commands.CreateListForTask(TaskId ?? string.Empty, name), cancellationToken)
            : Task.FromResult(false);

    /// <summary>
    /// One list edit, then the pane and the editor are refreshed — the editor stays open with
    /// fresh chips and options, and its search is cleared as the web clears it after a choice.
    /// </summary>
    private async Task<bool> ChangeListsAsync(object command, CancellationToken cancellationToken)
    {
        if (TaskId is null)
        {
            return false;
        }
        var response = await _core.CallAsync(command, cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        await ReloadAsync(cancellationToken);
        await LoadListPicksAsync(string.Empty, cancellationToken);
        return true;
    }

    /// <summary>Give the task to someone, or to no one.</summary>
    /// <remarks>
    /// An ordinary update carrying an <c>assigneeId</c> — null clears it, which is why the value is
    /// written rather than omitted. The core's edits distinguish "leave alone" from "clear".
    /// </remarks>
    public Task<bool> AssignAsync(string? userId, CancellationToken cancellationToken = default) =>
        UpdateAsync(new Dictionary<string, object?> { ["assigneeId"] = userId }, cancellationToken);

    /// <summary>
    /// Won't do, or Reopen: the one menu entry, sending what the task's state calls for.
    /// </summary>
    public Task<bool> ToggleWontDoAsync(CancellationToken cancellationToken = default) =>
        SetClosedReasonAsync(IsCanceled ? null : "canceled", cancellationToken);

    /// <summary>
    /// Close the open task as something other than done, or reopen it with null (task 016ce981).
    /// </summary>
    /// <remarks>
    /// Its own command rather than an update carrying the flag: a canceled close must not roll a
    /// repeating task forward, and the core is where that rule lives.
    /// </remarks>
    public async Task<bool> SetClosedReasonAsync(string? closedReason, CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return false;
        }
        var response = await _core.CallAsync(Commands.SetClosedReason(TaskId, closedReason), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        await ReloadAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Fill the Status submenu from the core: the task's board's columns, with the current one lit.
    /// </summary>
    public async Task<bool> LoadStatusChoicesAsync(CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return false;
        }
        var response = await _core.CallAsync(Commands.TaskStatusOptions(TaskId), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        Replace(StatusChoices, Read<StatusChoice>(response.Value, "columns"));
        return true;
    }

    /// <summary>
    /// Put the open task in a column. The core makes the same move a dragged card makes, so Done
    /// here means completed there too.
    /// </summary>
    public async Task<bool> SetStatusAsync(string columnId, CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return false;
        }
        var response = await _core.CallAsync(Commands.SetTaskStatus(TaskId, columnId), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        await ReloadAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Mint a share link for the open task. The address, or null with the reason in
    /// <see cref="ErrorMessage"/> — a share that cannot happen offline is a failure worth a word.
    /// </summary>
    /// <summary>
    /// Copy the open task into a list, with or without its comments, as the web's Copy does.
    /// </summary>
    /// <returns>The copy's id, or null when the copy did not happen — the pane's error line says why.</returns>
    /// <remarks>
    /// The server makes the copy, so this needs a connection; an offline attempt is reported
    /// rather than journalled, because a copy that does not exist yet cannot be shown.
    /// </remarks>
    public async Task<string?> CopyAsync(string? targetListId, bool includeComments,
        CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return null;
        }
        var response = await _core.CallAsync(
            Commands.CopyTask(TaskId, targetListId, includeComments), cancellationToken);
        if (!response.Ok)
        {
            // Unlike a journalled write, an offline copy genuinely did not happen — the server
            // makes the copy — so it is reported rather than treated as pending.
            ErrorMessage = response.Error?.Message;
            return null;
        }
        ErrorMessage = null;
        return response.Value.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
    }

    public async Task<string?> ShareAsync(CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return null;
        }
        var response = await _core.CallAsync(Commands.ShareTask(TaskId), cancellationToken);
        if (!Handle(response))
        {
            return null;
        }
        return response.Value.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String
            ? url.GetString()
            : null;
    }

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
        // The mark flips at once; the reload afterwards says what completing really did — a
        // repeating task rolls forward and comes back unchecked (task cdb30d3d).
        Completed = completed;
        var response = await _core.CallAsync(Commands.CompleteTask(TaskId, completed), cancellationToken);
        if (!Handle(response))
        {
            await RevertAsync(cancellationToken);
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

    // ── @person, #list, !task (task 3271a0c5) ───────────────────────────────────────────────
    //
    // The comment box and the reply box share one popup. When a popup opens, what is offered,
    // and what a choice puts into the text are the core's rules, mirrored from the web; this
    // holds the rows and which one is lit.

    private int _suggestionIndex;

    /// <summary>The popup's rows for the text as it stands. Empty means no popup.</summary>
    public ObservableCollection<Suggestion> CommentSuggestions { get; } = [];

    public bool HasCommentSuggestions => CommentSuggestions.Count > 0;

    /// <summary>Which row Return or Tab would take.</summary>
    public int SuggestionIndex
    {
        get => _suggestionIndex;
        private set => Set(ref _suggestionIndex, value);
    }

    /// <summary>Ask what the box should offer for this text and caret. Answers whether there is anything.</summary>
    public async Task<bool> SuggestCommentAsync(string text, int caret, CancellationToken cancellationToken = default)
    {
        if (TaskId is null)
        {
            return false;
        }
        var response = await _core.CallAsync(Commands.CommentSuggestions(TaskId, text, caret), cancellationToken);
        var suggestions = response.Ok ? response.Read<CommentSuggestions>() : null;
        Replace(CommentSuggestions, suggestions?.Trigger is null ? [] : suggestions.Items);
        SuggestionIndex = 0;
        Raise(nameof(HasCommentSuggestions));
        return HasCommentSuggestions;
    }

    /// <summary>Move the lit row, wrapping at either end as the web's list does.</summary>
    public void MoveSuggestion(int delta)
    {
        if (CommentSuggestions.Count == 0)
        {
            return;
        }
        var count = CommentSuggestions.Count;
        SuggestionIndex = ((SuggestionIndex + delta) % count + count) % count;
    }

    public void ClearSuggestions()
    {
        if (CommentSuggestions.Count > 0)
        {
            CommentSuggestions.Clear();
            Raise(nameof(HasCommentSuggestions));
        }
        SuggestionIndex = 0;
    }

    /// <summary>
    /// Put a row into the text — the lit one when none is named. Answers the new text and caret,
    /// or null when there was nothing to put.
    /// </summary>
    public async Task<AppliedSuggestion?> ApplyCommentSuggestionAsync(Suggestion? chosen, string text, int caret,
        CancellationToken cancellationToken = default)
    {
        chosen ??= SuggestionIndex < CommentSuggestions.Count ? CommentSuggestions[SuggestionIndex] : null;
        if (chosen is null)
        {
            return null;
        }
        var response = await _core.CallAsync(
            Commands.ApplyCommentSuggestion(text, caret, chosen.Kind, chosen.Id, chosen.Label), cancellationToken);
        ClearSuggestions();
        return response.Ok ? response.Read<AppliedSuggestion>() : null;
    }

    // ── Reply, edit, delete (task 97c817dd) ─────────────────────────────────────────────────
    //
    // One comment at a time is being replied to, and one edited; the rows carry the flags so the
    // template can draw the box or the editor in place. The thread is re-read after every write,
    // which is also what clears the flags.

    private string? _replyingToId;
    private string? _editingCommentId;

    /// <summary>The comment a reply is being written under, if one is.</summary>
    public string? ReplyingToId
    {
        get => _replyingToId;
        private set
        {
            if (Set(ref _replyingToId, value))
            {
                FlagRows();
            }
        }
    }

    /// <summary>The comment whose text is open for typing, if one is.</summary>
    public string? EditingCommentId
    {
        get => _editingCommentId;
        private set
        {
            if (Set(ref _editingCommentId, value))
            {
                FlagRows();
            }
        }
    }

    /// <summary>Open a reply box under a comment. Closes any editor: one thing at a time.</summary>
    public void BeginReply(string commentId)
    {
        EditingCommentId = null;
        ReplyingToId = commentId;
    }

    public void CancelReply() => ReplyingToId = null;

    /// <summary>Post the reply under the comment the box is open on.</summary>
    public async Task<bool> SendReplyAsync(string content, CancellationToken cancellationToken = default)
    {
        var trimmed = content.Trim();
        if (TaskId is null || ReplyingToId is not { } parent || trimmed.Length == 0)
        {
            return false;
        }
        var response = await _core.CallAsync(Commands.PostComment(TaskId, trimmed, parent), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        ReplyingToId = null;
        await ReloadAsync(cancellationToken);
        return true;
    }

    /// <summary>Open a comment's text for typing, in place of its bubble.</summary>
    public void BeginEdit(string commentId)
    {
        ReplyingToId = null;
        EditingCommentId = commentId;
    }

    public void CancelEdit() => EditingCommentId = null;

    /// <summary>Save what was typed into the open editor.</summary>
    public async Task<bool> SaveEditAsync(string content, CancellationToken cancellationToken = default)
    {
        var trimmed = content.Trim();
        if (EditingCommentId is not { } commentId || trimmed.Length == 0)
        {
            return false;
        }
        var current = Comments.FirstOrDefault(comment => comment.Id == commentId);
        if (current is not null && current.Content == trimmed)
        {
            EditingCommentId = null;
            return false;
        }
        var response = await _core.CallAsync(Commands.EditComment(commentId, trimmed), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        EditingCommentId = null;
        await ReloadAsync(cancellationToken);
        return true;
    }

    /// <summary>Take a comment out. Offline, it is gone here and goes from the server when it can.</summary>
    public async Task<bool> DeleteCommentAsync(string commentId, CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.DeleteComment(commentId), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        if (EditingCommentId == commentId)
        {
            EditingCommentId = null;
        }
        if (ReplyingToId == commentId)
        {
            ReplyingToId = null;
        }
        await ReloadAsync(cancellationToken);
        return true;
    }

    /// <summary>Put the reply and edit flags on the rows they belong to, and off the rest.</summary>
    private void FlagRows()
    {
        for (var index = 0; index < Comments.Count; index++)
        {
            var row = Comments[index];
            var flagged = row with
            {
                IsEditing = row.Id == EditingCommentId,
                IsReplying = row.Id == ReplyingToId,
            };
            if (!flagged.Equals(row))
            {
                Comments[index] = flagged;
            }
        }
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

    /// <summary>
    /// Put an optimistic change back after a refused write: reload from the cache, but keep the
    /// reason the write was refused, which the reload would otherwise clear (task cdb30d3d).
    /// </summary>
    private async Task RevertAsync(CancellationToken cancellationToken)
    {
        var reason = ErrorMessage;
        await ReloadAsync(cancellationToken);
        ErrorMessage = reason;
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
            // What a cancelled edit goes back to.
            _savedTitle = Title;
            _savedDescription = Description;
            DescriptionBlocks = Read<MarkdownBlock>(value, "descriptionBlocks");
            Priority = task.TryGetProperty("priority", out var priority) ? priority.GetInt32() : 0;
            Completed = task.TryGetProperty("completed", out var completed) && completed.GetBoolean();
        }

        IsCanceled = value.TryGetProperty("isCanceled", out var canceled)
                     && canceled.ValueKind == JsonValueKind.True;
        IsCopyOnly = value.TryGetProperty("isCopyOnly", out var copyOnly)
                     && copyOnly.ValueKind == JsonValueKind.True;
        Link = value.TryGetProperty("link", out var link) && link.ValueKind == JsonValueKind.String
            ? link.GetString()
            : null;
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
        Replace(Comments, Read<CommentSummary>(value, "comments")
            .Select(row => row with
            {
                IsEditing = row.Id == EditingCommentId,
                IsReplying = row.Id == ReplyingToId,
            })
            .ToList());
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

    /// <summary>The text as the web draws it — pills and markdown — rendered in the core (task 3271a0c5).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("blocks")]
    public IReadOnlyList<MarkdownBlock> Blocks { get; init; } = [];

    /// <summary>The comment this one answers, when it answers one (task 97c817dd).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("parentId")]
    public string? ParentId { get; init; }

    /// <summary>Drawn nested under its parent.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("isReply")]
    public bool IsReply { get; init; }

    /// <summary>Which side a reply steps in from: the parent author's, as the web does it.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("indentRight")]
    public bool IndentRight { get; init; }

    /// <summary>This comment's text is open for typing. Set by the view model, not the wire.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEditing { get; init; }

    /// <summary>A reply is being written under this comment. Set by the view model, not the wire.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsReplying { get; init; }

    /// <summary><c>none</c>, <c>left</c> or <c>right</c>: which side a reply steps in from, for the shell's margin.</summary>
    public string IndentSide => !IsReply ? "none" : IndentRight ? "right" : "left";

    /// <summary>A bubble is anything that is not a system note.</summary>
    public bool IsBubble => !IsSystem;

    /// <summary>The bubble is drawn unless the editor has taken its place.</summary>
    public bool ShowsBubble => IsBubble && !IsEditing;

    /// <summary>Only a top-level comment takes replies, as on the web; a reply to a reply would be a thread nobody can draw.</summary>
    public bool CanReply => IsBubble && !IsReply;

    /// <summary>Your own words are yours to change or take back; nobody else's.</summary>
    public bool CanEdit => IsBubble && IsMine;

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
