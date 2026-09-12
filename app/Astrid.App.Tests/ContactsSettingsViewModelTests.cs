using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>The Contacts page (task 438494c7).</summary>
public sealed class ContactsSettingsViewModelTests
{
    /// <summary>
    /// The Contacts page lists what the server holds, with a name when there is one and the
    /// address when there is not, and clearing empties it; offline says so (task 438494c7).
    /// </summary>
    [Fact]
    public async Task Contacts_are_listed_and_cleared_and_offline_says_so_task_438494c7()
    {
        var core = new FakeCore()
            .AnswerOk("contacts", new
            {
                total = 2,
                contacts = new[]
                {
                    new { id = "c1", email = "ann@x.io", name = (string?)"Ann" },
                    new { id = "c2", email = "bo@x.io", name = (string?)null },
                },
            })
            .AnswerOk("clearContacts", new { deleted = 2 })
            .AnswerFailure("contacts", AstridFailureKind.Offline, "no network");
        var settings = new SettingsViewModel(core);
        var view = settings.Contacts;
        Assert.False(view.ContactsAreEmpty, "not asked yet is not empty");

        Assert.True(await view.LoadContactsAsync());
        Assert.Equal(2, view.ContactsTotal);
        Assert.True(view.HasContacts);
        Assert.Equal("Ann", view.Contacts[0].Label);
        Assert.Equal("bo@x.io", view.Contacts[1].Label);

        Assert.True(await view.ClearContactsAsync());
        Assert.Empty(view.Contacts);
        Assert.True(view.ContactsAreEmpty);

        Assert.False(await view.LoadContactsAsync());
        Assert.Equal("Contacts need a connection.", settings.ErrorMessage);
    }
}
