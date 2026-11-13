using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The lists down the side, and which one is open.
/// </summary>
/// <remarks>
/// Board columns never appear here, nor labels, milestones or iterations. A column is a state a
/// task is in, not a place to put one, and a sidebar that offers "Doing" beside "Home" invites
/// somebody to file a task into a state. The core answers that question, and the ordering, and the
/// number beside each entry, in one <c>sidebar</c> call (task e22621e2); this binds the answer.
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
            var response = await _core.CallAsync(Commands.Sidebar(), cancellationToken);
            if (!response.Ok)
            {
                ErrorMessage = response.IsStillPending ? null : response.Error?.Message;
                return;
            }
            ErrorMessage = null;

            // Bound, not assembled. The core has already dropped what is not a place, put both
            // sections in order and put each entry's badge on it (task e22621e2) — one call where
            // there were three, and no second opinion about any of the three questions.
            var sections = response.Read<SidebarSections>() ?? new SidebarSections();

            Replace(MyTasks, sections.MyTasks is null ? [] : [sections.MyTasks]);
            Replace(Favorites, sections.Favorites);
            Replace(Lists, sections.Lists);

            // Keep the open list open across a refresh. Re-selecting by identity would drop the
            // selection every time the list was re-fetched, which is several times a minute.
            if (Selected is not null)
            {
                Selected = sections.Favorites.Concat(sections.Lists)
                    .FirstOrDefault(list => list.Id == Selected.Id) ?? Selected;
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
