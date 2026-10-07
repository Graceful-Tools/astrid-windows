using System.Text.RegularExpressions;
using System.Xml.Linq;
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
/// </remarks>
public sealed class CorePinFollowsIosTests
{
    /// <summary>The repeat presets, as iOS spells them (D49).</summary>
    private static readonly string[] RepeatPresetKeys =
    [
        "repeating.one_time_only", "repeating.daily", "repeating.weekly", "repeating.monthly",
        "repeating.yearly", "repeating.custom",
    ];

    /// <summary>The completion filter's rows, which gained two and lost one.</summary>
    private static readonly string[] CompletionFilterKeys =
    [
        "filter.completion.recent", "filter.completion.all", "filter.completion.completed",
        "filter.completion.incomplete",
    ];

    /// <summary>The priority filter's rows, which the core names per level.</summary>
    private static readonly string[] PriorityFilterKeys =
    [
        "lists.highest_priority", "lists.high_priority", "lists.medium_priority",
        "lists.low_priority",
    ];

    /// <summary>Keys the pinned core no longer sends. A word for one is a word nobody can reach.</summary>
    private static readonly string[] RetiredKeys =
    [
        "filter.completion.hide",
        "repeat.never", "repeat.daily", "repeat.weekly", "repeat.monthly", "repeat.yearly",
        "repeat.custom",
    ];

    /// <summary>
    /// Every key the pinned core sends has a word, in the resources and in the fallback table.
    /// </summary>
    /// <remarks>
    /// Both, because they fail differently: a missing resource is a key on screen in a packaged
    /// build, and a missing fallback is a key on screen in an unpackaged one. A key with neither
    /// comes back as itself — <c>Strings.Get</c> says so — so "repeating.daily" is what the reader
    /// sees.
    /// </remarks>
    [Fact]
    public void Every_key_the_pinned_core_sends_has_a_word_task_6ee938cc()
    {
        var app = LocalisationTests.AppDirectory();
        var resources = ResourceNames(app);
        var fallback = FallbackKeys(app);

        var missing = RepeatPresetKeys.Concat(CompletionFilterKeys).Concat(PriorityFilterKeys)
            .SelectMany(key => new[]
                {
                    resources.Contains(Flatten(key)) ? null : $"{key} — no resource",
                    fallback.Contains(key) ? null : $"{key} — no fallback",
                }.OfType<string>())
            .ToList();

        Assert.True(missing.Count == 0,
            "the pinned core sends keys this shell has no word for:\n  " + string.Join("\n  ", missing));
    }

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
            "repeat.on_day_of_month", "repeat.on_nth_weekday", "repeat.on_month_and_day",
            "repeat.ends_after", "repeat.ends_on",
        ];

        var unhandled = summaryKeys
            .Where(key => !switchBody.Contains($"\"{key}\"", StringComparison.Ordinal))
            .ToList();

        Assert.True(unhandled.Count == 0,
            "RepeatSummaryConverter has no arm for these, so each reads as nothing:\n  "
            + string.Join("\n  ", unhandled));
    }

    /// <summary>
    /// A list's default-repeat picker offers the same keys the core's preset list does.
    /// </summary>
    /// <remarks>
    /// That picker is the shell's own — the core has no command for a list's defaults — so it
    /// builds its labels from the repeat values. It still has to build the keys the resources
    /// carry, and after D49 those are <c>repeating.*</c>, with "never" spelt
    /// <c>repeating.one_time_only</c>.
    /// </remarks>
    [Fact]
    public void The_list_default_repeat_picker_uses_the_cores_preset_keys_task_6ee938cc()
    {
        var source = File.ReadAllText(Path.Combine(
            LocalisationTests.AppDirectory(), "..", "Astrid.App.ViewModels", "ListSettingsViewModel.cs"));

        Assert.DoesNotContain("$\"repeat.{value}\"", source, StringComparison.Ordinal);
        Assert.Contains("repeating.one_time_only", source, StringComparison.Ordinal);
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
