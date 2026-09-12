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
/// The Appearance page: the theme, the quick-add chord, the task-detail layout, smart parsing and where subtasks go.
/// </summary>
public sealed partial class AppearanceSection : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(AppearanceSection),
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

    public AppearanceSection()
    {
        InitializeComponent();
        // The page's words, from the strings table (tasks c0f3db19, 6ac2639a).
        LayoutBox.Header = Strings.Get("smart.layout");
        LayoutHint.Text = Strings.Get("smart.layout_hint");
        SmartParsingToggle.Header = Strings.Get("smart.parsing");
        SmartParsingHint.Text = Strings.Get("smart.parsing_hint");
        SubtasksBox.Header = Strings.Get("smart.subtasks");
        SubtasksHint.Text = Strings.Get("smart.subtasks_hint");
    }

    private async void OnSmartTaskChoiceChosen(object sender, SelectionChangedEventArgs args)
    {
        if (SettingsLoading)
        {
            return;
        }
        await SmartTaskChoice.ChosenAsync(Shell.Settings, sender);
    }

    private async void OnSmartParsingToggled(object sender, RoutedEventArgs args)
    {
        if (SettingsLoading
            || sender is not ToggleSwitch toggle
            || toggle.IsOn == Shell.Settings.SmartParsingEnabled)
        {
            return;
        }
        await Shell.Settings.SetSmartTaskAsync("smartTaskCreationEnabled", toggle.IsOn);
    }

    /// <summary>The Apply beside the shortcut box: the core judges the chord, the window holds it.</summary>
    private async void OnApplyHotkey(object sender, RoutedEventArgs args)
    {
        if (SettingsLoading)
        {
            return;
        }
        await Shell.Settings.SetHotkeyAsync(HotkeyBox.Text);
    }

    private async void OnThemeChosen(object sender, SelectionChangedEventArgs args)
    {
        if (SettingsLoading
            || sender is not ComboBox box
            || box.SelectedItem is not string theme
            || theme == Shell.Settings.Theme)
        {
            return;
        }
        await Shell.Settings.SetThemeAsync(theme);
    }
}
