using System.Text.Json;
using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// What a list has been doing (task 48c624ad): the window picker, the chart geometry, and the
/// readings the panel draws. Every number here is the server's — these tests are about what the
/// shell does with them, never about what they should be.
/// </summary>
public sealed class ListInsightsViewModelTests
{
    private static object Insights(object? lanes = null, object[]? series = null,
        object? createToComplete = null, object[]? contributors = null, int days = 30) => new
    {
        window = new { days, timeZone = "America/Los_Angeles", start = "2026-09-23", end = "2026-10-22" },
        series = series ?? [new { date = "2026-10-22", created = 2, completed = 3 }],
        totals = new { created = 60, completed = 45 },
        createToComplete = createToComplete ?? new { medianMs = (long?)111_600_000, sampleSize = 42 },
        lanes,
        contributors = contributors ?? [],
    };

    /// <summary>
    /// The window and the machine's zone are the whole question. A zone left off buckets an
    /// evening west of Greenwich into the following day, which would make a chart disagree with
    /// the same list on the web.
    /// </summary>
    [Fact]
    public async Task The_request_carries_the_window_and_an_iana_zone_task_48c624ad()
    {
        var core = new FakeCore().AnswerOk("listInsights", Insights());
        var model = new ListInsightsViewModel(core);

        await model.LoadAsync("l1");

        var sent = JsonDocument.Parse(core.Sent[0]).RootElement;
        Assert.Equal("listInsights", sent.GetProperty("kind").GetString());
        Assert.Equal("l1", sent.GetProperty("listId").GetString());
        Assert.Equal(30, sent.GetProperty("days").GetInt32());
        var zone = sent.GetProperty("timeZone").GetString();
        Assert.False(string.IsNullOrEmpty(zone));
        Assert.DoesNotContain(" ", zone);
    }

    /// <summary>
    /// A null median means there was nothing to take a median of. It has to stay distinguishable
    /// from a fast one: zero would read as "completed instantly", and the sample that qualifies a
    /// real median has nothing to qualify.
    /// </summary>
    [Fact]
    public async Task A_null_median_is_not_a_zero_one_task_48c624ad()
    {
        var core = new FakeCore().AnswerOk("listInsights",
            Insights(createToComplete: new { medianMs = (long?)null, sampleSize = 0 }));
        var model = new ListInsightsViewModel(core);

        await model.LoadAsync("l1");

        Assert.False(model.TimeToComplete.HasMedian);
        Assert.Null(model.TimeToComplete.Key);
    }

    /// <summary>
    /// The unit is chosen here and the words come from the resources, so a reading is a key and a
    /// number. Minutes under an hour, hours under two days, then days — the web's own thresholds.
    /// </summary>
    [Theory]
    [InlineData(9_000L, "listInsights.duration.minutes", 1d)]
    [InlineData(1_800_000L, "listInsights.duration.minutes", 30d)]
    [InlineData(5_400_000L, "listInsights.duration.hours", 1.5d)]
    [InlineData(111_600_000L, "listInsights.duration.hours", 31d)]
    [InlineData(432_000_000L, "listInsights.duration.days", 5d)]
    public void A_median_is_shown_at_the_one_unit_that_reads_naturally_task_48c624ad(
        long ms, string key, double amount)
    {
        var label = ListInsightsViewModel.Label(ms, 3);

        Assert.Equal(key, label.Key);
        Assert.Equal(amount, label.Amount);
        Assert.Equal(3, label.SampleSize);
    }

    /// <summary>
    /// Both series share one scale, so "New" above "Completed" on a day means it on the chart
    /// too. Two independently scaled lines would be two charts drawn on top of each other.
    /// </summary>
    [Fact]
    public async Task The_two_series_are_drawn_against_one_scale_task_48c624ad()
    {
        var core = new FakeCore().AnswerOk("listInsights", Insights(series:
        [
            new { date = "2026-10-20", created = 0, completed = 0 },
            new { date = "2026-10-21", created = 5, completed = 10 },
            new { date = "2026-10-22", created = 10, completed = 5 },
        ]));
        var model = new ListInsightsViewModel(core);

        await model.LoadAsync("l1");

        Assert.Equal([0d, 0.5d, 1d], model.CreatedSeries.Select(point => point.X));
        Assert.Equal([0d, 0.5d, 1d], model.CreatedSeries.Select(point => point.Y));
        Assert.Equal([0d, 1d, 0.5d], model.CompletedSeries.Select(point => point.Y));
    }

    /// <summary>A window where nothing happened is a flat line, not a division by zero.</summary>
    [Fact]
    public async Task A_window_with_no_activity_flattens_rather_than_dividing_by_zero_task_48c624ad()
    {
        var core = new FakeCore().AnswerOk("listInsights", Insights(series:
        [
            new { date = "2026-10-21", created = 0, completed = 0 },
            new { date = "2026-10-22", created = 0, completed = 0 },
        ]));
        var model = new ListInsightsViewModel(core);

        await model.LoadAsync("l1");

        Assert.All(model.CreatedSeries, point => Assert.Equal(0d, point.Y));
        Assert.All(model.CompletedSeries, point => Assert.Equal(0d, point.Y));
    }

    /// <summary>
    /// A list that is not a board has no columns to time, which is not the same as a board whose
    /// columns are untimed — the section is absent rather than empty.
    /// </summary>
    [Fact]
    public async Task A_list_that_is_not_a_board_shows_no_columns_section_task_48c624ad()
    {
        var core = new FakeCore().AnswerOk("listInsights", Insights(lanes: null));
        var model = new ListInsightsViewModel(core);

        await model.LoadAsync("l1");

        Assert.False(model.HasLanes);
        Assert.False(model.LanesNeedHistory);
        Assert.Empty(model.Lanes);
    }

    /// <summary>
    /// A board whose every column has nothing to measure gets a sentence rather than a row of
    /// dashes: lane moves are only recorded from the AWTD-1329 deploy onward, so it fills in as
    /// cards move rather than meaning the board is idle.
    /// </summary>
    [Fact]
    public async Task A_board_with_no_recorded_moves_says_so_rather_than_showing_dashes_task_48c624ad()
    {
        var core = new FakeCore().AnswerOk("listInsights", Insights(lanes: new[]
        {
            new { role = "ready", name = "Ready", medianMs = (long?)null, sampleSize = 0 },
            new { role = "doing", name = "Doing", medianMs = (long?)null, sampleSize = 0 },
        }));
        var model = new ListInsightsViewModel(core);

        await model.LoadAsync("l1");

        Assert.True(model.HasLanes);
        Assert.True(model.LanesNeedHistory);

        var moved = new FakeCore().AnswerOk("listInsights", Insights(lanes: new[]
        {
            new { role = "ready", name = "Ready", medianMs = (long?)null, sampleSize = 0 },
            new { role = "doing", name = "Doing", medianMs = (long?)3_600_000, sampleSize = 2 },
        }));
        var board = new ListInsightsViewModel(moved);
        await board.LoadAsync("l1");

        Assert.False(board.LanesNeedHistory);
        Assert.Equal("Doing", board.Lanes[1].Name);
        Assert.Equal("listInsights.duration.hours", board.Lanes[1].Median.Key);
        Assert.False(board.Lanes[0].Median.HasMedian);
    }

    /// <summary>
    /// A contributor row carries numbers, never a sentence (AWTD-1333): the three profile metrics
    /// the ranking uses, then the activity that breaks their ties.
    /// </summary>
    [Fact]
    public async Task A_contributor_hands_over_numbers_for_the_resources_to_word_task_48c624ad()
    {
        var core = new FakeCore().AnswerOk("listInsights", Insights(contributors: new[]
        {
            new
            {
                userId = "u1", name = "Dana", image = (string?)null, isAIAgent = false,
                completed = 3, inspired = 1, supported = 2,
                created = 1, edited = 4, commented = 2, total = 6,
            },
        }));
        var model = new ListInsightsViewModel(core);

        await model.LoadAsync("l1");

        var row = Assert.Single(model.Contributors);
        Assert.Equal("Dana", row.Name);
        Assert.False(row.IsAIAgent);
        Assert.Equal(3, row.Completed);
        Assert.Equal(1, row.Inspired);
        Assert.Equal(2, row.Supported);
        Assert.Equal(4, row.Edited);
        Assert.True(model.HasContributors);
        Assert.False(model.NoActivity);
    }

    /// <summary>Nobody touched the list, which is a sentence rather than an empty box.</summary>
    [Fact]
    public async Task A_window_nobody_touched_says_so_task_48c624ad()
    {
        var core = new FakeCore().AnswerOk("listInsights", Insights());
        var model = new ListInsightsViewModel(core);

        await model.LoadAsync("l1");

        Assert.True(model.NoActivity);
        Assert.False(model.HasContributors);
    }

    /// <summary>Choosing a window asks again for that window, and marks it on the picker.</summary>
    [Fact]
    public async Task Choosing_a_window_reloads_for_it_task_48c624ad()
    {
        var core = new FakeCore()
            .AnswerOk("listInsights", Insights())
            .AnswerOk("listInsights", Insights(days: 7));
        var model = new ListInsightsViewModel(core);
        await model.LoadAsync("l1");

        await model.SetDaysAsync(7);

        Assert.Equal(7, model.Days);
        Assert.Equal(2, core.Sent.Count);
        Assert.Equal(7, JsonDocument.Parse(core.Sent[1]).RootElement.GetProperty("days").GetInt32());
        Assert.True(model.WindowChoices.Single(choice => choice.Days == 7).IsSelected);
        Assert.False(model.WindowChoices.Single(choice => choice.Days == 30).IsSelected);
    }

    /// <summary>
    /// An answer for a window the picker has already left is stale — drawing it would show 90
    /// days of history under a button that says "7 days".
    /// </summary>
    [Fact]
    public async Task An_answer_for_a_window_already_left_is_not_drawn_task_48c624ad()
    {
        var core = new FakeCore()
            .AnswerOk("listInsights", Insights())
            .AnswerOk("listInsights", Insights(days: 90));
        var model = new ListInsightsViewModel(core);
        await model.LoadAsync("l1");
        var before = model.CreatedTotal;

        await model.SetDaysAsync(7);

        Assert.Equal(7, model.Days);
        Assert.Equal(before, model.CreatedTotal);
        Assert.False(model.Failed, "a window that answered is not a failure, only not this one");
    }

    /// <summary>
    /// A failure says so and leaves the picker working, so another window is a fresh attempt
    /// rather than a screen that has to be closed and reopened.
    /// </summary>
    [Fact]
    public async Task A_refused_window_can_be_followed_by_a_working_one_task_48c624ad()
    {
        var core = new FakeCore()
            .AnswerFailure("listInsights", AstridFailureKind.Offline)
            .AnswerOk("listInsights", Insights(days: 7));
        var model = new ListInsightsViewModel(core);

        await model.LoadAsync("l1");
        Assert.True(model.Failed);
        Assert.False(model.HasInsights);

        await model.SetDaysAsync(7);

        Assert.False(model.Failed);
        Assert.True(model.HasInsights);
        Assert.Equal(60, model.CreatedTotal);
    }

    /// <summary>
    /// The zone sent must be one the server knows. Windows names its own zones
    /// ("Pacific Standard Time"), which the route answers 400 to, so it is converted — and a
    /// machine whose zone has no IANA name falls back to UTC rather than to an error.
    /// </summary>
    [Fact]
    public void The_machines_zone_is_sent_as_an_iana_id_task_48c624ad()
    {
        var zone = ListInsightsViewModel.SystemTimeZone();

        Assert.False(string.IsNullOrEmpty(zone));
        Assert.DoesNotContain(" ", zone);
        Assert.True(
            zone == "UTC" || TimeZoneInfo.TryConvertIanaIdToWindowsId(zone, out _),
            $"'{zone}' is not a zone the server would accept");
    }
}
