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
/// The AI agents page: the modes, the credentials, the webhook, and the account's own agents.
/// </summary>
public sealed partial class AgentsSection : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(AgentsSection),
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

    public AgentsSection()
    {
        InitializeComponent();
    }

    private async void OnSaveWebhook(object sender, RoutedEventArgs args)
    {
        await Shell.Settings.SaveWebhookAsync();
    }

    /// <summary>Save, and ask for a new signing secret while doing it.</summary>
    /// <remarks>
    /// Its own button rather than a checkbox beside Save: rotating the secret stops every delivery
    /// until the other end is updated, which is not something to do by leaving a box ticked.
    /// </remarks>
    private async void OnRegenerateWebhookSecret(object sender, RoutedEventArgs args)
    {
        await Shell.Settings.SaveWebhookAsync(regenerateSecret: true);
    }

    private async void OnTestWebhook(object sender, RoutedEventArgs args)
    {
        await Shell.Settings.TestWebhookAsync();
    }

    private async void OnDeleteWebhook(object sender, RoutedEventArgs args)
    {
        await Shell.Settings.DeleteWebhookAsync();
    }

    /// <summary>Register an agent, and show the credentials the server returns once.</summary>
    private async void OnRegisterCustomAgent(object sender, RoutedEventArgs args)
    {
        var secret = await Shell.Settings.RegisterAgentAsync(NewAgentNameBox.Text);
        NewAgentNameBox.Text = string.Empty;
        if (string.IsNullOrEmpty(secret))
        {
            return;
        }
        await new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Strings.Get("agents.secret_title"),
            Content = secret,
            CloseButtonText = Strings.Get("dialog.close"),
        }.ShowAsync();
    }

    private async void OnDeleteCustomAgent(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string agentId })
        {
            await Shell.Settings.DeleteAgentAsync(agentId);
        }
    }

    private async void OnAgentModeChosen(object sender, SelectionChangedEventArgs args)
    {
        if (SettingsLoading
            || sender is not ComboBox box
            || box.Tag is not string agentId
            || box.SelectedItem is not string mode)
        {
            return;
        }
        var current = Shell.Settings.Agents.FirstOrDefault(agent => agent.Id == agentId);
        if (current?.Mode == mode)
        {
            // The box is set from what was loaded; writing then would send the mode back to the
            // server on every open.
            return;
        }
        await Shell.Settings.SetAgentModeAsync(agentId, mode);
    }

    /// <summary>
    /// A key, saved on Enter.
    /// </summary>
    /// <remarks>
    /// On Enter rather than on every keystroke, which would send a dozen half-typed keys to the
    /// server and put one of them in a log somewhere. Cleared straight afterwards: the box shows
    /// nothing for a stored key, and leaving one on screen is a secret sitting in a window.
    /// </remarks>
    private async void OnCredentialKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter
            || sender is not PasswordBox box
            || box.Tag is not string serviceId)
        {
            return;
        }
        args.Handled = true;
        if (await Shell.Settings.SaveCredentialAsync(serviceId, box.Password))
        {
            box.Password = string.Empty;
        }
    }
}
