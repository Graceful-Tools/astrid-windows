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
        _core.Changed += OnChanged;
    }

    public SidebarViewModel Sidebar { get; }

    public TaskListViewModel Tasks { get; }

    public SignInViewModel SignIn { get; }

    /// <summary>The open task, when there is one.</summary>
    public TaskDetailViewModel Detail { get; }

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
