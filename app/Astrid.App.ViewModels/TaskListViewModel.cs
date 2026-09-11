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
    private TaskRow? _selected;
    private string _searchQuery = string.Empty;
    private bool _isSearching;
    private bool _isFiltered;
    private string? _lastCreatedTitle;

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

    /// <summary>
    /// The row the keyboard acts on.
    /// </summary>
    /// <remarks>
    /// Held here rather than read from the ListView, because the shared shortcut scheme is
    /// selection-scoped — half its actions do nothing without one — and a selection that only
    /// exists in a control cannot be asked about from a view model or a test.
    /// </remarks>
    public TaskRow? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                Raise(nameof(HasSelection));
            }
        }
    }

    public bool HasSelection => Selected is not null;

    /// <summary>
    /// What is being searched for, or empty.
    /// </summary>
    /// <remarks>
    /// Searching replaces what the list shows rather than opening a separate screen: the results
    /// are rows like any other, they complete and open the same way, and a second surface that
    /// behaved almost-but-not-quite like the list is how two code paths for one thing begin.
    /// </remarks>
    public string SearchQuery
    {
        get => _searchQuery;
        private set
        {
            if (Set(ref _searchQuery, value))
            {
                Raise(nameof(IsShowingSearchResults));
            }
        }
    }

    public bool IsShowingSearchResults => _isSearching;

    public bool IsEmpty => Rows.Count == 0 && !IsLoading;

    /// <summary>Move the selection by <paramref name="delta"/> rows, stopping at the ends.</summary>
    /// <remarks>
    /// Stopping rather than wrapping: a list that jumps from the last row to the first when
    /// somebody holds the down arrow reads as a bug, whatever the intent.
    /// </remarks>
    public void MoveSelection(int delta)
    {
        if (Rows.Count == 0)
        {
            Selected = null;
            return;
        }

        var current = Selected is null ? -1 : Rows.IndexOf(Selected);
        // With nothing selected, down picks the first row and up picks the last — which is what
        // pressing a direction key on an unfocused list is asking for.
        var next = current < 0
            ? (delta > 0 ? 0 : Rows.Count - 1)
            : Math.Clamp(current + delta, 0, Rows.Count - 1);
        Selected = Rows[next];
    }

    /// <summary>Show a different list.</summary>
    public async Task OpenAsync(string listId, string listName, CancellationToken cancellationToken = default)
    {
        // Choosing a list is a way out of a search, and leaving the query in the box while showing
        // a list's contents would be showing one thing and saying another.
        SearchQuery = string.Empty;
        _isSearching = false;
        Raise(nameof(IsShowingSearchResults));
        ListId = listId;
        ListName = listName;
        Rows.Clear();
        Total = 0;
        // A new list is a new question, so it gets a new generation. Anything still in flight for
        // the previous one will find its generation stale and drop its answer rather than filling
        // the rows that have just been cleared for this one.
        await LoadWindowAsync(++_generation, cancellationToken);
    }

    /// <summary>
    /// Fetch the next window of rows.
    /// </summary>
    /// <remarks>
    /// Scrolling past the last loaded row asks for the next page, and asking twice at once would
    /// fetch the same window twice — hence the in-flight guard. Choosing a LIST is not that, and
    /// used to be turned away by the same guard: the rows were cleared for the new list, the fetch
    /// was refused because one was already running, and then the old list's answer arrived and
    /// filled them. See <see cref="LoadWindowAsync"/>.
    /// </remarks>
    public Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading)
        {
            return Task.CompletedTask;
        }
        if (Rows.Count > 0 && Rows.Count >= Total)
        {
            return Task.CompletedTask;
        }
        return LoadWindowAsync(_generation, cancellationToken);
    }

    /// <summary>
    /// One window, for one generation of the question.
    /// </summary>
    /// <remarks>
    /// The generation is what makes a late answer harmless. A response is applied only if the list
    /// it was asked for is still the list on screen; otherwise it is dropped, because rows from a
    /// list nobody is looking at are worse than no rows at all — they look exactly like the filter
    /// being broken.
    /// </remarks>
    private async Task LoadWindowAsync(int generation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(ListId))
        {
            return;
        }

        IsLoading = true;
        try
        {
            var response = await _core
                .CallAsync(Commands.RowsForList(ListId, Rows.Count, PageSize), cancellationToken);
            if (generation != _generation)
            {
                return;
            }
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
            // Only the current generation owns the flag. A stale load clearing it would let the
            // pager fire again underneath the load that replaced it.
            if (generation == _generation)
            {
                IsLoading = false;
                Raise(nameof(IsEmpty));
            }
        }
    }

    /// <summary>Which question the rows on screen are the answer to.</summary>
    private int _generation;

    /// <summary>
    /// Search, or go back to the list when the query is emptied.
    /// </summary>
    /// <remarks>
    /// The core decides what is too short to search for and answers with nothing, so a query of one
    /// character shows an empty result list rather than flashing the whole account on the way to
    /// the answer.
    /// </remarks>
    public async Task SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        SearchQuery = query;
        if (string.IsNullOrWhiteSpace(query))
        {
            _isSearching = false;
            Raise(nameof(IsShowingSearchResults));
            await RefreshAsync(cancellationToken);
            return;
        }

        _isSearching = true;
        Raise(nameof(IsShowingSearchResults));
        IsLoading = true;
        try
        {
            var response = await _core.CallAsync(
                Commands.SearchTasks(query, limit: PageSize), cancellationToken);
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

    /// <summary>What this list is filtered and sorted by, loaded when the sheet opens.</summary>
    public ObservableCollection<FilterGroup> FilterGroups { get; } = [];

    /// <summary>
    /// Whether anything is narrowing what the list shows.
    /// </summary>
    /// <remarks>
    /// Worth saying on the button. A list quietly hiding half its tasks because of a setting made
    /// last month — possibly on another client — is a list that looks like it lost them.
    /// </remarks>
    public bool IsFiltered
    {
        get => _isFiltered;
        private set => Set(ref _isFiltered, value);
    }

    /// <summary>Fetch the filter and sort choices for this list.</summary>
    public async Task LoadFiltersAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ListId))
        {
            return;
        }
        var response = await _core.CallAsync(Commands.FilterOptions(ListId), cancellationToken);
        if (!Handle(response))
        {
            return;
        }
        var options = response.Read<FilterOptions>();
        if (options is null)
        {
            return;
        }
        IsFiltered = options.IsFiltered;
        FilterGroups.Clear();
        foreach (var group in options.Groups)
        {
            FilterGroups.Add(group);
        }
    }

    /// <summary>Set one filter, and redraw the list it changes.</summary>
    public async Task<bool> SetFilterAsync(string field, string value,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(ListId))
        {
            return false;
        }
        var response = await _core.CallAsync(
            Commands.SetFilter(ListId, field, value), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        await RefreshAsync(cancellationToken);
        await LoadFiltersAsync(cancellationToken);
        return true;
    }

    /// <summary>Reload from the top. What a change notification and a pull-to-refresh both do.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        // A refresh while search results are on screen re-runs the search. Reloading the list
        // underneath would replace what somebody is reading with something they did not ask for,
        // every time a colleague touched anything.
        if (_isSearching)
        {
            await SearchAsync(SearchQuery, cancellationToken);
            return;
        }
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
                .CallAsync(Commands.RowsForList(ListId, 0, wanted), cancellationToken);
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
            var selectedId = Selected?.Id;
            Replace(window.Rows);
            // Keep the selection across a refresh, by id: the row object is replaced every time,
            // and a selection that vanishes whenever a colleague edits something in the same list
            // makes the keyboard unusable.
            Selected = selectedId is null
                ? null
                : Rows.FirstOrDefault(row => row.Id == selectedId);
        }
        finally
        {
            IsLoading = false;
            Raise(nameof(IsEmpty));
        }
    }

    /// <summary>
    /// The title of the task just added, as the core made it — after any <c>#list</c> tags came
    /// out — so the screen can say what happened (task 79d4604c). Null once the notice is gone.
    /// </summary>
    /// <remarks>
    /// The web answers a quick add with a toast, "Task created: … has been added". Here the box
    /// empties and the row appears, but a row among fifty is easy to miss and a person who is not
    /// sure the task went in adds it again. The row also carries a pending mark until the server
    /// has it; this is the word beside the box.
    /// </remarks>
    public string? LastCreatedTitle
    {
        get => _lastCreatedTitle;
        private set
        {
            if (Set(ref _lastCreatedTitle, value))
            {
                Raise(nameof(HasCreatedNotice));
            }
        }
    }

    public bool HasCreatedNotice => LastCreatedTitle is not null;

    /// <summary>Take the notice down; the shell does this a moment after it appears.</summary>
    public void ClearCreatedNotice() => LastCreatedTitle = null;

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
        // From the quick-add box, so the core may read `#list` tags out of it — or not, as the
        // account's smart parsing says (task 6ac2639a).
        var response = await _core
            .CallAsync(Commands.CreateTask(trimmed, listIds, quickAdd: true,
                // The reader's language decides which words are dates and priorities.
                locale: System.Globalization.CultureInfo.CurrentUICulture.Name), cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        LastCreatedTitle = response.Value.TryGetProperty("title", out var made)
                           && made.GetString() is { Length: > 0 } named
            ? named
            : trimmed;

        await RefreshAsync(cancellationToken);
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
        // The row flips before the core is asked, and flips back only if the core refuses. The
        // cache write is sub-millisecond, but the round trip to it and the re-read behind it
        // are not, and a checkbox that fills a beat after the click reads as a slow app.
        var before = Flip(taskId, row => row with { Completed = completed });
        var response = await _core
            .CallAsync(Commands.CompleteTask(taskId, completed), cancellationToken);
        if (!Handle(response))
        {
            Unflip(before);
            return false;
        }

        // The re-read is what makes the flip honest: a repeating task rolled forward rather than
        // finishing, and only the core knows what the row looks like now.
        await RefreshAsync(cancellationToken);
        return true;
    }

    /// <summary>Set a task's priority.</summary>
    public async Task<bool> SetPriorityAsync(string taskId, int priority,
        CancellationToken cancellationToken = default)
    {
        var before = Flip(taskId, row => row with { Priority = priority });
        var response = await _core.CallAsync(
            Commands.UpdateTask(taskId, new Dictionary<string, object?> { ["priority"] = priority }),
            cancellationToken);
        if (!Handle(response))
        {
            Unflip(before);
            return false;
        }
        await RefreshAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Change one row on screen, now, and hand back what it was so a refusal can put it back.
    /// </summary>
    /// <returns>The row as it was, with its position; null when the task is not on screen.</returns>
    private (int Index, TaskRow Row)? Flip(string taskId, Func<TaskRow, TaskRow> change)
    {
        for (var index = 0; index < Rows.Count; index++)
        {
            if (Rows[index].Id == taskId)
            {
                var before = Rows[index];
                Rows[index] = change(before);
                return (index, before);
            }
        }
        return null;
    }

    private void Unflip((int Index, TaskRow Row)? before)
    {
        if (before is { } was && was.Index < Rows.Count && Rows[was.Index].Id == was.Row.Id)
        {
            Rows[was.Index] = was.Row;
        }
    }

    /// <summary>
    /// Take a task's due date off.
    /// </summary>
    /// <remarks>
    /// An explicit null, which is what clears a field — an absent one would leave the date exactly
    /// where it was. See <c>astrid_core::services::TaskChanges</c>.
    /// </remarks>
    public async Task<bool> ClearDueDateAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.UpdateTask(taskId, new Dictionary<string, object?> { ["dueDateTime"] = null }),
            cancellationToken);
        if (!Handle(response))
        {
            return false;
        }
        await RefreshAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var response = await _core
            .CallAsync(Commands.DeleteTask(taskId), cancellationToken);
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
