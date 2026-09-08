using System.Collections.Generic;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Astrid.App;

/// <summary>
/// Every word this app puts on screen.
/// </summary>
/// <remarks>
/// <para>
/// The core returns keys, never words — <c>picker.today</c>, <c>filter.due.overdue</c> — so the
/// date arithmetic and the filter rules live in one place and the language lives here, where it
/// can be translated. This class is the only thing in the shell that turns a key into text.
/// </para>
/// <para>
/// The strings come from <c>Strings/en-US/Resources.resw</c>, which is what a translator adds a
/// language beside. The table below is the fallback, and it exists for one specific failure: an
/// unpackaged build whose <c>.pri</c> file did not make it next to the executable loses every
/// string at once, and an app whose every label is blank is much harder to diagnose than one that
/// is merely not translated. It is also what the tests read, so they do not need a resource
/// context.
/// </para>
/// <para>
/// A key with no string anywhere comes back as itself. Ugly on screen and obvious in a screenshot,
/// which is the point — blank would be invisible.
/// </para>
/// </remarks>
internal static class Strings
{
    private static readonly ResourceMap? Map = Load();

    /// <summary>The word for a key, in the reader's language when there is one.</summary>
    internal static string Get(string key)
    {
        if (Map is not null)
        {
            try
            {
                // A resource name cannot contain a dot, and a slash makes a scope — which would
                // make filter.button both a value and the parent of filter.button.active, and the
                // resource compiler refuses that. So the keys are stored flat, with underscores.
                var candidate = Map.TryGetValue(key.Replace('.', '_'))?.ValueAsString;
                if (!string.IsNullOrEmpty(candidate))
                {
                    return candidate;
                }
            }
            catch (Exception)
            {
                // A missing subtree throws rather than returning null. Fall through to the table.
            }
        }
        return Fallback.TryGetValue(key, out var text) ? text : key;
    }

    /// <summary>The word for a key with a number in it — "Every 2 weeks".</summary>
    internal static string Get(string key, params object[] values) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), values);

    private static ResourceMap? Load()
    {
        try
        {
            return new ResourceManager().MainResourceMap.TryGetSubtree("Resources");
        }
        catch (Exception)
        {
            // No resource file beside the executable. English, from the table below.
            return null;
        }
    }

    private static readonly Dictionary<string, string> Fallback = new(StringComparer.Ordinal)
    {
        ["picker.no_due_date"] = "No due date",
        ["picker.today"] = "Today",
        ["picker.tomorrow"] = "Tomorrow",
        ["picker.in_3_days"] = "In 3 days",
        ["picker.next_week"] = "Next week",
        ["picker.morning"] = "Morning",
        ["picker.afternoon"] = "Afternoon",
        ["picker.evening"] = "Evening",
        ["picker.night"] = "Night",
        ["assignee.unassigned"] = "Unassigned",
        ["user.unknown"] = "Unknown user",
        ["due.today"] = "Today",
        ["due.tomorrow"] = "Tomorrow",
        ["due.yesterday"] = "Yesterday",
        ["due.none"] = "No due date",
        ["repeat.never"] = "Never",
        ["repeat.daily"] = "Daily",
        ["repeat.weekly"] = "Weekly",
        ["repeat.monthly"] = "Monthly",
        ["repeat.yearly"] = "Yearly",
        ["repeat.custom"] = "Custom…",
        ["repeat.none"] = "Does not repeat",
        ["repeat.every_day"] = "Every day",
        ["repeat.every_n_days"] = "Every {0} days",
        ["repeat.every_week"] = "Every week",
        ["repeat.every_n_weeks"] = "Every {0} weeks",
        ["repeat.every_month"] = "Every month",
        ["repeat.every_n_months"] = "Every {0} months",
        ["repeat.every_year"] = "Every year",
        ["repeat.every_n_years"] = "Every {0} years",
        ["repeat.on_weekdays"] = "on {0}",
        ["repeat.on_day_of_month"] = "on the {0}",
        ["repeat.on_nth_weekday"] = "on the {0} {1}",
        ["repeat.on_month_and_day"] = "on {0} {1}",
        ["repeat.ends_after"] = "({0}x)",
        ["repeat.ends_on"] = "until {0}",
        ["repeat.from_due_date"] = "from due date",
        ["reminder.none"] = "No reminder",
        ["reminder.set"] = "Reminder set",
        ["reminder.at_due_time"] = "At the time it is due",
        ["reminder.5_minutes_before"] = "5 minutes before",
        ["reminder.15_minutes_before"] = "15 minutes before",
        ["reminder.30_minutes_before"] = "30 minutes before",
        ["reminder.hour_before"] = "An hour before",
        ["reminder.2_hours_before"] = "2 hours before",
        ["reminder.day_before"] = "A day before",
        ["reminder.week_before"] = "A week before",
        ["filter.any"] = "Any",
        ["filter.button"] = "Filter",
        ["filter.button.active"] = "Filtered",
        ["filter.completion"] = "Finished tasks",
        ["filter.completion.recent"] = "Recently finished",
        ["filter.completion.hide"] = "Hide them",
        ["filter.completion.all"] = "Show them all",
        ["filter.priority"] = "Priority",
        ["filter.due"] = "Due",
        ["filter.due.overdue"] = "Overdue",
        ["filter.due.today"] = "Today",
        ["filter.due.this_week"] = "This week",
        ["filter.due.this_month"] = "This month",
        ["filter.due.none"] = "No due date",
        ["filter.assignee"] = "Assigned to",
        ["filter.assignee.me"] = "Me",
        ["filter.assignee.someone_else"] = "Somebody else",
        ["filter.assignee.nobody"] = "Nobody",
        ["filter.repeat"] = "Repeat",
        ["filter.repeat.never"] = "Does not repeat",
        ["filter.assigned_by"] = "Assigned by",
        ["filter.assigned_by.me"] = "Me",
        ["filter.assigned_by.someone_else"] = "Somebody else",
        ["filter.lists"] = "Lists",
        ["filter.lists.in_a_list"] = "In a list",
        ["filter.lists.not_in_a_list"] = "Not in a list",
        ["filter.lists.public"] = "In a public list",
        ["priority.none"] = "No priority",
        ["priority.low"] = "Low",
        ["priority.medium"] = "Medium",
        ["priority.high"] = "High",
        ["sort"] = "Sort by",
        ["sort.auto"] = "Automatic",
        ["sort.priority"] = "Priority",
        ["sort.when"] = "When it is due",
        ["sort.created"] = "When it was added",
        ["sort.manual"] = "The order I arranged",
        ["chat.sending"] = "{0} · sending",
        ["board.column_heading"] = "{0} ({1})",
        ["role.owner"] = "Owner",
        ["role.admin"] = "Admin",
        ["role.member"] = "Member",
        ["role.viewer"] = "Viewer",
        ["timer.start"] = "Start timer",
        ["timer.stop"] = "Stop timer",
        ["timer.logged"] = "{0} logged",
        ["stats.completed"] = "{0} finished",
        ["stats.inspired"] = "{0} inspired",
        ["stats.supported"] = "{0} supported",
    };

    /// <summary>The fallback table, for the tests that check every key has a string.</summary>
    internal static IReadOnlyDictionary<string, string> All => Fallback;
}
