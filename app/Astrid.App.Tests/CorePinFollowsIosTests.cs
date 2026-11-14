using System.Text.RegularExpressions;
using System.Xml.Linq;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// What the shell owes the core after the pin moved onto the follow-iOS revisions (task 6ee938cc).
/// </summary>
/// <remarks>
/// <para>
/// The core returns keys and change names, never words and never screens. So a revision that
/// renames a key or splits a change in two breaks nothing that compiles: the picker draws
/// <c>repeating.daily</c> where it used to say "Daily", the repeat summary falls through its switch
/// and reads "Does not repeat", and an Outbox delivery stops redrawing anything. Each of those is a
/// silent regression, which is why they are asserted here rather than left to a screenshot.
/// </para>
/// <para>
/// The key lists below are the pinned core's, read from <c>astrid-rules</c>: <c>rows/repeat.rs</c>
/// for the presets and the summary, <c>rows/filter_picks.rs</c> for the filter sheet. They are
/// written out rather than derived because the core is a separate repository pinned by revision —
/// its source is not here to scan, and the bindings contract next door makes the same trade for
/// the same reason.
/// </para>
/// <para>
/// <strong>One of those lists is gone (task b63d8679.)</strong> There used to be fourteen keys
/// copied out here under the heading "every key the pinned core sends has a word", and astrid-core's
/// <c>copy.rs</c> names this file by name as the hand copy its string catalog replaces. The core
/// now publishes the whole set — <c>astrid_core::copy_keys()</c>, computed from the very tables the
/// pickers read — so that question is asked of all 106 of them in
/// <c>crates/astrid-ffi/tests/copy_catalog_contract.rs</c>, in Rust, where the pinned crate is
/// actually linked. What stays here is what a Rust test cannot see: the retired keys, and the arms
/// of a WinUI converter's <c>switch</c>.
/// </para>
/// </remarks>
public sealed class CorePinFollowsIosTests
{
    /// <summary>Keys the pinned core no longer sends. A word for one is a word nobody can reach.</summary>
    /// <remarks>
    /// <c>repeat.daily</c> and its four siblings were here until task b63d8679, on the belief that
    /// D49 retired them with the rest. D49 renamed the repeat <em>picker</em>'s keys; the repeat
    /// <em>filter</em> group still asks for <c>repeat.daily</c>, which is why this shell drew that
    /// key as text in the filter sheet (CONTRACTS D60) while a test asserted it must have no word.
    /// That cannot happen quietly again: a key listed here that the core still emits is required to
    /// have a word by <c>copy_catalog_contract.rs</c> and required not to by this test, so the two
    /// gates disagree out loud rather than agreeing on a wrong answer.
    /// </remarks>
    private static readonly string[] RetiredKeys =
    [
        "filter.completion.hide", "repeat.never",
    ];

    /// <summary>
    /// And the keys it stopped sending are gone from both, rather than left behind.
    /// </summary>
    /// <remarks>
    /// A renamed key leaves its old word sitting in the resw, where it is a translator's wasted
    /// hour and a reader's false clue that the shell still draws that row.
    /// </remarks>
    [Fact]
    public void The_keys_the_pinned_core_retired_are_gone_task_6ee938cc()
    {
        var app = LocalisationTests.AppDirectory();
        var resources = ResourceNames(app);
        var fallback = FallbackKeys(app);

        var stale = RetiredKeys
            .SelectMany(key => new[]
                {
                    resources.Contains(Flatten(key)) ? $"{key} — still in the resw" : null,
                    fallback.Contains(key) ? $"{key} — still in the fallback table" : null,
                }.OfType<string>())
            .ToList();

        Assert.True(stale.Count == 0,
            "words for keys the pinned core no longer sends:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// The repeat summary has an arm for every part the pinned core can put in one.
    /// </summary>
    /// <remarks>
    /// <c>RepeatSummaryConverter.Say</c> ends in <c>_ => string.Empty</c>, so an unknown key is not
    /// an exception but a silence — and a summary of nothing but silences renders as "Does not
    /// repeat" on a task that repeats daily. The switch is read out of the source because the
    /// converter is a WinUI type and this project is deliberately plain <c>net9.0</c>.
    /// </remarks>
    [Fact]
    public void The_repeat_summary_knows_every_part_the_pinned_core_sends_task_6ee938cc()
    {
        var switchBody = RepeatSummarySwitch(LocalisationTests.AppDirectory());
        string[] summaryKeys =
        [
            // Renamed to iOS's spelling by D49; the qualifiers below kept theirs.
            "repeating.daily", "repeating.weekly", "repeating.monthly", "repeating.yearly",
            "repeat.from_due_date", "repeat.every_n_days", "repeat.every_n_weeks",
            "repeat.every_n_months", "repeat.every_n_years", "repeat.on_weekdays",
            "repeat.on_day_of_month", "repeat.on_nth_weekday", "repeat.on_last_weekday",
            "repeat.on_month_and_day", "repeat.ends_after", "repeat.ends_on",
        ];

        var unhandled = summaryKeys
            .Where(key => !switchBody.Contains($"\"{key}\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(unhandled.Count == 0,
            "RepeatSummaryConverter has no arm for these, so each reads as nothing:\n  "
            + string.Join("\n  ", unhandled));
    }

    /// <summary>
    /// "The last Thursday of the month" reads as words, not as a silence (task e4f160cc).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A monthly repeat on <c>weekOfMonth: -1</c> — iCalendar's <c>BYDAY=-1MO</c>, which web settled
    /// on in AWTD-1295 — does not come back as <c>repeat.on_nth_weekday</c> with a count of −1,
    /// because "the −1th Thursday" is not a sentence. The core gives it its own key with no count,
    /// so the shell owes it a separate word and a separate arm.
    /// </para>
    /// <para>
    /// Asserted on its own rather than only by adding a string to the lists above, because the three
    /// failures differ: no resource is the key on screen in a packaged build, no fallback is the key
    /// on screen in an unpackaged one, and no arm in <c>RepeatSummaryConverter.Say</c> is worse than
    /// either — the switch ends in <c>_ => string.Empty</c>, so the part vanishes and a task that
    /// repeats monthly reads "Does not repeat".
    /// </para>
    /// </remarks>
    [Fact]
    public void The_last_weekday_of_the_month_has_a_word_and_an_arm_task_e4f160cc()
    {
        var app = LocalisationTests.AppDirectory();
        const string key = "repeat.on_last_weekday";

        Assert.Contains(Flatten(key), ResourceNames(app));
        Assert.Contains(key, FallbackKeys(app));
        Assert.Contains($"\"{key}\"", RepeatSummarySwitch(app), StringComparison.Ordinal);
    }

    /// <summary>
    /// A list's default-repeat picker offers the same keys the core's preset list does, and the
    /// shell has a resource for each of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to read the view model's own source text for the key it built, because that
    /// picker was the shell's own. It is the core's since task 92830d81
    /// (<c>astrid_rules::rows::list_default_picks</c>), so the keys are asked for rather than
    /// grepped — the hand copy the cross-client review named. After D49 they are
    /// <c>repeating.*</c>, with "never" spelt <c>repeating.one_time_only</c>.
    /// </para>
    /// <para>
    /// Asking the door also catches the half a source grep never could: a key the core emits that
    /// this shell has no resource for, which draws as the key itself.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_list_default_repeat_picker_uses_the_cores_preset_keys_task_6ee938cc()
    {
        var picks = AstridClient.ListDefaultPicks(new ListDefaults());
        var keys = picks.Repeating.Select(pick => pick.Key).ToList();

        Assert.Contains("repeating.one_time_only", keys);
        Assert.DoesNotContain(keys, key => key?.StartsWith("repeat.", StringComparison.Ordinal) == true);

        var resources = ResourceNames(LocalisationTests.AppDirectory());
        foreach (var key in picks.Priority.Concat(picks.Repeating).Concat(picks.DueDate)
                     .Concat(picks.DueTime)
                     .Select(pick => pick.Key)
                     .Where(key => key is not null))
        {
            Assert.Contains(Flatten(key!), resources);
        }
    }

    /// <summary>
    /// The due-date flyout still has a way to clear the date.
    /// </summary>
    /// <remarks>
    /// It used to be a row the core supplied; the view model's half is asserted next door in
    /// <c>TaskDetailViewModelTests</c>. This is the other half, because a method nothing calls
    /// clears nothing: without a control in the flyout the date is one-way on screen, however
    /// green the view-model test is.
    /// </remarks>
    [Fact]
    public void The_due_flyout_offers_a_way_to_clear_the_date_task_6ee938cc()
    {
        var pane = File.ReadAllText(Path.Combine(
            LocalisationTests.AppDirectory(), "Views", "TaskDetailPane.xaml"));

        Assert.Contains("OnDueCleared", pane, StringComparison.Ordinal);
    }

    private static string Flatten(string key) => key.Replace('.', '_');

    private static HashSet<string> ResourceNames(string app) =>
        XDocument.Load(Path.Combine(app, "Strings", "en-US", "Resources.resw")).Root!.Elements("data")
            .Select(data => data.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>The keys in <c>Strings.cs</c>'s fallback table, read from its source.</summary>
    private static HashSet<string> FallbackKeys(string app)
    {
        var source = File.ReadAllText(Path.Combine(app, "Strings.cs"));
        var keys = Regex.Matches(source, @"\[""([^""]+)""\]\s*=")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(keys.Count >= 100, $"found only {keys.Count} fallback keys; the scan has stopped working");
        return keys;
    }

    /// <summary>The body of <c>RepeatSummaryConverter.Say</c>'s switch, and nothing else.</summary>
    private static string RepeatSummarySwitch(string app)
    {
        var source = File.ReadAllText(Path.Combine(app, "Converters.cs"));
        var start = source.IndexOf("return part.Key switch", StringComparison.Ordinal);
        Assert.True(start >= 0, "RepeatSummaryConverter.Say no longer switches on the key");
        var end = source.IndexOf("_ => string.Empty", start, StringComparison.Ordinal);
        Assert.True(end > start, "the switch no longer ends in a silent default");
        return source[start..end];
    }
}
