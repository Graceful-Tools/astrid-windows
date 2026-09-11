using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The bell: what happened to work you are involved in, and how much of it is new.
/// </summary>
/// <remarks>
/// <para>
/// Reads from the cache first, so the badge is right the moment the window opens, and asks the
/// server when told to — after a sync, and when the core says the inbox moved. The web sends no
/// live event for the inbox, so "moved" is the sync pass noticing a difference.
/// </para>
/// <para>
/// Marking read clears the badge on the click: the core updates its cache before it asks the
/// server, and a refusal is reported through <see cref="ErrorMessage"/> rather than by putting
/// the badge back — an acknowledgement is not somebody's work, and un-reading it would be more
/// confusing than a line of text.
/// </para>
/// </remarks>
public sealed class NotificationsViewModel : ObservableObject
{
    private readonly IAstridCore _core;
    private int _unreadCount;
    private bool _isLoading;
    private string? _errorMessage;
    private bool _needsSignIn;

    public NotificationsViewModel(IAstridCore core)
    {
        _core = core;
    }

    /// <summary>Newest first, as the server orders them.</summary>
    public ObservableCollection<NotificationItem> Items { get; } = [];

    public int UnreadCount
    {
        get => _unreadCount;
        private set
        {
            if (Set(ref _unreadCount, value))
            {
                Raise(nameof(HasUnread));
                Raise(nameof(UnreadText));
            }
        }
    }

    public bool HasUnread => _unreadCount > 0;

    /// <summary>What the badge says. Capped the way a badge is: nobody reads "137".</summary>
    public string UnreadText => _unreadCount > 99 ? "99+" : _unreadCount.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public bool IsEmpty => Items.Count == 0;

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

    public bool NeedsSignIn
    {
        get => _needsSignIn;
        private set => Set(ref _needsSignIn, value);
    }

    /// <summary>Draw what the cache has. No network.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.Notifications(), cancellationToken);
        Apply(response);
    }

    /// <summary>Ask the server, then draw. Quiet when it cannot: the cache stands.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            var response = await _core.CallAsync(Commands.RefreshNotifications(), cancellationToken);
            if (response.Ok)
            {
                Apply(response);
            }
            else if (response.NeedsSignIn)
            {
                NeedsSignIn = true;
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task<bool> MarkReadAsync(string id, CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.MarkNotificationsRead([id]), cancellationToken);
        return Apply(response);
    }

    public async Task<bool> MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        if (!HasUnread)
        {
            return true;
        }
        var response = await _core.CallAsync(Commands.MarkAllNotificationsRead(), cancellationToken);
        return Apply(response);
    }

    private bool Apply(AstridResponse response)
    {
        if (!response.Ok)
        {
            NeedsSignIn = response.NeedsSignIn;
            ErrorMessage = response.IsStillPending ? null : response.Error?.Message;
            return false;
        }
        ErrorMessage = null;
        var inbox = response.Read<Inbox>();
        if (inbox is null)
        {
            return false;
        }
        UnreadCount = inbox.UnreadCount;
        Items.Clear();
        foreach (var item in inbox.Notifications)
        {
            Items.Add(item);
        }
        Raise(nameof(IsEmpty));
        return true;
    }
}
