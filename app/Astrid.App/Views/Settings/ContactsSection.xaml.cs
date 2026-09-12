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
/// The Contacts page (task 438494c7): who has been imported, and the button that clears them.
/// </summary>
public sealed partial class ContactsSection : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(ContactsSection),
        new PropertyMetadata(null));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public ContactsSection()
    {
        InitializeComponent();
        // The page's words (task 438494c7).
        ContactsDescription.Text = Strings.Get("contacts.description");
        ContactsCountSuffix.Text = Strings.Get("contacts.count_suffix");
        ContactsEmpty.Text = Strings.Get("contacts.empty");
        ClearContactsButton.Content = Strings.Get("contacts.clear");
    }

    /// <summary>Clear every imported contact, after asking — the web asks too.</summary>
    private void OnClearContacts(object sender, RoutedEventArgs args)
    {
        var confirm = new Button
        {
            Content = Strings.Get("contacts.clear_yes"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(confirm, "Confirm clear contacts");
        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 8,
                MaxWidth = 260,
                Children =
                {
                    new TextBlock { TextWrapping = TextWrapping.Wrap, Text = Strings.Get("contacts.clear_confirm") },
                    confirm,
                },
            },
        };
        confirm.Click += async (_, _) =>
        {
            flyout.Hide();
            await Shell.Settings.Contacts.ClearContactsAsync();
        };
        flyout.ShowAt(ClearContactsButton);
    }
}
