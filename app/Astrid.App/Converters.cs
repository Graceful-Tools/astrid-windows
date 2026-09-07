using Astrid.Core.Bindings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
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
            "today" => "Today",
            "tomorrow" => "Tomorrow",
            "yesterday" => "Yesterday",
            "on" => Format(due),
            _ => string.Empty,
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
