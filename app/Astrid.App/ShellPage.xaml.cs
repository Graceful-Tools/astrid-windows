using System.Diagnostics;
using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Astrid.App;

/// <summary>
/// The app's one page: a sidebar, a list, and the events that connect them to the view model.
/// </summary>
/// <remarks>
/// <para>
/// Every handler here does two things: read what the user did, and call a view model. Nothing in
/// this file decides anything — no filtering, no ordering, no rule about what completing a task
/// means. That is rule 9 of <c>docs/ASTRID.md</c> §0, and a window is where it is easiest to break
/// by accident, because "just this once, in the click handler" is always the shortest path.
/// </para>
/// <para>
/// <b>Change notifications arrive on a Rust pool thread.</b> The view model is handed a post
/// function that marshals to the dispatcher queue; without it, the first live update from a
/// colleague crashes the window.
/// </para>
/// </remarks>
public sealed partial class ShellPage : UserControl
{
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private readonly ShortcutDispatcher _shortcuts;
    private readonly Reminders _reminders;
    private GlobalHotkey? _hotkey;
    /// <summary>
    /// True while the account flyout is being filled in.
    /// </summary>
    /// <remarks>
    /// A toggle raises its change event while being set from what was just loaded, and writing then
    /// saves the value back on every open — a settings screen that keeps sending the server what it
    /// already said.
    /// </remarks>
    private bool _settingsLoading;

    public ShellPage()
    {
        InitializeComponent();
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // With no core there is nothing to bind to. The window still opens — and says why — rather
        // than the process exiting with nothing on screen.
        IAstridCore core = App.Core is { } running
            ? running
            : new UnavailableCore(App.StartupError);
        Shell = new ShellViewModel(core, Post);
        _reminders = new Reminders(Shell, Post);
        _shortcuts = new ShortcutDispatcher(core, Shell);
        _shortcuts.ShellActionRequested += OnShellAction;
        Loaded += OnLoaded;
        // Every protocol activation, launch or redirected, arrives here. The core decides which
        // are sign-in callbacks; a deep link to a task uses the same scheme.
        App.UriActivated += OnUriActivated;
        Unloaded += (_, _) =>
        {
            App.UriActivated -= OnUriActivated;
            _reminders.Stop();
            _hotkey?.Dispose();
            Shell.Dispose();
        };
    }

    /// <summary>What the window binds to.</summary>
    public ShellViewModel Shell { get; }

    /// <summary>
    /// Bring the window forward with the quick-add box ready.
    /// </summary>
    /// <remarks>
    /// What the global hotkey is for: the thought arrives while you are in something else, and the
    /// two seconds it takes to find a window are the two seconds in which it is forgotten.
    /// </remarks>
    private void QuickAdd()
    {
        Post(() =>
        {
            App.BringToFront();
            QuickAddBox.Focus(FocusState.Programmatic);
            return Task.CompletedTask;
        });
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        // Banners before the first load: a reminder that came due while the app was closed should
        // arrive as the window opens, not a half-minute later when the loop first ticks.
        _reminders.Start();
        _hotkey = new GlobalHotkey(QuickAdd);
        _hotkey.Start();
        await Shell.StartAsync();
        await Shell.RaiseRemindersAsync();
        SyncSelectionFromViewModel();
    }

    private async void OnListSelected(object sender, SelectionChangedEventArgs args)
    {
        if (sender is not ListView view || view.SelectedItem is not ListSummary selected)
        {
            return;
        }

        // Two list views, one selection. Clearing the other keeps the highlight where the user
        // clicked instead of leaving two rows looking selected.
        if (ReferenceEquals(view, FavoritesList))
        {
            ListsList.SelectedItem = null;
        }
        else
        {
            FavoritesList.SelectedItem = null;
        }

        Shell.Sidebar.Selected = selected;
        await Shell.OpenSelectedAsync();
    }

    private async void OnQuickAdd(object sender, RoutedEventArgs args) => await AddTypedTask();

    private async void OnQuickAddKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }
        args.Handled = true;
        await AddTypedTask();
    }

    private async Task AddTypedTask()
    {
        var title = QuickAddBox.Text;
        if (await Shell.Tasks.CreateTaskAsync(title))
        {
            // Cleared only on success, so a task that could not be created is not also a task the
            // user has to retype.
            QuickAddBox.Text = string.Empty;
        }
        QuickAddBox.Focus(FocusState.Programmatic);
    }

    private async void OnRowChecked(object sender, RoutedEventArgs args) =>
        await SetCompleted(sender, completed: true);

    private async void OnRowUnchecked(object sender, RoutedEventArgs args) =>
        await SetCompleted(sender, completed: false);

    private async Task SetCompleted(object sender, bool completed)
    {
        if (sender is FrameworkElement { Tag: string taskId })
        {
            // Always the complete command. A repeating task rolls forward rather than finishing,
            // and only that path does it — see astrid_core::services::task.
            await Shell.Tasks.SetCompletedAsync(taskId, completed);
        }
    }

    private async void OnDeleteRow(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: string taskId })
        {
            await Shell.Tasks.DeleteTaskAsync(taskId);
        }
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

    private async void OnSync(object sender, RoutedEventArgs args) => await Shell.SyncAsync();

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

    /// <summary>
    /// Open the browser to sign in.
    /// </summary>
    /// <remarks>
    /// The URL comes from the core, which minted the PKCE verifier and the state that go with it.
    /// The shell only opens what it is given — building the URL here would be a second place for
    /// the hand-off's rules to live.
    /// </remarks>
    private async void OnSignIn(object sender, RoutedEventArgs args)
    {
        var url = await Shell.SignIn.BeginAsync();
        if (url is null)
        {
            return;
        }
        await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }

    private async void OnCancelSignIn(object sender, RoutedEventArgs args) =>
        await Shell.SignIn.CancelAsync();

    private async void OnSignOut(object sender, RoutedEventArgs args) => await Shell.SignOutAsync();

    /// <summary>
    /// Windows activated the app with a URL — the browser coming back, most likely.
    /// </summary>
    /// <remarks>
    /// The activation arrives on the UI thread for a redirect and during startup for a cold
    /// launch, so it is posted rather than awaited directly: handling it inline during
    /// <c>OnLaunched</c> would run the sign-in exchange before the window had finished its first
    /// layout.
    /// </remarks>
    private void OnUriActivated(Uri uri) =>
        Post(() => Shell.HandleActivationAsync(uri.ToString()));

    /// <summary>
    /// The keyboard.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two schemes meet here, and they do not overlap. The <b>bare keys</b> are a cross-platform
    /// contract — locked by <c>contracts/fixtures/shortcuts.json</c>, resolved by
    /// <c>astrid_core::keyboard</c> — so muscle memory transfers between web, Mac and Windows
    /// unchanged, and this window asks what a key means rather than knowing. The <b>Ctrl chords</b>
    /// are what a Windows user expects from any app; they are additive, local, and deliberately
    /// not in the shared table, because a bare key from it must never be shadowed by an
    /// accelerator.
    /// </para>
    /// <para>
    /// A bare key is only offered to the core when nothing is being typed into. That guard is in
    /// the core too — it is part of the contract — but the answer to "is a text field focused?" is
    /// something only the window can see.
    /// </para>
    /// </remarks>
    private async void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var control = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (control)
        {
            switch (args.Key)
            {
                case VirtualKey.N:
                    args.Handled = true;
                    QuickAddBox.Focus(FocusState.Programmatic);
                    return;
                case VirtualKey.R:
                    args.Handled = true;
                    await Shell.SyncAsync();
                    return;
                default:
                    // Any other chord belongs to Windows or to a control. Not ours to swallow.
                    return;
            }
        }

        var key = KeyNames.For(args.Key);
        if (key is null)
        {
            return;
        }

        args.Handled = await _shortcuts.HandleAsync(key, IsTypingSomewhere(), isModalPresented: false);
    }

    /// <summary>Whether the focus is inside something that takes text.</summary>
    /// <remarks>
    /// The one input to the shared guard that only the window can answer. Getting it wrong in
    /// either direction is bad in a specific way: too eager and typing "n" in a task title creates
    /// a new task instead; too shy and the shortcuts never work at all.
    /// </remarks>
    private bool IsTypingSomewhere()
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        return focused is TextBox or RichEditBox or AutoSuggestBox or PasswordBox;
    }

    /// <summary>An action the window has to perform rather than a view model.</summary>
    private void OnShellAction(string action)
    {
        switch (action)
        {
            case "newTask":
                QuickAddBox.Focus(FocusState.Programmatic);
                break;
            // The rest — editing a title in place, the description, a comment, the detail panel —
            // arrive with the task detail view. Until then the key is swallowed rather than left
            // to fall through to the list, where it would do something else entirely.
            default:
                break;
        }
    }

    // ── The task detail ──────────────────────────────────────────────────────────────────────
    //
    // Edits save as they are made rather than on a Save button. Every write goes to the Outbox, so
    // "saved" and "sent" are already different things, and a Save button would be claiming to do
    // the second when it does the first.

    /// <summary>
    /// Selecting a task opens it.
    /// </summary>
    /// <remarks>
    /// Selection and opening are one thing on web — a click sets <c>selectedTaskId</c>, and that
    /// is what the pane draws from, so moving with j and k opens each task in turn. This was a
    /// double-tap here, which made the pane the only screen a keyboard could not reach and asked
    /// for a gesture no other client asks for.
    /// </remarks>
    private async void OnRowSelected(object sender, SelectionChangedEventArgs args)
    {
        if (Shell.Tasks.Selected is { } row)
        {
            await Shell.OpenTaskAsync(row.Id);
            SyncDetailPriority();
        }
    }

    private void OnCloseDetail(object sender, RoutedEventArgs args) => Shell.Detail.Close();

    /// <summary>
    /// Somebody chose a quick due-date option.
    /// </summary>
    /// <remarks>
    /// The button carries the option's key and the view model holds the instant that goes with it,
    /// so the window never computes a date. Every part of that arithmetic — daylight saving, all-day
    /// storage, what "morning" means where the reader is — is decided in the core for all three
    /// clients.
    /// </remarks>
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

    private async void OnDetailChecked(object sender, RoutedEventArgs args) =>
        await Shell.Detail.SetCompletedAsync(true);

    private async void OnDetailUnchecked(object sender, RoutedEventArgs args) =>
        await Shell.Detail.SetCompletedAsync(false);

    /// <summary>
    /// Save the title when the box loses focus.
    /// </summary>
    /// <remarks>
    /// On focus loss rather than on every keystroke: a command per character would put a hundred
    /// entries in the Outbox for one rename, and every one of them a separate request when the
    /// network came back.
    /// </remarks>
    private async void OnDetailTitleCommitted(object sender, RoutedEventArgs args) =>
        await Shell.Detail.SaveTitleAsync(DetailTitleBox.Text);

    private async void OnToggleTimer(object sender, RoutedEventArgs args)
    {
        await Shell.Detail.SetTimingAsync(!Shell.Detail.IsTiming);
    }

    // ── The account ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Load the account as its flyout opens, and put the times in the pickers.
    /// </summary>
    /// <remarks>
    /// The time pickers are set here rather than bound, because a two-way binding on a control that
    /// raises its change event while being populated writes the value straight back — which is a
    /// settings screen that saves what it just read, on every open.
    /// </remarks>
    private async void OnAccountOpening(object sender, object args)
    {
        _settingsLoading = true;
        try
        {
            await Shell.LoadSettingsAsync();
            var reminders = Shell.Settings.Reminders;
            DigestTimeBox.SelectedTime = ParseTime(reminders.DailyDigestTime);
            QuietStartBox.SelectedTime = ParseTime(reminders.QuietHoursStart);
            QuietEndBox.SelectedTime = ParseTime(reminders.QuietHoursEnd);
            DefaultOffsetBox.SelectedIndex = IndexOfOffset(reminders.DefaultReminderTime);
        }
        finally
        {
            _settingsLoading = false;
        }
    }

    private async void OnPushToggled(object sender, RoutedEventArgs args)
    {
        if (!_settingsLoading && PushToggle.IsOn != Shell.Settings.PushEnabled)
        {
            await Shell.Settings.SetAsync("enablePushReminders", PushToggle.IsOn);
        }
    }

    private async void OnEmailToggled(object sender, RoutedEventArgs args)
    {
        if (!_settingsLoading && EmailToggle.IsOn != Shell.Settings.EmailEnabled)
        {
            await Shell.Settings.SetAsync("enableEmailReminders", EmailToggle.IsOn);
        }
    }

    private async void OnDigestToggled(object sender, RoutedEventArgs args)
    {
        if (!_settingsLoading && DigestToggle.IsOn != Shell.Settings.DigestEnabled)
        {
            await Shell.Settings.SetAsync("enableDailyDigest", DigestToggle.IsOn);
        }
    }

    private async void OnDigestTimeChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
    {
        if (!_settingsLoading && args.NewTime is { } time)
        {
            await Shell.Settings.SetAsync("dailyDigestTime", Clock(time));
        }
    }

    private async void OnQuietHoursToggled(object sender, RoutedEventArgs args)
    {
        if (_settingsLoading)
        {
            return;
        }
        if (!QuietToggle.IsOn)
        {
            // Both ends cleared: the server reads their absence as "no quiet hours", and a window
            // with only one end is something nothing can act on.
            await Shell.Settings.SetQuietHoursAsync(null, null);
            return;
        }
        await Shell.Settings.SetQuietHoursAsync(
            Clock(QuietStartBox.SelectedTime ?? new TimeSpan(22, 0, 0)),
            Clock(QuietEndBox.SelectedTime ?? new TimeSpan(8, 0, 0)));
    }

    private async void OnQuietHoursChanged(TimePicker sender, TimePickerSelectedValueChangedEventArgs args)
    {
        if (_settingsLoading || !QuietToggle.IsOn)
        {
            return;
        }
        await Shell.Settings.SetQuietHoursAsync(
            Clock(QuietStartBox.SelectedTime ?? new TimeSpan(22, 0, 0)),
            Clock(QuietEndBox.SelectedTime ?? new TimeSpan(8, 0, 0)));
    }

    private async void OnDefaultOffsetChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_settingsLoading || DefaultOffsetBox.SelectedItem is not ReminderOffset offset)
        {
            return;
        }
        await Shell.Settings.SetAsync("defaultReminderTime", offset.Minutes);
    }

    /// <summary>An <c>HH:MM</c> string, which is what the server stores.</summary>
    private static string Clock(TimeSpan time) => $"{time.Hours:D2}:{time.Minutes:D2}";

    private static TimeSpan? ParseTime(string? text) =>
        TimeSpan.TryParse(text, out var parsed) ? parsed : null;

    private int IndexOfOffset(int minutes)
    {
        for (var index = 0; index < Shell.Settings.Offsets.Count; index++)
        {
            if (Shell.Settings.Offsets[index].Minutes == minutes)
            {
                return index;
            }
        }
        return -1;
    }

    // ── Attachments ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Open an attachment, fetching it first if this machine does not have it.
    /// </summary>
    /// <remarks>
    /// Opened with whatever program reads that kind of file, which is the point of downloading it
    /// to a path rather than carrying the bytes across the boundary.
    /// </remarks>
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

    private async void OnChatKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }
        args.Handled = true;
        if (await Shell.Chat.SendAsync(ChatBox.Text))
        {
            ChatBox.Text = string.Empty;
        }
    }

    // ── The list's settings and members ──────────────────────────────────────────────────────

    private async void OnListSettingsOpening(object sender, object args)
    {
        await Shell.LoadListSettingsAsync();
    }

    private async void OnListRenamed(object sender, RoutedEventArgs args)
    {
        await Shell.ListSettings.RenameAsync(ListNameBox.Text);
        await Shell.Sidebar.LoadAsync();
    }

    private async void OnInvite(object sender, RoutedEventArgs args)
    {
        if (await Shell.ListSettings.InviteAsync(InviteBox.Text))
        {
            InviteBox.Text = string.Empty;
        }
    }

    private async void OnInviteKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }
        args.Handled = true;
        if (await Shell.ListSettings.InviteAsync(InviteBox.Text))
        {
            InviteBox.Text = string.Empty;
        }
    }

    private async void OnRemoveMember(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string userId)
        {
            await Shell.ListSettings.RemoveAsync(userId);
        }
    }

    private async void OnLeaveList(object sender, RoutedEventArgs args)
    {
        await Shell.LeaveListAsync();
    }

    private async void OnDeleteList(object sender, RoutedEventArgs args)
    {
        await Shell.DeleteListAsync();
    }

    // ── The board ────────────────────────────────────────────────────────────────────────────

    private async void OnToggleBoard(object sender, RoutedEventArgs args)
    {
        await Shell.ShowBoardAsync(BoardToggle.IsChecked == true);
    }

    private async void OnCardOpened(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string taskId)
        {
            await Shell.OpenTaskAsync(taskId);
            SyncDetailPriority();
        }
    }

    /// <summary>
    /// Moving a card. A menu rather than a drag, for now.
    /// </summary>
    /// <remarks>
    /// A drag is the gesture people expect from a board and it will come; a card that can ONLY be
    /// dragged is a card a keyboard cannot move at all, so the menu is the one that has to exist.
    /// The columns come from the board rather than from a list typed here — a board can be renamed
    /// and can have columns of its own.
    /// </remarks>
    private void OnCardRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (sender is not FrameworkElement card || card.Tag is not string taskId)
        {
            return;
        }
        var menu = new MenuFlyout();
        foreach (var column in Shell.Board.Columns)
        {
            var item = new MenuFlyoutItem { Text = column.Name, Tag = column.Id };
            item.Click += async (_, _) => await Shell.Board.MoveAsync(taskId, column.Id);
            menu.Items.Add(item);
        }
        menu.ShowAt(card, args.GetPosition(card));
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
        await Shell.Detail.LoadAssigneesAsync();
    }

    private async void OnAssigneeChosen(object sender, RoutedEventArgs args)
    {
        // A null tag is the unassigned row, and clearing is a real choice rather than a no-op.
        var userId = (sender as FrameworkElement)?.Tag as string;
        await Shell.Detail.AssignAsync(userId);
    }

    private async void OnDetailTitleKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }
        args.Handled = true;
        await Shell.Detail.SaveTitleAsync(DetailTitleBox.Text);
    }

    private async void OnDetailDescriptionCommitted(object sender, RoutedEventArgs args) =>
        await Shell.Detail.SaveDescriptionAsync(DetailDescriptionBox.Text);

    private async void OnDetailPriorityChanged(object sender, SelectionChangedEventArgs args)
    {
        // The combo raises this while the pane is being filled in as well as when somebody picks
        // something, and saving then would write the value back that was just read.
        if (!Shell.Detail.IsOpen || DetailPriority.SelectedIndex < 0
            || DetailPriority.SelectedIndex == Shell.Detail.Priority)
        {
            return;
        }
        await Shell.Detail.SetPriorityAsync(DetailPriority.SelectedIndex);
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
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }
        args.Handled = true;
        if (await Shell.Detail.AddCommentAsync(CommentBox.Text))
        {
            CommentBox.Text = string.Empty;
        }
    }

    /// <summary>Put the priority combo where the open task says it should be.</summary>
    /// <remarks>
    /// A ComboBox has no two-way binding to an index that survives the list being rebuilt, so the
    /// selection is set once when a task is opened. The guard in the changed handler is what stops
    /// this from being read back as an edit.
    /// </remarks>
    private void SyncDetailPriority() => DetailPriority.SelectedIndex = Shell.Detail.Priority;

    /// <summary>Put the highlight where the view model says the selection is.</summary>
    private void SyncSelectionFromViewModel()
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

    /// <summary>Run work on the UI thread.</summary>
    private void Post(Func<Task> work) =>
        _dispatcher.TryEnqueue(() => _ = work());
}

/// <summary>
/// Stands in when the core would not start, so the window has something to bind to.
/// </summary>
/// <remarks>
/// Every command fails with the reason the core gave, which puts the real message in front of the
/// user instead of an empty list they cannot explain.
/// </remarks>
internal sealed class UnavailableCore(string? reason) : IAstridCore
{
    private readonly string _reason = reason ?? "the core did not start";

#pragma warning disable CS0067 // Nothing ever changes: there is no core to hear from.
    public event Action<ChangeNotification>? Changed;
#pragma warning restore CS0067

    public Task<AstridResponse> CallAsync(object command, CancellationToken cancellationToken = default) =>
        Task.FromResult(AstridResponse.Parse(
            $"{{\"ok\":false,\"error\":{{\"kind\":\"cache\",\"message\":{System.Text.Json.JsonSerializer.Serialize(_reason)}}}}}"));
}
