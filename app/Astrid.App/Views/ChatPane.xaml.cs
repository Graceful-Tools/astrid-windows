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

namespace Astrid.App.Views;

/// <summary>
/// The list's conversation: the bubbles, and the box that posts to it.
/// </summary>
public sealed partial class ChatPane : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(ChatPane),
        new PropertyMetadata(null));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public ChatPane()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Hide the pane, and show it again from the rail (task ab4cb9ee).
    /// </summary>
    /// <remarks>
    /// Two buttons rather than one that changes label, because a WinUI <c>x:Uid</c> names a static
    /// resource: a single control whose words swapped would be a string no translator could reach,
    /// which the localisation tests fail the build over. Each state has its own control, in its own
    /// half of the control's two states, and exactly one is ever on screen.
    /// </remarks>
    private async void OnHideChat(object sender, RoutedEventArgs args) =>
        await Shell.ShowChatAsync(false);

    private async void OnShowChat(object sender, RoutedEventArgs args) =>
        await Shell.ShowChatAsync(true);

    private async void OnChatKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }
        args.Handled = true;
        await SendChat();
    }

    private async void OnSendChat(object sender, RoutedEventArgs args) => await SendChat();

    /// <summary>
    /// Post what is in the box.
    /// </summary>
    /// <remarks>
    /// The key and the button run the same path. Return used to be the only way to send, which
    /// made the one action the panel has a keystroke you had to already know about.
    /// </remarks>
    private async Task SendChat()
    {
        if (await Shell.Chat.SendAsync(ChatBox.Text))
        {
            ChatBox.Text = string.Empty;
        }
    }
}
