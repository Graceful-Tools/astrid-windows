using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The multi-list project board: each chosen list is a ROW — a swimlane — and the project's
/// status columns stay the columns (task f8b844dd; core AWTD-1676, web AWTD-1660).
/// </summary>
/// <remarks>
/// <para>
/// A different layout from <see cref="BoardViewModel"/> rather than a variation of it. That one
/// draws one list's cards under its project's columns; this draws several lists' cards under one
/// project's columns, one row each, and has a second thing to persist on top: which lists are
/// rows at all.
/// </para>
/// <para>
/// It decides none of it. Which lists are rows, in what order, which lists could be added, which
/// column a card falls in and whether another row would fit are all <c>astrid_rules::board_rows</c>
/// — locked there against astrid-web's own implementation by the <c>board-rows.json</c> fixture,
/// because a swimlane that disagrees between two clients is a board that lost somebody's work
/// without raising anything.
/// </para>
/// <para>
/// <strong>Nothing saved is not an empty set.</strong> The core is careful to distinguish the two
/// and this keeps both: <see cref="ProjectBoardRowsAnswer.SavedListIds"/> is <c>null</c> until
/// somebody chooses rows, which means the board shows the project's own lists; an empty set is a
/// board emptied on purpose. Both draw no swimlanes and they are not the same board, so they do
/// not say the same thing — see <see cref="ShowsNoRowsNotice"/> and
/// <see cref="ShowsEmptiedNotice"/>.
/// </para>
/// </remarks>
public sealed class ProjectBoardViewModel : ObservableObject
{
    private readonly IAstridCore _core;
    /// <summary>A move into Waiting that is waiting on a date or a task.</summary>
    private readonly PendingWait _pendingWait;
    /// <summary>
    /// The lists as last cached, by id — what names a row header and the "Add list" menu.
    /// </summary>
    /// <remarks>
    /// The core answers in ids, because which lists are rows is a rule about lists and not about
    /// their names. Turning an id into a word is rendering, so it happens here; and a row whose
    /// list is not in the cache keeps its id as its name rather than disappearing, because the
    /// core said to draw it.
    /// </remarks>
    private readonly Dictionary<string, ListSummary> _lists = new(StringComparer.Ordinal);
    private string _projectId = string.Empty;
    private ProjectBoardRowsAnswer? _answer;
    private bool _isLoading;
    private string? _errorMessage;

    public ProjectBoardViewModel(IAstridCore core)
    {
        _core = core;
        _pendingWait = new PendingWait(() => Raise(nameof(WaitPrompt)));
        Goals = new GoalsViewModel(core);
    }

    /// <summary>The board's goals and guardrails, above its columns (task ec11e539).</summary>
    public GoalsViewModel Goals { get; }

    /// <summary>The rows, in the order the core gave them.</summary>
    public ObservableCollection<ProjectBoardRowView> Rows { get; } = [];

    /// <summary>
    /// The project's columns, for the one header strip above every row.
    /// </summary>
    /// <remarks>
    /// Taken from the rows rather than asked for separately: every row answers under the same
    /// columns — that is what makes this a board and not several — so the first row's are the
    /// board's. A board with no rows has no strip to draw, and says why it is empty instead.
    /// </remarks>
    public IReadOnlyList<BoardColumn> Columns { get; private set; } = [];

    /// <summary>The project this board was last loaded for, or empty if it has not been.</summary>
    public string ProjectId => _projectId;

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    /// <summary>Whether the board on screen is the last answer rather than the server's.</summary>
    public bool IsFromCache => _answer?.FromCache == true;

    /// <summary>Whether another row would fit — the core's hundred-row ceiling.</summary>
    /// <remarks>
    /// The shell stops offering rather than offering a control whose only outcome is a refusal,
    /// the same trade the board toggle makes on a list with no board.
    /// </remarks>
    public bool CanAddRow => _answer?.CanAddRow == true && AddListCandidates.Count > 0;

    /// <summary>What "Add list" offers, in the order the core put them in.</summary>
    /// <remarks>
    /// Not re-sorted here. The core sorts by name with case folded, so that four clients offer one
    /// order; a shell that sorted again would be a second rule about it, and would disagree with
    /// the others on exactly the names that are hard — "iOS" among the I's rather than after every
    /// capital.
    /// </remarks>
    public IReadOnlyList<ListSummary> AddListCandidates { get; private set; } = [];

    /// <summary>
    /// Whether this board was emptied on purpose, as against never set up.
    /// </summary>
    /// <remarks>
    /// An empty <c>savedListIds</c>, not an absent one. The core goes out of its way to tell the
    /// two apart — an emptied board stays empty rather than falling back to the project's lists —
    /// and this is where that distinction has to survive, because it is the only thing that makes
    /// the two empty boards read differently.
    /// </remarks>
    public bool IsEmptiedOnPurpose => _answer?.SavedListIds is { Count: 0 };

    /// <summary>Whether to say that this board has no rows yet.</summary>
    public bool ShowsNoRowsNotice =>
        Rows.Count == 0 && !IsEmptiedOnPurpose && ErrorMessage is null && _answer is not null;

    /// <summary>Whether to say that this board was emptied, and how to put a list back.</summary>
    public bool ShowsEmptiedNotice =>
        Rows.Count == 0 && IsEmptiedOnPurpose && ErrorMessage is null;

    /// <summary>
    /// The condition the shell must ask for before a move into Waiting can happen, if one is
    /// outstanding.
    /// </summary>
    public WaitPrompt? WaitPrompt => _pendingWait.Prompt;

    /// <summary>Open a project's board: which lists are rows, then each row's cards.</summary>
    public async Task LoadAsync(string projectId, CancellationToken cancellationToken = default)
    {
        _projectId = projectId;
        IsLoading = true;
        try
        {
            await LoadListNamesAsync(cancellationToken);
            await ReloadRowsAsync(cancellationToken);
            await Goals.LoadForProjectAsync(projectId, cancellationToken);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Reload what is on screen: the rows, and the cards in them.
    /// </summary>
    /// <remarks>
    /// The list names are NOT re-read. They are needed to turn the core's ids into words and they
    /// do not change when a card moves, so a refresh after every drag would be a second round trip
    /// for nothing. Opening the board reads them.
    /// </remarks>
    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        string.IsNullOrEmpty(_projectId)
            ? Task.CompletedTask
            : ReloadRowsAsync(cancellationToken);

    /// <summary>Whether a card for this task is on the board as last loaded.</summary>
    public bool Has(string taskId) => Rows
        .Any(row => row.Columns.Any(column => column.Cards.Any(card => card.Id == taskId)));

    /// <summary>Add a list as a row, at the end.</summary>
    /// <remarks>
    /// Sends the one list and lets the core do the arithmetic against the rows it last drew, which
    /// is what the command is shaped for: reading the set, appending here and writing it back would
    /// drop a row somebody added on another device in between.
    /// </remarks>
    public Task<bool> AddRowAsync(string listId, CancellationToken cancellationToken = default) =>
        SaveRowsAsync(Commands.SaveProjectBoardRows(_projectId, addListId: listId),
            cancellationToken);

    /// <summary>Take a row off the board — the cross on its header.</summary>
    /// <remarks>
    /// The list and its tasks are untouched: which lists this person's board draws is a view
    /// preference, saved per person per project, and removing a row is not removing anything from
    /// the project.
    /// </remarks>
    public Task<bool> RemoveRowAsync(string listId, CancellationToken cancellationToken = default) =>
        SaveRowsAsync(Commands.SaveProjectBoardRows(_projectId, removeListId: listId),
            cancellationToken);

    /// <summary>
    /// Save the whole ordered set of rows — including an empty one, which empties the board.
    /// </summary>
    /// <remarks>
    /// An empty set is sent as <c>[]</c>: the command's serializer drops nulls, so an empty list
    /// is the only way to say "no rows" rather than "I am not naming the rows in this write".
    /// </remarks>
    public Task<bool> SetRowsAsync(IReadOnlyList<string> listIds,
        CancellationToken cancellationToken = default) =>
        SaveRowsAsync(Commands.SaveProjectBoardRows(_projectId, listIds: listIds),
            cancellationToken);

    /// <summary>
    /// Move a card to another column of the row it is in.
    /// </summary>
    /// <param name="rowListId">The row the card was dragged on, which is the list it is in.</param>
    /// <remarks>
    /// The whole board is reloaded rather than the card moved in place, for the reason the
    /// single-list board reloads: a move can change more than one column — a repeating card dropped
    /// on Done rolls forward and stays where it was — and a card on two row lists is a card in two
    /// rows, so one move can redraw two swimlanes.
    /// </remarks>
    public async Task<bool> MoveAsync(string taskId, string columnId, string rowListId,
        WaitCondition? wait = null, CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.MoveTaskToColumn(taskId, columnId, ColumnContextListId(rowListId), wait),
            cancellationToken);
        if (response.NeedsWaitCondition)
        {
            // Nothing was written, so the card is still where it was. Ask, rather than report.
            _pendingWait.Ask(
                new WaitPrompt(columnId, NameOf(columnId), taskId, null),
                (condition, token) => MoveAsync(taskId, columnId, rowListId, condition, token));
            return false;
        }
        if (!response.Ok)
        {
            // Offline is not a failure: the move is in the Outbox and the board already shows it.
            ErrorMessage = response.IsStillPending ? null : response.Error?.Message;
        }
        await RefreshAsync(cancellationToken);
        return response.Ok;
    }

    /// <summary>Answer an outstanding <see cref="WaitPrompt"/>, which sends the move again.</summary>
    public Task<bool> ResolveWaitAsync(WaitCondition condition,
        CancellationToken cancellationToken = default) =>
        _pendingWait.ResolveAsync(condition, cancellationToken);

    /// <summary>Drop the prompt and leave the card where it was.</summary>
    public void CancelWaitPrompt() => _pendingWait.Cancel();

    /// <summary>
    /// Which list id a column move should name, for a card dragged on <paramref name="rowListId"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The core resolves a move's target against the columns of the project the given list belongs
    /// to — that is the documented purpose of the id, and the reason the single-list board sends
    /// the list it was opened from. This board was opened from a PROJECT, and a row's list is
    /// allowed to belong to another one: the core draws a foreign list's cards under this project's
    /// columns on purpose (D67). Sending that foreign id would resolve the other project's columns,
    /// and the column being dropped on would not be among them.
    /// </para>
    /// <para>
    /// So the row's own list when it is this project's — which is every ordinary row — and
    /// otherwise any list of this project, because the id names the project and nothing else. Which
    /// list the card is filed in is not changed by a column move and is not what this is for.
    /// </para>
    /// </remarks>
    private string ColumnContextListId(string rowListId)
    {
        if (_lists.TryGetValue(rowListId, out var row) && row.ProjectId == _projectId)
        {
            return rowListId;
        }
        return _lists.Values.FirstOrDefault(list => list.ProjectId == _projectId)?.Id ?? rowListId;
    }

    /// <summary>A column's name as the core last gave it, for the wait prompt's title.</summary>
    private string NameOf(string columnId) =>
        Columns.FirstOrDefault(column => column.Id == columnId)?.Name ?? columnId;

    /// <summary>A row list's name, or its id when the lists have not caught up with it.</summary>
    internal string NameOfList(string listId) =>
        _lists.TryGetValue(listId, out var list) && !string.IsNullOrEmpty(list.Name)
            ? list.Name
            : listId;

    /// <summary>A row list's colour, for the dot on its header — the sidebar's own fallback.</summary>
    internal string ColorOfList(string listId) =>
        _lists.TryGetValue(listId, out var list) ? list.DisplayColor : "#3b82f6";

    /// <summary>Read the cached lists, which is what names the rows and the candidates.</summary>
    private async Task LoadListNamesAsync(CancellationToken cancellationToken)
    {
        var response = await _core.CallAsync(Commands.Lists(), cancellationToken);
        if (!response.Ok)
        {
            // Not an error worth a banner: the rows still draw, under their ids.
            return;
        }
        _lists.Clear();
        foreach (var list in response.ReadArray<ListSummary>())
        {
            _lists[list.Id] = list;
        }
    }

    /// <summary>Ask which lists are rows, then ask each row for its cards.</summary>
    private async Task ReloadRowsAsync(CancellationToken cancellationToken)
    {
        var response = await _core.CallAsync(
            Commands.ProjectBoardRows(_projectId), cancellationToken);
        if (!response.Ok || response.Read<ProjectBoardRowsAnswer>() is not { } answer)
        {
            ErrorMessage = response.Error?.Message;
            _answer = null;
            await RebuildAsync([], cancellationToken);
            return;
        }
        ErrorMessage = null;
        await AdoptAsync(answer, cancellationToken);
    }

    /// <summary>
    /// Take a rows answer — a read's or a write's — and draw the board it describes.
    /// </summary>
    /// <remarks>
    /// A write answers in the same shape as the read for exactly this reason: the board after a row
    /// is added is the core's answer, not this class's guess at what its own write did.
    /// </remarks>
    private async Task AdoptAsync(ProjectBoardRowsAnswer answer,
        CancellationToken cancellationToken)
    {
        _answer = answer;
        AddListCandidates = answer.CandidateListIds
            .Select(id => _lists.TryGetValue(id, out var list)
                ? list
                : new ListSummary { Id = id, Name = id })
            .ToList();
        await RebuildAsync(answer.RowListIds, cancellationToken);
    }

    /// <summary>Ask each row for its cards, then put the swimlanes on screen.</summary>
    private async Task RebuildAsync(IReadOnlyList<string> rowListIds,
        CancellationToken cancellationToken)
    {
        var drawn = new List<ProjectBoardRowView>(rowListIds.Count);
        foreach (var listId in rowListIds)
        {
            var response = await _core.CallAsync(
                Commands.ProjectBoardRow(_projectId, listId), cancellationToken);
            // A row the core would not answer for is drawn empty rather than dropped: the rows
            // answer said to draw it, and a swimlane that silently disappears reads as a list
            // somebody removed.
            var columns = response.Ok ? response.Read<Board>()?.Columns ?? [] : [];
            drawn.Add(new ProjectBoardRowView(this, listId, columns));
        }

        Rows.Clear();
        foreach (var row in drawn)
        {
            Rows.Add(row);
        }
        Columns = drawn.Count > 0 ? drawn[0].Source : [];
        Raise(nameof(Columns));
        Raise(nameof(CanAddRow));
        Raise(nameof(AddListCandidates));
        Raise(nameof(IsFromCache));
        Raise(nameof(IsEmptiedOnPurpose));
        Raise(nameof(ShowsNoRowsNotice));
        Raise(nameof(ShowsEmptiedNotice));
    }

    /// <summary>
    /// Send a rows write and draw whatever it answers.
    /// </summary>
    /// <remarks>
    /// Unlike a card move, a refusal here is reported and nothing is redrawn — the board is still
    /// the board it was, because the core refuses before the write leaves the device when it can
    /// (more than a hundred rows, an empty id) and the server refuses the rest. And unlike a card
    /// move, <em>offline is a failure worth saying</em>: this write is online-only in the core,
    /// exactly as it is on the web, because it is a view preference the server checks against what
    /// this person can see rather than task data the Outbox can carry. A board that drew rows which
    /// were never saved would forget them on the next load with nothing to explain it.
    /// </remarks>
    private async Task<bool> SaveRowsAsync(object command, CancellationToken cancellationToken)
    {
        var response = await _core.CallAsync(command, cancellationToken);
        if (!response.Ok || response.Read<ProjectBoardRowsAnswer>() is not { } answer)
        {
            ErrorMessage = response.Error?.Message;
            return false;
        }
        ErrorMessage = null;
        await AdoptAsync(answer, cancellationToken);
        return true;
    }
}

/// <summary>
/// One row of a project board: a list, and its cards under the project's columns.
/// </summary>
public sealed class ProjectBoardRowView
{
    private readonly ProjectBoardViewModel _board;

    internal ProjectBoardRowView(ProjectBoardViewModel board, string listId,
        IReadOnlyList<BoardColumn> columns)
    {
        _board = board;
        ListId = listId;
        Source = columns;
        Columns = columns.Select(column => new ProjectBoardColumnView(listId, column)).ToList();
    }

    /// <summary>The list this row draws.</summary>
    public string ListId { get; }

    /// <summary>Its name, or its id when the cached lists do not have it.</summary>
    public string Name => _board.NameOfList(ListId);

    /// <summary>The dot on the row header, as the sidebar draws a list's.</summary>
    public string Color => _board.ColorOfList(ListId);

    /// <summary>The columns as the core described them, for the board's header strip.</summary>
    internal IReadOnlyList<BoardColumn> Source { get; }

    /// <summary>This row's cells, left to right.</summary>
    public IReadOnlyList<ProjectBoardColumnView> Columns { get; }

    /// <summary>Take this row off the board. The list itself is untouched.</summary>
    public Task<bool> RemoveAsync() => _board.RemoveRowAsync(ListId);
}

/// <summary>
/// One cell of a project board: the cards of one row's list that are in one of the project's
/// columns.
/// </summary>
/// <remarks>
/// It carries the row's list id as well as the column's, because a drop needs both — which column
/// it landed in, and which row it was dragged on. The single-list board can keep the column id in
/// a <c>Tag</c>; two ids do not fit in one.
/// </remarks>
public sealed class ProjectBoardColumnView
{
    private readonly BoardColumn _column;

    internal ProjectBoardColumnView(string rowListId, BoardColumn column)
    {
        RowListId = rowListId;
        _column = column;
        // Done's cards carry their finished-on pill here too, as on the single-list board and on
        // every other platform's swimlanes (Jon, 2026-11-19). The order stays the core's.
        Cards = CompletedDayPill.OnCards(column);
    }

    /// <summary>The list of the row this cell is in.</summary>
    public string RowListId { get; }

    /// <summary>A status role, or one of the two virtual ids for Inbox and Done.</summary>
    public string Id => _column.Id;

    public string Name => _column.Name;

    /// <summary><c>inbox</c>, <c>status</c> or <c>done</c>.</summary>
    public string Kind => _column.Kind;

    /// <summary>How many of this row's cards the column holds, not how many were carried.</summary>
    public int Total => _column.Total;

    /// <summary>The cards, as the core sent them and in its order; Done's with their pill.</summary>
    public IReadOnlyList<TaskRow> Cards { get; }
}
