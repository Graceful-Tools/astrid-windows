using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>The bell: what is new, and clearing it.</summary>
public sealed class NotificationsViewModelTests
{
    private static object Inbox(int unread, params (string Id, string Kind, bool Read)[] rows) => new
    {
        unreadCount = unread,
        notifications = rows.Select(row => new
        {
            id = row.Id,
            kind = row.Kind,
            labelKey = $"notification.{row.Kind}",
            taskId = "t1",
            taskTitle = "Book flights",
            taskIdentifier = "AST-142",
            taskCompleted = false,
            isRead = row.Read,
            createdAt = "2026-09-07T11:00:00Z",
        }).ToArray(),
    };

    /// <summary>The badge is right the moment the window opens: it comes from the cache.</summary>
    [Fact]
    public async Task The_badge_comes_from_the_cache_with_no_network()
    {
        var core = new FakeCore().AnswerOk("notifications", Inbox(2, ("n1", "assigned", false), ("n2", "mentioned", false)));
        var view = new NotificationsViewModel(core);

        await view.LoadAsync();

        Assert.Equal(["notifications"], core.SentKinds());
        Assert.Equal(2, view.UnreadCount);
        Assert.True(view.HasUnread);
        Assert.Equal("2", view.UnreadText);
        Assert.Equal(2, view.Items.Count);
        Assert.Equal("notification.assigned", view.Items[0].LabelKey);
        Assert.False(view.IsEmpty);
    }

    [Fact]
    public async Task A_refresh_asks_the_server_and_draws_what_it_says()
    {
        var core = new FakeCore().AnswerOk("refreshNotifications", Inbox(1, ("n1", "replied", false)));
        var view = new NotificationsViewModel(core);

        await view.RefreshAsync();

        Assert.Equal(["refreshNotifications"], core.SentKinds());
        Assert.Equal(1, view.UnreadCount);
    }

    /// <summary>Offline, the cached inbox stands and nothing is said.</summary>
    [Fact]
    public async Task A_refresh_that_cannot_reach_the_server_keeps_the_cache_and_says_nothing()
    {
        var core = new FakeCore()
            .AnswerOk("notifications", Inbox(1, ("n1", "assigned", false)))
            .AnswerFailure("refreshNotifications", AstridFailureKind.Offline);
        var view = new NotificationsViewModel(core);
        await view.LoadAsync();

        await view.RefreshAsync();

        Assert.Equal(1, view.UnreadCount);
        Assert.Null(view.ErrorMessage);
    }

    [Fact]
    public async Task Marking_all_read_clears_the_badge()
    {
        var core = new FakeCore()
            .AnswerOk("notifications", Inbox(2, ("n1", "assigned", false), ("n2", "mentioned", false)))
            .AnswerOk("markAllNotificationsRead", Inbox(0, ("n1", "assigned", true), ("n2", "mentioned", true)));
        var view = new NotificationsViewModel(core);
        await view.LoadAsync();

        Assert.True(await view.MarkAllReadAsync());

        Assert.Equal(0, view.UnreadCount);
        Assert.False(view.HasUnread);
        Assert.True(view.Items.All(item => item.IsRead));
    }

    /// <summary>Nothing unread means nothing to send.</summary>
    [Fact]
    public async Task Marking_all_read_with_nothing_unread_sends_nothing()
    {
        var core = new FakeCore().AnswerOk("notifications", Inbox(0, ("n1", "assigned", true)));
        var view = new NotificationsViewModel(core);
        await view.LoadAsync();

        Assert.True(await view.MarkAllReadAsync());

        Assert.DoesNotContain("markAllNotificationsRead", core.SentKinds());
    }

    [Fact]
    public async Task Marking_one_read_names_it()
    {
        var core = new FakeCore()
            .AnswerOk("markNotificationsRead", Inbox(0, ("n1", "assigned", true)));
        var view = new NotificationsViewModel(core);

        Assert.True(await view.MarkReadAsync("n1"));

        Assert.Contains("\"ids\":[\"n1\"]", core.Sent[0], StringComparison.Ordinal);
    }

    /// <summary>A badge reading "137" is noise; the web caps it too.</summary>
    [Fact]
    public async Task The_badge_is_capped()
    {
        var core = new FakeCore().AnswerOk("notifications", new { unreadCount = 137, notifications = Array.Empty<object>() });
        var view = new NotificationsViewModel(core);

        await view.LoadAsync();

        Assert.Equal("99+", view.UnreadText);
    }
}
