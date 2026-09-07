using Astrid.Core.Bindings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System.Text;
using Windows.UI;

namespace Astrid.App;

/// <summary>
/// A colour for a priority, or for a list's hex.
/// </summary>
/// <remarks>
/// The hex values match every other client — see <c>Task.Priority.color</c> on Apple. They are
/// stated in App.xaml as brushes and read here rather than written twice.
/// </remarks>
public sealed partial class PriorityBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        int priority => new SolidColorBrush(PriorityColor(priority)),
        // A list colour arrives as the hex the server stores.
        string hex => new SolidColorBrush(Parse(hex)),
        _ => new SolidColorBrush(Colors.Transparent),
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("colours are read-only in the UI");

    private static Color PriorityColor(int priority) => priority switch
    {
        3 => Color.FromArgb(0xFF, 0xEF, 0x44, 0x44),
        2 => Color.FromArgb(0xFF, 0xF5, 0x9E, 0x0B),
        1 => Color.FromArgb(0xFF, 0x10, 0xB9, 0x81),
        // No priority draws nothing at all rather than grey: a stripe on every row is a stripe
        // that says nothing, and the point of the stripe is that it is glanceable.
        _ => Colors.Transparent,
    };

    /// <summary>
    /// Read <c>#rrggbb</c> or <c>#aarrggbb</c>.
    /// </summary>
    /// <remarks>
    /// Anything unreadable falls back to the brand blue rather than throwing. A list colour comes
    /// from the server and from other clients; one bad value must not take the window down.
    /// </remarks>
    private static Color Parse(string hex)
    {
        var text = hex.TrimStart('#');
        if (text.Length == 6 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
        {
            return Color.FromArgb(0xFF, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
        if (text.Length == 8 && uint.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var argb))
        {
            return Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        }
        return Color.FromArgb(0xFF, 0x3B, 0x82, 0xF6);
    }
}

/// <summary>Turns a subtask depth into a left margin.</summary>
public sealed partial class DepthIndentConverter : IValueConverter
{
    /// <summary>
    /// How far one level of nesting moves a row.
    /// </summary>
    /// <remarks>
    /// Enough to read as nested at a glance and small enough that four levels still leave room for
    /// a title. The depth itself is capped by the core — see <c>filters::subtasks</c> — so a bad
    /// parent chain cannot indent a row off the screen.
    /// </remarks>
    private const double PerDepth = 24;

    public object Convert(object value, Type targetType, object parameter, string language) =>
        new Thickness(value is int depth ? depth * PerDepth : 0, 0, 0, 0);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("indentation is read-only in the UI");
}

/// <summary>
/// Shows an element when there is something to show.
/// </summary>
/// <remarks>
/// Handles a bool, a count, and a string, because all three mean "is there anything here?" and
/// three converters that differ only in their type check would be three places to fix the next
/// time the answer changes.
/// </remarks>
public sealed partial class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        bool flag => flag ? Visibility.Visible : Visibility.Collapsed,
        int count => count > 0 ? Visibility.Visible : Visibility.Collapsed,
        string text => string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible,
        null => Visibility.Collapsed,
        _ => Visibility.Visible,
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("visibility is read-only in the UI");
}

/// <summary>What goes above a chat bubble: who said it, and whether it has landed.</summary>
/// <remarks>
/// In the shell rather than on the model, because "· sending" is a word in a language. A message
/// the server wrote gets no byline at all — it is nobody's message.
/// </remarks>
public sealed partial class MessageBylineConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not MessageRow message || message.IsSystem)
        {
            return string.Empty;
        }
        var author = message.AuthorName ?? Strings.Get("user.unknown");
        return message.IsPending ? Strings.Get("chat.sending", author) : author;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a byline is read-only in the UI");
}

/// <summary>A board column's header: its name and how many cards it holds.</summary>
/// <remarks>
/// The count is of every card in the column, not of the ones that crossed the boundary — the same
/// distinction the task list's total makes.
/// </remarks>
public sealed partial class ColumnHeadingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is BoardColumn column
            ? Strings.Get("board.column_heading", column.Name, column.Total)
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a heading is read-only in the UI");
}

/// <summary>What the filter button says: whether anything is being hidden.</summary>
/// <remarks>
/// A list quietly showing half its tasks because of a setting made last month — possibly on
/// another client — is a list that looks like it lost them. The button is where that gets noticed.
/// </remarks>
public sealed partial class FilterStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        Strings.Get(value is true ? "filter.button.active" : "filter.button");

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a filter is chosen from the sheet");
}

/// <summary>Visible when the value is false — for the half of a pair that is not showing.</summary>
public sealed partial class NotVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("visibility is read-only in the UI");
}

/// <summary>
/// Turns the core's due-date answer into words.
/// </summary>
/// <remarks>
/// <para>
/// The core deliberately returns a key rather than a sentence — see <c>astrid_core::rows</c> — so
/// the arithmetic lives in one place and the words live where they can be translated. This is the
/// other half of that split.
/// </para>
/// <para>
/// The literals here are the placeholder for the <c>.resw</c> resources that arrive with
/// localisation. They are in one class rather than scattered through XAML precisely so that swap
/// is a small change.
/// </para>
/// </remarks>
public sealed partial class DueLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not DueLabel due)
        {
            return string.Empty;
        }

        return due.Key switch
        {
            "today" => Strings.Get("due.today"),
            "tomorrow" => Strings.Get("due.tomorrow"),
            "yesterday" => Strings.Get("due.yesterday"),
            "on" => Format(due),
            // A task with no date reads as "No due date" rather than as nothing: this text is on a
            // button, and a button with no label is one nobody knows they can press.
            _ => Strings.Get("due.none"),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("due labels are read-only in the UI");

    /// <summary>
    /// A date far enough out to need naming, in the reader's own format.
    /// </summary>
    /// <remarks>
    /// With the weekday, because what you want to know about "12 August" when scheduling is
    /// whether it is a Wednesday or a Saturday, and nothing else on the row says so.
    /// </remarks>
    private static string Format(DueLabel due)
    {
        if (!DateOnly.TryParse(due.Date, out var date))
        {
            return string.Empty;
        }

        var day = date.ToDateTime(TimeOnly.MinValue).ToString("ddd d MMM");
        return due.Time is { Length: > 0 } time ? $"{day}, {time}" : day;
    }
}

/// <summary>
/// A resource key from the core, as words.
/// </summary>
/// <remarks>
/// <para>
/// The core returns <c>picker.today</c> rather than "Today" on purpose — see
/// <c>astrid_core::rows::due_picks</c> — so the arithmetic lives in one place and the words live
/// where they can be translated. This is the lookup, and it is the only place in the shell that
/// turns a key into English.
/// </para>
/// <para>
/// The table here is the placeholder for the <c>.resw</c> resources. Keeping it in one converter
/// rather than scattered through XAML is what makes that swap a small change; an unknown key falls
/// back to itself, which is ugly on screen and immediately obvious in a screenshot, rather than
/// blank and invisible.
/// </para>
/// </remarks>
/// <summary>Whether a task has a reminder, as words.</summary>
/// <remarks>
/// The state, not the instant: the row says whether anything will happen, and the picker beneath
/// it says when. A row showing a raw timestamp beside a due date is two dates and no explanation.
/// </remarks>
public sealed partial class ReminderStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        Strings.Get(value is true ? "reminder.set" : "reminder.none");

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a reminder is chosen from the picker");
}

/// <summary>
/// A repeat, as a sentence.
/// </summary>
/// <remarks>
/// <para>
/// The core hands over parts — a key, a number, some weekday names — rather than a sentence,
/// because a sentence assembled from fragments is what does not survive translation: the order of
/// "every 2 weeks" and "on Mondays" is not the English order everywhere. Joining them is this
/// converter's job, and it is the only place in the shell that knows the English order.
/// </para>
/// <para>
/// Weekday and month names come from .NET's culture data rather than a table typed here, so they
/// are already right in whatever the reader's Windows is set to.
/// </para>
/// </remarks>
public sealed partial class RepeatSummaryConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not IEnumerable<SummaryPart> parts)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        foreach (var part in parts)
        {
            var piece = Say(part);
            if (piece.Length == 0)
            {
                continue;
            }
            if (text.Length > 0)
            {
                // Only the trailing qualifier takes a comma, the way the Apple clients word it.
                text.Append(part.Key == "repeat.from_due_date" ? ", " : " ");
            }
            text.Append(piece);
        }
        // A task that does not repeat says so. An empty label beside "No due date" reads as a
        // field that failed to load rather than one with nothing in it.
        return text.Length > 0 ? text.ToString() : Strings.Get("repeat.none");
    }

    private static string Say(SummaryPart part)
    {
        var count = part.Count ?? 1;
        var format = System.Globalization.CultureInfo.CurrentCulture;
        // The singular is its own key rather than a rule about the number: "every 1 week" is wrong
        // in English and differently wrong in languages with more than two plural forms.
        return part.Key switch
        {
            "repeat.daily" or "repeat.weekly" or "repeat.monthly" or "repeat.yearly"
                or "repeat.from_due_date" => Strings.Get(part.Key),
            "repeat.every_n_days" => count == 1
                ? Strings.Get("repeat.every_day")
                : Strings.Get("repeat.every_n_days", count),
            "repeat.every_n_weeks" => count == 1
                ? Strings.Get("repeat.every_week")
                : Strings.Get("repeat.every_n_weeks", count),
            "repeat.every_n_months" => count == 1
                ? Strings.Get("repeat.every_month")
                : Strings.Get("repeat.every_n_months", count),
            "repeat.every_n_years" => count == 1
                ? Strings.Get("repeat.every_year")
                : Strings.Get("repeat.every_n_years", count),
            "repeat.on_weekdays" => Strings.Get(
                "repeat.on_weekdays", string.Join(", ", part.Values.Select(Weekday))),
            "repeat.on_day_of_month" => Strings.Get("repeat.on_day_of_month", Ordinal(count)),
            "repeat.on_nth_weekday" => Strings.Get(
                "repeat.on_nth_weekday",
                Ordinal(count),
                Weekday(part.Values.FirstOrDefault() ?? string.Empty)),
            "repeat.on_month_and_day" => Strings.Get(
                "repeat.on_month_and_day", Month(part.Values.FirstOrDefault()), Ordinal(count)),
            "repeat.ends_after" => Strings.Get("repeat.ends_after", count),
            "repeat.ends_on" => DateTimeOffset.TryParse(part.Date, out var until)
                ? Strings.Get("repeat.ends_on", until.ToLocalTime().ToString("d", format))
                : string.Empty,
            _ => string.Empty,
        };
    }

    /// <summary>A wire weekday as this reader's Windows names it.</summary>
    private static string Weekday(string wire) =>
        Enum.TryParse<DayOfWeek>(wire, ignoreCase: true, out var day)
            ? System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat
                .GetAbbreviatedDayName(day)
            : wire;

    private static string Month(string? number) =>
        int.TryParse(number, out var month) && month is >= 1 and <= 12
            ? System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month)
            : string.Empty;

    private static string Ordinal(long number) => (number % 100) is >= 11 and <= 13
        ? $"{number}th"
        : (number % 10) switch
        {
            1 => $"{number}st",
            2 => $"{number}nd",
            3 => $"{number}rd",
            _ => $"{number}th",
        };

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a repeat is chosen from the picker");
}

/// <summary>
/// Who holds a task, as words.
/// </summary>
/// <remarks>
/// Unassigned is a state in its own right, not an empty name, so it gets the word both Apple
/// clients use — under the key they use, <c>assignee.unassigned</c>, rather than English typed
/// into a third place.
/// </remarks>
public sealed partial class AssigneeNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is UserSummary user ? user.DisplayName : Strings.Get("assignee.unassigned");

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("the assignee is chosen from the picker");
}

public sealed partial class PickTitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string key ? Strings.Get(key) : value ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("titles are read-only in the UI");
}
