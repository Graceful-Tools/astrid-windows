using System.Text.Json;
using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// "Task titles": how much of a title a row shows (DS19, task 3d5cca89). Wrap by default, and two
/// lines or one on request — kept for this machine, like the theme, so it goes through the core's
/// cache and never through the Outbox.
/// </summary>
public sealed class RowTitleDisplayTests
{
    private static FakeCore CoreAt(string display, object? maxLines) =>
        new FakeCore().AnswerOk("titleDisplay", new
        {
            titleDisplay = display,
            maxLines,
            choices = new[] { "wrap", "twoLines", "oneLine" },
        });

    /// <summary>
    /// The default, and the behaviour change this task carries: Windows stopped every title at two
    /// lines before it. A title is the task, so the whole of it is what you get unless you ask for
    /// less — and the shell's "no limit" is 0, not null, because that is what a TextBlock means.
    /// Task 3d5cca89.
    /// </summary>
    [Fact]
    public async Task A_title_wraps_until_somebody_asks_for_less()
    {
        var view = new SettingsViewModel(CoreAt("wrap", (object?)null)).Appearance;

        await view.LoadTitleDisplayAsync();

        Assert.Equal("wrap", view.TitleDisplay);
        Assert.Equal(0, view.TitleMaxLines);
    }

    /// <summary>
    /// The three options, in the core's order and each carrying the key for its own words — a
    /// picker that showed "twoLines" would be a hardcoded string in the only place this app does
    /// not allow one. Task 3d5cca89.
    /// </summary>
    [Fact]
    public async Task The_picker_offers_three_options_and_names_none_of_them_itself()
    {
        var view = new SettingsViewModel(CoreAt("wrap", (object?)null)).Appearance;

        await view.LoadTitleDisplayAsync();

        Assert.Equal(
            new[] { "wrap", "twoLines", "oneLine" },
            view.TitleDisplayChoices.Select(choice => choice.Value));
        Assert.Equal(
            new[] { "titles.wrap", "titles.twoLines", "titles.oneLine" },
            view.TitleDisplayChoices.Select(choice => choice.TitleKey));
        Assert.All(view.TitleDisplayChoices, choice => Assert.Null(choice.Text));
        Assert.Equal("wrap", view.TitleDisplayChoice?.Value);
    }

    /// <summary>
    /// Each option carries its own line count, and the row binds that rather than matching on the
    /// names — so a fourth option would be a core change alone. Task 3d5cca89.
    /// </summary>
    [Theory]
    [InlineData("wrap", null, 0)]
    [InlineData("twoLines", 2, 2)]
    [InlineData("oneLine", 1, 1)]
    public async Task Each_option_sets_the_line_count_the_rows_draw_with(
        string display, int? fromCore, int expected)
    {
        var core = CoreAt("wrap", (object?)null)
            .AnswerOk("setTitleDisplay", new { titleDisplay = display, maxLines = fromCore });
        var view = new SettingsViewModel(core).Appearance;
        await view.LoadTitleDisplayAsync();
        var announced = 0;
        view.TitleDisplayChanged += () => announced++;

        Assert.True(await view.SetTitleDisplayAsync(display));

        Assert.Equal(display, view.TitleDisplay);
        Assert.Equal(expected, view.TitleMaxLines);
        Assert.Equal(1, announced);
        Assert.Equal(display, view.TitleDisplayChoice?.Value);
        using var sent = JsonDocument.Parse(core.Sent.Last());
        Assert.Equal(display, sent.RootElement.GetProperty("display").GetString());
        Assert.Equal("setTitleDisplay", sent.RootElement.GetProperty("kind").GetString());
    }

    /// <summary>
    /// It is kept where the theme is kept — the core's cache, this installation's. The test that
    /// matters is the one the Outbox would fail: the setting is a command of its own and never a
    /// task or account write, so nothing about it syncs. Task 3d5cca89.
    /// </summary>
    [Fact]
    public async Task The_choice_belongs_to_this_machine_and_never_travels()
    {
        var core = CoreAt("wrap", (object?)null)
            .AnswerOk("setTitleDisplay", new { titleDisplay = "oneLine", maxLines = 1 });
        var view = new SettingsViewModel(core).Appearance;

        await view.LoadTitleDisplayAsync();
        await view.SetTitleDisplayAsync("oneLine");

        Assert.All(core.Sent, body =>
        {
            using var sent = JsonDocument.Parse(body);
            var kind = sent.RootElement.GetProperty("kind").GetString();
            Assert.True(kind is "titleDisplay" or "setTitleDisplay",
                $"{kind} is not how a per-machine setting is written");
        });
    }

    /// <summary>
    /// A core that will not answer leaves the rows as they were rather than collapsing them. The
    /// reason reaches the screen, as the theme's and the chord's do. Task 3d5cca89.
    /// </summary>
    [Fact]
    public async Task A_refused_change_is_reported_and_nothing_moves()
    {
        var core = CoreAt("twoLines", 2)
            .AnswerFailure("setTitleDisplay", AstridFailureKind.BadRequest, "not a known option");
        var settings = new SettingsViewModel(core);
        var view = settings.Appearance;
        await view.LoadTitleDisplayAsync();
        var announced = false;
        view.TitleDisplayChanged += () => announced = true;

        Assert.False(await view.SetTitleDisplayAsync("threeLines"));

        Assert.Equal("twoLines", view.TitleDisplay);
        Assert.Equal(2, view.TitleMaxLines);
        Assert.False(announced, "nothing changed, so the rows have nothing to redraw");
        Assert.Equal("not a known option", settings.ErrorMessage);
    }
}
