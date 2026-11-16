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
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.System;

namespace Astrid.App.Views;

/// <summary>
/// The sidebar: My Tasks, the favourites, the lists, and the box that makes a new one.
/// </summary>
public sealed partial class SidebarView : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(SidebarView),
        new PropertyMetadata(null));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public SidebarView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The footer entry opens the settings screen; the shell owns whether it is open. It came
    /// from the list header with the account row (task ffcc6abf).
    /// </summary>
    private void OnOpenSettings(object sender, RoutedEventArgs args) => Shell.ShowSettings(true);

    // ── The bell (task 96759883) ─────────────────────────────────────────────────────────────
    //
    // It was the list header's until web put its own bell in the sidebar header (AWTD-1357).

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

    /// <summary>Make every live row resolve its style's theme brushes again; see <c>ShellPage.ApplyTheme</c>.</summary>
    internal void Restyle()
    {
        Restyling.Reapply(MyTasksList);
        Restyling.Reapply(FavoritesList);
        Restyling.Reapply(ListsList);
    }

    // ── Public lists (task f6bc59e8) ─────────────────────────────────────────────────────────

    /// <summary>The catalogue is the server's, fetched as the flyout opens.</summary>
    private async void OnPublicListsOpening(object sender, object args) =>
        await Shell.PublicLists.LoadAsync();

    /// <summary>Copy one into this account; the shell opens the copy, so the flyout can go.</summary>
    private async void OnCopyPublicList(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not string listId)
        {
            return;
        }
        if (await Shell.CopyPublicListAsync(listId))
        {
            PublicListsFlyout.Hide();
            SyncSelectionFromViewModel();
        }
    }

    // ── A row dropped on a list (task 27cae198) ─────────────────────────────────────────────
    //
    // The web moves a task between lists by dragging its row onto a list in the sidebar, and
    // adds it instead while Shift is held. What is dragged is a task id as text — the same
    // package the board's cards carry — and what a drop writes is the view model's decision.

    /// <summary>The list a row of the sidebar draws, when the sender is one.</summary>
    private static ListSummary? ListOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as ListSummary;

    private static bool ShiftHeld(DragEventArgs args) =>
        args.Modifiers.HasFlag(DragDropModifiers.Shift);

    /// <summary>A list will take a task — a real list, not a filter — as a move, or as an add with Shift.</summary>
    private void OnListDragOver(object sender, DragEventArgs args)
    {
        var accepts = ListOf(sender) is { IsDropTarget: true }
            && args.DataView.Contains(StandardDataFormats.Text);
        args.AcceptedOperation = !accepts
            ? DataPackageOperation.None
            : ShiftHeld(args) ? DataPackageOperation.Copy : DataPackageOperation.Move;
    }

    /// <summary>
    /// A task was dropped on a list. Two ids and whether Shift was held go to the view model;
    /// the deferral keeps the package alive across the await, as the board's drop does.
    /// </summary>
    private async void OnListDrop(object sender, DragEventArgs args)
    {
        if (ListOf(sender) is not { IsDropTarget: true } list
            || !args.DataView.Contains(StandardDataFormats.Text))
        {
            return;
        }
        var add = ShiftHeld(args);
        var deferral = args.GetDeferral();
        try
        {
            var taskId = await args.DataView.GetTextAsync();
            if (!string.IsNullOrEmpty(taskId))
            {
                await Shell.DropTaskOnListAsync(taskId, list.Id, add);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// A list dragged out of the sidebar, which is one of the three ways a project board's rows
    /// are chosen (task f8b844dd).
    /// </summary>
    /// <remarks>
    /// <para>
    /// It travels under a private clipboard format rather than as text — see
    /// <see cref="ListDrag"/>. Every drop target in the app that takes text reads it as a task id,
    /// including the sidebar row directly under the pointer as the drag begins, so a list sent as
    /// text would be accepted as a task nothing can find.
    /// </para>
    /// <para>
    /// A saved filter is not offered: it is a question rather than a place, the core does not
    /// offer it as a row candidate, and a drag that can only end in a refusal is worse than one
    /// that never starts.
    /// </para>
    /// </remarks>
    private void OnListDragStarting(object sender, DragItemsStartingEventArgs args)
    {
        if (args.Items.FirstOrDefault() is not ListSummary { IsVirtual: not true } list
            || string.IsNullOrEmpty(list.Id))
        {
            args.Cancel = true;
            return;
        }
        ListDrag.Set(args.Data, list.Id);
    }

    private async void OnListSelected(object sender, SelectionChangedEventArgs args)
    {
        if (sender is not ListView view || view.SelectedItem is not ListSummary selected)
        {
            return;
        }

        // Three list views, one selection. Clearing the others keeps the highlight where the user
        // clicked instead of leaving several rows looking selected.
        foreach (var other in new[] { MyTasksList, FavoritesList, ListsList })
        {
            if (!ReferenceEquals(view, other))
            {
                other.SelectedItem = null;
            }
        }

        Shell.Sidebar.Selected = selected;
        await Shell.OpenSelectedAsync();
    }

    // ── The Projects section (task f8b844dd) ────────────────────────────────────────────────────

    /// <summary>The chevron: open or close a project's lists. It opens no board.</summary>
    /// <remarks>
    /// Two controls on one row because they do two things — the web's Projects section is the
    /// same. A single row that both navigated and expanded would make "see what is in this
    /// project" and "go to this project" the same gesture, and one of them would always be the
    /// one you did not want.
    /// </remarks>
    private void OnToggleProject(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is SidebarProjectView project)
        {
            project.ToggleExpanded();
        }
    }

    /// <summary>The project's name: open its board.</summary>
    private async void OnOpenProjectBoard(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string projectId)
        {
            await Shell.OpenProjectBoardAsync(projectId);
        }
    }

    /// <summary>
    /// A list under a project: open the list, as clicking it in the Lists section would.
    /// </summary>
    /// <remarks>
    /// Through the sidebar's own <c>Selected</c>, so the highlight lands on that list's row in
    /// the Lists section above — where every list has exactly one row, whichever way it was
    /// reached. The core puts a project's lists in both sections on purpose.
    /// </remarks>
    private async void OnOpenProjectList(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not string listId)
        {
            return;
        }
        var list = Shell.Sidebar.Favorites.Concat(Shell.Sidebar.Lists)
            .FirstOrDefault(candidate => candidate.Id == listId);
        if (list is null)
        {
            return;
        }
        Shell.Sidebar.Selected = list;
        await Shell.OpenSelectedAsync();
    }

    private async void OnAddList(object sender, RoutedEventArgs args) => await AddTypedList();

    private async void OnNewListKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }
        args.Handled = true;
        await AddTypedList();
    }

    private async Task AddTypedList()
    {
        if (await Shell.Sidebar.CreateListAsync(NewListBox.Text))
        {
            NewListBox.Text = string.Empty;
            SyncSelectionFromViewModel();
            await Shell.OpenSelectedAsync();
        }
    }

    /// <summary>
    /// Hide the sidebar, and bring it back from the rail (task ab4cb9ee).
    /// </summary>
    /// <remarks>
    /// Two handlers for the two controls, for the reason <see cref="ChatPane"/> gives: an
    /// <c>x:Uid</c> names a static resource, so a button whose label changed would be a string
    /// no translator could reach.
    /// </remarks>
    private void OnHideSidebar(object sender, RoutedEventArgs args) => Shell.ShowSidebar(false);

    private void OnShowSidebar(object sender, RoutedEventArgs args) => Shell.ShowSidebar(true);

    /// <summary>Put the highlight where the view model says the selection is.</summary>
    internal void SyncSelectionFromViewModel()
    {
        var selected = Shell.Sidebar.Selected;
        if (selected is null)
        {
            return;
        }

        if (Shell.Sidebar.Favorites.Contains(selected))
        {
            FavoritesList.SelectedItem = selected;
            ListsList.SelectedItem = null;
        }
        else
        {
            ListsList.SelectedItem = selected;
            FavoritesList.SelectedItem = null;
        }
    }
}
