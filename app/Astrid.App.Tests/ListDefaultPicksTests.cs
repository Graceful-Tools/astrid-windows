using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// A list's defaults editor asks the core what it may offer (task 92830d81).
/// </summary>
/// <remarks>
/// <c>ListSettingsViewModel</c> held four tables — the repeats, the when options, the priority
/// keys and an inline range of hours — under a comment saying the core had no command for them,
/// while iOS and Android held their own copies. The rule is
/// <c>astrid_rules::rows::list_default_picks</c>, locked by
/// <c>contracts/fixtures/list-default-picks.json</c>; these go through the real door, so a pin
/// that moved the rule fails here rather than in a picker a person is looking at.
/// </remarks>
public class ListDefaultPicksTests
{
    [Fact]
    public void The_core_offers_the_four_pickers_task_92830d81()
    {
        var picks = AstridClient.ListDefaultPicks(new ListDefaults());

        Assert.Equal(["0", "1", "2", "3"], picks.Priority.Select(pick => pick.Value));
        Assert.Equal(
            ["never", "daily", "weekly", "monthly", "yearly"],
            picks.Repeating.Select(pick => pick.Value));
        Assert.Equal(
            ["none", "today", "tomorrow", "next_week"],
            picks.DueDate.Select(pick => pick.Value));
        // All day, then 06:00 to 22:00 inclusive.
        Assert.Equal(18, picks.DueTime.Count);
        Assert.Null(picks.DueTime[0].Value);
        Assert.Equal("06:00", picks.DueTime[1].Value);
        Assert.Equal("22:00", picks.DueTime[17].Value);
    }

    /// <summary>
    /// Every label is a resource key except an hour's: <c>06:00</c> reads as "6 AM" or as
    /// "06:00" depending on the reader's clock, so the shell formats that one and only that one.
    /// </summary>
    [Fact]
    public void Every_label_is_a_resource_key_except_an_hour_s()
    {
        var picks = AstridClient.ListDefaultPicks(new ListDefaults());

        Assert.All(
            picks.Priority.Concat(picks.Repeating).Concat(picks.DueDate),
            pick => Assert.False(string.IsNullOrEmpty(pick.Key)));
        Assert.Equal("repeating.one_time_only", picks.Repeating[0].Key);
        Assert.Equal("defaults.all_day", picks.DueTime[0].Key);
        Assert.All(picks.DueTime.Skip(1), pick => Assert.Null(pick.Key));
    }

    /// <summary>
    /// A list nobody has configured is not four empty pickers: the one-time, no-date and
    /// no-priority rows are ticked, which is what web reads (<c>defaultRepeating || "never"</c>).
    /// </summary>
    [Fact]
    public void An_unconfigured_list_ticks_once_no_date_and_no_priority()
    {
        var picks = AstridClient.ListDefaultPicks(new ListDefaults());

        Assert.Equal("0", Selected(picks.Priority));
        Assert.Equal("never", Selected(picks.Repeating));
        Assert.Equal("none", Selected(picks.DueDate));
        Assert.True(picks.DueTime[0].IsSelected, "all day");
    }

    [Fact]
    public void The_list_s_own_defaults_are_the_ones_ticked()
    {
        var picks = AstridClient.ListDefaultPicks(new ListDefaults
        {
            Priority = 3,
            Repeating = "weekly",
            DueDate = "tomorrow",
            DueTime = "09:00",
        });

        Assert.Equal("3", Selected(picks.Priority));
        Assert.Equal("weekly", Selected(picks.Repeating));
        Assert.Equal("tomorrow", Selected(picks.DueDate));
        Assert.Equal("09:00", Selected(picks.DueTime));
    }

    /// <summary>
    /// A time set where the control is free-form — the web's <c>TimePicker</c> — is offered
    /// rather than rounded to an hour this picker happens to carry, and it lands in clock order.
    /// </summary>
    [Fact]
    public void A_time_that_is_not_a_quick_hour_is_offered_in_clock_order()
    {
        var picks = AstridClient.ListDefaultPicks(new ListDefaults { DueTime = "09:15" });

        Assert.Equal(19, picks.DueTime.Count);
        Assert.Equal("09:15", picks.DueTime[5].Value);
        Assert.Equal("09:15", Selected(picks.DueTime));
        Assert.False(picks.DueTime[0].IsSelected);
    }

    /// <summary>
    /// Unset and unrecognised are different. A <c>custom</c> repeat — which this picker cannot
    /// draw — ticks nothing, rather than reading as "once" and being lost on the next save.
    /// </summary>
    [Fact]
    public void A_stored_value_the_picker_cannot_draw_ticks_nothing()
    {
        var picks = AstridClient.ListDefaultPicks(new ListDefaults { Repeating = "custom" });

        Assert.Null(Selected(picks.Repeating));
        Assert.DoesNotContain(picks.Repeating, pick => pick.IsSelected);
    }

    private static string? Selected(IReadOnlyList<DefaultPick> picks) =>
        picks.FirstOrDefault(pick => pick.IsSelected)?.Value;
}
