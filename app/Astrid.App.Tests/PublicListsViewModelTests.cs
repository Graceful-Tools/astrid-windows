using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>The public-lists browser (task f6bc59e8): the server's catalogue, and copying from it.</summary>
public sealed class PublicListsViewModelTests
{
    private static object Catalogue() => new object[]
    {
        new
        {
            id = "pub1", name = "Recipes", description = "Weeknight dinners", color = "#ef4444",
            owner = new { id = "them", name = "Dana", email = "dana@x.io" },
            taskCount = 3, memberCount = 2,
        },
        new
        {
            id = "pub2", name = "Packing", description = (string?)null, color = (string?)null,
            owner = new { id = "other", name = (string?)null, email = "o@x.io" },
            taskCount = 0, memberCount = 1,
        },
    };

    /// <summary>The catalogue is drawn as the server answers it, with whose each list is.</summary>
    [Fact]
    public async Task The_catalogue_is_listed_with_its_owners_and_counts_task_f6bc59e8()
    {
        var core = new FakeCore().AnswerOk("publicLists", Catalogue());
        var view = new PublicListsViewModel(core);
        Assert.False(view.IsEmpty, "not asked yet is not empty");

        await view.LoadAsync();

        Assert.Equal(2, view.Lists.Count);
        Assert.Equal("Dana", view.Lists[0].OwnerName);
        Assert.Equal("o@x.io", view.Lists[1].OwnerName);
        Assert.Equal(3, view.Lists[0].TaskCount);
        Assert.Equal("Copy Recipes", view.Lists[0].CopyActionName);
        Assert.False(view.IsEmpty);
        Assert.Null(view.ErrorMessage);
    }

    /// <summary>Copying answers the new list; offline, the browser says so and copies nothing.</summary>
    [Fact]
    public async Task Copying_answers_the_new_list_and_offline_says_so_task_f6bc59e8()
    {
        var core = new FakeCore()
            .AnswerOk("copyList", new { id = "mine1", name = "Recipes" })
            .AnswerFailure("publicLists", AstridFailureKind.Offline, "no network");
        var view = new PublicListsViewModel(core);

        var copied = await view.CopyAsync("pub1");
        Assert.Equal("mine1", copied?.Id);
        Assert.Equal("Recipes", copied?.Name);
        Assert.Contains(core.Sent, json => json.Contains("\"kind\":\"copyList\"") && json.Contains("\"listId\":\"pub1\""));

        await view.LoadAsync();
        Assert.Equal("Browsing public lists needs a connection.", view.ErrorMessage);
        Assert.False(view.IsEmpty, "an error is not an empty catalogue");
    }
}
