using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The window: a sidebar, a list, and the state that spans both.
/// </summary>
/// <remarks>
/// <para>
/// It owns the two smaller view models and the wiring between them — selecting a list opens it,
/// a change notification refreshes what is on screen, a sync updates the "not synced yet"
/// indicator.
/// </para>
/// <para>
/// <b>Notifications arrive on a pool thread.</b> The shell hands in a <see cref="Post"/> that
/// marshals to the UI thread; without one, everything still works in tests and crashes in the app.
/// Making it a required constructor argument rather than an optional property is deliberate: a
/// default that silently does the wrong thing on one of the two callers is worse than a compile
/// error.
/// </para>
/// </remarks>
public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IAstridCore _core;
    private readonly Action<Func<Task>> _post;
    private bool _hasUnsentWork;
    private bool _isSyncing;
    private bool _needsSignIn;
    private string? _statusMessage;
    private bool _disposed;
    private bool _isBoardView;
    private bool _isChatOpen;
    private bool _isPaletteOpen;
    private bool _isTourOpen;

    /// <param name="post">
    /// Runs work on the UI thread. Given by the shell; a test passes something that runs it inline.
    /// </param>
    public ShellViewModel(IAstridCore core, Action<Func<Task>> post)
    {
        _core = core;
        _post = post;
        Sidebar = new SidebarViewModel(core);
        Tasks = new TaskListViewModel(core);
        SignIn = new SignInViewModel(core);
        Detail = new TaskDetailViewModel(core);
        Board = new BoardViewModel(core);
        ListSettings = new ListSettingsViewModel(core);
        Chat = new ChatViewModel(core);
        Settings = new SettingsViewModel(core);
        _core.Changed += OnChanged;

        // The task-detail layout decides what the leading control does on every row and in the
        // open task (task c0f3db19). A change has to be seen at once, not after the next sync.
        Settings.DisplayModeChanged += () => _post(async () =>
        {
            await Tasks.RefreshAsync();
            if (IsBoardView)
            {
                await Board.RefreshAsync();
            }
            if (Detail.IsOpen)
            {
                await Detail.ReloadAsync();
            }
        });

        // The board's expanded card and the open detail are one fact seen from two sides
        // (task 91a25b8a). Closing the detail — from its own menu, or by opening a different
        // list — collapses the card; a card the board no longer has closes the detail it was
        // drawn under. Neither side loops: each only acts when the other has actually moved.
        Detail.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(TaskDetailViewModel.IsOpen) && !Detail.IsOpen)
            {
                Board.Collapse();
            }
            if (args.PropertyName == nameof(TaskDetailViewModel.IsOpen))
            {
                Raise(nameof(ShowsDetailInline));
                Raise(nameof(ShowsDetailPane));
            }
        };
        Board.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(BoardViewModel.ExpandedTaskId))
            {
                return;
            }
            if (Board.ExpandedTaskId is null && IsBoardView && Detail.IsOpen)
            {
                Detail.Close();
            }
            Raise(nameof(ShowsDetailInline));
            Raise(nameof(ShowsDetailPane));
        };
    }

    /// <summary>
    /// Reminders that have come due and want a banner.
    /// </summary>
    /// <remarks>
    /// An event rather than a collection: a reminder is a moment, not a state, and what the shell
    /// does with it — a toast, a window, a sound — is a platform decision this layer must not
    /// make. Nothing marks itself shown here either: the shell says so once a banner is actually
    /// on screen, because one that failed to appear is still owed.
    /// </remarks>
    public event Action<IReadOnlyList<Reminder>>? RemindersDue;

    public SidebarViewModel Sidebar { get; }

    public TaskListViewModel Tasks { get; }

    public SignInViewModel SignIn { get; }

    /// <summary>The open task, when there is one.</summary>
    public TaskDetailViewModel Detail { get; }

    /// <summary>The board the open list belongs to, when it belongs to one.</summary>
    public BoardViewModel Board { get; }

    /// <summary>The open list's settings and members, loaded when they are asked for.</summary>
    public ListSettingsViewModel ListSettings { get; }

    /// <summary>The open list's conversation.</summary>
    public ChatViewModel Chat { get; }

    /// <summary>The account, and how it wants to be reminded.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>
    /// Whether to show the first-run tour.
    /// </summary>
    /// <remarks>
    /// Three things somebody cannot discover by looking: the global hotkey, the palette, and that
    /// the bare keys do anything at all. Everything else in this app is on screen.
    /// </remarks>
    public bool IsTourOpen
    {
        get => _isTourOpen;
        private set => Set(ref _isTourOpen, value);
    }

    /// <summary>
    /// Show the tour if this machine has not seen it.
    /// </summary>
    /// <remarks>
    /// Not while signed out. It landed on top of the sign-in card on a fresh machine, telling
    /// somebody about a hotkey for a window with nothing in it and a palette that can find nothing
    /// — and it marks itself seen when dismissed, so the one moment it was written for was the one
    /// moment it was spent on.
    /// </remarks>
    public async Task MaybeShowTourAsync(CancellationToken cancellationToken = default)
    {
        if (SignIn.NeedsSignIn)
        {
            return;
        }
        var response = await _core.CallAsync(Commands.HasSeenTour(), cancellationToken);
        if (response.Ok
            && response.Value.TryGetProperty("seen", out var seen)
            && !seen.GetBoolean())
        {
            IsTourOpen = true;
        }
    }

    /// <summary>Close the tour, and do not show it again on this machine.</summary>
    public async Task DismissTourAsync(CancellationToken cancellationToken = default)
    {
        IsTourOpen = false;
        await _core.CallAsync(Commands.TourSeen(), cancellationToken);
    }

    /// <summary>
    /// What the palette found.
    /// </summary>
    /// <remarks>
    /// Ranked in the core with the Mac's matcher. This class asks and shows; it does not decide
    /// which row is the best answer, because that ranking is the difference between a palette and
    /// a list of everything.
    /// </remarks>
    public ObservableCollection<PaletteRow> PaletteRows { get; } = [];

    public bool IsPaletteOpen
    {
        get => _isPaletteOpen;
        private set => Set(ref _isPaletteOpen, value);
    }

    /// <summary>Open or close the palette. Opening fills it, so it teaches what it can do.</summary>
    public async Task ShowPaletteAsync(bool open, CancellationToken cancellationToken = default)
    {
        IsPaletteOpen = open;
        if (open)
        {
            await SearchPaletteAsync(string.Empty, cancellationToken);
        }
        else
        {
            PaletteRows.Clear();
        }
    }

    public async Task SearchPaletteAsync(string query, CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.Palette(query), cancellationToken);
        if (!response.Ok)
        {
            return;
        }
        var palette = response.Read<Palette>();
        PaletteRows.Clear();
        foreach (var row in palette?.Rows ?? [])
        {
            PaletteRows.Add(row);
        }
    }

    /// <summary>
    /// Do what a palette row says.
    /// </summary>
    /// <remarks>
    /// A list opens it, a task opens it, and a command is dispatched by the action name the shared
    /// keyboard table carries — the same names web uses, so one action is not described two ways.
    /// </remarks>
    public async Task RunPaletteRowAsync(PaletteRow row, CancellationToken cancellationToken = default)
    {
        IsPaletteOpen = false;
        switch (row.Kind)
        {
            case "list":
                await OpenListAsync(row.Id, row.Title, cancellationToken);
                break;
            case "task":
                await OpenTaskAsync(row.Id, cancellationToken);
                break;
            default:
                PaletteCommandRequested?.Invoke(row.Id);
                break;
        }
    }

    /// <summary>
    /// A command was chosen in the palette.
    /// </summary>
    /// <remarks>
    /// Raised rather than run here: the actions are the keyboard scheme's, and the shell already
    /// has one place that carries them out. Two implementations of "new task" is how they come to
    /// differ.
    /// </remarks>
    public event Action<string>? PaletteCommandRequested;

    /// <summary>Load the account screen.</summary>
    public async Task LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        await Settings.LoadAsync(cancellationToken);
        // This screen asks the server for the account, so it is often the first to notice an
        // expired session — which belongs on the sign-in screen rather than beside somebody's name.
        NeedsSignIn |= Settings.NeedsSignIn;
    }

    /// <summary>Whether the conversation is on screen.</summary>
    public bool IsChatOpen
    {
        get => _isChatOpen;
        private set => Set(ref _isChatOpen, value);
    }

    /// <summary>Show or hide the conversation for the open list.</summary>
    public async Task ShowChatAsync(bool open, CancellationToken cancellationToken = default)
    {
        IsChatOpen = open;
        if (open)
        {
            await Chat.OpenAsync(Tasks.ListId, cancellationToken);
        }
    }

    /// <summary>Load the open list's settings.</summary>
    public async Task LoadListSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(Tasks.ListId))
        {
            return;
        }
        await ListSettings.LoadAsync(Tasks.ListId, cancellationToken);
        // Membership is the one screen that reaches the network on its own, so it is usually the
        // first to notice an expired session. That belongs on the sign-in screen, not in a red line
        // beside an empty member list.
        NeedsSignIn |= ListSettings.NeedsSignIn;
    }

    /// <summary>
    /// Delete the open list.
    /// </summary>
    /// <remarks>
    /// Through the Outbox like every other write, so it works offline — and it takes the list from
    /// everybody it is shared with, which is why the button that calls this asks first.
    /// </remarks>
    public async Task DeleteListAsync(CancellationToken cancellationToken = default)
    {
        var listId = Tasks.ListId;
        if (string.IsNullOrEmpty(listId))
        {
            return;
        }
        var response = await _core.CallAsync(Commands.DeleteList(listId), cancellationToken);
        if (response.Ok || response.IsStillPending)
        {
            await Sidebar.LoadAsync(cancellationToken);
        }
    }

    /// <summary>Leave the open list, and stop showing it.</summary>
    public async Task LeaveListAsync(CancellationToken cancellationToken = default)
    {
        if (await ListSettings.LeaveAsync(cancellationToken))
        {
            await Sidebar.LoadAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Whether the board is on screen instead of the list.
    /// </summary>
    /// <remarks>
    /// A view of the same tasks, not a different set: the board and the list show one list's work
    /// two ways, which is why this is a flag here rather than a separate screen with its own
    /// loading and its own idea of what is selected.
    /// </remarks>
    public bool IsBoardView
    {
        get => _isBoardView;
        private set
        {
            if (Set(ref _isBoardView, value))
            {
                Raise(nameof(ShowsNoBoardNotice));
                Raise(nameof(ShowsDetailInline));
                Raise(nameof(ShowsDetailPane));
            }
        }
    }

    /// <summary>
    /// Whether to say that this list has no board.
    /// </summary>
    /// <remarks>
    /// The board toggle is offered for every list, because whether a list belongs to a board is
    /// something only the answer to <c>board</c> tells us. Switching to it on a list that has none
    /// left a blank white pane, which reads as a board that failed to load rather than one that
    /// was never there.
    /// </remarks>
    public bool ShowsNoBoardNotice => IsBoardView && !Board.HasBoard;

    /// <summary>Swap between the list and its board.</summary>
    /// <remarks>
    /// An open task comes along when it can. Switching to the board with a task open expands its
    /// card, so the thing being read is still on screen; switching back to the list collapses
    /// the card and the pane takes over. A task open in the side pane that is on no card — the
    /// board is still loading, or the list has no board — is closed rather than left floating
    /// beside a view it has no place in.
    /// </remarks>
    public async Task ShowBoardAsync(bool board, CancellationToken cancellationToken = default)
    {
        IsBoardView = board;
        if (board)
        {
            await Board.LoadAsync(Tasks.ListId, cancellationToken);
            if (Detail.IsOpen && Detail.TaskId is { } open && Board.Has(open))
            {
                Board.Expand(open);
            }
            else if (Detail.IsOpen)
            {
                Detail.Close();
            }
        }
        else
        {
            Board.Collapse();
        }
        Raise(nameof(ShowsNoBoardNotice));
        Raise(nameof(ShowsDetailInline));
        Raise(nameof(ShowsDetailPane));
    }

    /// <summary>
    /// Whether the open task is drawn inside its card on the board, as astrid-web's board draws
    /// it (task 91a25b8a).
    /// </summary>
    public bool ShowsDetailInline => IsBoardView && Detail.IsOpen && Board.ExpandedTaskId is not null;

    /// <summary>Whether the open task is drawn in the side pane beside the list.</summary>
    public bool ShowsDetailPane => Detail.IsOpen && !ShowsDetailInline;

    /// <summary>
    /// A card was tapped: open its task in place, or close it if that task is the open one.
    /// </summary>
    /// <remarks>
    /// The board's version of <see cref="OpenOrCloseTaskAsync"/>, with the same shape: a second
    /// tap on the open card is a dismissal, and a tap on a different card is a different question.
    /// The card is expanded before the detail loads so the column makes room straight away rather
    /// than after the round trip.
    /// </remarks>
    public async Task ToggleCardAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (Board.ExpandedTaskId == taskId)
        {
            Detail.Close();
            return;
        }
        Board.Expand(taskId);
        await OpenTaskAsync(taskId, cancellationToken);
    }

    /// <summary>True while anything is waiting in the Outbox.</summary>
    public bool HasUnsentWork
    {
        get => _hasUnsentWork;
        private set => Set(ref _hasUnsentWork, value);
    }

    public bool IsSyncing
    {
        get => _isSyncing;
        private set => Set(ref _isSyncing, value);
    }

    /// <summary>Set when the session has gone and the user has to sign in again.</summary>
    public bool NeedsSignIn
    {
        get => _needsSignIn;
        private set => Set(ref _needsSignIn, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    /// <summary>
    /// Draw what is already cached, then go and look for more.
    /// </summary>
    /// <remarks>
    /// In that order, and it is the whole feel of the app: the first paint comes from disk and owes
    /// nothing to the network, and the sync that follows updates what is already on screen. An app
    /// that waited for the network to draw its first list would be unusable on a train and
    /// noticeably slower everywhere else.
    /// </remarks>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        // Whether we are signed in decides what the window shows, so it is asked first. It is a
        // cache read — no network — so it costs nothing to put ahead of the first paint.
        await SignIn.RefreshAsync(cancellationToken);
        if (SignIn.NeedsSignIn)
        {
            // Nothing to draw and nothing to sync until there is a session. Loading the sidebar
            // anyway would show the previous user's lists behind a sign-in screen.
            return;
        }

        await Sidebar.LoadAsync(cancellationToken);
        await OpenSelectedAsync(cancellationToken);
        await RefreshOutboxAsync(cancellationToken);
        await SyncAsync(cancellationToken);
    }

    /// <summary>Open a task in the detail pane.</summary>
    public Task OpenTaskAsync(string taskId, CancellationToken cancellationToken = default)
        => Detail.OpenAsync(taskId, cancellationToken);

    /// <summary>Open a list by id — from the palette, or from a <c>#list</c> pill in a description.</summary>
    public async Task OpenListAsync(string listId, string name, CancellationToken cancellationToken = default)
    {
        await Tasks.OpenAsync(listId, name, cancellationToken);
        // And the sidebar follows, so the app is not showing one list with another highlighted.
        Sidebar.Selected = Sidebar.Favorites.Concat(Sidebar.Lists)
            .FirstOrDefault(list => list.Id == listId) ?? Sidebar.Selected;
    }

    /// <summary>
    /// A row was tapped: open its task, or close the pane if that task is already the open one.
    /// </summary>
    /// <remarks>
    /// Selection alone cannot express this. Tapping the row that is already selected raises no
    /// selection change, so the pane sat open with no way to dismiss it from the list it came from
    /// — the close was in the header's overflow menu and nowhere a hand would look (task 8ac00791).
    ///
    /// Tapping a DIFFERENT row opens that one. A second task is a different question, not a
    /// dismissal, which is the case a plain "toggle" would get wrong.
    /// </remarks>
    public Task OpenOrCloseTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (Detail.IsOpen && Detail.TaskId == taskId)
        {
            Detail.Close();
            return Task.CompletedTask;
        }
        return OpenTaskAsync(taskId, cancellationToken);
    }

    /// <summary>Open whatever the sidebar has selected.</summary>
    public async Task OpenSelectedAsync(CancellationToken cancellationToken = default)
    {
        var selected = Sidebar.Selected;
        if (selected is null)
        {
            return;
        }
        // A different list means the open task probably is not in it. Closing is more honest than
        // leaving a detail pane showing something the list beside it no longer contains.
        Detail.Close();
        await Tasks.OpenAsync(selected.Id, selected.Name, cancellationToken);
        NeedsSignIn |= Tasks.NeedsSignIn;
    }

    /// <summary>One sync pass: push what is queued, fetch what is new.</summary>
    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        if (IsSyncing)
        {
            return;
        }
        IsSyncing = true;
        try
        {
            var response = await _core.CallAsync(Commands.Sync(), cancellationToken);
            if (response.NeedsSignIn)
            {
                NeedsSignIn = true;
                // The session has gone, so the window has to go back to the sign-in screen rather
                // than sit on a list it can no longer refresh.
                await SignIn.RefreshAsync(cancellationToken);
                return;
            }

            // A pass that could not reach the server is not an error. It says so, and the app
            // carries on with what it has.
            var fetched = response.Ok
                && response.Value.TryGetProperty("fetched", out var element)
                && element.GetBoolean();
            StatusMessage = fetched ? null : "offline";

            if (fetched)
            {
                await Sidebar.LoadAsync(cancellationToken);
                await Tasks.RefreshAsync(cancellationToken);
            }
            await RefreshOutboxAsync(cancellationToken);
        }
        finally
        {
            IsSyncing = false;
        }
    }

    public async Task RefreshOutboxAsync(CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.OutboxStats(), cancellationToken);
        var stats = response.Read<OutboxStats>();
        HasUnsentWork = stats?.HasUnsentWork ?? false;
    }

    /// <summary>
    /// Windows activated the app with a URL.
    /// </summary>
    /// <remarks>
    /// Every protocol activation comes here, not only sign-in callbacks — the core decides which
    /// is which. A completed sign-in is followed by a full start, because until now there was
    /// nothing to draw.
    /// </remarks>
    public async Task HandleActivationAsync(string url, CancellationToken cancellationToken = default)
    {
        if (await SignIn.CompleteAsync(url, cancellationToken))
        {
            NeedsSignIn = false;
            await StartAsync(cancellationToken);
            // Here rather than at launch on a fresh machine: the tour is about a hotkey, a palette
            // and the bare keys, none of which do anything until there is a list to use them on.
            await MaybeShowTourAsync(cancellationToken);
        }
    }

    /// <summary>Sign out, and put the window back to the sign-in screen.</summary>
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await SignIn.SignOutAsync(cancellationToken);
        Sidebar.Favorites.Clear();
        Sidebar.Lists.Clear();
        Sidebar.Selected = null;
        Tasks.Rows.Clear();
        HasUnsentWork = false;
        NeedsSignIn = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        // Unsubscribing matters: the core outlives a window that is being closed, and a handler on
        // a disposed view model would keep it alive and then touch a UI that is gone.
        _core.Changed -= OnChanged;
    }

    /// <summary>Ask what is outstanding and hand it to whoever draws banners.</summary>
    public async Task RaiseRemindersAsync(CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.RemindersDue(), cancellationToken);
        if (!response.Ok)
        {
            return;
        }
        var due = response.Read<RemindersDue>();
        if (due is { Reminders.Count: > 0 })
        {
            RemindersDue?.Invoke(due.Reminders);
        }
    }

    /// <summary>Remember that a reminder reached the screen.</summary>
    public Task ReminderShownAsync(string taskId, CancellationToken cancellationToken = default) =>
        _core.CallAsync(Commands.ReminderShown(taskId), cancellationToken);

    /// <summary>Move a reminder forward and let it ask again.</summary>
    public async Task SnoozeReminderAsync(string taskId, int minutes,
        CancellationToken cancellationToken = default)
    {
        await _core.CallAsync(Commands.SnoozeReminder(taskId, minutes), cancellationToken);
        await Tasks.RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Finish a task from its reminder.
    /// </summary>
    /// <remarks>
    /// Through the same completion path as everywhere else, so a repeating task rolls forward to
    /// its next occurrence instead of finishing — a banner is not a special case.
    /// </remarks>
    public async Task CompleteFromReminderAsync(string taskId,
        CancellationToken cancellationToken = default)
    {
        await _core.CallAsync(Commands.CompleteTask(taskId, true), cancellationToken);
        await Tasks.RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Something changed underneath us. Refresh exactly that.
    /// </summary>
    /// <remarks>
    /// Not everything. The live stream can deliver several notifications a second while a
    /// colleague works in the same list, and a full reload on each would make the app slower the
    /// more people are using it.
    /// </remarks>
    private void OnChanged(ChangeNotification notification)
    {
        if (_disposed)
        {
            return;
        }

        _post(async () =>
        {
            switch (notification.Change)
            {
                case "task":
                    await Tasks.RefreshAsync();
                    if (IsBoardView)
                    {
                        await Board.RefreshAsync();
                    }
                    // Only when it is the task on screen. Reloading the detail pane for every
                    // task somebody else touches would make the open task flicker while a
                    // colleague works elsewhere in the same list.
                    if (Detail.IsOpen && (notification.Id is null || notification.Id == Detail.TaskId))
                    {
                        await Detail.ReloadAsync();
                    }
                    await RefreshOutboxAsync();
                    break;
                case "comments":
                    if (Detail.IsOpen && notification.Id == Detail.TaskId)
                    {
                        await Detail.ReloadAsync();
                    }
                    break;
                case "list":
                    await Sidebar.LoadAsync();
                    break;
                case "chat":
                    if (IsChatOpen)
                    {
                        await Chat.RefreshAsync();
                    }
                    break;
                case "remindersDue":
                    await RaiseRemindersAsync();
                    break;
                case "needsSync":
                    await SyncAsync();
                    break;
                default:
                    // A change this build does not draw anything for. The next sync carries it.
                    break;
            }
        });
    }
}
