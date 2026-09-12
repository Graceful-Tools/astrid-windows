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
/// The Tasks page: email-to-task and the defaults for tasks created by email (task c0f3db19).
/// </summary>
public sealed partial class TasksSection : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(TasksSection),
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

    public TasksSection()
    {
        InitializeComponent();
        // The page's words, from the strings table (task c0f3db19).
        EmailToTaskToggle.Header = Strings.Get("smart.email_to_task");
        EmailToTaskHint.Text = Strings.Get("smart.email_to_task_hint");
        EmailToTaskAddress.Text = Strings.Get("smart.email_address");
        DueOffsetBox.Header = Strings.Get("smart.default_due_date");
        DueOffsetHint.Text = Strings.Get("smart.default_due_date_hint");
        DueTimeBox.Header = Strings.Get("smart.default_due_time");
        DueTimeHint.Text = Strings.Get("smart.default_due_time_hint");
    }

    private async void OnSmartTaskChoiceChosen(object sender, SelectionChangedEventArgs args)
    {
        if (SettingsLoading)
        {
            return;
        }
        await SmartTaskChoice.ChosenAsync(Shell.Settings, sender);
    }

    private async void OnEmailToTaskToggled(object sender, RoutedEventArgs args)
    {
        if (SettingsLoading
            || sender is not ToggleSwitch toggle
            || toggle.IsOn == Shell.Settings.EmailToTaskEnabled)
        {
            return;
        }
        await Shell.Settings.SetSmartTaskAsync("emailToTaskEnabled", toggle.IsOn);
    }
}
