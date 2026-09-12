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
