using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The public lists anybody may browse and copy (task f6bc59e8), as the web's public-lists
/// browser shows them: most copied first, with whose they are and what is in them.
/// </summary>
/// <remarks>
/// Online-only, like Share: the server keeps the catalogue and makes the copy. Offline the
/// browser says so rather than showing a stale one.
/// </remarks>
public sealed class PublicListsViewModel : ObservableObject
{
    private readonly IAstridCore _core;
    private bool _isLoading;
    private bool _loaded;
    private string? _errorMessage;

    public PublicListsViewModel(IAstridCore core)
    {
        _core = core;
    }

    public ObservableCollection<PublicListSummary> Lists { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (Set(ref _isLoading, value))
            {
                Raise(nameof(IsEmpty));
            }
        }
    }

    /// <summary>Asked, answered, and none: the "nothing to browse" line.</summary>
    public bool IsEmpty => _loaded && !IsLoading && Lists.Count == 0 && ErrorMessage is null;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (Set(ref _errorMessage, value))
            {
                Raise(nameof(IsEmpty));
            }
        }
    }

    /// <summary>Fetch the catalogue, as the browser opens.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var response = await _core.CallAsync(Commands.PublicLists(), cancellationToken);
            if (!response.Ok)
            {
                ErrorMessage = response.IsStillPending || response.Error?.Kind == AstridFailureKind.Offline
                    ? "Browsing public lists needs a connection."
                    : response.Error?.Message;
                return;
            }
            ErrorMessage = null;
            Lists.Clear();
            foreach (var list in response.ReadArray<PublicListSummary>())
            {
                Lists.Add(list);
            }
        }
        finally
        {
            _loaded = true;
            IsLoading = false;
            Raise(nameof(IsEmpty));
        }
    }

    /// <summary>
    /// Copy one into this account. The server answers with the new list, which the core has
    /// cached by the time this returns; null when it could not be copied.
    /// </summary>
    public async Task<ListSummary?> CopyAsync(string listId, CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.CopyList(listId), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.IsStillPending || response.Error?.Kind == AstridFailureKind.Offline
                ? "Copying a list needs a connection."
                : response.Error?.Message;
            return null;
        }
        ErrorMessage = null;
        return response.Read<ListSummary>();
    }
}
