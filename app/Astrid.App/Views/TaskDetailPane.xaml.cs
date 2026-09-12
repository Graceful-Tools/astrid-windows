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
/// The task detail: the fields, the description, the subtasks, the attachments, the thread and the comment box.
/// </summary>
public sealed partial class TaskDetailPane : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(TaskDetailPane),
        new PropertyMetadata(null, (sender, _) => ((TaskDetailPane)sender).OnShellChanged()));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public TaskDetailPane()
    {
        InitializeComponent();
        // Where a pill in a comment goes when clicked: the same places a pill in the description
        // goes (task 3271a0c5).
        WontDoChip.Text = Strings.Get("detail.wont_do_chip");
        MarkdownView.ReferenceFollowed = reference => _ = FollowReferenceAsync(reference);
        MarkdownView.LinkFollowed = link => _ = FollowLinkAsync(link);
    }

    private void OnShellChanged()
    {
        // The squares follow the open task wherever its priority came from — a tap, a sync, a
        // task opened from the palette or a pill — not only the handlers that remembered to
        // repaint (task 204c9d98).
        Shell.Detail.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName is nameof(TaskDetailViewModel.Priority)
                or nameof(TaskDetailViewModel.IsOpen))
            {
                SyncPriority();
            }
            // The description is redrawn from its blocks whenever they change (task 11cfaf6d).
            if (changed.PropertyName == nameof(TaskDetailViewModel.DescriptionBlocks))
            {
                RenderDescription();
            }
        };
    }

    // ── Where the pane sits (task 91a25b8a) ──────────────────────────────────────────────────
    //
    // The page moves this one pane between the column beside the list and the slot after a
    // board's expanded card. What changes with the move is the pane's own shape, which is here.

    /// <summary>
    /// A card, not a panel: every edge drawn, the corner the cards have, and the same 6px gap
    /// under it the cards keep between themselves.
    /// </summary>
    internal void WearAsCard()
    {
        DetailPane.Width = double.NaN;
        DetailPane.BorderThickness = new Thickness(1);
        DetailPane.CornerRadius = new CornerRadius(10);
        DetailPane.Margin = new Thickness(0, 0, 0, 6);
    }

    /// <summary>The column beside the list: one rule down its left edge, and the width it was given.</summary>
    internal void WearAsColumn()
    {
        DetailPane.Width = 360;
        DetailPane.BorderThickness = new Thickness(1, 0, 0, 0);
        DetailPane.CornerRadius = new CornerRadius(0);
        DetailPane.Margin = new Thickness(0);
    }

    /// <summary>
    /// Point the pane's arrow at the row it is describing.
    /// </summary>
    /// <remarks>
    /// Geometry, so the shell measures it: where a row sits on screen is not something the view
    /// model can know or should be told. astrid-web does the same thing with an `arrowTop` it
    /// recomputes as the list moves (task 830e63b9). The page decides whether there is a row to
    /// point at; this only measures it.
    /// </remarks>
    internal void PointArrowAt(FrameworkElement container)
    {
        try
        {
            var middle = container
                .TransformToVisual(DetailArrow.Parent as UIElement)
                .TransformPoint(new Windows.Foundation.Point(0, container.ActualHeight / 2));

            // Off the top or bottom of the pane: the row is scrolled away, so there is nothing to
            // join to.
            if (middle.Y < 0 || middle.Y > ((FrameworkElement)DetailArrow.Parent).ActualHeight)
            {
                DetailArrow.Visibility = Visibility.Collapsed;
                return;
            }

            DetailArrowOffset.Y = middle.Y - (DetailArrow.Height / 2);
            DetailArrow.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            // TransformToVisual throws when either element is not in the tree yet, which happens
            // on the first layout pass. Nothing to point at yet is not an error.
            DetailArrow.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Hidden rather than parked at the top when there is no row to point at — a row scrolled out
    /// of view, or a task opened from search or a deep link with no row on screen at all. An arrow
    /// aimed at nothing is worse than no arrow.
    /// </summary>
    internal void HideArrow() => DetailArrow.Visibility = Visibility.Collapsed;

    private async void OnCloseDetail(object sender, RoutedEventArgs args) => await Shell.Detail.CloseAsync();

    /// <summary>
    /// Word the task menu as the task stands, and fill its Status submenu from the core.
    /// </summary>
    /// <remarks>
    /// The words are set here rather than in the markup because "Won't do" reads "Reopen" on a
    /// canceled task — the web's one entry, flipped — and the columns are asked for each time
    /// because they are the task's board's, which can be renamed under an open pane. The shell
    /// draws what it is given; which columns exist and which one is lit are the core's to say
    /// (task 016ce981).
    /// </remarks>
    private async void OnTaskActionsOpening(object sender, object args)
    {
        CopyLinkItem.Text = Strings.Get("detail.copy_link");
        ShareItem.Text = Strings.Get("detail.share");
        StatusSubMenu.Text = Strings.Get("detail.status");
        WontDoItem.Text = Strings.Get(Shell.Detail.WontDoLabelKey);
        WontDoItem.Icon = new FontIcon { Glyph = Shell.Detail.IsCanceled ? "\uE7A7" : "\uE711" };

        await Shell.Detail.LoadStatusChoicesAsync();
        StatusSubMenu.Items.Clear();
        foreach (var choice in Shell.Detail.StatusChoices)
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = choice.Name,
                IsChecked = choice.IsCurrent,
                GroupName = "TaskStatus",
                Tag = choice.Id,
            };
            item.Click += OnStatusChosen;
            StatusSubMenu.Items.Add(item);
        }
    }

    private async void OnStatusChosen(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: string columnId })
        {
            await Shell.Detail.SetStatusAsync(columnId);
        }
    }

    private async void OnToggleWontDo(object sender, RoutedEventArgs args) =>
        await Shell.Detail.ToggleWontDoAsync();

    private void OnCopyTaskLink(object sender, RoutedEventArgs args) =>
        ClipboardText.Copy(Shell.Detail.Link);

    // ── Copy ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copy the open task: pick a list, say whether the comments come, as the web's copy dialog.
    /// </summary>
    /// <remarks>
    /// Built here rather than in XAML because the lists to choose from are the sidebar's, and a
    /// flyout declared inside the task menu cannot see them until it opens. The choices and the
    /// words are the core's and the resource file's; this only arranges them.
    /// </remarks>
    private void OnCopyTask(object sender, RoutedEventArgs args)
    {
        var lists = new ComboBox
        {
            Header = Strings.Get("copy.title"),
            DisplayMemberPath = nameof(ListSummary.Name),
            MinWidth = 260,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(lists, "Copy target list");
        // First choice: where the task already is. Then every place the sidebar offers.
        var sameLists = new ListSummary { Id = string.Empty, Name = Strings.Get("copy.same_list") };
        lists.Items.Add(sameLists);
        foreach (var list in Shell.Sidebar.Favorites.Concat(Shell.Sidebar.Lists))
        {
            lists.Items.Add(list);
        }
        lists.SelectedIndex = 0;

        var comments = new CheckBox { Content = Strings.Get("copy.include_comments") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(comments, "Include comments");
        var confirm = new Button
        {
            Content = Strings.Get("copy.button"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(confirm, "Confirm copy task");
        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 8,
                Children = { lists, comments, confirm },
            },
        };
        confirm.Click += async (_, _) =>
        {
            var target = lists.SelectedItem as ListSummary;
            var targetId = string.IsNullOrEmpty(target?.Id) ? null : target!.Id;
            flyout.Hide();
            var copied = await Shell.Detail.CopyAsync(targetId, comments.IsChecked == true);
            if (copied is not null)
            {
                await Shell.Tasks.RefreshAsync();
            }
        };
        flyout.ShowAt(TaskActionsButton);
    }

    /// <summary>
    /// Share: mint the link on the server, copy it, and show it.
    /// </summary>
    /// <remarks>
    /// The web shows the minted address in a modal with a copy button; here the copy is the
    /// point, so it happens at once and the flyout only confirms what is now on the clipboard. A
    /// share that fails — offline, or a task not yet on the server — is reported through the
    /// pane's error line by the view model, so there is nothing to do here but stop.
    /// </remarks>
    private async void OnShareTask(object sender, RoutedEventArgs args)
    {
        var url = await Shell.Detail.ShareAsync();
        if (url is null)
        {
            return;
        }
        ClipboardText.Copy(url);
        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 4,
                MaxWidth = 320,
                Children =
                {
                    new TextBlock { Text = Strings.Get("detail.share_copied") },
                    new TextBlock
                    {
                        Text = url,
                        IsTextSelectionEnabled = true,
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
        };
        flyout.ShowAt(TaskActionsButton);
    }

    /// <summary>
    /// Throw away the task the panel is showing.
    /// </summary>
    /// <remarks>
    /// The panel closes first: the task is about to stop existing, and a pane still describing it
    /// while the row underneath disappears is the worse of the two orders. Deleting goes through
    /// the list, which is the one that owns the rows on screen.
    /// </remarks>
    private async void OnDeleteOpenTask(object sender, RoutedEventArgs args)
    {
        var taskId = Shell.Detail.TaskId;
        if (string.IsNullOrEmpty(taskId))
        {
            return;
        }
        Shell.Detail.Close();
        await Shell.Tasks.DeleteTaskAsync(taskId);
    }

    /// <summary>
    /// Somebody chose a quick due-date option.
    /// </summary>
    /// <remarks>
    /// The button carries the option's key and the view model holds the instant that goes with it,
    /// so the window never computes a date. Every part of that arithmetic — daylight saving, all-day
    /// storage, what "morning" means where the reader is — is decided in the core for all three
    /// clients.
    /// </remarks>
    /// <summary>
    /// A day was chosen from the calendar.
    /// </summary>
    /// <remarks>
    /// Guarded against the null the control raises while it is being reset, which would otherwise
    /// clear the date every time the flyout closed.
    /// </remarks>
    private async void OnDueDayPicked(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args)
    {
        if (args.NewDate is not { } day)
        {
            return;
        }
        await Shell.Detail.SetDueDayAsync(day);
    }

    private async void OnDuePickChosen(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: string key })
        {
            return;
        }
        var pick = Shell.Detail.DatePicks.Concat(Shell.Detail.TimePicks)
            .FirstOrDefault(option => option.TitleKey == key);
        if (pick is not null)
        {
            await Shell.Detail.TakeDuePickAsync(pick);
        }
    }

    /// <summary>The copy control in the header, on a task that can only be copied (task f6bc59e8).</summary>
    private async void OnDetailCopyClicked(object sender, RoutedEventArgs args) =>
        await Shell.Detail.CopyToMineAsync();

    /// <summary>The mark in the detail header, tapped. The row's handler, one task over.</summary>
    private async void OnDetailMarkClicked(object sender, RoutedEventArgs args) =>
        await Shell.Detail.SetCompletedAsync(!Shell.Detail.Completed);

    /// <summary>
    /// Save the title when the box loses focus.
    /// </summary>
    /// <remarks>
    /// On focus loss rather than on every keystroke: a command per character would put a hundred
    /// entries in the Outbox for one rename, and every one of them a separate request when the
    /// network came back.
    /// </remarks>
    private async void OnDetailTitleCommitted(object sender, RoutedEventArgs args) =>
        await Shell.Detail.EndEditingAsync(TaskDetailViewModel.TitleEditor);

    // ── One editing session at a time (PRODUCT_CONTRACT.md §6, task e71ed760) ──────────────
    //
    // Focus begins an editor, blur ends it, Escape cancels it; a flyout begins on Opening and
    // ends on Closed. Which of those saves, and what a second editor does to the first, is the
    // core's rule — see TaskDetailViewModel — and the boxes update the view model on every
    // keystroke so what gets committed is what is on screen.

    private async void OnDetailTitleFocused(object sender, RoutedEventArgs args) =>
        await Shell.Detail.BeginEditingAsync(TaskDetailViewModel.TitleEditor);

    private async void OnDetailDescriptionFocused(object sender, RoutedEventArgs args) =>
        await Shell.Detail.BeginEditingAsync(TaskDetailViewModel.DescriptionEditor);

    /// <summary>Escape in the description box: revert, and go back to the drawing.</summary>
    private async void OnDetailDescriptionKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Escape)
        {
            return;
        }
        args.Handled = true;
        await Shell.Detail.CancelEditingAsync(TaskDetailViewModel.DescriptionEditor);
    }

    private async void OnAssigneeFlyoutClosed(object sender, object args) =>
        await Shell.Detail.EndEditingAsync(TaskDetailViewModel.AssigneeEditor);

    private async void OnListsFlyoutClosed(object sender, object args) =>
        await Shell.Detail.EndEditingAsync(TaskDetailViewModel.ListsEditor);

    private async void OnToggleTimer(object sender, RoutedEventArgs args)
    {
        await Shell.Detail.SetTimingAsync(!Shell.Detail.IsTiming);
    }

    // ── Attachments ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Open an attachment, fetching it first if this machine does not have it.
    /// </summary>
    /// <remarks>
    /// Opened with whatever program reads that kind of file, which is the point of downloading it
    /// to a path rather than carrying the bytes across the boundary.
    /// </remarks>
    /// <summary>A file drawn in a comment, opened the same way one on the task is.</summary>
    /// <remarks>
    /// The same handler body rather than a second one: a file is a file, and the only difference
    /// is which list it was drawn in.
    /// </remarks>
    private void OnOpenCommentFile(object sender, RoutedEventArgs args) =>
        OnOpenAttachment(sender, args);

    private async void OnOpenAttachment(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not string fileId)
        {
            return;
        }
        var path = await Shell.Detail.DownloadAsync(fileId);
        if (path is null)
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            // No program registered for that kind of file, or the shell refused. The file is still
            // downloaded and the path is still right; saying so beats a silent nothing.
            App.Log($"could not open an attachment: {error.Message}");
        }
    }

    /// <summary>
    /// Attach a file from this machine.
    /// </summary>
    /// <remarks>
    /// The picker is a WinRT one, and an unpackaged app has to tell it which window it belongs to —
    /// without that it throws rather than opening, which is the kind of failure that looks like the
    /// button doing nothing.
    /// </remarks>
    private async void OnAttachFile(object sender, RoutedEventArgs args)
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await Shell.Detail.AttachAsync(file.Path);
        }
    }

    private async void OnReminderFlyoutOpening(object sender, object args)
    {
        await Shell.Detail.LoadReminderPicksAsync();
    }

    private async void OnReminderChosen(object sender, RoutedEventArgs args)
    {
        // A null tag is "no reminder", which is a choice rather than a no-op.
        await Shell.Detail.SetReminderAsync((sender as FrameworkElement)?.Tag as string);
    }

    private async void OnRepeatFlyoutOpening(object sender, object args)
    {
        await Shell.Detail.LoadRepeatAsync();
    }

    private async void OnRepeatPresetChosen(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string value)
        {
            await Shell.Detail.SetRepeatAsync(value);
        }
    }

    private async void OnRepeatFromToggled(object sender, RoutedEventArgs args)
    {
        // Only when it differs: the switch is set from the task as the flyout opens, and reacting
        // to that would write the value back to itself on every open.
        if (RepeatFromDueDate.IsOn != Shell.Detail.RepeatsFromDueDate)
        {
            await Shell.Detail.SetRepeatFromAsync(RepeatFromDueDate.IsOn);
        }
    }

    private async void OnCustomRepeatSaved(object sender, RoutedEventArgs args)
    {
        var unit = (RepeatUnit.SelectedItem as FrameworkElement)?.Tag as string ?? "days";
        var days = RepeatWeekdays.Children
            .OfType<ToggleButton>()
            .Where(button => button.IsChecked == true)
            .Select(button => (string)button.Tag)
            .ToList();
        var ends = (RepeatEnd.SelectedItem as FrameworkElement)?.Tag as string;
        await Shell.Detail.SetCustomRepeatAsync(
            unit,
            (int)RepeatInterval.Value,
            days,
            ends,
            (int)RepeatEndCount.Value,
            RepeatEndDate.Date?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }

    /// <summary>
    /// Load the picker's rows as it opens, so they are never stale and never fetched for a screen
    /// nobody opened.
    /// </summary>
    private async void OnAssigneeFlyoutOpening(object sender, object args)
    {
        await Shell.Detail.BeginEditingAsync(TaskDetailViewModel.AssigneeEditor);
        await Shell.Detail.LoadAssigneesAsync();
    }

    private async void OnAssigneeChosen(object sender, RoutedEventArgs args)
    {
        // A null tag is the unassigned row, and clearing is a real choice rather than a no-op.
        var userId = (sender as FrameworkElement)?.Tag as string;
        await Shell.Detail.AssignAsync(userId);
    }

    // ── The lists a task is in (task d3f3b111) ──────────────────────────────────────────────

    private async void OnListsFlyoutOpening(object sender, object args)
    {
        await Shell.Detail.BeginEditingAsync(TaskDetailViewModel.ListsEditor);
        await Shell.Detail.LoadListPicksAsync(string.Empty);
    }

    private async void OnListSearchChanged(object sender, TextChangedEventArgs args)
    {
        if (sender is TextBox box && box.Text != Shell.Detail.ListSearch)
        {
            await Shell.Detail.LoadListPicksAsync(box.Text);
        }
    }

    private async void OnAddToList(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string listId)
        {
            await Shell.Detail.AddToListAsync(listId);
            await Shell.Tasks.RefreshAsync();
        }
    }

    private async void OnRemoveFromList(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string listId)
        {
            await Shell.Detail.RemoveFromListAsync(listId);
            // The open list may have just lost this task.
            await Shell.Tasks.RefreshAsync();
        }
    }

    /// <summary>A new list, and the task in it. The sidebar gains the list too.</summary>
    private async void OnCreateListForTask(object sender, RoutedEventArgs args)
    {
        if (await Shell.Detail.CreateListAsync())
        {
            await Shell.Sidebar.LoadAsync();
            await Shell.Tasks.RefreshAsync();
        }
    }

    /// <summary>
    /// Enter saves the title; Escape puts back what the task had.
    /// </summary>
    /// <remarks>
    /// Enter ends the session and begins it again, because the caret stays in the box: without
    /// the second step the next blur would be a stale end and anything typed after Enter would be
    /// lost. Escape ends it for good — the blur that follows finds nothing open, which is what
    /// "discarded" means.
    /// </remarks>
    private async void OnDetailTitleKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.Enter:
                args.Handled = true;
                await Shell.Detail.EndEditingAsync(TaskDetailViewModel.TitleEditor);
                await Shell.Detail.BeginEditingAsync(TaskDetailViewModel.TitleEditor);
                break;
            case VirtualKey.Escape:
                args.Handled = true;
                await Shell.Detail.CancelEditingAsync(TaskDetailViewModel.TitleEditor);
                break;
        }
    }

    private async void OnDetailDescriptionCommitted(object sender, RoutedEventArgs args) =>
        await Shell.Detail.EndEditingAsync(TaskDetailViewModel.DescriptionEditor);

    // ── The description, drawn (task 11cfaf6d) ───────────────────────────────────────────────

    /// <summary>
    /// True for the tap that followed a link, so the tap does not also open the editor.
    /// </summary>
    /// <remarks>
    /// A hyperlink's Click and the panel's Tapped both fire for one click on a link, and the
    /// first cannot mark the second handled. Following the link is the whole of what that click
    /// meant.
    /// </remarks>
    private bool _descriptionLinkFollowed;

    /// <summary>Draw the description's blocks into the panel, replacing what was there.</summary>
    private void RenderDescription()
    {
        DetailDescriptionBlocks.Children.Clear();
        var renderer = new MarkdownRenderer(
            ThemedBrush,
            reference => _ = FollowReferenceAsync(reference),
            link => _ = FollowLinkAsync(link),
            ActualTheme == ElementTheme.Dark);
        foreach (var element in renderer.Render(Shell.Detail.DescriptionBlocks))
        {
            DetailDescriptionBlocks.Children.Add(element);
        }
    }

    /// <summary>A themed brush by key, from the dictionary the current theme resolves.</summary>
    private static Brush ThemedBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var brush) && brush is Brush themed
            ? themed
            : new SolidColorBrush(Colors.Gray);

    /// <summary>The rendered description was clicked: open the editor where it was.</summary>
    private async void OnDescriptionTapped(object sender, TappedRoutedEventArgs args)
    {
        if (_descriptionLinkFollowed)
        {
            _descriptionLinkFollowed = false;
            return;
        }
        await Shell.Detail.BeginEditingAsync(TaskDetailViewModel.DescriptionEditor);
        // The box was collapsed a moment ago; it takes focus once layout has shown it.
        DispatcherQueue.TryEnqueue(() => DetailDescriptionBox.Focus(FocusState.Programmatic));
    }

    /// <summary>The web's hover: a border appears to say the text can be clicked.</summary>
    private void OnDescriptionPointerEntered(object sender, PointerRoutedEventArgs args) =>
        DetailDescriptionRendered.BorderBrush = ThemedBrush("AstridBorder");

    private void OnDescriptionPointerExited(object sender, PointerRoutedEventArgs args) =>
        DetailDescriptionRendered.BorderBrush = new SolidColorBrush(Colors.Transparent);

    /// <summary>A pill was clicked: a task opens here, a list opens here, a person opens on the web.</summary>
    private async Task FollowReferenceAsync(MarkdownInline reference)
    {
        _descriptionLinkFollowed = true;
        switch (reference.Reference)
        {
            case "task":
                if (Shell.IsBoardView && Shell.Board.Has(reference.Id))
                {
                    await Shell.ToggleCardAsync(reference.Id);
                }
                else
                {
                    await Shell.OpenTaskAsync(reference.Id);
                }
                SyncPriority();
                break;
            case "list":
                await Shell.OpenListAsync(reference.Id, reference.Label);
                break;
            default:
                await FollowLinkAsync($"https://astrid.cc/u/{Uri.EscapeDataString(reference.Id)}");
                break;
        }
    }

    /// <summary>A link was clicked. The core kept only addresses a browser may open.</summary>
    private async Task FollowLinkAsync(string link)
    {
        _descriptionLinkFollowed = true;
        if (Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private async void OnDetailPriorityPicked(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: string tag }
            || !int.TryParse(tag, out var priority)
            || !Shell.Detail.IsOpen
            || priority == Shell.Detail.Priority)
        {
            return;
        }
        await Shell.Detail.SetPriorityAsync(priority);
        SyncPriority();
    }

    private async void OnSubtaskKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }
        args.Handled = true;
        if (await Shell.Detail.AddSubtaskAsync(SubtaskBox.Text))
        {
            SubtaskBox.Text = string.Empty;
        }
    }

    private async void OnCommentKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.V && IsControlDown())
        {
            // Handled only when the core says there is something to attach: a text paste has to
            // stay a text paste, which is the trade this whole path is careful about.
            if (await PasteIntoCommentAsync())
            {
                args.Handled = true;
            }
            return;
        }
        if (sender is TextBox box && await HandleSuggestionKeyAsync(box, args))
        {
            return;
        }
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }
        args.Handled = true;
        await SendCommentAsync();
    }

    // ── @person, #list, !task (task 3271a0c5) ───────────────────────────────────────────────
    //
    // One transient flyout serves the comment box and every reply box: it is shown at whichever
    // box is being typed in and never takes the focus, so typing carries on underneath it. The
    // rules — when it opens, what it offers, what a choice writes — are the core's.

    private Flyout? _suggestionFlyout;

    private ListView? _suggestionList;

    private TextBox? _suggestionBox;

    private async void OnCommentTextChanged(object sender, TextChangedEventArgs args)
    {
        if (sender is not TextBox box)
        {
            return;
        }
        var offered = await Shell.Detail.SuggestCommentAsync(box.Text, box.SelectionStart);
        if (offered)
        {
            ShowSuggestions(box);
        }
        else
        {
            HideSuggestions();
        }
    }

    private void ShowSuggestions(TextBox box)
    {
        if (_suggestionFlyout is null)
        {
            _suggestionList = new ListView
            {
                ItemsSource = Shell.Detail.CommentSuggestions,
                ItemTemplate = (DataTemplate)Resources["SuggestionTemplate"],
                SelectionMode = ListViewSelectionMode.Single,
                IsItemClickEnabled = true,
                MinWidth = 220,
                MaxHeight = 260,
            };
            _suggestionList.ItemClick += async (_, clicked) =>
            {
                if (clicked.ClickedItem is Suggestion chosen && _suggestionBox is { } target)
                {
                    await ApplySuggestionAsync(target, chosen);
                }
            };
            _suggestionFlyout = new Flyout
            {
                Content = _suggestionList,
                ShowMode = FlyoutShowMode.Transient,
                Placement = FlyoutPlacementMode.Top,
                ShouldConstrainToRootBounds = false,
            };
            Shell.Detail.PropertyChanged += (_, changed) =>
            {
                if (changed.PropertyName == nameof(TaskDetailViewModel.SuggestionIndex)
                    && _suggestionList is not null)
                {
                    _suggestionList.SelectedIndex = Shell.Detail.SuggestionIndex;
                }
            };
        }
        _suggestionBox = box;
        _suggestionList!.SelectedIndex = Shell.Detail.SuggestionIndex;
        if (!_suggestionFlyout.IsOpen || !ReferenceEquals(_suggestionFlyout.Target, box))
        {
            _suggestionFlyout.ShowAt(box, new FlyoutShowOptions { ShowMode = FlyoutShowMode.Transient });
        }
    }

    private void HideSuggestions()
    {
        if (_suggestionFlyout is { IsOpen: true })
        {
            _suggestionFlyout.Hide();
        }
    }

    /// <summary>The keys the popup takes while it is open: Up, Down, Return, Tab, Escape.</summary>
    private async Task<bool> HandleSuggestionKeyAsync(TextBox box, KeyRoutedEventArgs args)
    {
        if (!Shell.Detail.HasCommentSuggestions)
        {
            return false;
        }
        switch (args.Key)
        {
            case VirtualKey.Down:
                Shell.Detail.MoveSuggestion(1);
                args.Handled = true;
                return true;
            case VirtualKey.Up:
                Shell.Detail.MoveSuggestion(-1);
                args.Handled = true;
                return true;
            case VirtualKey.Enter:
            case VirtualKey.Tab:
                args.Handled = true;
                await ApplySuggestionAsync(box, null);
                return true;
            case VirtualKey.Escape:
                args.Handled = true;
                Shell.Detail.ClearSuggestions();
                HideSuggestions();
                return true;
            default:
                return false;
        }
    }

    private async Task ApplySuggestionAsync(TextBox box, Suggestion? chosen)
    {
        var applied = await Shell.Detail.ApplyCommentSuggestionAsync(chosen, box.Text, box.SelectionStart);
        HideSuggestions();
        if (applied is null)
        {
            return;
        }
        box.Text = applied.Text;
        box.SelectionStart = Math.Min(applied.Caret, box.Text.Length);
        box.Focus(FocusState.Programmatic);
    }

    private static bool IsControlDown() =>
        Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>
    /// Ctrl+V with a file or a picture on the clipboard attaches it to the open task.
    /// </summary>
    /// <remarks>
    /// The clipboard is read here and interpreted in the core: which of the things on the board
    /// was meant, and what to call a screenshot that has no name, are rules with tests rather than
    /// a guess made in a key handler. Everything downstream is the path the Attach button uses.
    /// </remarks>
    /// <returns>Whether this paste was taken as an attachment rather than as text.</returns>
    private async Task<bool> PasteIntoCommentAsync()
    {
        DataPackageView board;
        try
        {
            board = Clipboard.GetContent();
        }
        catch (Exception error)
        {
            // Another process can hold the clipboard open. A paste that cannot read it is a paste
            // that types, which is the safe half of the trade.
            App.Log($"could not read the clipboard: {error.Message}");
            return false;
        }

        var paths = new List<string>();
        if (board.Contains(StandardDataFormats.StorageItems))
        {
            foreach (var item in await board.GetStorageItemsAsync())
            {
                if (item is Windows.Storage.StorageFile file)
                {
                    paths.Add(file.Path);
                }
            }
        }
        // Windows hands a pasted screenshot over as a bitmap with no format named. PNG is what
        // every modern source puts there and what we write it back out as.
        var hasImage = board.Contains(StandardDataFormats.Bitmap);
        var hasText = board.Contains(StandardDataFormats.Text);

        var decided = await Shell.Detail.DecidePasteAsync(paths, hasImage, hasText);
        return decided.Action switch
        {
            "files" => await Shell.Detail.AttachPastedAsync(decided.Files) > 0,
            "image" => decided.Name is not null
                && await AttachPastedImageAsync(board, decided.Name),
            // "text", or a shape a newer core answers with. Either way it types.
            _ => false,
        };
    }

    /// <summary>Write the clipboard's picture to a file under the name the core chose.</summary>
    /// <remarks>
    /// To a temporary file rather than across the boundary as bytes: the attachment path takes a
    /// path, and it copies what it is given into its own pending directory, so this file is a
    /// hand-off rather than something to keep.
    /// </remarks>
    private async Task<bool> AttachPastedImageAsync(DataPackageView board, string name)
    {
        try
        {
            var reference = await board.GetBitmapAsync();
            using var source = await reference.OpenReadAsync();
            var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(
                System.IO.Path.GetTempPath());
            var file = await folder.CreateFileAsync(
                name, Windows.Storage.CreationCollisionOption.ReplaceExisting);
            using (var destination = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
            {
                await Windows.Storage.Streams.RandomAccessStream.CopyAsync(source, destination);
            }
            var attached = await Shell.Detail.AttachAsync(file.Path);
            // The attachment path has taken its own copy by now, so this one has done its job.
            try
            {
                await file.DeleteAsync();
            }
            catch (Exception)
            {
                // A temporary file left behind is untidy, not a failure worth reporting.
            }
            return attached;
        }
        catch (Exception error)
        {
            App.Log($"could not attach a pasted image: {error.Message}");
            return false;
        }
    }

    private async void OnSendComment(object sender, RoutedEventArgs args) =>
        await SendCommentAsync();

    // ── Reply, edit, delete (task 97c817dd) ─────────────────────────────────────────────────
    //
    // The boxes live inside the row template, so a button reaches its box through its Tag —
    // an ElementName binding inside the template — rather than through a page-level name.

    private void OnReplyToComment(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string commentId)
        {
            Shell.Detail.BeginReply(commentId);
        }
    }

    /// <summary>The box beside a button, found by walking its row: a template cannot name it.</summary>
    private static TextBox? BoxBeside(object sender)
    {
        var element = sender as DependencyObject;
        while (element is not null)
        {
            if (element is Panel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is TextBox box)
                    {
                        return box;
                    }
                }
            }
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private async void OnReplySent(object sender, RoutedEventArgs args)
    {
        if (BoxBeside(sender) is { } box)
        {
            await Shell.Detail.SendReplyAsync(box.Text);
        }
    }

    private async void OnReplyKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (sender is TextBox typing && await HandleSuggestionKeyAsync(typing, args))
        {
            return;
        }
        if (args.Key == VirtualKey.Enter && sender is TextBox box)
        {
            args.Handled = true;
            await Shell.Detail.SendReplyAsync(box.Text);
        }
        else if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            Shell.Detail.CancelReply();
        }
    }

    private void OnReplyCancelled(object sender, RoutedEventArgs args) => Shell.Detail.CancelReply();

    private void OnEditComment(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string commentId)
        {
            Shell.Detail.BeginEdit(commentId);
        }
    }

    private async void OnCommentEditSaved(object sender, RoutedEventArgs args)
    {
        if (BoxBeside(sender) is { } box)
        {
            await Shell.Detail.SaveEditAsync(box.Text);
        }
    }

    /// <summary>Return saves; Shift+Return is a new line, as in the web's editor; Escape cancels.</summary>
    private async void OnCommentEditKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape)
        {
            args.Handled = true;
            Shell.Detail.CancelEdit();
            return;
        }
        if (args.Key != VirtualKey.Enter || sender is not TextBox box)
        {
            return;
        }
        var shift = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (shift)
        {
            return;
        }
        args.Handled = true;
        await Shell.Detail.SaveEditAsync(box.Text);
    }

    private void OnCommentEditCancelled(object sender, RoutedEventArgs args) => Shell.Detail.CancelEdit();

    /// <summary>Delete asks first: there is no undo for it on any client.</summary>
    private void OnDeleteCommentAsked(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: string commentId } anchor)
        {
            return;
        }
        var confirm = new Button
        {
            Content = Strings.Get("comments.delete_yes"),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(confirm, "Confirm delete comment");
        var flyout = new Flyout
        {
            Content = new StackPanel
            {
                Spacing = 8,
                MaxWidth = 240,
                Children =
                {
                    new TextBlock { TextWrapping = TextWrapping.Wrap, Text = Strings.Get("comments.delete_confirm") },
                    confirm,
                },
            },
        };
        confirm.Click += async (_, _) =>
        {
            flyout.Hide();
            await Shell.Detail.DeleteCommentAsync(commentId);
        };
        flyout.ShowAt(anchor);
    }

    /// <summary>Post what is in the comment box. What Return and the Send button both do.</summary>
    private async Task SendCommentAsync()
    {
        if (await Shell.Detail.AddCommentAsync(Shell.Detail.CommentDraft))
        {
            Shell.Detail.CommentDraft = string.Empty;
        }
    }

    /// <summary>Put the priority combo where the open task says it should be.</summary>
    /// <remarks>
    /// A ComboBox has no two-way binding to an index that survives the list being rebuilt, so the
    /// selection is set once when a task is opened. The guard in the changed handler is what stops
    /// this from being read back as an edit.
    /// </remarks>
    /// <summary>
    /// Paint the four priority squares against the value the task actually has.
    /// </summary>
    /// <remarks>
    /// astrid-web fills the chosen square with its colour and outlines the other three in theirs,
    /// so the field reads at a glance without being opened. Done here rather than as four bindings
    /// because "which of the four is on" is one comparison, and four copies of it drift.
    /// </remarks>
    internal void SyncPriority()
    {
        var chosen = Shell.Detail.Priority;
        Paint(PriorityNone, 0);
        Paint(PriorityLow, 1);
        Paint(PriorityMedium, 2);
        Paint(PriorityHigh, 3);

        // Which square is lit and in what is a rule (PrioritySwatch, tested); this only paints.
        void Paint(Button button, int level)
        {
            var (background, foreground, border) = PrioritySwatch.For(level, chosen);
            button.BorderBrush = SwatchBrush(border);
            button.Background = SwatchBrush(background);
            button.Foreground = SwatchBrush(foreground);
        }

        static Brush SwatchBrush(string hex) => hex == PrioritySwatch.Transparent
            ? new SolidColorBrush(Colors.Transparent)
            : new SolidColorBrush(Colours.Parse(hex) ?? Colors.Gray);
    }
}
