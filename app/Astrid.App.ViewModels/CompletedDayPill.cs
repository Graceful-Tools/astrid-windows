using System.Globalization;
using System.Text.RegularExpressions;

namespace Astrid.App.ViewModels;

/// <summary>
/// The words on a Done card's finished-on pill: "Nov 19", or "Nov 19, 2025" when it was another
/// year (Jon, 2026-11-19 — the same pill on every platform).
/// </summary>
/// <remarks>
/// <para>
/// The day itself is the core's (<c>completedOn</c>, already in the reader's zone); this only says
/// it. Short month and day in the reader's own culture, and the year only when it is not this
/// one — a pill reading "Nov 19, 2026" all November says nothing the reader did not know.
/// </para>
/// <para>
/// Built from the culture's own patterns rather than a fixed <c>"MMM d"</c>, so a German reader
/// gets "19. Nov." and a Japanese one "11月19日". .NET has no short-month pattern of its own, so
/// the month-day and long-date patterns are shortened: the full month becomes the abbreviated
/// one, and the long date loses its weekday.
/// </para>
/// </remarks>
public static partial class CompletedDayPill
{
    /// <summary>The pill's words, or null when there is no day to show.</summary>
    /// <param name="completedOn">The core's <c>YYYY-MM-DD</c>, or null while the task is open.</param>
    /// <param name="today">The reader's today, which decides whether the year is said.</param>
    /// <param name="culture">Whose month names and order to use.</param>
    public static string? Label(string? completedOn, DateOnly today, CultureInfo culture)
    {
        if (!DateOnly.TryParseExact(completedOn, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day))
        {
            return null;
        }
        var format = culture.DateTimeFormat;
        var pattern = day.Year == today.Year
            ? Shorten(format.MonthDayPattern)
            : Shorten(Weekday().Replace(format.LongDatePattern, string.Empty).Trim(' ', ','));
        return day.ToString(pattern, culture);
    }

    /// <summary>The label for today, in the reader's culture — what the board draws.</summary>
    public static string? Label(string? completedOn) =>
        Label(completedOn, DateOnly.FromDateTime(DateTime.Now), CultureInfo.CurrentCulture);

    /// <summary>The full month name, as the abbreviated one.</summary>
    private static string Shorten(string pattern) =>
        pattern.Replace("MMMM", "MMM", StringComparison.Ordinal);

    /// <summary>A weekday name in a pattern, with the separator that joins it to the date.</summary>
    [GeneratedRegex(@"dddd[,\s]*|[,\s]*dddd")]
    private static partial Regex Weekday();
}
