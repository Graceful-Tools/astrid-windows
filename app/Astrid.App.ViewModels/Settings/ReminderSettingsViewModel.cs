using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The Reminders page: push and email, the default offset, the daily digest and quiet hours.
/// </summary>
/// <remarks>
/// The settings themselves are the server's and are shared with every client, which is why nothing
/// about their meaning is decided here. A change is written through immediately rather than behind
/// a Save button.
/// </remarks>
public sealed class ReminderSettingsViewModel : ObservableObject
{
    private readonly SettingsSession _session;
    private ReminderSettings _reminders = new();
    private string? _calendarFeedUrl;

    public ReminderSettingsViewModel(SettingsSession session)
    {
        _session = session;
        foreach (var value in new[] { "all", "with_due_times", "none" })
        {
            CalendarSyncChoices.Add(new DefaultChoice("calendarSyncType", value, $"calendar.{value}", null, false));
        }
        _session.AccountRead += account =>
        {
            Reminders = account.ReminderSettings;
            CalendarFeedUrl = account.CalendarFeedUrl;
            Offsets.Clear();
            foreach (var offset in account.Offsets)
            {
                Offsets.Add(offset);
            }
        };
    }

    /// <summary>The reminder offsets a new task can default to.</summary>
    public ObservableCollection<ReminderOffset> Offsets { get; } = [];

    /// <summary>The settings as the server has them.</summary>
    public ReminderSettings Reminders
    {
        get => _reminders;
        private set
        {
            if (Set(ref _reminders, value))
            {
                Raise(nameof(PushEnabled));
                Raise(nameof(EmailEnabled));
                Raise(nameof(DigestEnabled));
                Raise(nameof(QuietHoursEnabled));
                Raise(nameof(DailyDigestTime));
                Raise(nameof(QuietHoursStart));
                Raise(nameof(QuietHoursEnd));
                Raise(nameof(DefaultReminderTime));
                Raise(nameof(CalendarSyncEnabled));
                Raise(nameof(CalendarSyncType));
                Raise(nameof(SelectedCalendarSyncChoice));
                Raise(nameof(CalendarSyncDescriptionKey));
                Raise(nameof(HasCalendarFeed));
            }
        }
    }

    // ── The calendar feed (task 28c5c6a9) ────────────────────────────────────────────────────
    //
    // Two more fields in the same blob the toggles above write, and an address the core builds:
    // the web's calendar integration, as a screen rather than a service.

    /// <summary>Whether the account's tasks are offered to calendar apps.</summary>
    public bool CalendarSyncEnabled => Reminders.EnableCalendarSync;

    /// <summary><c>all</c>, <c>with_due_times</c> or <c>none</c>. The server's default is <c>all</c>.</summary>
    public string CalendarSyncType =>
        string.IsNullOrEmpty(Reminders.CalendarSyncType) ? "all" : Reminders.CalendarSyncType;

    /// <summary>The web's three choices, as rows for the combo; the words are the shell's.</summary>
    public ObservableCollection<DefaultChoice> CalendarSyncChoices { get; } = [];

    public DefaultChoice? SelectedCalendarSyncChoice =>
        CalendarSyncChoices.FirstOrDefault(choice => choice.Value == CalendarSyncType);

    /// <summary>The line under the combo, as a key: what the chosen type puts in the calendar.</summary>
    public string CalendarSyncDescriptionKey => $"calendar.{CalendarSyncType}_desc";

    /// <summary>Where a calendar app subscribes; null until the account has been read.</summary>
    public string? CalendarFeedUrl
    {
        get => _calendarFeedUrl;
        private set
        {
            if (Set(ref _calendarFeedUrl, value))
            {
                Raise(nameof(HasCalendarFeed));
            }
        }
    }

    /// <summary>There is an address to offer: sync is on, something is included, and the core said where.</summary>
    public bool HasCalendarFeed =>
        CalendarSyncEnabled && CalendarSyncType != "none" && !string.IsNullOrEmpty(CalendarFeedUrl);

    public Task<bool> SetCalendarSyncAsync(bool enabled, CancellationToken cancellationToken = default) =>
        SetAsync("enableCalendarSync", enabled, cancellationToken);

    public Task<bool> SetCalendarSyncTypeAsync(string type, CancellationToken cancellationToken = default) =>
        SetAsync("calendarSyncType", type, cancellationToken);

    public bool PushEnabled => Reminders.EnablePushReminders;

    public bool EmailEnabled => Reminders.EnableEmailReminders;

    public bool DigestEnabled => Reminders.EnableDailyDigest;

    /// <summary>Quiet hours are on when there is a start to be quiet from.</summary>
    public bool QuietHoursEnabled => !string.IsNullOrEmpty(Reminders.QuietHoursStart);

    /// <summary>The four the pickers are set from, as the server stores them (<c>HH:MM</c>, minutes).</summary>
    public string? DailyDigestTime => Reminders.DailyDigestTime;

    public string? QuietHoursStart => Reminders.QuietHoursStart;

    public string? QuietHoursEnd => Reminders.QuietHoursEnd;

    public int DefaultReminderTime => Reminders.DefaultReminderTime;

    /// <summary>Change one setting.</summary>
    /// <remarks>
    /// One field at a time, merged in the core: a screen sending a single toggle must not clear
    /// everything else somebody has set, possibly on another client.
    /// </remarks>
    public Task<bool> SetAsync(string field, object? value,
        CancellationToken cancellationToken = default) =>
        WriteAsync(new Dictionary<string, object?> { [field] = value }, cancellationToken);

    /// <summary>Turn quiet hours on with a window, or off entirely.</summary>
    /// <remarks>
    /// Both ends together, because the server reads their absence as "no quiet hours" — sending
    /// only a start would leave a window with no end, which nothing can act on.
    /// </remarks>
    public Task<bool> SetQuietHoursAsync(string? start, string? end,
        CancellationToken cancellationToken = default) =>
        WriteAsync(
            new Dictionary<string, object?>
            {
                ["quietHoursStart"] = start,
                ["quietHoursEnd"] = end,
            },
            cancellationToken);

    private async Task<bool> WriteAsync(IReadOnlyDictionary<string, object?> changes,
        CancellationToken cancellationToken)
    {
        var response = await _session.Core.CallAsync(
            Commands.UpdateReminderSettings(changes), cancellationToken);
        return _session.Read(response);
    }
}
