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
/// The account flyout: a rail of pages, one page shown at a time.
/// </summary>
public sealed partial class SettingsFlyout : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(SettingsFlyout),
        new PropertyMetadata(null, (sender, _) => ((SettingsFlyout)sender).OnShellChanged()));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public SettingsFlyout()
    {
        InitializeComponent();
        // The rail's own words, from the strings table like everything else (tasks c0f3db19, 438494c7).
        TasksNavLabel.Text = Strings.Get("smart.title");
        ContactsNavLabel.Text = Strings.Get("contacts.title");
        HelpNavLabel.Text = Strings.Get("help.title");
        PrivacyNavLabel.Text = Strings.Get("privacy.title");
        TermsNavLabel.Text = Strings.Get("terms.title");
    }

    private void OnShellChanged()
    {
        AccountSection.Shell = Shell;
        TasksSection.Shell = Shell;
        ContactsSection.Shell = Shell;
        RemindersSection.Shell = Shell;
        AppearanceSection.Shell = Shell;
        AgentsSection.Shell = Shell;
        ApiAccessSection.Shell = Shell;
        IntegrationsSection.Shell = Shell;
        DataSection.Shell = Shell;
    }

    /// <summary>
    /// True while the flyout is being filled in.
    /// </summary>
    /// <remarks>
    /// A toggle raises its change event while being set from what was just loaded, and writing then
    /// saves the value back on every open — a settings screen that keeps sending the server what it
    /// already said. Every page that reads the flag is told at once.
    /// </remarks>
    private bool SettingsLoading
    {
        set
        {
            TasksSection.SettingsLoading = value;
            RemindersSection.SettingsLoading = value;
            AppearanceSection.SettingsLoading = value;
            AgentsSection.SettingsLoading = value;
            IntegrationsSection.SettingsLoading = value;
        }
    }

    /// <summary>
    /// Load the account as its flyout opens, and put the times in the pickers.
    /// </summary>
    /// <remarks>
    /// The time pickers are set here rather than bound, because a two-way binding on a control that
    /// raises its change event while being populated writes the value straight back — which is a
    /// settings screen that saves what it just read, on every open.
    /// </remarks>
    internal async Task OpenedAsync()
    {
        SettingsLoading = true;
        try
        {
            // The account reads from the cache before it asks the server, so the first section
            // has something to draw at once. The other four answers are the server's, belong to
            // sections further down, and do not depend on each other — so they are asked for
            // together rather than one after another, which was four round trips of blank panel.
            var others = Task.WhenAll(
                Shell.Settings.Account.LoadPasskeysAsync(),
                Shell.Settings.Agents.LoadAgentsAsync(),
                Shell.Settings.Integrations.LoadGoogleSyncModeAsync(),
                Shell.Settings.Agents.LoadWebhookAsync());
            await Shell.LoadSettingsAsync();
            await others;
            RemindersSection.ShowLoaded();
            // Opens on the first section every time. A panel that reopens three pages deep in
            // whatever was last poked at is a panel you have to navigate out of before you can use.
            SettingsSections.SelectedIndex = 0;
            ShowSettingsSection("Account");
        }
        finally
        {
            SettingsLoading = false;
        }
    }

    /// <summary>
    /// The panel is gone, so the plaintexts it was holding go with it.
    /// </summary>
    /// <remarks>
    /// A minted token lives for as long as the screen showing it and no longer. That is the whole
    /// of its storage policy, and it only holds if something actually forgets.
    /// </remarks>
    internal void Closed() => Shell.Settings.ApiAccess.ForgetMintedCredentials();

    private async void OnSettingsSectionChosen(object sender, SelectionChangedEventArgs args)
    {
        if (SettingsSections.SelectedItem is not FrameworkElement { Tag: string section })
        {
            return;
        }
        // Three entries are doors, not pages: the web's own Help, Privacy and Terms open in the
        // browser, so there is one copy of each (task 438494c7). The rail stays where it was.
        var door = section switch
        {
            "Help" => "https://astrid.cc/help",
            "Privacy" => "https://astrid.cc/privacy",
            "Terms" => "https://astrid.cc/terms",
            _ => null,
        };
        if (door is not null)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(door));
            return;
        }
        ShowSettingsSection(section);

        // Loaded when its page is opened rather than when the flyout is: it is a network round
        // trip for a page most opens never reach, and the account flyout already makes four.
        if (section == "ApiAccess")
        {
            await Shell.Settings.ApiAccess.LoadApiAccessAsync();
        }
        if (section == "Contacts")
        {
            await Shell.Settings.Contacts.LoadContactsAsync();
        }
    }

    /// <summary>
    /// Show one settings page and hide the rest.
    /// </summary>
    /// <remarks>
    /// astrid-web navigates between pages behind its hub; a flyout has nowhere to navigate to, so
    /// the pages are all here and one is visible. The rail's tag names the page, so adding one is
    /// a <c>ListViewItem</c> and a panel with the same name rather than an entry in a table
    /// somewhere else.
    /// </remarks>
    private void ShowSettingsSection(string section)
    {
        SettingsSectionTitle.Text = section switch
        {
            "Reminders" => Strings.Get("settings.reminders"),
            "Tasks" => Strings.Get("smart.title"),
            "Contacts" => Strings.Get("contacts.title"),
            "Appearance" => Strings.Get("settings.appearance"),
            "Agents" => Strings.Get("settings.agents"),
            "ApiAccess" => Strings.Get("settings.api_access"),
            "Integrations" => Strings.Get("settings.integrations"),
            "Data" => Strings.Get("settings.data"),
            _ => Strings.Get("settings.account"),
        };

        AccountSection.Visibility = Visible("Account");
        TasksSection.Visibility = Visible("Tasks");
        ContactsSection.Visibility = Visible("Contacts");
        RemindersSection.Visibility = Visible("Reminders");
        AppearanceSection.Visibility = Visible("Appearance");
        AgentsSection.Visibility = Visible("Agents");
        ApiAccessSection.Visibility = Visible("ApiAccess");
        IntegrationsSection.Visibility = Visible("Integrations");
        DataSection.Visibility = Visible("Data");

        Visibility Visible(string name) =>
            name == section ? Visibility.Visible : Visibility.Collapsed;
    }
}
