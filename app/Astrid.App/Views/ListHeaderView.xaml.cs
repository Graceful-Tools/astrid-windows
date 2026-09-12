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
/// The row above the list: its name, the filter, the chat, the settings, the board toggle, the search, the bell and the account.
/// </summary>
public sealed partial class ListHeaderView : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(ListHeaderView),
        new PropertyMetadata(null, (sender, _) => ((ListHeaderView)sender).OnShellChanged()));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public ListHeaderView()
    {
        InitializeComponent();
    }

    /// <summary>The two flyouts bind to the same view model this row does.</summary>
    private void OnShellChanged()
    {
        ListSettings.Shell = Shell;
        Settings.Shell = Shell;
    }

    private async void OnAccountOpening(object sender, object args) => await Settings.OpenedAsync();

    private void OnAccountClosed(object sender, object args) => Settings.Closed();

    /// <summary>
    /// Search as the query is typed.
    /// </summary>
    /// <remarks>
    /// Only when the change came from the keyboard: the box also raises this when its text is set
    /// in code, and searching for what was just put there would fight whoever set it. The search
    /// itself is a cache read, so there is no debounce — a round trip that never leaves the process
    /// is faster than the delay a debounce would add.
    /// </remarks>
    private async void OnSearchChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }
        await Shell.Tasks.SearchAsync(sender.Text);
    }

    // ── What the list shows ──────────────────────────────────────────────────────────

    private async void OnFiltersOpening(object sender, object args)
    {
        await Shell.Tasks.LoadFiltersAsync();
    }

    /// <summary>
    /// A filter was chosen.
    /// </summary>
    /// <remarks>
    /// The already-chosen one fires this too, as the flyout draws itself and sets the radio button
    /// that was on. Writing then would send the list's own setting back to it on every open — a
    /// pointless round trip, and an edit in the Outbox that nobody made.
    /// </remarks>
    private async void OnFilterChosen(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not FilterPick pick || pick.IsSelected)
        {
            return;
        }
        await Shell.Tasks.SetFilterAsync(pick.Field, pick.Value);
    }

    // ── The conversation ─────────────────────────────────────────────────────────────────────

    private async void OnToggleChat(object sender, RoutedEventArgs args)
    {
        await Shell.ShowChatAsync(ChatToggle.IsChecked == true);
    }

    // ── The list's settings and members ──────────────────────────────────────────────────────

    private async void OnListSettingsOpening(object sender, object args)
    {
        // The settings draw from the cache and then catch up with the server. The links and
        // the agent choices (task f44b4a0c) are the server's, independent of each other, and
        // asked for together once the list's own facts are on screen.
        await Shell.LoadListSettingsAsync();
        await Task.WhenAll(
            Shell.ListSettings.LoadExternalAsync(),
            Shell.ListSettings.LoadAgentOptionsAsync());
    }

    // ── The board ────────────────────────────────────────────────────────────────────────────

    private async void OnToggleBoard(object sender, RoutedEventArgs args)
    {
        await Shell.ShowBoardAsync(BoardToggle.IsChecked == true);
    }

    // ── The bell ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Opening the bell asks the server; the badge was already right from the cache.</summary>
    private async void OnNotificationsOpening(object sender, object args) =>
        await Shell.Notifications.RefreshAsync();

    private async void OnMarkAllNotificationsRead(object sender, RoutedEventArgs args) =>
        await Shell.Notifications.MarkAllReadAsync();

    /// <summary>A row is the task it is about: mark it read, open the task.</summary>
    private async void OnNotificationOpened(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is not NotificationItem item)
        {
            return;
        }
        NotificationsFlyout.Hide();
        if (!item.IsRead)
        {
            await Shell.Notifications.MarkReadAsync(item.Id);
        }
        if (item.TaskId is { } taskId)
        {
            await Shell.OpenTaskAsync(taskId);
        }
    }
}
