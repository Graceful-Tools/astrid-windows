using Astrid.Core.Bindings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;
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

    /// <summary>
    /// The colour for a priority, from the one table that also describes the row marks.
    /// </summary>
    /// <remarks>
    /// These were hardcoded here, in values that disagreed with the checkbox images every row
    /// draws — priority 1 was green here and blue on screen (task 204c9d98). The table is now
    /// <see cref="PriorityPalette"/>, pinned against the shipped assets by a test.
    ///
    /// Zero is the exception, and stays transparent: no priority draws nothing at all rather than
    /// grey, because a stripe on every row is a stripe that says nothing and the point of the
    /// stripe is that it is glanceable. The picker's swatch, which needs a visible outline, asks
    /// the palette for the grey directly.
    /// </remarks>
    private static Color PriorityColor(int priority) => priority is 1 or 2 or 3
        ? Parse(PriorityPalette.Hex(priority))
        : Colors.Transparent;

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

/// <summary>One of the three numbers on a profile, with its label.</summary>
/// <remarks>
/// One converter with a key rather than three classes: the numbers differ, the sentence does not.
/// </remarks>
public sealed partial class StatConverter : IValueConverter
{
    /// <summary>Which number this instance shows: <c>stats.completed</c> and its two siblings.</summary>
    public string Key { get; set; } = "stats.completed";

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not ProfileStats stats)
        {
            return string.Empty;
        }
        var count = Key switch
        {
            "stats.inspired" => stats.Inspired,
            "stats.supported" => stats.Supported,
            _ => stats.Completed,
        };
        return Strings.Get(Key, count);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("statistics are read-only in the UI");
}

/// <summary>Whether a service has a key stored.</summary>
/// <remarks>
/// "Set up" rather than the key itself, because the server never answers with a key — which is the
/// right shape, and the reason the box beside this is empty even when a key exists.
/// </remarks>
public sealed partial class CredentialStateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        Strings.Get(value is true ? "agents.key_set" : "agents.no_key");

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a key's state is read-only in the UI");
}

/// <summary>A list's colour at a tenth of its strength, for a chip behind its name.</summary>
/// <remarks>
/// astrid-web writes this as <c>{color}15</c> — the colour with an eight-percent alpha — so a chip
/// carries the list's identity without competing with the task title in front of it.
/// </remarks>
public sealed partial class ChipTintConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var colour = Colours.Parse(value as string) ?? Windows.UI.Color.FromArgb(255, 59, 130, 246);
        return new SolidColorBrush(
            Windows.UI.Color.FromArgb(0x15, colour.R, colour.G, colour.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a tint is not a value to read back");
}

/// <summary>The funnel goes accent-coloured when something is narrowing the list.</summary>
/// <remarks>
/// The word "Filtered" used to say it. With an icon-only toolbar the colour has to, or a list
/// quietly hiding half its tasks looks like a list that lost them.
/// </remarks>
public sealed partial class FilterTintConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true
            ? Application.Current.Resources["AstridAccentBrush"]
            : Application.Current.Resources["AstridTextPrimary"];

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a tint is not a value to read back");
}

/// <summary>A count worth showing: present, and more than none.</summary>
/// <remarks>
/// A badge reading "0" beside every empty list is noise on a sidebar that exists to be scanned,
/// and a list with no count at all has nothing to say.
///
/// Pass <c>empty</c> as the parameter to ask the opposite question — whether there is nothing —
/// which is what an empty-state placeholder is bound to.
/// </remarks>
public sealed partial class CountVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var any = value is int count && count > 0;
        var wanted = parameter as string != "empty";
        return any == wanted ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a count is not a visibility to read back");
}

/// <summary>A finished task is quieter, at the three-quarters the web uses.</summary>
public sealed partial class CompletedOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? 0.75 : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("an opacity is not a value to read back");
}

/// <summary>And struck through, which is how a list says "done" without a word.</summary>
public sealed partial class CompletedStrikeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true
            ? Windows.UI.Text.TextDecorations.Strikethrough
            : Windows.UI.Text.TextDecorations.None;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a decoration is not a value to read back");
}

/// <summary>Reading a `#rrggbb` from the core into a colour.</summary>
internal static class Colours
{
    public static Windows.UI.Color? Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            return null;
        }
        var text = hex.Trim().TrimStart('#');
        if (text.Length != 6 || !int.TryParse(
                text, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var packed))
        {
            return null;
        }
        return Windows.UI.Color.FromArgb(
            255, (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed);
    }
}

/// <summary>A picture, or something to open.</summary>
/// <remarks>
/// Which of the two a file is comes from the core, not from the extension read here — the same
/// rule decides it on every surface. See <c>astrid_core::rows::comment::renders_inline</c>.
/// </remarks>
public sealed partial class FileGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? "" : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a glyph is not a value to read back");
}

/// <summary>What the timer button says.</summary>
public sealed partial class TimerButtonConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        Strings.Get(value is true ? "timer.stop" : "timer.start");

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("the timer is a button, not a field");
}

/// <summary>How long a task has been worked on.</summary>
/// <remarks>
/// The caption the Mac keeps once the timer is stopped, so hiding the section never hides the data.
/// The words are the core's <c>lastValue</c> when there is one, because that string is stored on
/// the task and read by every client — a Mac showing "1h 5m" beside a Windows "65 minutes" for the
/// same session is a difference nobody can explain.
/// </remarks>
public sealed partial class LoggedTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not TimerState timer || timer.LoggedMinutes <= 0)
        {
            return string.Empty;
        }
        var hours = timer.LoggedMinutes / 60;
        var minutes = timer.LoggedMinutes % 60;
        var total = hours > 0 ? $"{hours}h {minutes}m" : $"{minutes}m";
        return Strings.Get("timer.logged", total);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("logged time is read-only in the UI");
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

/// <summary>
/// Which side of the thread a comment bubble sits on.
/// </summary>
/// <remarks>
/// astrid-web reverses the bubble row for its own messages (<c>.chat-bubble-row-mine</c>). The
/// core decides whose a comment is; this only turns that into an alignment.
/// </remarks>
public sealed partial class BubbleAlignConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("an alignment is not a value to read back");
}

/// <summary>The bubble's own ground: mine on the surface tint, theirs a shade deeper.</summary>
/// <remarks>
/// astrid-web's <c>.chat-bubble-mine</c> / <c>.chat-bubble-other</c>. Two shades rather than a
/// colour, because a coloured bubble competes with the list colours the panel is full of.
/// </remarks>
public sealed partial class BubbleBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        Application.Current.Resources[value is true ? "AstridBgSelected" : "AstridSurfaceHover"];

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a bubble's ground is not a value to read back");
}

/// <summary>A list's colour at full strength, for a chip that carries white text.</summary>
/// <remarks>
/// The task-detail chips are the list's own colour with white on top — astrid-web draws them from
/// <c>list.color</c> directly. <see cref="ChipTintConverter"/> is the row's quieter version.
/// </remarks>
public sealed partial class ChipColourConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        new SolidColorBrush(
            Colours.Parse(value as string) ?? Windows.UI.Color.FromArgb(255, 59, 130, 246));

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a chip's colour is not a value to read back");
}

/// <summary>
/// A file on disk, as something an <c>Image</c> can draw.
/// </summary>
/// <remarks>
/// A path is not an image source: XAML needs a <see cref="BitmapImage"/>, and binding the string
/// draws nothing at all rather than failing loudly. Decoded at the width it is drawn, because a
/// phone photograph decoded at full size to fill a 220px bubble is forty megabytes of bitmap per
/// comment.
///
/// Anything unreadable comes back null and the row falls back to its chip — a half-written file in
/// the pending directory must not take the window down.
/// </remarks>
public sealed partial class FileThumbnailConverter : IValueConverter
{
    /// <summary>The widest a thumbnail is drawn, and so the widest it needs decoding.</summary>
    private const int DecodeWidth = 240;

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }
        try
        {
            var image = new BitmapImage
            {
                DecodePixelWidth = DecodeWidth,
                UriSource = new Uri(path),
            };
            return image;
        }
        catch (Exception)
        {
            // An unreadable file is a chip, not a crash.
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a thumbnail is not a value to read back");
}

/// <summary>
/// Which template a board column item takes: a card, or the slot the expanded card's detail is
/// drawn in (task 91a25b8a).
/// </summary>
/// <remarks>
/// The column's items are cards with one slot among them, and a selector is how an
/// <c>ItemsControl</c> draws two kinds of thing from one list without the template deciding
/// anything — the board view model already said which item is which.
/// </remarks>
public sealed partial class BoardItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Card { get; set; }

    public DataTemplate? DetailSlot { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) =>
        item is Astrid.App.ViewModels.InlineDetailSlot ? DetailSlot : Card;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) =>
        SelectTemplateCore(item);
}

/// <summary>
/// What a default's choice is called: a member's name when it has one, otherwise the word its
/// resource key names (task c4102c67).
/// </summary>
public sealed partial class ChoiceLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is Astrid.App.ViewModels.DefaultChoice choice
            ? choice.Text ?? (choice.TitleKey is { } key ? Strings.Get(key) : string.Empty)
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a label is not a value to read back");
}

/// <summary>The ring round the chosen colour swatch: a 2px border when chosen, none otherwise.</summary>
public sealed partial class SwatchRingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? new Thickness(2) : new Thickness(0);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a border is not a value to read back");
}

/// <summary>
/// How wide a board column is: the ordinary width, or the wider one that holds the expanded
/// card's detail.
/// </summary>
/// <remarks>
/// astrid-web's columns are <c>min-w-[18rem] max-w-[28rem] flex-1</c>, so the one carrying an
/// expanded task grows towards the upper bound. The two widths here are those bounds in pixels,
/// which keeps the detail's field rows — laid out for the 360px side pane — from folding.
/// </remarks>
public sealed partial class BoardColumnWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? 448.0 : 288.0;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException("a width is not a value to read back");
}
