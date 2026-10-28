using System.Collections.ObjectModel;
using System.Text.Json;
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

    /// <summary>
    /// My Tasks: what you are holding, wherever it lives. Above the favourites, because it is the
    /// view the app opens on rather than one of the lists.
    /// </summary>
    /// <remarks>
    /// Its id and name are the core's — it has no row on the server, so nothing else could name
    /// it, and inventing the id here would be the shell deciding what My Tasks is.
    /// </remarks>
    public ObservableCollection<ListSummary> MyTasks { get; } = [];

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

            // Places only. A board column is a state and a label is a tag; neither is somewhere
            // to go, and the web's sidebar offers neither.
            var all = response.ReadArray<ListSummary>()
                .Where(list => list.IsDomainList)
                .ToList();

            var mine = await _core.CallAsync(Commands.MyTasksList(), cancellationToken);
            var entry = mine.Ok ? mine.Read<ListSummary>() : null;

            var numbers = await NumbersAsync(response.Value, entry?.Id, cancellationToken);
            all = all.Select(list => list with { TaskCount = Number(numbers, list.Id) }).ToList();
            entry = entry is null ? null : entry with { TaskCount = Number(numbers, entry.Id) };

            Replace(MyTasks, entry is null ? [] : [entry]);

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
            Selected ??= MyTasks.FirstOrDefault()
                ?? Favorites.FirstOrDefault()
                ?? Lists.FirstOrDefault();
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

    /// <summary>
    /// The core's number for each list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>lists</c> carries a <c>taskCount</c> of its own, but it is the server's total for the
    /// list — every task it has ever held, completed ones included — which read as "124" beside a
    /// list of a dozen open tasks (task 8d37457b). The number a sidebar is scanned for is the open
    /// ones, and which tasks those are is the core's answer, not a sum the shell may take.
    /// </para>
    /// <para>
    /// <c>null</c> when the core could not say. Falling back to the total would be drawing the
    /// wrong number, which is the bug; the badge is left off instead.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, int>?> NumbersAsync(
        JsonElement lists, string? myTasksId, CancellationToken cancellationToken)
    {
        if (lists.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        var response = await _core.CallAsync(Commands.ListBadges(lists, myTasksId), cancellationToken);
        return response.Ok ? response.ReadCounts() : null;
    }

    /// <summary>A list's number: none at all when the core could not count.</summary>
    /// <remarks>
    /// Zero for a list the answer omits, which the core does not do — it answers for every list it
    /// was handed — but "it was counted and came to nothing" is the right reading if it ever does.
    /// </remarks>
    private static int? Number(IReadOnlyDictionary<string, int>? numbers, string listId) =>
        numbers is null ? null : numbers.TryGetValue(listId, out var count) ? count : 0;

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
