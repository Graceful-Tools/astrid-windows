using System.Collections.Generic;
using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Astrid.App;

/// <summary>
/// Reminders, as Windows notifications.
/// </summary>
/// <remarks>
/// <para>
/// The core decides which reminders are due and remembers which have been shown; this only turns
/// one into a banner and turns a button on that banner back into a command. That split is what
/// keeps "have I already shown this?" out of the shell, where it would be lost on every restart.
/// </para>
/// <para>
/// The app is unpackaged, so the notification platform has to be registered by hand at startup —
/// <see cref="Start"/> — and unregistered on the way out. Registration can fail on a machine where
/// notifications are switched off at the policy level; that is not a reason to refuse to run, so a
/// failure here leaves the app working with no banners rather than not starting.
/// </para>
/// <para>
/// Complete and Snooze are buttons on the banner. They arrive through
/// <see cref="AppNotificationManager.NotificationInvoked"/> on a background thread, which is why
/// everything they do is posted back to the UI thread by the caller's <c>post</c>.
/// </para>
/// </remarks>
public sealed class Reminders
{
    private readonly ShellViewModel _shell;
    private readonly Action<Func<Task>> _post;
    private bool _registered;

    public Reminders(ShellViewModel shell, Action<Func<Task>> post)
    {
        _shell = shell;
        _post = post;
    }

    /// <summary>What Windows was told about the last banner. Read by the tests and the log.</summary>
    public string? LastPayload { get; private set; }

    /// <summary>Whether banners are available at all on this machine.</summary>
    public bool IsAvailable => _registered;

    public void Start()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnInvoked;
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception error)
        {
            // No banners, but an app that still works. A machine with notifications switched off
            // by policy is a normal machine, not a broken install.
            App.Log($"reminders unavailable: {error.Message}");
            _registered = false;
        }

        _shell.RemindersDue += OnRemindersDue;
    }

    public void Stop()
    {
        _shell.RemindersDue -= OnRemindersDue;
        if (!_registered)
        {
            return;
        }
        try
        {
            AppNotificationManager.Default.NotificationInvoked -= OnInvoked;
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception error)
        {
            App.Log($"could not unregister reminders: {error.Message}");
        }
    }

    private void OnRemindersDue(IReadOnlyList<Reminder> reminders)
    {
        foreach (var reminder in reminders)
        {
            Show(reminder);
        }
    }

    /// <summary>Put one reminder on screen, and remember that it got there.</summary>
    private void Show(Reminder reminder)
    {
        if (!_registered)
        {
            // Nothing was shown, so nothing is marked shown: the reminder is still owed, and the
            // next build — or the next machine — will show it.
            return;
        }

        var toast = new AppNotificationBuilder()
            .AddText("Astrid")
            .AddText(reminder.Title)
            .AddButton(new AppNotificationButton("Complete")
                .AddArgument("action", "complete")
                .AddArgument("taskId", reminder.TaskId))
            .AddButton(new AppNotificationButton("Snooze 10 min")
                .AddArgument("action", "snooze")
                .AddArgument("minutes", "10")
                .AddArgument("taskId", reminder.TaskId))
            // The banner itself opens the task, which is what tapping a reminder means everywhere
            // else. The buttons are the shortcuts, not the only way through.
            .AddArgument("action", "open")
            .AddArgument("taskId", reminder.TaskId)
            .BuildNotification();

        LastPayload = toast.Payload;
        try
        {
            AppNotificationManager.Default.Show(toast);
        }
        catch (Exception error)
        {
            App.Log($"could not show a reminder: {error.Message}");
            return;
        }

        _post(async () => await _shell.ReminderShownAsync(reminder.TaskId));
    }

    /// <summary>A button on a banner, or the banner itself.</summary>
    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        var arguments = args.Arguments;
        if (!arguments.TryGetValue("taskId", out var taskId) || string.IsNullOrEmpty(taskId))
        {
            return;
        }
        arguments.TryGetValue("action", out var action);

        _post(async () =>
        {
            switch (action)
            {
                case "complete":
                    await _shell.CompleteFromReminderAsync(taskId);
                    break;
                case "snooze":
                    var minutes = arguments.TryGetValue("minutes", out var text)
                        && int.TryParse(text, out var parsed)
                        ? parsed
                        : 10;
                    await _shell.SnoozeReminderAsync(taskId, minutes);
                    break;
                default:
                    await _shell.OpenTaskAsync(taskId);
                    break;
            }
        });
    }
}
