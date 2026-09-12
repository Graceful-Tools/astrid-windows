using System.Diagnostics;
using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Astrid.App.Views.Settings;

/// <summary>
/// The Reminders page: push and email, the default offset, the daily digest and quiet hours.
/// </summary>
public sealed partial class RemindersSection : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(RemindersSection),
        new PropertyMetadata(null));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    /// <summary>
    /// True while the settings flyout is being filled in.
    /// </summary>
    /// <remarks>
    /// A toggle raises its change event while being set from what was just loaded, and writing then
    /// saves the value back on every open — a settings screen that keeps sending the server what it
    /// already said. <see cref="SettingsFlyout"/> sets it on every page it holds.
    /// </remarks>
    internal bool SettingsLoading { get; set; }

    public RemindersSection()
    {
        InitializeComponent();
    }

    /// <summary>Put the loaded times and offset in the pickers. Called by the flyout while it is loading.</summary>
    internal void ShowLoaded()
    {
        var reminders = Shell.Settings.Reminders;
        DigestTimeBox.SelectedTime = ParseTime(reminders.DailyDigestTime);
        QuietStartBox.SelectedTime = ParseTime(reminders.QuietHoursStart);
        QuietEndBox.SelectedTime = ParseTime(reminders.QuietHoursEnd);
        DefaultOffsetBox.SelectedIndex = IndexOfOffset(reminders.DefaultReminderTime);
    }

    private async void OnPushToggled(object sender, RoutedEventArgs args)
    {
        if (!SettingsLoading && PushToggle.IsOn != Shell.Settings.PushEnabled)
        {
            await Shell.Settings.SetAsync("enablePushReminders", PushToggle.IsOn);
        }
    }

    private async void OnEmailToggled(object sender, RoutedEventArgs args)
    {
        if (!SettingsLoading && EmailToggle.IsOn != Shell.Settings.EmailEnabled)
        {
            await Shell.Settings.SetAsync("enableEmailReminders", EmailToggle.IsOn);
        }
    }

    private async void OnDigestToggled(object sender, RoutedEventArgs args)
    {
        if (!SettingsLoading && DigestToggle.IsOn != Shell.Settings.DigestEnabled)
        {
            await Shell.Settings.SetAsync("enableDailyDigest", DigestToggle.IsOn);
        }
    }

    private async void OnDigestTimeChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
    {
        if (!SettingsLoading && args.NewTime is { } time)
        {
            await Shell.Settings.SetAsync("dailyDigestTime", Clock(time));
        }
    }

    private async void OnQuietHoursToggled(object sender, RoutedEventArgs args)
    {
        if (SettingsLoading)
        {
            return;
        }
        if (!QuietToggle.IsOn)
        {
            // Both ends cleared: the server reads their absence as "no quiet hours", and a window
            // with only one end is something nothing can act on.
            await Shell.Settings.SetQuietHoursAsync(null, null);
            return;
        }
        await Shell.Settings.SetQuietHoursAsync(
            Clock(QuietStartBox.SelectedTime ?? new TimeSpan(22, 0, 0)),
            Clock(QuietEndBox.SelectedTime ?? new TimeSpan(8, 0, 0)));
    }

    private async void OnQuietHoursChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
    {
        if (SettingsLoading || !QuietToggle.IsOn)
        {
            return;
        }
        await Shell.Settings.SetQuietHoursAsync(
            Clock(QuietStartBox.SelectedTime ?? new TimeSpan(22, 0, 0)),
            Clock(QuietEndBox.SelectedTime ?? new TimeSpan(8, 0, 0)));
    }

    private async void OnDefaultOffsetChanged(object sender, SelectionChangedEventArgs args)
    {
        if (SettingsLoading || DefaultOffsetBox.SelectedItem is not ReminderOffset offset)
        {
            return;
        }
        await Shell.Settings.SetAsync("defaultReminderTime", offset.Minutes);
    }

    /// <summary>An <c>HH:MM</c> string, which is what the server stores.</summary>
    private static string Clock(TimeSpan time) => $"{time.Hours:D2}:{time.Minutes:D2}";

    private static TimeSpan? ParseTime(string? text) =>
        TimeSpan.TryParse(text, out var parsed) ? parsed : null;

    private int IndexOfOffset(int minutes)
    {
        for (var index = 0; index < Shell.Settings.Offsets.Count; index++)
        {
            if (Shell.Settings.Offsets[index].Minutes == minutes)
            {
                return index;
            }
        }
        return -1;
    }
}
