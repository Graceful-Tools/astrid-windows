using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// One list's conversation.
/// </summary>
/// <remarks>
/// <para>
/// Drawn from the cache first and caught up afterwards, the same order the task list uses: opening
/// a conversation onto a spinner, when every message in it is already on this machine, is the
/// difference between an app that feels local and one that does not.
/// </para>
/// <para>
/// A message typed offline is in the transcript at the moment it was typed, marked as still going.
/// Whose message it is, whether it has been delivered, and whether anybody said it at all are all
/// decided in <c>astrid_core::rows::chat</c>.
/// </para>
/// </remarks>
public sealed class ChatViewModel : ObservableObject
{
    private readonly IAstridCore _core;
    private string _listId = string.Empty;
    private string? _channelId;
    private bool _isLoading;
    private string? _errorMessage;

    public ChatViewModel(IAstridCore core)
    {
        _core = core;
    }

    /// <summary>The transcript, oldest first.</summary>
    public ObservableCollection<MessageRow> Messages { get; } = [];

    /// <summary>Whether this list has a conversation at all.</summary>
    public bool HasChannel => _channelId is not null;

    /// <summary>Nothing said yet — as opposed to nothing loaded.</summary>
    public bool IsEmpty => Messages.Count == 0 && !IsLoading;

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

    /// <summary>Open a list's conversation: the cache now, the server next.</summary>
    public async Task OpenAsync(string listId, CancellationToken cancellationToken = default)
    {
        _listId = listId;
        await ReadAsync(Commands.Chat(listId), cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    /// <summary>Catch up with the server.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_listId))
        {
            return;
        }
        IsLoading = true;
        try
        {
            await ReadAsync(Commands.RefreshChat(_listId), cancellationToken);
        }
        finally
        {
            IsLoading = false;
            Raise(nameof(IsEmpty));
        }
    }

    /// <summary>Say something.</summary>
    public async Task<bool> SendAsync(string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content) || _channelId is null)
        {
            return false;
        }
        var response = await _core.CallAsync(
            Commands.SendChatMessage(_channelId, content.Trim()), cancellationToken);
        if (!response.Ok && !response.IsStillPending)
        {
            ErrorMessage = response.Error?.Message;
            return false;
        }
        // Straight back to the cache: the message is already in it, at the moment it was typed.
        await ReadAsync(Commands.Chat(_listId), cancellationToken);
        return true;
    }

    private async Task ReadAsync(object command, CancellationToken cancellationToken)
    {
        var response = await _core.CallAsync(command, cancellationToken);
        if (!response.Ok)
        {
            // A conversation that could not be caught up is not an error worth a red line: what is
            // already on screen is still true, and the next refresh will carry the rest.
            ErrorMessage = response.IsStillPending ? null : response.Error?.Message;
            return;
        }
        ErrorMessage = null;
        var panel = response.Read<ChatPanel>();
        if (panel is null)
        {
            return;
        }
        _channelId = panel.ChannelId;
        Replace(Messages, panel.Messages);
        Raise(nameof(HasChannel));
        Raise(nameof(IsEmpty));
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
