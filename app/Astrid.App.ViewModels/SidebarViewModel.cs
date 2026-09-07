using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The lists down the side, and which one is open.
/// </summary>
/// <remarks>
/// Board columns never appear here. A column is a state a task is in, not a place to put one, and
/// a sidebar that offers "Doing" beside "Home" invites somebody to file a task into a state. The
/// core answers that question — <c>listType == "status"</c> — and this reads the answer.
/// </remarks>
public sealed class SidebarViewModel : ObservableObject
{
    private readonly IAstridCore _core;
    private ListSummary? _selected;
    private bool _isLoading;
    private string? _errorMessage;

    public SidebarViewModel(IAstridCore core)
    {
        _core = core;
    }

    /// <summary>Favourites, in the order the user arranged them.</summary>
    public ObservableCollection<ListSummary> Favorites { get; } = [];

    /// <summary>Everything else that can hold a task.</summary>
    public ObservableCollection<ListSummary> Lists { get; } = [];

    public ListSummary? Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }

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

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var response = await _core.CallAsync(Commands.Lists(), cancellationToken);
            if (!response.Ok)
            {
                ErrorMessage = response.IsStillPending ? null : response.Error?.Message;
                return;
            }
            ErrorMessage = null;

            var all = response.ReadArray<ListSummary>()
                .Where(list => !list.IsStatusList)
                .ToList();

            Replace(Favorites, all
                .Where(list => list.IsFavorite == true)
                .OrderBy(list => list.FavoriteOrder ?? int.MaxValue)
                // A favourite saved before ordering existed has no order at all, and leaving those
                // in whatever order they arrived reshuffles the sidebar on every launch.
                .ThenBy(list => list.Name, StringComparer.CurrentCultureIgnoreCase));

            Replace(Lists, all
                .Where(list => list.IsFavorite != true)
                .OrderBy(list => list.Name, StringComparer.CurrentCultureIgnoreCase));

            // Keep the open list open across a refresh. Re-selecting by identity would drop the
            // selection every time the list was re-fetched, which is several times a minute.
            if (Selected is not null)
            {
                Selected = all.FirstOrDefault(list => list.Id == Selected.Id) ?? Selected;
            }
            Selected ??= Favorites.FirstOrDefault() ?? Lists.FirstOrDefault();
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task<bool> CreateListAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var response = await _core.CallAsync(Commands.CreateList(trimmed), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.IsStillPending ? null : response.Error?.Message;
            return false;
        }

        await LoadAsync(cancellationToken);
        var created = response.Read<ListSummary>();
        if (created is not null)
        {
            Selected = Favorites.Concat(Lists).FirstOrDefault(list => list.Id == created.Id) ?? Selected;
        }
        return true;
    }

    public async Task<bool> SetFavoriteAsync(string listId, bool favorite,
        CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.SetListFavorite(listId, favorite), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.IsStillPending ? null : response.Error?.Message;
            return false;
        }
        await LoadAsync(cancellationToken);
        return true;
    }

    private static void Replace(ObservableCollection<ListSummary> target, IEnumerable<ListSummary> source)
    {
        var wanted = source.ToList();
        for (var index = 0; index < wanted.Count; index++)
        {
            if (index < target.Count)
            {
                if (!target[index].Equals(wanted[index]))
                {
                    target[index] = wanted[index];
                }
            }
            else
            {
                target.Add(wanted[index]);
            }
        }
        while (target.Count > wanted.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
