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
/// </remarks>
public sealed class BoardViewModel : ObservableObject
{
    private readonly IAstridCore _core;
    private string _listId = string.Empty;
    private string? _projectId;
    private bool _isLoading;
    private string? _errorMessage;

    public BoardViewModel(IAstridCore core)
    {
        _core = core;
    }

    /// <summary>The columns, in the order the board shows them.</summary>
    public ObservableCollection<BoardColumn> Columns { get; } = [];

    /// <summary>Whether the open list belongs to a board at all.</summary>
    public bool HasBoard => _projectId is not null;

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
                _projectId = null;
                Columns.Clear();
                Raise(nameof(HasBoard));
                return;
            }
            var board = response.Read<Board>();
            _projectId = board?.ProjectId;
            Replace(Columns, board?.Columns ?? []);
            Raise(nameof(HasBoard));
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Reload what is on screen.</summary>
    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        string.IsNullOrEmpty(_listId) ? Task.CompletedTask : LoadAsync(_listId, cancellationToken);

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

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
