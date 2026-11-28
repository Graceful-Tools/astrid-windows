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
    /// <summary>A move into Waiting that is waiting on a date or a task (task 7462618e).</summary>
    private readonly PendingWait _pendingWait;
    private string _listId = string.Empty;
    private Board? _board;
    private bool _isLoading;
    private string? _errorMessage;
    private string? _expandedTaskId;
    private string? _goalsListId;

    public BoardViewModel(IAstridCore core)
    {
        _core = core;
        _pendingWait = new PendingWait(() => Raise(nameof(WaitPrompt)));
        Goals = new GoalsViewModel(core);
    }

    /// <summary>The board's goals and guardrails, above its columns (task ec11e539).</summary>
    public GoalsViewModel Goals { get; }

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

    /// <summary>
    /// The condition the shell must ask for before this move into Waiting can happen, if one is
    /// outstanding (task 7462618e).
    /// </summary>
    /// <remarks>
    /// Not an error: nothing was written, and the card never left its column. The view draws the
    /// "waiting until… / waiting on…" picker while this is set, and answers with
    /// <see cref="ResolveWaitAsync"/> or <see cref="CancelWaitPrompt"/>.
    /// </remarks>
    public WaitPrompt? WaitPrompt => _pendingWait.Prompt;

    /// <summary>The list this board was last loaded for, or empty if it has not been.</summary>
    /// <remarks>
    /// Read by the shell to decide whether a <c>task</c> change is worth a reload at all (task
    /// 05c95986): a change in some other list leaves this board exactly as it was drawn.
    /// </remarks>
    public string ListId => _listId;

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
                Goals.Clear();
                _goalsListId = null;
                return;
            }
            _board = response.Read<Board>();
            Rebuild();
            // Only a list on a board has goals to show, and they are read once per list opened:
            // a refresh after every drag is not a reason to ask again. A goals change re-reads them
            // through the shell instead.
            if (!HasBoard)
            {
                Goals.Clear();
                _goalsListId = null;
            }
            else if (_goalsListId != listId)
            {
                _goalsListId = listId;
                await Goals.LoadForListAsync(listId, cancellationToken);
            }
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
    /// <param name="wait">
    /// The wait condition, when this is the second attempt and the user has answered for one. A
    /// first attempt sends none: whether the column needs one is the core's decision, not a
    /// predicate this class evaluates against a column name (task 7462618e).
    /// </param>
    public async Task<bool> MoveAsync(string taskId, string columnId,
        WaitCondition? wait = null, CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.MoveTaskToColumn(taskId, columnId, _listId, wait), cancellationToken);
        if (response.NeedsWaitCondition)
        {
            // Nothing was written, so the card is still where it was. Ask, rather than report.
            _pendingWait.Ask(
                new WaitPrompt(columnId, NameOf(columnId), taskId, null),
                (condition, token) => MoveAsync(taskId, columnId, condition, token));
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

    /// <summary>
    /// Answer an outstanding <see cref="WaitPrompt"/>, which sends the refused move again.
    /// </summary>
    /// <returns>
    /// <c>false</c> when there was nothing to answer, when the answer named neither a date nor a
    /// task, or when the core refused it as well — in the last case the prompt is up again.
    /// </returns>
    public Task<bool> ResolveWaitAsync(WaitCondition condition,
        CancellationToken cancellationToken = default) =>
        _pendingWait.ResolveAsync(condition, cancellationToken);

    /// <summary>Drop the prompt and leave the task where it was.</summary>
    public void CancelWaitPrompt() => _pendingWait.Cancel();

    /// <summary>
    /// A column's name as the core last gave it, for the prompt's title.
    /// </summary>
    /// <remarks>
    /// The id is the fallback rather than a hardcoded "Waiting": the column is whatever this
    /// board calls it, and only a stale board would fail to name it.
    /// </remarks>
    private string NameOf(string columnId) =>
        _board?.Columns.FirstOrDefault(column => column.Id == columnId)?.Name ?? columnId;

    /// <summary>
    /// Complete a card's task, or bring it back (task 6979e694).
    /// </summary>
    /// <remarks>
    /// Always the complete command, never an update carrying <c>completed</c>: a repeating task
    /// rolls forward rather than finishing, and only that path does it — see
    /// <c>astrid_core::services::task</c>. The board is re-read afterwards for the same reason a
    /// move re-reads it: a rolled-forward task is a different card now, with a new date and
    /// possibly a different column, and only the core knows what it looks like.
    /// </remarks>
    /// <summary>The picker a card's mark opens, which is always what a card's mark does.</summary>
    public LeadingPickerViewModel LeadingPicker => _leadingPicker ??=
        new LeadingPickerViewModel(_core, RefreshAsync);

    private LeadingPickerViewModel? _leadingPicker;

    /// <summary>
    /// A card's mark, tapped (task 3c0dfb73).
    /// </summary>
    /// <remarks>
    /// A board card always opens the picker, in both display modes: a board is where a task has a
    /// status, so the control is how you set it. Completing outright from a card is the trapdoor
    /// tasks <c>9be8cb1b</c> and <c>f9d7ed42</c> closed on Apple — one tap that reads as "pick this
    /// one" finished the task instead, and Windows had it the whole time. The surface still asks
    /// rather than assuming, because the answer is the core's and a copy-only card is a card too.
    /// </remarks>
    public async Task<LeadingOutcome> ActivateLeadingAsync(string taskId,
        CancellationToken cancellationToken = default)
    {
        var card = Columns
            .SelectMany(column => column.Cards)
            .FirstOrDefault(candidate => candidate.Id == taskId);
        if (card is null)
        {
            return LeadingOutcome.Gone;
        }
        if (card.IsCopyOnly)
        {
            await CopyAsync(taskId, cancellationToken);
            return LeadingOutcome.Copied;
        }
        if (card.OpensPicker)
        {
            await LeadingPicker.OpenAsync(card, cancellationToken);
            return LeadingOutcome.OpenPicker;
        }
        await SetCompletedAsync(taskId, completed: !card.Completed, cancellationToken);
        return LeadingOutcome.Completed;
    }

    public async Task<bool> SetCompletedAsync(string taskId, bool completed,
        CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.CompleteTask(taskId, completed), cancellationToken);
        if (!response.Ok)
        {
            // Offline is not a failure, as with a move: the write is in the Outbox.
            ErrorMessage = response.IsStillPending ? null : response.Error?.Message;
        }
        await RefreshAsync(cancellationToken);
        return response.Ok;
    }

    /// <summary>
    /// Copy a card's task into this account's own tasks (task 6979e694).
    /// </summary>
    /// <remarks>
    /// The fourth thing a card's mark can be: a task in a public list the reader cannot edit draws
    /// the web's copy control, and the tap copies rather than completes. The board is re-read so
    /// the copy appears if it belongs in one of these columns.
    /// </remarks>
    public async Task<bool> CopyAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.CopyTask(taskId, targetListId: null, includeComments: false), cancellationToken);
        if (!response.Ok)
        {
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
    /// What a column's add field will make: the priority and assignee its pickers chose, and the
    /// preview the core drew from them (task 71ad20a0).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Held on the board rather than on the column objects, for the reason the drafts are: a
    /// column view is rebuilt on every load, so a pick kept in one would be lost the moment
    /// another column's create refreshed the board.
    /// </para>
    /// <para>
    /// Per column, not per board. Each column has its own field, so each has its own half-made
    /// card — the same reason the drafts are keyed this way.
    /// </para>
    /// </remarks>
    private readonly Dictionary<string, QuickAddDefaults> _quickAdd = [];
    private readonly Dictionary<string, int?> _priorityPicks = [];
    private readonly Dictionary<string, string?> _assigneePicks = [];

    internal QuickAddDefaults? QuickAddFor(string columnId) =>
        _quickAdd.TryGetValue(columnId, out var defaults) ? defaults : null;

    /// <summary>
    /// Ask the core what a card born in this column would look like, after every pick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same question the list's field asks, with the board's own list id: a card typed into a
    /// column belongs to the board's list, and the column decides its status rather than its list.
    /// So no core change was needed for the preview — only for carrying the two values.
    /// </para>
    /// <para>
    /// Re-asked rather than adjusted here, because which of the three leading states a pick
    /// produces is the same rule that produced the first answer. A failure leaves the last answer
    /// standing and raises nothing: the field still adds cards without its preview, and an error
    /// banner over a decoration would be worse than the missing decoration.
    /// </para>
    /// </remarks>
    internal async Task LoadQuickAddDefaultsAsync(string columnId,
        CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.QuickAddDefaults(
                string.IsNullOrEmpty(_listId) ? null : _listId,
                _assigneePicks.TryGetValue(columnId, out var who) ? who : null,
                _priorityPicks.TryGetValue(columnId, out var priority) ? priority : null),
            cancellationToken);
        if (!response.Ok || response.Read<QuickAddDefaults>() is not { } defaults)
        {
            return;
        }
        _quickAdd[columnId] = defaults;
        Raise(nameof(Columns));
    }

    /// <summary>Choose the priority the next card in this column is born with.</summary>
    internal Task PickQuickAddPriorityAsync(string columnId, int priority,
        CancellationToken cancellationToken = default)
    {
        _priorityPicks[columnId] = priority;
        return LoadQuickAddDefaultsAsync(columnId, cancellationToken);
    }

    /// <summary>
    /// Choose who the next card goes to. <paramref name="userId"/> is null for the unassigned row.
    /// </summary>
    /// <remarks>
    /// Nobody is sent as <c>"unassigned"</c>, not as a null — a null means "nothing chosen" and
    /// the list's own default would fill it back in, so choosing nobody would do nothing at all in
    /// exactly the lists where it matters. The same trap the list's field documents.
    /// </remarks>
    internal Task PickQuickAddAssigneeAsync(string columnId, string? userId,
        CancellationToken cancellationToken = default)
    {
        _assigneePicks[columnId] = userId ?? "unassigned";
        return LoadQuickAddDefaultsAsync(columnId, cancellationToken);
    }

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
    public Task<bool> AddCardAsync(string columnId, CancellationToken cancellationToken = default)
    {
        var title = DraftFor(columnId).Trim();
        if (title.Length == 0)
        {
            return Task.FromResult(false);
        }
        _drafts[columnId] = string.Empty;
        return AddCardAsync(columnId, title, null, cancellationToken);

    }

    /// <summary>
    /// Create the card, with a wait condition when the column turned out to need one.
    /// </summary>
    /// <remarks>
    /// The title is a parameter rather than re-read from the draft: the field was cleared before
    /// the first attempt, so a retry that read it again would create an empty card.
    /// </remarks>
    private async Task<bool> AddCardAsync(string columnId, string title, WaitCondition? wait,
        CancellationToken cancellationToken)
    {
        var response = await _core.CallAsync(
            Commands.AddBoardCard(
                _listId,
                columnId,
                title,
                wait,
                _priorityPicks.TryGetValue(columnId, out var priority) ? priority : null,
                _assigneePicks.TryGetValue(columnId, out var who) ? who : null),
            cancellationToken);
        if (response.NeedsWaitCondition)
        {
            // A card born in Waiting is a task entering Waiting, so the same rule and the same ask.
            _pendingWait.Ask(
                new WaitPrompt(columnId, NameOf(columnId), null, title),
                (condition, token) => AddCardAsync(columnId, title, condition, token));
            return false;
        }
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
        // Done's cards carry the day they were finished, as a pill under the title (Jon,
        // 2026-11-19). Copied onto the card here because only the column knows it is Done; their
        // ORDER — newest finished first — is the core's, and is not touched.
        Cards = CompletedDayPill.OnCards(column);
        // The expanded card's detail is NOT in this list any more (task eb2684bb). It used to be:
        // an `InlineDetailSlot` took the card's own place and the column grew wider to hold it,
        // which meant detail arrived as another card-shaped block pushed in between cards. The
        // board opens a callout anchored to the card now, as the Mac and the iPad do, so the cards
        // are just the cards and nothing moves when one is opened.
        ExpandedTaskId = expandedTaskId;
    }

    /// <summary>A status role, or one of the two virtual ids for Inbox and Done.</summary>
    public string Id => _column.Id;

    public string Name => _column.Name;

    public string Description => _column.Description;

    /// <summary><c>inbox</c>, <c>status</c> or <c>done</c>.</summary>
    public string Kind => _column.Kind;

    /// <summary>What this column says when it holds no cards: a key, which the view puts into words (task 4ac294ab).</summary>
    /// <remarks>
    /// The core's board answer carries it on every column since astrid-core ADEV-347 (the rule's
    /// column type holds it as a non-optional field), so it is passed through as sent — no
    /// fallback of the shell's own (task b9893539).
    /// </remarks>
    public string EmptyMessageKey => _column.EmptyMessageKey;

    /// <summary>
    /// The local empathy frame beside the empty bubble, or null — the view then keeps the brand
    /// mark (task b9893539).
    /// </summary>
    public string? EmptyFramePath => _column.EmptyFramePath;

    /// <summary>How many cards the column holds, not how many crossed the boundary.</summary>
    public int Total => _column.Total;

    /// <summary>The cards, as the core sent them and in its order; Done's with their pill.</summary>
    public IReadOnlyList<TaskRow> Cards { get; }

    /// <summary>
    /// Which card in this column is open, if any — what the callout is anchored to (task eb2684bb).
    /// </summary>
    /// <remarks>
    /// The id rather than a flag, because the view has to find that card's element to measure it:
    /// where a row sits on screen is geometry, and the core's placement rule is given the rectangle
    /// the shell measured. A column with nothing open answers null.
    /// </remarks>
    public string? ExpandedTaskId { get; }

    /// <summary>Whether the open card is in this column at all.</summary>
    public bool HoldsExpandedCard =>
        ExpandedTaskId is not null && Cards.Any(card => card.Id == ExpandedTaskId);

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

    // ── What this column's add field will make (task 71ad20a0) ──────────────────────────────
    //
    // Read through to the board for the same reason `Draft` is: a column object does not survive
    // a reload, and these have to.

    private QuickAddDefaults? Defaults => _board.QuickAddFor(Id);

    /// <summary>The priority the next card here is born with.</summary>
    public int QuickAddPriority => Defaults?.Priority ?? 0;

    /// <summary>
    /// Whose mark the preview draws, when it is a square rather than a checkbox: the chosen
    /// assignee's glyph, read off the options the core sent with them.
    /// </summary>
    /// <remarks>
    /// An em dash for nobody, the same fallback the list's field uses — an empty square reads as
    /// a control that failed to draw rather than as a task nobody owns.
    /// </remarks>
    public string QuickAddMark => QuickAddAssignees
        .FirstOrDefault(option => option.UserId == Defaults?.AssigneeId)?.Glyph ?? "—";

    /// <summary>The people this column's card can go to.</summary>
    public IReadOnlyList<AssigneeOption> QuickAddAssignees => Defaults?.Options ?? [];

    /// <summary>Whether the preview is a checkbox — the card would be the reader's own.</summary>
    public bool QuickAddIsCheckbox => Defaults?.Leading.Kind is null or "checkbox";

    /// <summary>Or a priority-coloured square carrying somebody's mark.</summary>
    public bool QuickAddIsSquare => !QuickAddIsCheckbox;

    /// <summary>Ask the core what a card born here would look like.</summary>
    public Task LoadQuickAddDefaultsAsync() => _board.LoadQuickAddDefaultsAsync(Id);

    /// <summary>Choose the priority the next card here is born with.</summary>
    public Task PickQuickAddPriorityAsync(int priority) =>
        _board.PickQuickAddPriorityAsync(Id, priority);

    /// <summary>Choose who it goes to; null is the unassigned row.</summary>
    public Task PickQuickAddAssigneeAsync(string? userId) =>
        _board.PickQuickAddAssigneeAsync(Id, userId);
}


