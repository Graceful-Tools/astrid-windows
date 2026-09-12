using Astrid.App.ViewModels;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>The Integrations page: how Google lists get linked.</summary>
public sealed class IntegrationsSettingsViewModelTests
{
    /// <summary>
    /// The mode belongs to the account, so the screen reads it back rather than assuming its own
    /// last answer — somebody who chose "every list" on another machine sees that here.
    /// </summary>
    [Fact]
    public async Task The_google_sync_mode_is_read_from_the_account()
    {
        var core = new FakeCore().AnswerOk("googleSyncMode", new
        {
            mode = "all_bidirectional",
            suffix = "(G)",
        });
        var view = new SettingsViewModel(core).Integrations;

        await view.LoadGoogleSyncModeAsync();

        Assert.Equal("all_bidirectional", view.GoogleSyncMode);
    }

    /// <summary>Manual until the account says otherwise, since the all-lists modes make lists.</summary>
    [Fact]
    public void The_google_sync_mode_starts_manual()
    {
        Assert.Equal("manual", new SettingsViewModel(new FakeCore()).Integrations.GoogleSyncMode);
    }
}
