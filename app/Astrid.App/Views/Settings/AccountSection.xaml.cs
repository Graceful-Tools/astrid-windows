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
/// The Account page: sync and sign out, the profile, verification, passkeys, and deleting the account.
/// </summary>
public sealed partial class AccountSection : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(AccountSection),
        new PropertyMetadata(null));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public AccountSection()
    {
        InitializeComponent();
        WordAccountSection();
    }

    /// <summary>The account page's words, from the strings table (task 19fd9289).</summary>
    private void WordAccountSection()
    {
        ProfileTitle.Text = Strings.Get("account.profile");
        ChangePhotoButton.Content = Strings.Get("account.change_photo");
        PhotoHint.Text = Strings.Get("account.photo_hint");
        DisplayNameBox.Header = Strings.Get("account.display_name");
        SaveNameButton.Content = Strings.Get("account.save");
        VerificationTitle.Text = Strings.Get("account.verification");
        ResendButton.Content = Strings.Get("account.resend");
        PendingPrefix.Text = Strings.Get("account.waiting_for");
        VerificationSentLine.Text = Strings.Get("account.verification_sent");
        InfoTitle.Text = Strings.Get("account.information");
        InfoCreatedLabel.Text = Strings.Get("account.created");
        InfoUpdatedLabel.Text = Strings.Get("account.last_updated");
        InfoIdLabel.Text = Strings.Get("account.account_id");
        PasskeysTitle.Text = Strings.Get("account.passkeys");
        PasskeysNote.Text = Strings.Get("passkeys.note");
        PasskeysEmpty.Text = Strings.Get("passkeys.none");
        ManagePasskeysButton.Content = Strings.Get("passkeys.add");
        DeleteTitle.Text = Strings.Get("account.delete_title");
        DeleteWarning.Text = Strings.Get("account.delete_warning");
        DeleteConfirmationBox.PlaceholderText = Strings.Get("account.type_to_confirm");
        DeleteAccountButton.Content = Strings.Get("account.delete_button");
    }

    private async void OnSync(object sender, RoutedEventArgs args) => await Shell.SyncAsync();

    private async void OnSignOut(object sender, RoutedEventArgs args) => await Shell.SignOutAsync();

    /// <summary>
    /// Pick a picture for the profile. The kinds offered are the ones the server accepts for an
    /// upload; the core uploads the file and puts its address on the account.
    /// </summary>
    private async void OnChangePhoto(object sender, RoutedEventArgs args)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" })
        {
            picker.FileTypeFilter.Add(extension);
        }
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await Shell.Settings.SetPhotoAsync(file.Path);
        }
    }

    private async void OnSaveName(object sender, RoutedEventArgs args) =>
        await Shell.Settings.SaveNameAsync();

    private async void OnResendVerification(object sender, RoutedEventArgs args) =>
        await Shell.Settings.ResendVerificationAsync();

    /// <summary>Registering a passkey is the browser's WebAuthn ceremony; the list here follows.</summary>
    private async void OnManagePasskeys(object sender, RoutedEventArgs args) =>
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://astrid.cc/settings"));

    /// <summary>Rename a passkey: a box in a flyout, Enter to save (task 19fd9289).</summary>
    private void OnRenamePasskey(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: string id } anchor)
        {
            return;
        }
        var current = Shell.Settings.Passkeys.FirstOrDefault(key => key.Id == id);
        var box = new TextBox
        {
            Text = current?.Name ?? string.Empty,
            PlaceholderText = Strings.Get("passkeys.rename_prompt"),
            MinWidth = 220,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, "Passkey name");
        var flyout = new Flyout { Content = box };
        box.KeyDown += async (_, key) =>
        {
            if (key.Key == VirtualKey.Enter)
            {
                key.Handled = true;
                flyout.Hide();
                await Shell.Settings.RenamePasskeyAsync(id, box.Text);
            }
        };
        flyout.ShowAt(anchor);
    }

    /// <summary>Revoke a passkey, after asking — losing a way to sign in is worth a question.</summary>
    private void OnRevokePasskey(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: string id } anchor)
        {
            return;
        }
        var confirm = new Button
        {
            Content = Strings.Get("passkeys.revoke_yes"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(confirm, "Confirm revoke passkey");
        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 8,
                MaxWidth = 260,
                Children =
                {
                    new TextBlock { TextWrapping = TextWrapping.Wrap, Text = Strings.Get("passkeys.revoke_confirm") },
                    confirm,
                },
            },
        };
        confirm.Click += async (_, _) =>
        {
            flyout.Hide();
            await Shell.Settings.RevokePasskeyAsync(id);
        };
        flyout.ShowAt(anchor);
    }

    /// <summary>
    /// Delete the account. The core has already signed out by the time this returns true; what is
    /// left is to show the door, which is what signing out from here does.
    /// </summary>
    private async void OnDeleteAccount(object sender, RoutedEventArgs args)
    {
        if (await Shell.Settings.DeleteAccountAsync())
        {
            await Shell.SignOutAsync();
        }
    }
}
