using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;
using static Astrid.App.Tests.SettingsFixtures;

namespace Astrid.App.Tests;

/// <summary>
/// The account flyout as a whole: how it loads, and what the pages share. Each page's own
/// behaviour is tested beside its view model (<c>AccountSettingsViewModelTests</c> and the rest).
/// </summary>
public sealed class SettingsViewModelTests
{
    /// <summary>
    /// The cache first, the server second: an account screen that opens onto a spinner when the
    /// answer is already on this machine is the same mistake as a task list that does.
    /// </summary>
    [Fact]
    public async Task Loading_reads_the_cache_before_it_asks_the_server()
    {
        var core = new FakeCore()
            .AnswerOk("settings", Account())
            .AnswerOk("refreshSettings", Account())
            .AnswerOk("profileStats", new { completed = 1, inspired = 2, supported = 3 });
        var view = new SettingsViewModel(core);

        await view.LoadAsync();

        // The cache, then the server, then the numbers — which need to know who is signed in, so
        // they come after the account rather than beside it.
        Assert.Equal(["settings", "refreshSettings", "profileStats"], core.SentKinds());
        // One answer, three pages: the account, the reminders and the offsets each took their slice.
        Assert.Equal("Jon", view.Account.DisplayName);
        Assert.Equal("jon@example.test", view.Account.Email);
        Assert.True(view.Reminders.PushEnabled);
        Assert.Equal(2, view.Reminders.Offsets.Count);
    }

    /// <summary>
    /// Offline, what is on screen came from the cache and is still what this account last chose,
    /// so there is nothing to report.
    /// </summary>
    [Fact]
    public async Task Failing_to_catch_up_while_offline_says_nothing()
    {
        var core = new FakeCore()
            .AnswerOk("settings", Account())
            .AnswerFailure("refreshSettings", AstridFailureKind.Offline, "no network");
        var view = new SettingsViewModel(core);

        await view.LoadAsync();

        Assert.Null(view.ErrorMessage);
        Assert.Equal("Jon", view.Account.DisplayName);
    }

    /// <summary>
    /// The error line is the flyout's, not a page's: a failure on one page shows there, and the
    /// next success on any page clears it — as when the pages were one class.
    /// </summary>
    [Fact]
    public async Task The_error_line_is_shared_by_every_page()
    {
        var core = new FakeCore()
            .AnswerFailure("contacts", AstridFailureKind.Offline, "no network")
            .AnswerOk("exportAccount", new { path = "C:/exports/astrid.json", bytes = 1 });
        var view = new SettingsViewModel(core);
        var raised = new List<string?>();
        view.PropertyChanged += (_, changed) => raised.Add(changed.PropertyName);

        Assert.False(await view.Contacts.LoadContactsAsync());
        Assert.Equal("Contacts need a connection.", view.ErrorMessage);

        Assert.True(await view.Data.ExportAsync("json", "C:/exports/astrid.json"));
        Assert.Null(view.ErrorMessage);
        Assert.Contains(nameof(SettingsViewModel.ErrorMessage), raised);
    }
}
