using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// One public list, opened by its id and read (task 6fc92134) — the screen that lets Report an
/// Issue show the deployment's feedback list in-app rather than through a browser door.
/// </summary>
/// <remarks>
/// <para>
/// Read-only, and that is a decision rather than a shortcut. The core answers
/// <see cref="Commands.PublicList"/> without caching the list, because opening somebody else's
/// list is not joining it; whether the Outbox may then hold a <em>write</em> against a list this
/// device has not cached is still an open question in the core, and an offline bug report is
/// exactly the correlated case. So this screen reads, and the writing goes to the web.
/// </para>
/// <para>
/// <see cref="CanAddTask"/> and <see cref="CanComment"/> are held as the core gave them. They are
/// what the <em>next</em> task needs as its input, and re-deriving them from the list's public
/// type here would put the permission rule in two places — one of them the shell.
/// </para>
/// </remarks>
public sealed class PublicListViewModel : ObservableObject
{
    private readonly IAstridCore _core;
    private string? _listId;
    private string _name = string.Empty;
    private string _ownerName = string.Empty;
    private bool _canAddTask;
    private bool _canComment;
    private bool _isLoading;
    private string? _errorMessage;

    public PublicListViewModel(IAstridCore core)
    {
        _core = core;
    }

    /// <summary>The list on screen, or null when none is.</summary>
    public string? ListId
    {
        get => _listId;
        private set
        {
            if (Set(ref _listId, value))
            {
                Raise(nameof(IsOpen));
            }
        }
    }

    public bool IsOpen => _listId is not null;

    public string Name
    {
        get => _name;
        private set => Set(ref _name, value);
    }

    /// <summary>Whose list it is, by name or address. Empty when the core did not say.</summary>
    public string OwnerName
    {
        get => _ownerName;
        private set
        {
            if (Set(ref _ownerName, value))
            {
                Raise(nameof(HasOwner));
            }
        }
    }

    public bool HasOwner => _ownerName.Length > 0;

    /// <summary>Whether this reader may file a task in the list, as the core answered it.</summary>
    public bool CanAddTask
    {
        get => _canAddTask;
        private set => Set(ref _canAddTask, value);
    }

    /// <summary>Whether this reader may comment, as the core answered it.</summary>
    public bool CanComment
    {
        get => _canComment;
        private set => Set(ref _canComment, value);
    }

    public ObservableCollection<TaskRow> Rows { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    /// <summary>Why the list, or its rows, are not on screen.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (Set(ref _errorMessage, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    public bool HasError => _errorMessage is not null;

    /// <summary>
    /// Open a public list by its id. True when the list itself arrived — its rows may still have
    /// failed, which leaves the list on screen with <see cref="ErrorMessage"/> saying why.
    /// </summary>
    public async Task<bool> OpenAsync(string listId, CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        Rows.Clear();
        try
        {
            var opened = await _core.CallAsync(Commands.PublicList(listId), cancellationToken);
            if (!opened.Ok)
            {
                ListId = null;
                ErrorMessage = Reason(opened, "Opening a public list needs a connection.");
                return false;
            }
            var list = opened.Read<OpenedPublicList>();
            if (list is null)
            {
                ListId = null;
                ErrorMessage = "That list could not be read.";
                return false;
            }

            var summary = list.Summary;
            ListId = listId;
            Name = summary.Name;
            OwnerName = summary.Owner?.DisplayName ?? string.Empty;
            CanAddTask = list.CanAddTask;
            CanComment = list.CanComment;
            ErrorMessage = null;

            // The list goes back as it came. The core has not cached it, so this is the only copy
            // there is, and `featuredRows` without it projects against a synthesised empty list.
            var rows = await _core.CallAsync(
                Commands.FeaturedRows(listId, list.List), cancellationToken);
            if (!rows.Ok)
            {
                ErrorMessage = Reason(rows, "Reading this list's tasks needs a connection.");
                return true;
            }
            foreach (var row in rows.Read<RowWindow>()?.Rows ?? [])
            {
                Rows.Add(row);
            }
            return true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Put it away, so nothing of somebody else's list is left behind.</summary>
    public void Close()
    {
        ListId = null;
        Name = string.Empty;
        OwnerName = string.Empty;
        CanAddTask = false;
        CanComment = false;
        ErrorMessage = null;
        Rows.Clear();
    }

    /// <summary>
    /// The offline case in the deployment's words, anything else in the core's. The same shape as
    /// <see cref="PublicListsViewModel"/>, which is online-only for the same reason.
    /// </summary>
    private static string Reason(AstridResponse response, string offline) =>
        response.IsStillPending || response.Error?.Kind == AstridFailureKind.Offline
            ? offline
            : response.Error?.Message ?? offline;
}
