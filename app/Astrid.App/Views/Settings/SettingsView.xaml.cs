using Astrid.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Astrid.App.Views.Settings;

/// <summary>
/// Settings over the whole window: the web's hub of categories, and one page at a time beside it.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(SettingsView),
        new PropertyMetadata(null, (sender, _) => ((SettingsView)sender).OnShellChanged()));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    /// <summary>
    /// What each page is, as the web's hub says it: the line under the card and under the page's
    /// title, the icon, and its colour. One table, so the card and the page cannot disagree.
    /// </summary>
    private static readonly Dictionary<string, (string TitleKey, string LineKey, string Glyph, string Colour)> Pages =
        new(StringComparer.Ordinal)
        {
            ["Account"] = ("settings.account", "settings.account_line", "", "#3B82F6"),
            ["Reminders"] = ("settings.reminders", "settings.reminders_line", "", "#F97316"),
            ["Tasks"] = ("smart.title", "settings.tasks_line", "", "#3B82F6"),
            ["Agents"] = ("settings.agents", "settings.agents_line", "", "#A855F7"),
            ["ApiAccess"] = ("settings.api_access", "settings.api_access_line", "", "#2563EB"),
            ["Contacts"] = ("contacts.title", "settings.contacts_line", "", "#14B8A6"),
            ["Appearance"] = ("settings.appearance", "settings.appearance_line", "", "#EC4899"),
            ["Integrations"] = ("settings.integrations", "settings.integrations_line", "", "#10B981"),
            ["Data"] = ("settings.data", "settings.data_line", "", "#059669"),
            ["Help"] = ("help.title", "settings.help_line", "", "#06B6D4"),
            ["Privacy"] = ("privacy.title", "settings.privacy_line", "", "#6B7280"),
            ["Terms"] = ("terms.title", "settings.terms_line", "", "#6B7280"),
        };

    public SettingsView()
    {
        InitializeComponent();
        // The hub's own words, from the strings table like everything else (tasks c0f3db19, 438494c7).
        TasksNavLabel.Text = Strings.Get("smart.title");
        ContactsNavLabel.Text = Strings.Get("contacts.title");
        HelpNavLabel.Text = Strings.Get("help.title");
        PrivacyNavLabel.Text = Strings.Get("privacy.title");
        TermsNavLabel.Text = Strings.Get("terms.title");
        AccountCardLine.Text = Strings.Get(Pages["Account"].LineKey);
        RemindersCardLine.Text = Strings.Get(Pages["Reminders"].LineKey);
        TasksCardLine.Text = Strings.Get(Pages["Tasks"].LineKey);
        AgentsCardLine.Text = Strings.Get(Pages["Agents"].LineKey);
        ApiAccessCardLine.Text = Strings.Get(Pages["ApiAccess"].LineKey);
        ContactsCardLine.Text = Strings.Get(Pages["Contacts"].LineKey);
        AppearanceCardLine.Text = Strings.Get(Pages["Appearance"].LineKey);
        IntegrationsCardLine.Text = Strings.Get(Pages["Integrations"].LineKey);
        DataCardLine.Text = Strings.Get(Pages["Data"].LineKey);
        HelpCardLine.Text = Strings.Get(Pages["Help"].LineKey);
        PrivacyCardLine.Text = Strings.Get(Pages["Privacy"].LineKey);
        TermsCardLine.Text = Strings.Get(Pages["Terms"].LineKey);
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
    /// True while the pages are being filled in.
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
    /// Load the account as the settings open, and put the times in the pickers.
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
            // The account reads from the cache before it asks the server, so the first page has
            // something to draw at once. The other answers are the server's, belong to pages
            // further down, and do not depend on each other — so they are asked for together
            // rather than one after another, which was four round trips of blank panel.
            var others = Task.WhenAll(
                Shell.Settings.Account.LoadPasskeysAsync(),
                Shell.Settings.Agents.LoadAgentsAsync(),
                Shell.Settings.Agents.LoadAstridModelAsync(),
                Shell.Settings.Integrations.LoadGoogleSyncModeAsync(),
                Shell.Settings.Agents.LoadWebhookAsync());
            await Shell.LoadSettingsAsync();
            await others;
            RemindersSection.ShowLoaded();
            // Opens on the first page every time. A screen that reopens three pages deep in
            // whatever was last poked at is a screen you have to navigate out of before you can use.
            SettingsSections.SelectedIndex = 0;
            ShowSettingsSection("Account");
        }
        finally
        {
            SettingsLoading = false;
        }
    }

    /// <summary>
    /// The screen is gone, so the plaintexts it was holding go with it.
    /// </summary>
    /// <remarks>
    /// A minted token lives for as long as the screen showing it and no longer. That is the whole
    /// of its storage policy, and it only holds if something actually forgets.
    /// </remarks>
    internal void Closed() => Shell.Settings.ApiAccess.ForgetMintedCredentials();

    /// <summary>The bar's arrow: back to the list, exactly as it was.</summary>
    private void OnBack(object sender, RoutedEventArgs args) => Shell.ShowSettings(false);

    private async void OnSettingsSectionChosen(object sender, SelectionChangedEventArgs args)
    {
        if (SettingsSections.SelectedItem is not FrameworkElement { Tag: string section })
        {
            return;
        }
        // Three entries are doors, not pages: the web's own Help, Privacy and Terms open in the
        // browser, so there is one copy of each (task 438494c7). The page on the right stays.
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

        // Loaded when its page is opened rather than when the settings are: it is a network round
        // trip for a page most opens never reach, and opening already makes four.
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
    /// Show one settings page and hide the rest, under the header the web gives that page.
    /// </summary>
    /// <remarks>
    /// astrid-web navigates between pages behind its hub; this screen has nowhere to navigate to,
    /// so the pages are all here and one is visible. The hub card's tag names the page, so adding
    /// one is a <c>ListViewItem</c>, a panel with the same name, and a row in <see cref="Pages"/>.
    /// </remarks>
    private void ShowSettingsSection(string section)
    {
        var page = Pages.TryGetValue(section, out var known) ? known : Pages["Account"];
        SettingsSectionTitle.Text = Strings.Get(page.TitleKey);
        SettingsSectionLine.Text = Strings.Get(page.LineKey);
        SettingsSectionIcon.Glyph = page.Glyph;
        SettingsSectionIcon.Foreground = new SolidColorBrush(ColorOf(page.Colour));

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

    /// <summary>A <c>#RRGGBB</c> from the table, as a colour.</summary>
    private static Color ColorOf(string hex) =>
        Color.FromArgb(
            255,
            Convert.ToByte(hex.Substring(1, 2), 16),
            Convert.ToByte(hex.Substring(3, 2), 16),
            Convert.ToByte(hex.Substring(5, 2), 16));
}
