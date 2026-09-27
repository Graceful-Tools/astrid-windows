using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// A project board: its columns, and the cards in each.
/// </summary>
/// <remarks>
/// <para>
/// It decides nothing. Which columns a board has, which one a card is in, and what moving a card
/// writes are all <c>astrid_core::board</c>, locked against astrid-web's own implementation by a
/// generated fixture — because a card in the wrong column looks like somebody moved it and a card
/// in no column looks like it was deleted, and neither raises an error on any client.
/// </para>
/// <para>
/// Cards arrive as rows, so a card draws with the same converters a list row does.
/// </para>
/// <para>
/// One card can be EXPANDED (task 91a25b8a). astrid-web's board opens a tapped card in place — the
/// task detail grows out of the card inside its column — rather than in the side panel the list
/// uses. What this class knows about that is only <em>where</em>: a slot after the expanded card
/// that the shell fills with its one detail pane. The detail's contents are the detail view
/// model's, in both views, so the two cannot drift.
/// </para>
/// </remarks>
public sealed class BoardViewModel : ObservableObject
{
    private readonly IAstridCore _core;
    /// <summary>
    /// What is half-typed in each column's add field, by column id (task 95c7a68f).
    /// </summary>
    /// <remarks>
    /// Held here rather than on <see cref="BoardColumnView"/>, which <see cref="Rebuild"/> builds
    /// fresh on every load: a draft kept on the view would be erased whenever anything refreshed
    /// the board, including another column's own create. astrid-web keeps the same dictionary for
    /// the same reason.
    /// </remarks>
    private readonly Dictionary<string, string> _drafts = [];
    private string _listId = string.Empty;
    private Board? _board;
    private bool _isLoading;
    private string? _errorMessage;
    private string? _expandedTaskId;

    public BoardViewModel(IAstridCore core)
    {
        _core = core;
    }

    /// <summary>The columns, in the order the board shows them.</summary>
    public ObservableCollection<BoardColumnView> Columns { get; } = [];

    /// <summary>Whether the open list belongs to a board at all.</summary>
    public bool HasBoard => _board?.ProjectId is not null;

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

    /// <summary>
    /// The card that is open in place, if one is.
    /// </summary>
    /// <remarks>
    /// The web's <c>expandedTaskId</c>. Null is the ordinary state: cards are closed, and the
    /// board is a board.
    /// </remarks>
    public string? ExpandedTaskId
    {
        get => _expandedTaskId;
        private set => Set(ref _expandedTaskId, value);
    }

    /// <summary>Load the board the given list belongs to, if it belongs to one.</summary>
    public async Task LoadAsync(string listId, CancellationToken cancellationToken = default)
    {
        _listId = listId;
        IsLoading = true;
        try
        {
            var response = await _core.CallAsync(Commands.Board(listId), cancellationToken);
            if (!response.Ok)
            {
                // A list with no board is not an error, and neither is one that has not synced yet.
                ErrorMessage = response.Error?.Kind == AstridFailureKind.Refused
                    ? response.Error.Message
                    : null;
                _board = null;
                Rebuild();
                return;
            }
            _board = response.Read<Board>();
            Rebuild();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Reload what is on screen.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        string.IsNullOrEmpty(_listId) ? Task.CompletedTask : LoadAsync(_listId, cancellationToken);

    /// <summary>Whether a card for this task is on the board as last loaded.</summary>
    public bool Has(string taskId) =>
        _board?.Columns.Any(column => column.Cards.Any(card => card.Id == taskId)) == true;

    /// <summary>Open a card in place. Only one is ever open; this one replaces the last.</summary>
    public void Expand(string taskId)
    {
        if (ExpandedTaskId == taskId)
        {
            return;
        }
        ExpandedTaskId = taskId;
        Rebuild();
    }

    /// <summary>Close the open card, if one is open.</summary>
    public void Collapse()
    {
        if (ExpandedTaskId is null)
        {
            return;
        }
        ExpandedTaskId = null;
        Rebuild();
    }

    /// <summary>Move a card to a column.</summary>
    /// <remarks>
    /// The whole board is reloaded rather than the card moved in place: a move can change more than
    /// one column — dropping a repeating card on Done rolls it forward and leaves it in the column
    /// it came from — and a view that moved the card itself would have to know that.
    /// </remarks>
    public async Task<bool> MoveAsync(string taskId, string columnId,
        CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.MoveTaskToColumn(taskId, columnId, _listId), cancellationToken);
        if (!response.Ok)
        {
            // Offline is not a failure: the move is in the Outbox and the board already shows it.
            ErrorMessage = response.IsStillPending ? null : response.Error?.Message;
        }
        await RefreshAsync(cancellationToken);
        return response.Ok;
    }

    /// <summary>What is half-typed in a column's add field.</summary>
    internal string DraftFor(string columnId) =>
        _drafts.TryGetValue(columnId, out var draft) ? draft : string.Empty;

    /// <summary>Remember what is being typed into a column's add field.</summary>
    internal void SetDraft(string columnId, string draft) => _drafts[columnId] = draft;

    /// <summary>
    /// Add a card at the bottom of a column (task 95c7a68f).
    /// </summary>
    /// <remarks>
    /// <para>
    /// What the card is made of — which lists it is filed in, which status it carries, and whether
    /// Done means it is already finished — is the core's decision, so this sends a column id and a
    /// title and nothing else. The card is born in the column rather than created and then moved:
    /// the Mac dropped the role in between and every card appeared in the Inbox (AITD-328).
    /// </para>
    /// <para>
    /// The field is cleared BEFORE the write, as the Mac's <c>addCard(to:)</c> does. Somebody types
    /// the next card while the last one is still in flight, and a field cleared afterwards would
    /// swallow what they typed.
    /// </para>
    /// </remarks>
    public async Task<bool> AddCardAsync(string columnId, CancellationToken cancellationToken = default)
    {
        var title = DraftFor(columnId).Trim();
        if (title.Length == 0)
        {
            return false;
        }
        _drafts[columnId] = string.Empty;

        var response = await _core.CallAsync(
            Commands.AddBoardCard(_listId, columnId, title), cancellationToken);
        if (!response.Ok)
        {
            // Offline is not a failure: the card is in the Outbox and the board already shows it.
            ErrorMessage = response.IsStillPending ? null : response.Error?.Message;
        }
        await RefreshAsync(cancellationToken);
        return response.Ok;
    }

    /// <summary>
    /// Draw the columns from the last board that arrived, with the slot after the expanded card.
    /// </summary>
    /// <remarks>
    /// The slot is placed from scratch on every rebuild rather than kept in a column, which is
    /// what lets a refresh move it: the expanded task is dragged to another column, or a sync
    /// reorders the cards, and the slot is simply wherever the card now is. A card that is no
    /// longer on the board collapses — the web does the same (<c>expandedTaskId</c> is cleared
    /// when no board task carries it), because a detail drawn under nothing is a detail drawn
    /// nowhere.
    /// </remarks>
    private void Rebuild()
    {
        var columns = _board?.Columns ?? [];
        if (ExpandedTaskId is { } expanded
            && !columns.Any(column => column.Cards.Any(card => card.Id == expanded)))
        {
            // Set directly rather than through Collapse(), which would rebuild a second time.
            ExpandedTaskId = null;
        }

        Columns.Clear();
        foreach (var column in columns)
        {
            Columns.Add(new BoardColumnView(column, ExpandedTaskId, this));
        }
        Raise(nameof(HasBoard));
    }
}

/// <summary>
/// One column as the board draws it: the column the core described, plus where the expanded
/// card's detail goes.
/// </summary>
public sealed class BoardColumnView
{
    private readonly BoardColumn _column;
    private readonly BoardViewModel _board;

    public BoardColumnView(BoardColumn column, string? expandedTaskId, BoardViewModel board)
    {
        _column = column;
        _board = board;
        var items = new List<object>(column.Cards.Count + 1);
        foreach (var card in column.Cards)
        {
            items.Add(card);
            if (card.Id == expandedTaskId)
            {
                items.Add(new InlineDetailSlot(card.Id));
                HoldsExpandedCard = true;
            }
        }
        Items = items;
    }

    /// <summary>A status role, or one of the two virtual ids for Inbox and Done.</summary>
    public string Id => _column.Id;

    public string Name => _column.Name;

    public string Description => _column.Description;

    /// <summary><c>inbox</c>, <c>status</c> or <c>done</c>.</summary>
    public string Kind => _column.Kind;

    /// <summary>How many cards the column holds, not how many crossed the boundary.</summary>
    public int Total => _column.Total;

    /// <summary>The cards, as the core sent them.</summary>
    public IReadOnlyList<TaskRow> Cards => _column.Cards;

    /// <summary>
    /// What the column draws, top to bottom: every card, and the detail slot right after the
    /// expanded one.
    /// </summary>
    public IReadOnlyList<object> Items { get; }

    /// <summary>
    /// Whether the expanded card is in this column — the one that grows to make room for its
    /// detail, as the web's column does.
    /// </summary>
    public bool HoldsExpandedCard { get; }

    /// <summary>
    /// What is half-typed in this column's add field (task 95c7a68f).
    /// </summary>
    /// <remarks>
    /// Reads and writes through to the board, which is what survives a reload: a column view is
    /// rebuilt on every load, so a draft held in this object would be lost whenever another
    /// column's create refreshed the board.
    /// </remarks>
    public string Draft
    {
        get => _board.DraftFor(Id);
        set => _board.SetDraft(Id, value);
    }

    /// <summary>Add whatever is in this column's field as a card in this column.</summary>
    public Task<bool> AddCardAsync() => _board.AddCardAsync(Id);
}

/// <summary>
/// Where the expanded card's detail is drawn: the item right after that card in its column.
/// </summary>
/// <remarks>
/// A place, not a view. The shell puts its one task-detail pane here, so the board shows the same
/// detail the list does — fields, thread, comment box — without a second copy of any of it.
/// </remarks>
public sealed record InlineDetailSlot(string TaskId);
