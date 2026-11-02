using System.Text.Json;
using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// One public list, opened by its id and read (task 6fc92134) — the screen Report an Issue needs
/// so that filing a bug does not have to leave the app.
/// </summary>
/// <remarks>
/// <para>
/// This is the second half of core task <c>c5f7d870</c>, which added
/// <c>Command::PublicList { listId }</c>. Two things about that command decide the shape of
/// everything here, and both are easy to get wrong in a shell:
/// </para>
/// <para>
/// The permissions arrive <em>with</em> the list — <c>canAddTask</c> and <c>canComment</c> follow
/// <c>publicListType</c>, computed in the core and pinned by
/// <c>contracts/fixtures/permissions.json</c>. They are held here, never re-derived, because
/// deriving them in <c>app/</c> is business logic in the shell.
/// </para>
/// <para>
/// And the list is deliberately <em>not cached</em>: opening somebody else's list is not joining
/// it, and filing a bug must not leave the Bugs &amp; Feedback list in somebody's sidebar. So this
/// view model has to hold what it was handed, and hand it on to <c>featuredRows</c> — which would
/// otherwise look in the store, miss, and synthesise a nameless empty list.
/// </para>
/// </remarks>
public sealed class PublicListViewModelTests
{
    /// <summary>What <c>publicList</c> answers, as the core's own test pins it.</summary>
    private static object Opened(string id, string name, string publicListType) => new
    {
        list = new
        {
            id,
            name,
            ownerId = "them",
            privacy = "PUBLIC",
            publicListType,
            owner = new { id = "them", name = "Dana" },
        },
        role = "viewer",
        canAddTask = publicListType == "collaborative",
        canComment = publicListType == "collaborative",
    };

    private static object Rows(params string[] titles) => new
    {
        total = titles.Length,
        offset = 0,
        rows = titles.Select((title, index) => new { id = $"t{index}", title }).ToArray(),
    };

    /// <summary>
    /// A collaborative public list opens with its name, whose it is, what the reader may do, and
    /// its rows.
    /// </summary>
    [Fact]
    public async Task Opening_a_public_list_holds_the_list_its_rows_and_the_permissions_task_6fc92134()
    {
        var core = new FakeCore()
            .AnswerOk("publicList", Opened("bugs", "Bugs and Feedback", "collaborative"))
            .AnswerOk("featuredRows", Rows("It crashed on launch", "The badge is wrong"));
        var list = new PublicListViewModel(core);

        Assert.True(await list.OpenAsync("bugs"));

        Assert.Equal("bugs", list.ListId);
        Assert.Equal("Bugs and Feedback", list.Name);
        Assert.Equal("Dana", list.OwnerName);
        Assert.True(list.CanAddTask);
        Assert.True(list.CanComment);
        Assert.Equal(
            ["It crashed on launch", "The badge is wrong"],
            list.Rows.Select(row => row.Title));
        Assert.Null(list.ErrorMessage);
        Assert.Equal(["publicList", "featuredRows"], core.SentKinds());
    }

    /// <summary>
    /// The rows are asked for with the list <c>publicList</c> just answered, not by id alone.
    /// </summary>
    /// <remarks>
    /// The regression this pins is silent: <c>featuredRows</c> falls back to
    /// <c>app.store.list(list_id)</c> and then to a synthesised empty list, so a shell that passed
    /// only the id gets rows back and notices nothing — until something downstream wants the
    /// list's name or colour and finds it blank.
    /// </remarks>
    [Fact]
    public async Task The_rows_are_asked_for_with_the_list_the_core_handed_back_task_6fc92134()
    {
        var core = new FakeCore()
            .AnswerOk("publicList", Opened("bugs", "Bugs and Feedback", "collaborative"))
            .AnswerOk("featuredRows", Rows("It crashed on launch"));
        var list = new PublicListViewModel(core);

        await list.OpenAsync("bugs");

        using var sent = JsonDocument.Parse(core.Sent[1]);
        var asked = sent.RootElement;
        Assert.Equal("featuredRows", asked.GetProperty("kind").GetString());
        Assert.Equal("bugs", asked.GetProperty("listId").GetString());
        Assert.Equal("Bugs and Feedback", asked.GetProperty("list").GetProperty("name").GetString());
        // Passed through whole, so the core sees the fields this shell has no model for.
        Assert.Equal("collaborative", asked.GetProperty("list").GetProperty("publicListType").GetString());
    }

    /// <summary>
    /// A <c>copy_only</c> list is genuinely read-only, and this view model says so rather than
    /// working it out from the type.
    /// </summary>
    [Fact]
    public async Task A_copy_only_public_list_is_read_only_task_6fc92134()
    {
        var core = new FakeCore()
            .AnswerOk("publicList", Opened("recipes", "Recipes", "copy_only"))
            .AnswerOk("featuredRows", Rows("Sourdough"));
        var list = new PublicListViewModel(core);

        Assert.True(await list.OpenAsync("recipes"));

        Assert.False(list.CanAddTask);
        Assert.False(list.CanComment);
    }

    /// <summary>
    /// Offline says so. <c>publicList</c> is online-only on purpose — there is no cached copy to
    /// fall back to — so the screen explains itself instead of drawing an empty list.
    /// </summary>
    [Fact]
    public async Task Offline_says_so_rather_than_showing_an_empty_list_task_6fc92134()
    {
        var core = new FakeCore().AnswerFailure("publicList", AstridFailureKind.Offline);
        var list = new PublicListViewModel(core);

        Assert.False(await list.OpenAsync("bugs"));

        Assert.NotNull(list.ErrorMessage);
        Assert.Empty(list.Rows);
        Assert.False(list.IsLoading);
        // It did not go on to ask for rows it could not draw.
        Assert.Equal(["publicList"], core.SentKinds());
    }

    /// <summary>
    /// The list opened but its rows did not: the list is still on screen, with the reason the rows
    /// are missing.
    /// </summary>
    [Fact]
    public async Task A_list_that_opens_without_its_rows_still_opens_task_6fc92134()
    {
        var core = new FakeCore()
            .AnswerOk("publicList", Opened("bugs", "Bugs and Feedback", "collaborative"))
            .AnswerFailure("featuredRows", AstridFailureKind.Offline);
        var list = new PublicListViewModel(core);

        Assert.True(await list.OpenAsync("bugs"));

        Assert.Equal("Bugs and Feedback", list.Name);
        Assert.Empty(list.Rows);
        Assert.NotNull(list.ErrorMessage);
    }

    /// <summary>Opening a second list does not leave the first one's rows behind it.</summary>
    [Fact]
    public async Task Opening_another_list_clears_the_one_before_it_task_6fc92134()
    {
        var core = new FakeCore()
            .AnswerOk("publicList", Opened("bugs", "Bugs and Feedback", "collaborative"))
            .AnswerOk("featuredRows", Rows("It crashed on launch", "The badge is wrong"))
            .AnswerOk("publicList", Opened("recipes", "Recipes", "copy_only"))
            .AnswerOk("featuredRows", Rows("Sourdough"));
        var list = new PublicListViewModel(core);

        await list.OpenAsync("bugs");
        await list.OpenAsync("recipes");

        Assert.Equal("Recipes", list.Name);
        Assert.Equal(["Sourdough"], list.Rows.Select(row => row.Title));
        Assert.False(list.CanAddTask);
    }
}
