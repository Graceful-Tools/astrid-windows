using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

public sealed class ChatViewModelTests
{
    private static object Panel(params (string Content, bool Mine, bool Pending, string? Author)[] messages) => new
    {
        channelId = "c1",
        name = "Work",
        messages = messages.Select((message, index) => new
        {
            id = $"m{index}",
            content = message.Content,
            authorName = message.Author,
            initials = message.Author is null ? "" : message.Author[..2].ToUpperInvariant(),
            isMine = message.Mine,
            isPending = message.Pending,
            isSystem = message.Author is null,
        }).ToArray(),
    };

    /// <summary>
    /// The cache first, the server second. Opening a conversation onto a spinner when every
    /// message in it is already on this machine is the difference between an app that feels local
    /// and one that does not.
    /// </summary>
    [Fact]
    public async Task Opening_reads_the_cache_before_it_asks_the_server()
    {
        var core = new FakeCore()
            .AnswerOk("chat", Panel(("morning", true, false, "Jon")))
            .AnswerOk("refreshChat", Panel(("morning", true, false, "Jon"), ("hello", false, false, "Dana")));
        var view = new ChatViewModel(core);

        await view.OpenAsync("l1");

        Assert.Equal(["chat", "refreshChat"], core.SentKinds());
        Assert.Equal(2, view.Messages.Count);
        Assert.True(view.HasChannel);
    }

    /// <summary>A message still in the Outbox says so, in the byline rather than as an error.</summary>
    [Fact]
    public async Task A_message_that_has_not_been_delivered_says_sending()
    {
        var core = new FakeCore()
            .AnswerOk("chat", Panel(("on my way", true, true, "Jon")))
            .AnswerOk("refreshChat", Panel(("on my way", true, true, "Jon")));
        var view = new ChatViewModel(core);

        await view.OpenAsync("l1");

        Assert.Equal("Jon · sending", view.Messages[0].Byline);
    }

    /// <summary>"Dana joined the list" is nobody's message and gets no byline.</summary>
    [Fact]
    public async Task A_message_the_server_wrote_has_no_byline()
    {
        var core = new FakeCore()
            .AnswerOk("chat", Panel(("Dana joined the list", false, false, null)))
            .AnswerOk("refreshChat", Panel(("Dana joined the list", false, false, null)));
        var view = new ChatViewModel(core);

        await view.OpenAsync("l1");

        Assert.Equal(string.Empty, view.Messages[0].Byline);
        Assert.True(view.Messages[0].IsSystem);
    }

    /// <summary>
    /// Chat is a feature a deployment can be without: no channel means an empty panel with the box
    /// disabled, not an error.
    /// </summary>
    [Fact]
    public async Task A_list_with_no_conversation_shows_an_empty_panel()
    {
        var core = new FakeCore()
            .AnswerOk("chat", new { channelId = (string?)null, messages = Array.Empty<object>() })
            .AnswerOk("refreshChat", new { channelId = (string?)null, messages = Array.Empty<object>() });
        var view = new ChatViewModel(core);

        await view.OpenAsync("l1");

        Assert.False(view.HasChannel);
        Assert.True(view.IsEmpty);
        Assert.Null(view.ErrorMessage);
    }

    [Fact]
    public async Task Sending_puts_the_message_in_the_transcript()
    {
        var core = new FakeCore()
            .AnswerOk("chat", Panel())
            .AnswerOk("refreshChat", Panel())
            .AnswerOk("sendChatMessage")
            .AnswerOk("chat", Panel(("on my way", true, true, "Jon")));
        var view = new ChatViewModel(core);
        await view.OpenAsync("l1");

        Assert.True(await view.SendAsync("on my way"));

        Assert.Single(view.Messages);
        Assert.Contains("\"content\":\"on my way\"", core.Sent.First(sent => sent.Contains("sendChatMessage")));
    }

    /// <summary>A stray Enter is not a message.</summary>
    [Fact]
    public async Task An_empty_message_is_not_sent()
    {
        var core = new FakeCore()
            .AnswerOk("chat", Panel())
            .AnswerOk("refreshChat", Panel());
        var view = new ChatViewModel(core);
        await view.OpenAsync("l1");

        Assert.False(await view.SendAsync("   "));

        Assert.DoesNotContain("sendChatMessage", core.SentKinds());
    }

    /// <summary>
    /// A conversation that could not be caught up is not a red line: what is on screen is still
    /// true, and the next refresh carries the rest.
    /// </summary>
    [Fact]
    public async Task Failing_to_catch_up_while_offline_says_nothing()
    {
        var core = new FakeCore()
            .AnswerOk("chat", Panel(("morning", true, false, "Jon")))
            .AnswerFailure("refreshChat", AstridFailureKind.Offline, "no network");
        var view = new ChatViewModel(core);

        await view.OpenAsync("l1");

        Assert.Null(view.ErrorMessage);
        Assert.Single(view.Messages);
    }
}
