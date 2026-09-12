using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;
using static Astrid.App.Tests.SettingsFixtures;

namespace Astrid.App.Tests;

/// <summary>The Your data page: the export.</summary>
public sealed class DataSettingsViewModelTests
{
    [Fact]
    public async Task An_export_says_where_it_was_written()
    {
        var core = new FakeCore()
            .AnswerOk("settings", Account())
            .AnswerOk("refreshSettings", Account())
            .AnswerOk("profileStats", new { completed = 12, inspired = 3, supported = 5 })
            .AnswerOk("exportAccount", new { path = "C:/exports/astrid.json", bytes = 2048 });
        var settings = new SettingsViewModel(core);
        await settings.LoadAsync();

        Assert.Equal(12, settings.Account.Stats.Completed);
        Assert.True(await settings.Data.ExportAsync("json", "C:/exports/astrid.json"));
        Assert.Equal("C:/exports/astrid.json", settings.Data.LastExportPath);
    }

    /// <summary>An export is a fetch, so offline it did not happen and says so.</summary>
    [Fact]
    public async Task An_export_offline_reports_rather_than_pretending()
    {
        var core = new FakeCore()
            .AnswerOk("settings", Account())
            .AnswerOk("refreshSettings", Account())
            .AnswerOk("profileStats", new { completed = 0, inspired = 0, supported = 0 })
            .AnswerFailure("exportAccount", AstridFailureKind.Offline, "no network");
        var settings = new SettingsViewModel(core);
        await settings.LoadAsync();

        Assert.False(await settings.Data.ExportAsync("json", "C:/exports/astrid.json"));
        Assert.NotNull(settings.ErrorMessage);
        Assert.Null(settings.Data.LastExportPath);
    }
}
