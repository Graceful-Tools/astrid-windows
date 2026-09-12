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
/// The Integrations page: how Google lists get linked, and Copilot.
/// </summary>
public sealed partial class IntegrationsSection : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(IntegrationsSection),
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

    public IntegrationsSection()
    {
        InitializeComponent();
    }

    /// <summary>Choose how Google lists get linked.</summary>
    /// <remarks>
    /// Guarded against the load that fills the box in: without it, opening the flyout would send
    /// the mode the account already has back to the server every time.
    /// </remarks>
    private async void OnGoogleSyncModeChosen(object sender, SelectionChangedEventArgs args)
    {
        if (SettingsLoading
            || sender is not ComboBox box
            || box.SelectedItem is not string mode
            || mode == Shell.Settings.Integrations.GoogleSyncMode)
        {
            return;
        }
        await Shell.Settings.Integrations.SetGoogleSyncModeAsync(mode);
    }

    private async void OnConnectCopilot(object sender, RoutedEventArgs args)
    {
        var url = await Shell.Settings.Agents.SetCopilotAsync(connect: true);
        if (!string.IsNullOrEmpty(url))
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
    }

    private async void OnDisconnectCopilot(object sender, RoutedEventArgs args)
    {
        await Shell.Settings.Agents.SetCopilotAsync(connect: false);
    }
}
