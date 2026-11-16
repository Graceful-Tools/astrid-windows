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

    /// <summary>
    /// The projects, each expanding to its lists (task f8b844dd; core AWTD-1681).
    /// </summary>
    /// <remarks>
    /// Empty when the core drew no section — Project Mode off, or no project this reader can see.
    /// Whether the heading appears is <see cref="ShowsProjects"/>, which is that distinction and
    /// not "are there any rows": the core answers with no section rather than an empty one.
    /// </remarks>
    public ObservableCollection<SidebarProjectView> Projects { get; } = [];

    /// <summary>Whether to draw the Projects heading at all.</summary>
    public bool ShowsProjects => Projects.Count > 0;

    /// <summary>
    /// Which projects are expanded, by id.
    /// </summary>
    /// <remarks>
    /// Held here rather than on <see cref="SidebarProjectView"/>, which is rebuilt on every load:
    /// the sidebar reloads several times a minute, and an expansion kept on the row object would
    /// collapse under the reader every time anything refreshed.
    /// </remarks>
    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal);

    internal bool IsExpanded(string projectId) => _expanded.Contains(projectId);

    /// <summary>Open or close a project's lists.</summary>
    internal void ToggleExpanded(string projectId)
    {
        if (!_expanded.Remove(projectId))
        {
            _expanded.Add(projectId);
        }
        Rebuild();
    }

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
            // A list in a project is in BOTH sections, which is the core's decision and the
            // web's: the Projects section is a second way in, not a move.
            _projects = sections.Projects ?? [];
            Rebuild();

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

    /// <summary>The projects as the core last sent them, before the expansion is applied.</summary>
    private IReadOnlyList<SidebarProject> _projects = [];

    /// <summary>
    /// Draw the Projects section: each project, and its lists when it is open.
    /// </summary>
    /// <remarks>
    /// Rebuilt whole rather than patched in place, like <see cref="BoardViewModel"/>'s columns:
    /// the rows are few and a chevron turning is not a scroll position worth preserving, while a
    /// patch would have to reconcile two dimensions — which projects moved, and which of their
    /// lists did.
    /// </remarks>
    private void Rebuild()
    {
        Projects.Clear();
        foreach (var project in _projects)
        {
            Projects.Add(new SidebarProjectView(this, project));
        }
        Raise(nameof(ShowsProjects));
    }
}

/// <summary>
/// One project in the sidebar: the row that opens its board, and its lists when it is expanded
/// (task f8b844dd).
/// </summary>
public sealed class SidebarProjectView
{
    private readonly SidebarViewModel _sidebar;
    private readonly SidebarProject _project;

    internal SidebarProjectView(SidebarViewModel sidebar, SidebarProject project)
    {
        _sidebar = sidebar;
        _project = project;
    }

    public string Id => _project.Id;

    public string Name => _project.Name;

    /// <summary>Already resolved by the core, so there is no default to choose here.</summary>
    public string Color => _project.Color;

    /// <summary>
    /// Its lists when it is open, and nothing when it is shut.
    /// </summary>
    /// <remarks>
    /// Empty rather than hidden by a Visibility binding, so a collapsed project costs no row
    /// objects at all — the sidebar is rebuilt several times a minute.
    /// </remarks>
    public IReadOnlyList<ListSummary> Lists =>
        _sidebar.IsExpanded(Id) ? _project.Lists : [];

    /// <summary>Whether its lists are showing — which way the chevron points.</summary>
    public bool IsExpanded => _sidebar.IsExpanded(Id);

    /// <summary>The chevron's glyph: down when open, right when shut.</summary>
    public string Chevron => IsExpanded ? "" : "";

    /// <summary>Whether it has any lists to expand to.</summary>
    /// <remarks>
    /// A project with none still has its row — it is how it gets its first list — but a chevron
    /// that opens onto nothing is a control that does nothing.
    /// </remarks>
    public bool CanExpand => _project.Lists.Count > 0;

    /// <summary>Open or close this project's lists.</summary>
    public void ToggleExpanded() => _sidebar.ToggleExpanded(Id);
}
