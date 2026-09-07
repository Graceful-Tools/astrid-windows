using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// One list on screen: the rows in it, and what the user can do to them.
/// </summary>
/// <remarks>
/// <para>
/// It holds no rules. Which tasks belong in this list, in what order, with subtasks spliced in and
/// each row projected — all of that is one <c>rowsForList</c> command, decided in
/// <c>astrid_core::filters</c> and <c>astrid_core::rows</c>. This class asks, and puts the answer
/// somewhere XAML can see it.
/// </para>
/// <para>
/// <b>Rows load a window at a time.</b> A list of ten thousand crosses the boundary as the rows on
/// screen plus a screenful of margin. The alternative — fetch everything, virtualise in XAML — is
/// what the M0 spike was worried about, and it costs a JSON encode of the whole account on every
/// refresh, several times a minute while somebody else is working in the same list.
/// </para>
/// </remarks>
public sealed class TaskListViewModel : ObservableObject
{
    /// <summary>
    /// How many rows to fetch at once.
    /// </summary>
    /// <remarks>
    /// Comfortably more than a tall window shows, so scrolling a screen does not always cost a
    /// round trip, and small enough that the first paint is one page rather than an account.
    /// </remarks>
    public const int PageSize = 200;

    private readonly IAstridCore _core;
    private string _listId = string.Empty;
    private string _listName = string.Empty;
    private bool _isLoading;
    private int _total;
    private string? _errorMessage;
    private bool _needsSignIn;

    public TaskListViewModel(IAstridCore core)
    {
        _core = core;
    }

    /// <summary>The rows on screen, in order.</summary>
    public ObservableCollection<TaskRow> Rows { get; } = [];

    public string ListId
    {
        get => _listId;
        private set => Set(ref _listId, value);
    }

    public string ListName
    {
        get => _listName;
        private set => Set(ref _listName, value);
    }

    /// <summary>True while a load is in flight, for a progress ring.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    /// <summary>How many rows there are to scroll through, after filtering.</summary>
    public int Total
    {
        get => _total;
        private set => Set(ref _total, value);
    }

    /// <summary>
    /// Something to show the user, or null.
    /// </summary>
    /// <remarks>
    /// Never set for an offline write. That is not an error — the change is in the Outbox and will
    /// go when the network does — and showing it as one is how a working offline app comes to look
    /// broken.
    /// </remarks>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    /// <summary>Set when the session has gone, so the shell can send the user to sign in.</summary>
    public bool NeedsSignIn
    {
        get => _needsSignIn;
        private set => Set(ref _needsSignIn, value);
    }

    public bool IsEmpty => Rows.Count == 0 && !IsLoading;

    /// <summary>Show a different list.</summary>
    public async Task OpenAsync(string listId, string listName, CancellationToken cancellationToken = default)
    {
        ListId = listId;
        ListName = listName;
        Rows.Clear();
        Total = 0;
        await LoadMoreAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fetch the next window of rows.</summary>
    public async Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading || string.IsNullOrEmpty(ListId))
        {
            return;
        }
        if (Rows.Count > 0 && Rows.Count >= Total)
        {
            return;
        }

        IsLoading = true;
        try
        {
            var response = await _core
                .CallAsync(Commands.RowsForList(ListId, Rows.Count, PageSize), cancellationToken)
                .ConfigureAwait(false);
            if (!Handle(response))
            {
                return;
            }

            var window = response.Read<RowWindow>();
            if (window is null)
            {
                return;
            }

            Total = window.Total;
            foreach (var row in window.Rows)
            {
                Rows.Add(row);
            }
        }
        finally
        {
            IsLoading = false;
            Raise(nameof(IsEmpty));
        }
    }

    /// <summary>Reload from the top. What a change notification and a pull-to-refresh both do.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ListId))
        {
            return;
        }

        // Ask for as many rows as are already on screen, so a refresh does not scroll the list back
        // to the top under somebody's cursor.
        var wanted = Math.Max(PageSize, Rows.Count);
        IsLoading = true;
        try
        {
            var response = await _core
                .CallAsync(Commands.RowsForList(ListId, 0, wanted), cancellationToken)
                .ConfigureAwait(false);
            if (!Handle(response))
            {
                return;
            }

            var window = response.Read<RowWindow>();
            if (window is null)
            {
                return;
            }

            Total = window.Total;
            Replace(window.Rows);
        }
        finally
        {
            IsLoading = false;
            Raise(nameof(IsEmpty));
        }
    }

    /// <summary>Add a task to this list.</summary>
    public async Task<bool> CreateTaskAsync(string title, CancellationToken cancellationToken = default)
    {
        var trimmed = title.Trim();
        if (trimmed.Length == 0)
        {
            // An empty title is a stray Enter, not a task. Creating one puts an untitled row in
            // somebody's list that they then have to find and delete.
            return false;
        }

        var listIds = string.IsNullOrEmpty(ListId) ? Array.Empty<string>() : [ListId];
        var response = await _core
            .CallAsync(Commands.CreateTask(trimmed, listIds), cancellationToken)
            .ConfigureAwait(false);
        if (!Handle(response))
        {
            return false;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Complete or un-complete a row.
    /// </summary>
    /// <remarks>
    /// Always the <c>completeTask</c> command, never an update with a completed flag: a repeating
    /// task rolls forward instead of finishing, and only that path does it. See
    /// <c>astrid_core::services::task</c>.
    /// </remarks>
    public async Task<bool> SetCompletedAsync(string taskId, bool completed,
        CancellationToken cancellationToken = default)
    {
        var response = await _core
            .CallAsync(Commands.CompleteTask(taskId, completed), cancellationToken)
            .ConfigureAwait(false);
        if (!Handle(response))
        {
            return false;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var response = await _core
            .CallAsync(Commands.DeleteTask(taskId), cancellationToken)
            .ConfigureAwait(false);
        if (!Handle(response))
        {
            return false;
        }

        // Take it off screen without a round trip: it is already gone from the cache.
        var existing = Rows.FirstOrDefault(row => row.Id == taskId);
        if (existing is not null)
        {
            Rows.Remove(existing);
            Total = Math.Max(0, Total - 1);
            Raise(nameof(IsEmpty));
        }
        return true;
    }

    /// <summary>
    /// Read a response, and decide what the user should be told.
    /// </summary>
    /// <returns><c>true</c> when the caller should carry on.</returns>
    private bool Handle(AstridResponse response)
    {
        if (response.Ok)
        {
            ErrorMessage = null;
            return true;
        }

        NeedsSignIn = response.NeedsSignIn;
        // Offline is not a failure to report: the write is journalled. Everything else is.
        ErrorMessage = response.IsStillPending ? null : response.Error?.Message;
        return false;
    }

    /// <summary>
    /// Swap in a new set of rows, keeping the collection object the list is bound to.
    /// </summary>
    /// <remarks>
    /// Rows that have not changed are left alone, so a refresh does not tear down and rebuild every
    /// visible row — which loses the caret in an inline edit and makes the list flash.
    /// </remarks>
    private void Replace(IReadOnlyList<TaskRow> rows)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            if (index < Rows.Count)
            {
                if (!Rows[index].Equals(rows[index]))
                {
                    Rows[index] = rows[index];
                }
            }
            else
            {
                Rows.Add(rows[index]);
            }
        }

        while (Rows.Count > rows.Count)
        {
            Rows.RemoveAt(Rows.Count - 1);
        }
    }
}
