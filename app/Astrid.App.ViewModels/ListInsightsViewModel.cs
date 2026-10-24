using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// What a list has been doing lately (task 48c624ad; web AWTD-1329, AWTD-1333).
/// </summary>
/// <remarks>
/// <para>
/// Every number is the server's. The series, the medians and the ranking are computed once in
/// <c>lib/list-insights.ts</c> from task events the client does not hold, so this asks the core
/// for them and shapes the answer for a <c>Polyline</c> — nothing here decides what a number is.
/// </para>
/// <para>
/// The window picker is three fixed choices because the route answers 400 to a fourth. The core
/// normalises an unoffered window on the way out, so a shell that has fallen behind shows the
/// 30-day answer rather than an error.
/// </para>
/// </remarks>
public sealed class ListInsightsViewModel : ObservableObject
{
    private const long Minute = 60_000;
    private const long Hour = 60 * Minute;
    private const long Day = 24 * Hour;

    private readonly IAstridCore _core;
    private string _listId = string.Empty;
    private int _days = 30;
    private bool _isLoading;
    private bool _failed;
    private ListInsights? _insights;

    public ListInsightsViewModel(IAstridCore core)
    {
        _core = core;
        RefreshWindowChoices();
    }

    /// <summary>The windows the route offers, in the order the picker shows them.</summary>
    public static readonly int[] WindowDays = [7, 30, 90];

    /// <summary>The 7 / 30 / 90 picker, with the current window marked.</summary>
    public ObservableCollection<InsightWindowChoice> WindowChoices { get; } = [];

    /// <summary>One point per day, for each of the two series.</summary>
    public ObservableCollection<InsightSeriesPoint> CreatedSeries { get; } = [];

    public ObservableCollection<InsightSeriesPoint> CompletedSeries { get; } = [];

    /// <summary>The columns' medians; empty for a list that is not a board.</summary>
    public ObservableCollection<InsightLaneRow> Lanes { get; } = [];

    public ObservableCollection<InsightContributorRow> Contributors { get; } = [];

    /// <summary>Which window is being shown: 7, 30 or 90.</summary>
    public int Days
    {
        get => _days;
        private set
        {
            if (Set(ref _days, value))
            {
                RefreshWindowChoices();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => Set(ref _isLoading, value);
    }

    /// <summary>The last attempt did not answer. The picker still works, so another is a fresh try.</summary>
    public bool Failed
    {
        get => _failed;
        private set => Set(ref _failed, value);
    }

    /// <summary>True once an answer has been drawn — the sections are empty until then.</summary>
    public bool HasInsights => _insights is not null;

    public int CreatedTotal => _insights?.Totals.Created ?? 0;

    public int CompletedTotal => _insights?.Totals.Completed ?? 0;

    /// <summary>The median time to complete, as a unit and a number for the view to word.</summary>
    public InsightDurationLabel TimeToComplete => Label(_insights?.CreateToComplete);

    /// <summary>Whether to draw the columns section at all: only a board has lanes.</summary>
    public bool HasLanes => _insights?.Lanes is { Count: > 0 };

    /// <summary>
    /// A board whose every column has nothing to measure. Worth its own sentence rather than a
    /// row of dashes: lane moves are only recorded from the AWTD-1329 deploy onward, so this
    /// fills in as cards move rather than meaning the board is idle.
    /// </summary>
    public bool LanesNeedHistory =>
        _insights?.Lanes is { Count: > 0 } lanes && lanes.All(lane => lane.SampleSize == 0);

    public bool HasContributors => Contributors.Count > 0;

    /// <summary>Nobody touched the list in this window.</summary>
    public bool NoActivity => HasInsights && Contributors.Count == 0;

    /// <summary>Choose a window and reload. Ignores a choice already being shown.</summary>
    public async Task SetDaysAsync(int days, CancellationToken cancellationToken = default)
    {
        if (days == Days || !WindowDays.Contains(days))
        {
            return;
        }
        Days = days;
        await LoadAsync(_listId, cancellationToken);
    }

    /// <summary>
    /// Ask the core for this list's insights.
    /// </summary>
    /// <remarks>
    /// The machine's own IANA zone goes with the request so that days bucket like the reader's
    /// calendar; Windows reports it as a Windows id ("Pacific Standard Time"), which the server
    /// does not know, so it is converted first and falls back to UTC rather than to a 400.
    /// </remarks>
    public async Task LoadAsync(string listId, CancellationToken cancellationToken = default)
    {
        _listId = listId;
        if (string.IsNullOrEmpty(listId))
        {
            return;
        }
        IsLoading = true;
        try
        {
            var response = await _core.CallAsync(
                Commands.ListInsights(listId, Days, SystemTimeZone()), cancellationToken);
            // An answer for a window that is no longer the chosen one is stale: the picker moved
            // while it was in flight, and drawing it would show 90 days under a "7 days" button.
            var insights = response.Ok ? response.Read<ListInsights>() : null;
            if (insights is not null && insights.Window.Days != Days)
            {
                return;
            }
            Failed = insights is null;
            Apply(insights);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>This machine's zone as an IANA id, or UTC when it cannot be named as one.</summary>
    public static string SystemTimeZone()
    {
        var local = TimeZoneInfo.Local;
        if (local.HasIanaId)
        {
            return local.Id;
        }
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(local.Id, out var iana) ? iana : "UTC";
    }

    private void Apply(ListInsights? insights)
    {
        _insights = insights;
        Replace(CreatedSeries, Points(insights, day => day.Created));
        Replace(CompletedSeries, Points(insights, day => day.Completed));
        Replace(Lanes, insights?.Lanes?
            .Select(lane => new InsightLaneRow(lane.Name, Label(lane.MedianMs, lane.SampleSize)))
            .ToList() ?? []);
        Replace(Contributors, insights?.Contributors
            .Select(person => new InsightContributorRow(person))
            .ToList() ?? []);
        Raise(nameof(HasInsights));
        // The two series are refilled in place, so a chart bound through a converter — which is
        // the only way a Polyline can take fractions — hears nothing unless the property is
        // raised. The ItemsControls below redraw from the collection's own notifications.
        Raise(nameof(CreatedSeries));
        Raise(nameof(CompletedSeries));
        Raise(nameof(CreatedTotal));
        Raise(nameof(CompletedTotal));
        Raise(nameof(TimeToComplete));
        Raise(nameof(HasLanes));
        Raise(nameof(LanesNeedHistory));
        Raise(nameof(HasContributors));
        Raise(nameof(NoActivity));
    }

    /// <summary>
    /// The series as fractions of the plot: X across the window, Y up from zero.
    /// </summary>
    /// <remarks>
    /// Fractions rather than pixels because the chart is laid out by its container and the view
    /// model must not know how wide that is. Both series share one scale — the tallest value
    /// across the two — so that "New" above "Completed" on a day means it on the chart too; two
    /// independently scaled lines would be two charts drawn on top of each other. A flat window
    /// scales to zero, which puts every point on the baseline rather than dividing by it.
    /// </remarks>
    private static IReadOnlyList<InsightSeriesPoint> Points(ListInsights? insights, Func<InsightDay, int> value)
    {
        var series = insights?.Series;
        if (series is null || series.Count == 0)
        {
            return [];
        }
        var peak = series.Max(day => Math.Max(day.Created, day.Completed));
        var last = Math.Max(1, series.Count - 1);
        return series
            .Select((day, index) => new InsightSeriesPoint(
                day.Date,
                (double)index / last,
                peak == 0 ? 0 : (double)value(day) / peak,
                value(day)))
            .ToList();
    }

    private void RefreshWindowChoices() =>
        Replace(WindowChoices, WindowDays
            .Select(days => new InsightWindowChoice(days, days == _days))
            .ToList());

    /// <summary>
    /// A median as the one unit that reads naturally — minutes under an hour, hours under two
    /// days, then days — matching the web's <c>formatInsightDuration</c>.
    /// </summary>
    /// <remarks>
    /// Which unit to use is a decision, so it is made here; the words for it are not, so a key
    /// and a number go to the view and the <c>.resw</c> supplies "{0} min". Rounded to one
    /// decimal, and never below a minute: a stay of nine seconds reads as "1 min", because the
    /// alternative is "0 min" for something that did happen.
    /// </remarks>
    public static InsightDurationLabel Label(long? medianMs, int sampleSize = 0)
    {
        if (medianMs is not { } ms)
        {
            // Not zero: nothing was measured, and a zero would read as instantaneous.
            return new InsightDurationLabel(null, 0, 0);
        }
        if (ms < Hour)
        {
            return new InsightDurationLabel(
                "listInsights.duration.minutes",
                Math.Max(1, Math.Round((double)ms / Minute)),
                sampleSize);
        }
        if (ms < 2 * Day)
        {
            return new InsightDurationLabel(
                "listInsights.duration.hours", Math.Round((double)ms / Hour, 1), sampleSize);
        }
        return new InsightDurationLabel(
            "listInsights.duration.days", Math.Round((double)ms / Day, 1), sampleSize);
    }

    private static InsightDurationLabel Label(InsightDuration? duration) =>
        Label(duration?.MedianMs, duration?.SampleSize ?? 0);

    private static void Replace<T>(ObservableCollection<T> target, IReadOnlyList<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}

/// <summary>One button on the 7 / 30 / 90 picker.</summary>
public sealed record InsightWindowChoice(int Days, bool IsSelected);

/// <summary>
/// One day of one series, placed in the plot as fractions of its width and height.
/// </summary>
/// <param name="X">0 at the oldest day, 1 at the newest.</param>
/// <param name="Y">0 at the baseline, 1 at the busiest day in either series.</param>
public sealed record InsightSeriesPoint(string Date, double X, double Y, int Count);

/// <summary>
/// A median, as a unit and a number: the view supplies the words.
/// </summary>
/// <param name="Key">The <c>.resw</c> key for the unit, or null when there is nothing to show.</param>
public sealed record InsightDurationLabel(string? Key, double Amount, int SampleSize)
{
    /// <summary>False means an em dash — nothing was measured — and no sample to qualify it.</summary>
    public bool HasMedian => Key is not null;
}

/// <summary>One board column's median stay, ready to draw.</summary>
public sealed record InsightLaneRow(string Name, InsightDurationLabel Median);

/// <summary>
/// One contributor as the panel draws them: the three profile metrics the ranking uses, and the
/// activity that breaks their ties (AWTD-1333). Numbers only — the sentences are in the resources.
/// </summary>
public sealed record InsightContributorRow(InsightContributor Person)
{
    /// <summary>Null when the server has no name for them; the view draws its own placeholder.</summary>
    public string? Name => string.IsNullOrEmpty(Person.Name) ? null : Person.Name;

    public string? Image => Person.Image;

    public bool IsAIAgent => Person.IsAIAgent;

    public int Completed => Person.Completed;

    public int Inspired => Person.Inspired;

    public int Supported => Person.Supported;

    public int Created => Person.Created;

    public int Edited => Person.Edited;

    public int Commented => Person.Commented;
}
