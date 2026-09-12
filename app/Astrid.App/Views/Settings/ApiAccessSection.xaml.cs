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
/// The API access page: an MCP token and client credentials, minted here and shown once.
/// </summary>
public sealed partial class ApiAccessSection : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(ApiAccessSection),
        new PropertyMetadata(null));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public ApiAccessSection()
    {
        InitializeComponent();
    }

    // ── API access ───────────────────────────────────────────────────────────────────────────

    private async void OnCreateMcpToken(object sender, RoutedEventArgs args) =>
        await Shell.Settings.ApiAccess.CreateMcpTokenAsync();

    private async void OnRevokeMcpTokens(object sender, RoutedEventArgs args) =>
        await Shell.Settings.ApiAccess.RevokeMcpTokensAsync();

    private async void OnCreateOAuthClient(object sender, RoutedEventArgs args) =>
        await Shell.Settings.ApiAccess.CreateOAuthClientAsync();

    private async void OnDeleteOAuthClient(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: string clientId })
        {
            await Shell.Settings.ApiAccess.DeleteOAuthClientAsync(clientId);
        }
    }

    /// <summary>
    /// Put the token on the clipboard.
    /// </summary>
    /// <remarks>
    /// The only reason it is on screen. Selecting a credential by hand out of a wrapped read-only
    /// box is how somebody copies half of one and spends an afternoon on the 401 it causes.
    /// </remarks>
    private void OnCopyMcpToken(object sender, RoutedEventArgs args) =>
        ClipboardText.Copy(Shell.Settings.ApiAccess.McpToken);

    /// <summary>Both halves at once, labelled, because the pair is useless one at a time.</summary>
    private void OnCopyMintedClient(object sender, RoutedEventArgs args)
    {
        if (Shell.Settings.ApiAccess.MintedClient is not { } minted)
        {
            return;
        }
        ClipboardText.Copy(
            $"ASTRID_OAUTH_CLIENT_ID={minted.ClientId}{Environment.NewLine}"
            + $"ASTRID_OAUTH_CLIENT_SECRET={minted.ClientSecret}");
    }
}
