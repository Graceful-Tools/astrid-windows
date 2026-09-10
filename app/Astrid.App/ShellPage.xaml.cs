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
        Shell.PaletteCommandRequested += OnPaletteCommand;
        _shortcuts.ShellActionRequested += OnShellAction;
        Loaded += OnLoaded;
        // Where a pill in a comment goes when clicked: the same places a pill in the description
        // goes (task 3271a0c5).
        WontDoChip.Text = Strings.Get("detail.wont_do_chip");
        WordAccountSection();
        WordTasksSection();
        WordContactsSection();
        // The squares follow the open task wherever its priority came from — a tap, a sync, a
        // task opened from the palette or a pill — not only the handlers that remembered to
        // repaint (task 204c9d98).
        Shell.Detail.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName is nameof(TaskDetailViewModel.Priority)
                or nameof(TaskDetailViewModel.IsOpen))
            {
                SyncDetailPriority();
            }
        };
        MarkdownView.ReferenceFollowed = reference => _ = FollowReferenceAsync(reference);
        MarkdownView.LinkFollowed = link => _ = FollowLinkAsync(link);

        // The pane's place follows the open task and the expanded card (task 91a25b8a).
        Shell.Detail.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(TaskDetailViewModel.IsOpen))
            {
                PlaceDetailPane();
            }
            // The description is redrawn from its blocks whenever they change (task 11cfaf6d).
            if (changed.PropertyName == nameof(TaskDetailViewModel.DescriptionBlocks))
            {
                RenderDescription();
            }
        };
        Shell.Board.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(BoardViewModel.ExpandedTaskId))
            {
                PlaceDetailPane();
            }
        };
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
        // The look before the first paint, so the window does not flash the wrong one on the way
        // in. It comes from the cache, so this does not wait for a network.
        Shell.Settings.ThemeChanged += ApplyTheme;
        await Shell.Settings.LoadThemeAsync();
        await Shell.StartAsync();
        await Shell.RaiseRemindersAsync();
        await Shell.MaybeShowTourAsync();
        SyncSelectionFromViewModel();

        // The arrow has to follow the row, and a row moves when the list scrolls as well as when
        // the selection changes. The ScrollViewer is inside the ListView's template, so it does
        // not exist until the template is applied — which is why this is here and not in the
        // constructor.
        if (ScrollViewerInside(TaskRows) is { } scroller)
        {
            scroller.ViewChanged += (_, _) => PointArrowAtSelectedRow();
        }
        SizeChanged += (_, _) => PointArrowAtSelectedRow();
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
            QuickAddBox.Focus(FocusState.Programmatic);
            // The notice stays as long as the web's toast does. A second add in the meantime
            // restarts the clock rather than having the first one's clock take the second down.
            var ticket = ++_addedNoticeTicket;
            await Task.Delay(TimeSpan.FromSeconds(2.5));
            if (ticket == _addedNoticeTicket)
            {
                Shell.Tasks.ClearCreatedNotice();
            }
            return;
        }
        QuickAddBox.Focus(FocusState.Programmatic);
    }

    private int _addedNoticeTicket;

    /// <summary>
    /// The mark on a row, tapped.
    /// </summary>
    /// <remarks>
    /// One handler rather than the checked/unchecked pair a CheckBox gives, because the control is
    /// now an image: it draws the state the core reported and asks for the opposite. A repeating
    /// task does not finish here either way — it rolls forward — which is why this always goes
    /// through the complete command rather than writing a flag.
    /// </remarks>
    private async void OnRowMarkClicked(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: string taskId })
        {
            return;
        }
        var row = Shell.Tasks.Rows.FirstOrDefault(candidate => candidate.Id == taskId);
        await Shell.Tasks.SetCompletedAsync(taskId, completed: row is null || !row.Completed);
    }

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

    /// <summary>The Contacts page's words, and the three doors' (task 438494c7).</summary>
    private void WordContactsSection()
    {
        ContactsNavLabel.Text = Strings.Get("contacts.title");
        HelpNavLabel.Text = Strings.Get("help.title");
        PrivacyNavLabel.Text = Strings.Get("privacy.title");
        TermsNavLabel.Text = Strings.Get("terms.title");
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
            await Shell.Settings.ClearContactsAsync();
        };
        flyout.ShowAt(ClearContactsButton);
    }

    /// <summary>The Tasks page's words, and the layout combo's (task c0f3db19).</summary>
    private void WordTasksSection()
    {
        TasksNavLabel.Text = Strings.Get("smart.title");
        EmailToTaskToggle.Header = Strings.Get("smart.email_to_task");
        EmailToTaskHint.Text = Strings.Get("smart.email_to_task_hint");
        EmailToTaskAddress.Text = Strings.Get("smart.email_address");
        DueOffsetBox.Header = Strings.Get("smart.default_due_date");
        DueOffsetHint.Text = Strings.Get("smart.default_due_date_hint");
        DueTimeBox.Header = Strings.Get("smart.default_due_time");
        DueTimeHint.Text = Strings.Get("smart.default_due_time_hint");
        LayoutBox.Header = Strings.Get("smart.layout");
        LayoutHint.Text = Strings.Get("smart.layout_hint");
        AddedPrefix.Text = Strings.Get("quickadd.added");
        SmartParsingToggle.Header = Strings.Get("smart.parsing");
        SmartParsingHint.Text = Strings.Get("smart.parsing_hint");
        SubtasksBox.Header = Strings.Get("smart.subtasks");
        SubtasksHint.Text = Strings.Get("smart.subtasks_hint");
    }

    private async void OnSmartParsingToggled(object sender, RoutedEventArgs args)
    {
        if (_settingsLoading
            || sender is not ToggleSwitch toggle
            || toggle.IsOn == Shell.Settings.SmartParsingEnabled)
        {
            return;
        }
        await Shell.Settings.SetSmartTaskAsync("smartTaskCreationEnabled", toggle.IsOn);
    }

    private async void OnEmailToTaskToggled(object sender, RoutedEventArgs args)
    {
        if (_settingsLoading
            || sender is not ToggleSwitch toggle
            || toggle.IsOn == Shell.Settings.EmailToTaskEnabled)
        {
            return;
        }
        await Shell.Settings.SetSmartTaskAsync("emailToTaskEnabled", toggle.IsOn);
    }

    /// <summary>
    /// One handler for the three combos: each row knows which field it is a value of, so the
    /// control does not have to.
    /// </summary>
    private async void OnSmartTaskChoiceChosen(object sender, SelectionChangedEventArgs args)
    {
        if (_settingsLoading || sender is not ComboBox { SelectedItem: DefaultChoice choice })
        {
            return;
        }
        var current = choice.Field switch
        {
            "defaultTaskDueOffset" => Shell.Settings.SmartTasks.DefaultTaskDueOffset,
            "defaultDueTime" => Shell.Settings.SmartTasks.DefaultDueTime,
            "taskDisplayMode" => Shell.Settings.SmartTasks.TaskDisplayMode,
            "subtaskDisplay" => Shell.Settings.SmartTasks.SubtaskDisplay,
            _ => null,
        };
        if (choice.Value is null || choice.Value == current)
        {
            return;
        }
        await Shell.Settings.SetSmartTaskAsync(choice.Field, choice.Value);
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
                case VirtualKey.K:
                    // The palette. Ctrl+K is what every other app with one uses, so it is the
                    // chord people try first.
                    args.Handled = true;
                    await Shell.ShowPaletteAsync(true);
                    PaletteBox.Text = string.Empty;
                    PaletteBox.Focus(FocusState.Programmatic);
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
    /// <summary>
    /// True while a selection change is the tail of a pointer press.
    /// </summary>
    /// <remarks>
    /// A click raises PointerPressed, then SelectionChanged, then ItemClick. Only the last of
    /// those can tell a tap on the ALREADY selected row from a tap on a new one, because tapping
    /// the selected row raises no selection change at all — so the click path owns the decision
    /// and the selection path stands down for it.
    /// </remarks>
    private bool _selectionFromPointer;

    private void OnRowsPointerPressed(object sender, PointerRoutedEventArgs args) =>
        _selectionFromPointer = true;

    /// <summary>
    /// Selection moved. Opens the task — which is how the pane is reachable from a keyboard.
    /// </summary>
    /// <remarks>
    /// Stands down for the pointer: <see cref="OnRowClicked"/> handles that, and both acting would
    /// open a task and then immediately close it again.
    /// </remarks>
    private async void OnRowSelected(object sender, SelectionChangedEventArgs args)
    {
        if (_selectionFromPointer)
        {
            _selectionFromPointer = false;
            return;
        }
        if (Shell.Tasks.Selected is { } row)
        {
            // The view model tells a moved selection from a refresh re-selecting the same task.
            await Shell.SelectRowAsync(row.Id);
            SyncDetailPriority();
            PointArrowAtSelectedRow();
        }
    }

    /// <summary>The ScrollViewer a ListView's template wraps its items in, if it has one yet.</summary>
    private static ScrollViewer? ScrollViewerInside(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer found)
            {
                return found;
            }
            if (ScrollViewerInside(child) is { } deeper)
            {
                return deeper;
            }
        }
        return null;
    }

    /// <summary>
    /// Point the pane's arrow at the row it is describing.
    /// </summary>
    /// <remarks>
    /// Geometry, so the shell measures it: where a row sits on screen is not something the view
    /// model can know or should be told. astrid-web does the same thing with an `arrowTop` it
    /// recomputes as the list moves (task 830e63b9).
    ///
    /// Hidden rather than parked at the top when there is no row to point at — a row scrolled out
    /// of view, or a task opened from search or a deep link with no row on screen at all. An arrow
    /// aimed at nothing is worse than no arrow.
    /// </remarks>
    private void PointArrowAtSelectedRow()
    {
        if (!Shell.Detail.IsOpen
            || Shell.IsBoardView
            || Shell.Tasks.Selected is not { } selected
            || TaskRows.ContainerFromItem(selected) is not FrameworkElement container)
        {
            DetailArrow.Visibility = Visibility.Collapsed;
            return;
        }

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

    /// <summary>A row was tapped. The view model decides whether that opens or closes.</summary>
    private async void OnRowClicked(object sender, ItemClickEventArgs args)
    {
        _selectionFromPointer = false;
        if (args.ClickedItem is not TaskRow row)
        {
            return;
        }
        await Shell.OpenOrCloseTaskAsync(row.Id);
        SyncDetailPriority();
        PointArrowAtSelectedRow();
    }

    private void OnCloseDetail(object sender, RoutedEventArgs args) => Shell.Detail.Close();

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
        CopyToClipboard(Shell.Detail.Link);

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
        CopyToClipboard(url);
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
        await Shell.Detail.SaveTitleAsync(DetailTitleBox.Text);

    private async void OnToggleTimer(object sender, RoutedEventArgs args)
    {
        await Shell.Detail.SetTimingAsync(!Shell.Detail.IsTiming);
    }

    private async void OnDismissTour(object sender, RoutedEventArgs args)
    {
        await Shell.DismissTourAsync();
    }

    // ── The command palette ──────────────────────────────────────────────────────────────────

    private async void OnPaletteChanged(object sender, TextChangedEventArgs args)
    {
        await Shell.SearchPaletteAsync(PaletteBox.Text);
    }

    /// <summary>
    /// Enter runs the first row; Escape closes.
    /// </summary>
    /// <remarks>
    /// The first row rather than the selected one when nothing is selected, because typing three
    /// letters and pressing Enter is the whole gesture — reaching for the arrow keys first would
    /// make it slower than the sidebar it replaces.
    /// </remarks>
    private async void OnPaletteKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.Escape:
                args.Handled = true;
                await Shell.ShowPaletteAsync(false);
                break;
            case VirtualKey.Enter:
                args.Handled = true;
                var row = PaletteList.SelectedItem as PaletteRow ?? Shell.PaletteRows.FirstOrDefault();
                if (row is not null)
                {
                    await Shell.RunPaletteRowAsync(row);
                    SyncSelectionFromViewModel();
                }
                break;
            case VirtualKey.Down:
                args.Handled = true;
                Step(1);
                break;
            case VirtualKey.Up:
                args.Handled = true;
                Step(-1);
                break;
        }
    }

    /// <summary>Move the highlight without leaving the box, so typing can continue.</summary>
    private void Step(int by)
    {
        if (Shell.PaletteRows.Count == 0)
        {
            return;
        }
        var next = PaletteList.SelectedIndex + by;
        PaletteList.SelectedIndex = Math.Clamp(next, 0, Shell.PaletteRows.Count - 1);
        PaletteList.ScrollIntoView(PaletteList.SelectedItem);
    }

    private async void OnPaletteRowChosen(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is PaletteRow row)
        {
            await Shell.RunPaletteRowAsync(row);
            SyncSelectionFromViewModel();
        }
    }

    /// <summary>
    /// A command chosen in the palette, carried out where the keyboard's are.
    /// </summary>
    /// <remarks>
    /// The palette answers with the action name the shared keyboard table uses, which is the same
    /// name the shortcut dispatcher already handles — so there is one implementation of "new task"
    /// rather than two that drift.
    /// </remarks>
    private async void OnPaletteCommand(string action)
    {
        await _shortcuts.RunAsync(action);
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
            // The Account page is the one that opens, and its passkeys come from the server.
            await Shell.Settings.LoadPasskeysAsync();
            await Shell.Settings.LoadAgentsAsync();
            await Shell.Settings.LoadGoogleSyncModeAsync();
            await Shell.Settings.LoadWebhookAsync();
            var reminders = Shell.Settings.Reminders;
            DigestTimeBox.SelectedTime = ParseTime(reminders.DailyDigestTime);
            QuietStartBox.SelectedTime = ParseTime(reminders.QuietHoursStart);
            QuietEndBox.SelectedTime = ParseTime(reminders.QuietHoursEnd);
            DefaultOffsetBox.SelectedIndex = IndexOfOffset(reminders.DefaultReminderTime);
            // Opens on the first section every time. A panel that reopens three pages deep in
            // whatever was last poked at is a panel you have to navigate out of before you can use.
            SettingsSections.SelectedIndex = 0;
            ShowSettingsSection("Account");
        }
        finally
        {
            _settingsLoading = false;
        }
    }

    /// <summary>
    /// The panel is gone, so the plaintexts it was holding go with it.
    /// </summary>
    /// <remarks>
    /// A minted token lives for as long as the screen showing it and no longer. That is the whole
    /// of its storage policy, and it only holds if something actually forgets.
    /// </remarks>
    private void OnAccountClosed(object sender, object args) =>
        Shell.Settings.ForgetMintedCredentials();

    private async void OnSettingsSectionChosen(object sender, SelectionChangedEventArgs args)
    {
        if (SettingsSections.SelectedItem is not FrameworkElement { Tag: string section })
        {
            return;
        }
        // Three entries are doors, not pages: the web's own Help, Privacy and Terms open in the
        // browser, so there is one copy of each (task 438494c7). The rail stays where it was.
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

        // Loaded when its page is opened rather than when the flyout is: it is a network round
        // trip for a page most opens never reach, and the account flyout already makes four.
        if (section == "ApiAccess")
        {
            await Shell.Settings.LoadApiAccessAsync();
        }
        if (section == "Contacts")
        {
            await Shell.Settings.LoadContactsAsync();
        }
    }

    // ── API access ───────────────────────────────────────────────────────────────────────────

    private async void OnCreateMcpToken(object sender, RoutedEventArgs args) =>
        await Shell.Settings.CreateMcpTokenAsync();

    private async void OnRevokeMcpTokens(object sender, RoutedEventArgs args) =>
        await Shell.Settings.RevokeMcpTokensAsync();

    private async void OnCreateOAuthClient(object sender, RoutedEventArgs args) =>
        await Shell.Settings.CreateOAuthClientAsync();

    private async void OnDeleteOAuthClient(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: string clientId })
        {
            await Shell.Settings.DeleteOAuthClientAsync(clientId);
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
        CopyToClipboard(Shell.Settings.McpToken);

    /// <summary>Both halves at once, labelled, because the pair is useless one at a time.</summary>
    private void OnCopyMintedClient(object sender, RoutedEventArgs args)
    {
        if (Shell.Settings.MintedClient is not { } minted)
        {
            return;
        }
        CopyToClipboard(
            $"ASTRID_OAUTH_CLIENT_ID={minted.ClientId}{Environment.NewLine}"
            + $"ASTRID_OAUTH_CLIENT_SECRET={minted.ClientSecret}");
    }

    private static void CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    /// <summary>
    /// Show one settings page and hide the rest.
    /// </summary>
    /// <remarks>
    /// astrid-web navigates between pages behind its hub; a flyout has nowhere to navigate to, so
    /// the pages are all here and one is visible. The rail's tag names the page, so adding one is
    /// a <c>ListViewItem</c> and a panel with the same name rather than an entry in a table
    /// somewhere else.
    /// </remarks>
    private void ShowSettingsSection(string section)
    {
        SettingsSectionTitle.Text = section switch
        {
            "Reminders" => "Reminders",
            "Tasks" => Strings.Get("smart.title"),
            "Contacts" => Strings.Get("contacts.title"),
            "Appearance" => "Appearance",
            "Agents" => "AI agents",
            "ApiAccess" => "API access",
            "Integrations" => "Integrations",
            "Data" => "Your data",
            _ => "Account",
        };

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

    /// <summary>
    /// Write everything this account has to a file the person chooses.
    /// </summary>
    /// <remarks>
    /// The save dialog is the shell's job and the writing is the core's: the bytes never cross the
    /// boundary, because an export is somebody's entire history and a JSON round trip of it would
    /// be work for its own sake.
    /// </remarks>
    private async void OnExportAccount(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not string format)
        {
            return;
        }
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedFileName = $"astrid-export-{DateTime.Now:yyyy-MM-dd}",
        };
        picker.FileTypeChoices.Add(
            format == "csv" ? "Comma-separated values" : "JSON",
            new List<string> { format == "csv" ? ".csv" : ".json" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.MainWindowHandle);

        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await Shell.Settings.ExportAsync(format, file.Path);
        }
    }

    /// <summary>Choose how Google lists get linked.</summary>
    /// <remarks>
    /// Guarded against the load that fills the box in: without it, opening the flyout would send
    /// the mode the account already has back to the server every time.
    /// </remarks>
    private async void OnGoogleSyncModeChosen(object sender, SelectionChangedEventArgs args)
    {
        if (_settingsLoading
            || sender is not ComboBox box
            || box.SelectedItem is not string mode
            || mode == Shell.Settings.GoogleSyncMode)
        {
            return;
        }
        await Shell.Settings.SetGoogleSyncModeAsync(mode);
    }

    /// <summary>
    /// Wear the look this installation is set to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things, because a look is two things. The <b>appearance</b> is WinUI's own: setting
    /// RequestedTheme on the root makes every ThemeResource in the tree resolve light or dark, so
    /// the whole app follows without a single brush being restated. Leaving it Default is what
    /// "auto" means — Windows decides, and keeps deciding when the system changes.
    /// </para>
    /// <para>
    /// The <b>surface</b> is ours. Ocean is a light appearance behind a cyan chrome, which is the
    /// brand look and the default; the other three leave the surface to the appearance. That is
    /// the whole difference between ocean and light, and it is why the core answers with an
    /// appearance and a name rather than one enum the shell has to interpret twice.
    /// </para>
    /// </remarks>
    private void ApplyTheme()
    {
        var settings = Shell.Settings;
        if (Content is not FrameworkElement root)
        {
            return;
        }

        root.RequestedTheme = settings.ThemeIsDark switch
        {
            true => ElementTheme.Dark,
            false => ElementTheme.Light,
            // "auto": Windows decides, and goes on deciding.
            null => ElementTheme.Default,
        };

        var ocean = settings.Theme == "ocean";
        OceanSurface.Visibility = ocean ? Visibility.Visible : Visibility.Collapsed;

        // The sign-in screen covers everything, including the surface, so it wears the wash itself
        // rather than showing a plain panel on the one screen a first-time user actually sees.
        SignInLayer.Background = ocean
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AstridOceanBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                "SolidBackgroundFillColorBaseBrush"];

        // A ThemeResource in a Style SETTER is resolved when the style is applied to a container
        // and never again, so rows already on screen keep the colours of the theme they were born
        // in. Switching to dark left white cards carrying white text — unreadable until a restart.
        // Re-attaching the style makes every live container resolve its setters again.
        Restyle(TaskRows);
        Restyle(MyTasksList);
        Restyle(FavoritesList);
        Restyle(ListsList);

        static void Restyle(ListView view)
        {
            var style = view.ItemContainerStyle;
            view.ItemContainerStyle = null;
            view.ItemContainerStyle = style;
        }
    }

    /// <summary>Choose a look, and wear it immediately.</summary>
    /// <remarks>
    /// Guarded against the load that fills the box in, like the other pickers here: without it,
    /// opening the flyout would write back the theme the app is already wearing.
    /// </remarks>
    private async void OnThemeChosen(object sender, SelectionChangedEventArgs args)
    {
        if (_settingsLoading
            || sender is not ComboBox box
            || box.SelectedItem is not string theme
            || theme == Shell.Settings.Theme)
        {
            return;
        }
        await Shell.Settings.SetThemeAsync(theme);
    }

    private async void OnSaveWebhook(object sender, RoutedEventArgs args)
    {
        await Shell.Settings.SaveWebhookAsync();
    }

    /// <summary>Save, and ask for a new signing secret while doing it.</summary>
    /// <remarks>
    /// Its own button rather than a checkbox beside Save: rotating the secret stops every delivery
    /// until the other end is updated, which is not something to do by leaving a box ticked.
    /// </remarks>
    private async void OnRegenerateWebhookSecret(object sender, RoutedEventArgs args)
    {
        await Shell.Settings.SaveWebhookAsync(regenerateSecret: true);
    }

    private async void OnTestWebhook(object sender, RoutedEventArgs args)
    {
        await Shell.Settings.TestWebhookAsync();
    }

    private async void OnDeleteWebhook(object sender, RoutedEventArgs args)
    {
        await Shell.Settings.DeleteWebhookAsync();
    }

    /// <summary>Register an agent, and show the credentials the server returns once.</summary>
    private async void OnRegisterCustomAgent(object sender, RoutedEventArgs args)
    {
        var secret = await Shell.Settings.RegisterAgentAsync(NewAgentNameBox.Text);
        NewAgentNameBox.Text = string.Empty;
        if (string.IsNullOrEmpty(secret))
        {
            return;
        }
        await new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Strings.Get("agents.secret_title"),
            Content = secret,
            CloseButtonText = Strings.Get("dialog.close"),
        }.ShowAsync();
    }

    private async void OnDeleteCustomAgent(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string agentId })
        {
            await Shell.Settings.DeleteAgentAsync(agentId);
        }
    }

    private async void OnConnectCopilot(object sender, RoutedEventArgs args)
    {
        var url = await Shell.Settings.SetCopilotAsync(connect: true);
        if (!string.IsNullOrEmpty(url))
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
    }

    private async void OnDisconnectCopilot(object sender, RoutedEventArgs args)
    {
        await Shell.Settings.SetCopilotAsync(connect: false);
    }

    private async void OnAgentModeChosen(object sender, SelectionChangedEventArgs args)
    {
        if (_settingsLoading
            || sender is not ComboBox box
            || box.Tag is not string agentId
            || box.SelectedItem is not string mode)
        {
            return;
        }
        var current = Shell.Settings.Agents.FirstOrDefault(agent => agent.Id == agentId);
        if (current?.Mode == mode)
        {
            // The box is set from what was loaded; writing then would send the mode back to the
            // server on every open.
            return;
        }
        await Shell.Settings.SetAgentModeAsync(agentId, mode);
    }

    /// <summary>
    /// A key, saved on Enter.
    /// </summary>
    /// <remarks>
    /// On Enter rather than on every keystroke, which would send a dozen half-typed keys to the
    /// server and put one of them in a log somewhere. Cleared straight afterwards: the box shows
    /// nothing for a stored key, and leaving one on screen is a secret sitting in a window.
    /// </remarks>
    private async void OnCredentialKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter
            || sender is not PasswordBox box
            || box.Tag is not string serviceId)
        {
            return;
        }
        args.Handled = true;
        if (await Shell.Settings.SaveCredentialAsync(serviceId, box.Password))
        {
            box.Password = string.Empty;
        }
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

    // ── The list's settings and members ──────────────────────────────────────────────────────

    private async void OnListSettingsOpening(object sender, object args)
    {
        await Shell.LoadListSettingsAsync();
        await Shell.ListSettings.LoadExternalAsync();
        // The agents and repositories the list can be bound to (task f44b4a0c).
        await Shell.ListSettings.LoadAgentOptionsAsync();
    }

    /// <summary>
    /// Connect a provider, in the browser.
    /// </summary>
    /// <remarks>
    /// The same hand-off as signing in: somebody's Google password belongs in their browser, and
    /// an app that asked for it in its own window would be teaching a habit worth not having.
    /// </remarks>
    private async void OnConnectProvider(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not string provider)
        {
            return;
        }
        var url = await Shell.ListSettings.ConnectProviderAsync(provider);
        if (!string.IsNullOrEmpty(url))
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        }
    }

    /// <summary>Mirror this list to the chosen container.</summary>
    private async void OnMirrorChosen(object sender, SelectionChangedEventArgs args)
    {
        if (sender is not ComboBox box
            || box.Tag is not string provider
            || box.SelectedItem is not ExternalContainer container)
        {
            return;
        }
        var linked = Shell.ListSettings.Providers.FirstOrDefault(p => p.Provider == provider);
        if (linked?.Link?.RemoteContainerId == container.Id)
        {
            // Already mirrored there. The box is set from what was loaded, and writing then would
            // re-link on every open.
            return;
        }
        await Shell.ListSettings.SetLinkAsync(provider, container.Id, linked?.Link?.Id);
    }

    private async void OnListRenamed(object sender, RoutedEventArgs args)
    {
        await Shell.ListSettings.RenameAsync(ListNameBox.Text);
        await Shell.Sidebar.LoadAsync();
    }

    // ── How the list looks, and who can see it (task 53780e75) ─────────────────────────────

    /// <summary>A swatch was chosen. The sidebar mark and the chips follow on the reload.</summary>
    private async void OnListColourChosen(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string hex
            && await Shell.ListSettings.SetColorAsync(hex))
        {
            await Shell.Sidebar.LoadAsync();
            await Shell.Tasks.RefreshAsync();
        }
    }

    /// <summary>
    /// The favourite switch moved. It also moves when the binding sets it, so a value that already
    /// matches the list is not a request.
    /// </summary>
    private async void OnListFavoriteToggled(object sender, RoutedEventArgs args)
    {
        if (sender is ToggleSwitch toggle
            && toggle.IsOn != Shell.ListSettings.IsFavorite
            && await Shell.ListSettings.SetFavoriteAsync(toggle.IsOn))
        {
            await Shell.Sidebar.LoadAsync();
        }
    }

    // ── The board's columns (task e5214fba) ────────────────────────────────────────────────

    /// <summary>After a column changes, the board on screen — if it is on screen — redraws.</summary>
    private async Task StatusesChangedAsync()
    {
        if (Shell.IsBoardView)
        {
            await Shell.Board.RefreshAsync();
        }
    }

    private async void OnAddStatus(object sender, RoutedEventArgs args) => await AddTypedStatusAsync();

    private async void OnNewStatusKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }
        args.Handled = true;
        await AddTypedStatusAsync();
    }

    private async Task AddTypedStatusAsync()
    {
        if (await Shell.ListSettings.AddStatusAsync(NewStatusBox.Text))
        {
            NewStatusBox.Text = string.Empty;
            await StatusesChangedAsync();
        }
    }

    private async void OnStatusRenamed(object sender, RoutedEventArgs args)
    {
        if (sender is TextBox { Tag: string role } box
            && await Shell.ListSettings.RenameStatusAsync(role, box.Text))
        {
            await StatusesChangedAsync();
        }
    }

    private void OnStatusNameKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter && sender is TextBox box)
        {
            args.Handled = true;
            // Losing focus is what commits the rename, the same as the list's name above.
            box.IsEnabled = false;
            box.IsEnabled = true;
        }
    }

    private async void OnStatusMovedUp(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string role
            && await Shell.ListSettings.MoveStatusAsync(role, "up"))
        {
            await StatusesChangedAsync();
        }
    }

    private async void OnStatusMovedDown(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string role
            && await Shell.ListSettings.MoveStatusAsync(role, "down"))
        {
            await StatusesChangedAsync();
        }
    }

    private async void OnStatusRemoved(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string role
            && await Shell.ListSettings.RemoveStatusAsync(role))
        {
            await StatusesChangedAsync();
        }
    }

    /// <summary>A default for new tasks was chosen (task c4102c67). The view model writes only a change.</summary>
    private async void OnDefaultChosen(object sender, SelectionChangedEventArgs args)
    {
        if (sender is ComboBox { SelectedItem: DefaultChoice choice })
        {
            await Shell.ListSettings.ChooseDefaultAsync(choice);
        }
    }

    private async void OnListPrivacyChosen(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string privacy)
        {
            await Shell.ListSettings.SetPrivacyAsync(privacy);
        }
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

    /// <summary>
    /// Start dragging a card.
    /// </summary>
    /// <remarks>
    /// The task id travels as text on the clipboard package, which is how WinUI carries a drag. It
    /// is also what makes a card draggable out of the app into a text field — harmless, and better
    /// than a private format nothing else can read.
    /// </remarks>
    private void OnCardDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not string taskId)
        {
            args.Cancel = true;
            return;
        }
        args.Data.SetText(taskId);
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    /// <summary>A column will take a card.</summary>
    private void OnCardDragOver(object sender, DragEventArgs args)
    {
        args.AcceptedOperation = args.DataView.Contains(StandardDataFormats.Text)
            ? DataPackageOperation.Move
            : DataPackageOperation.None;
    }

    /// <summary>
    /// A card was dropped on a column.
    /// </summary>
    /// <remarks>
    /// What the move writes is the core's decision — a role, a completion, or neither — so this
    /// hands over two ids and nothing else. Dropping a card back where it came from is a no-op
    /// there rather than a write nobody asked for.
    /// </remarks>
    private async void OnCardDropped(object sender, DragEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not string columnId
            || !args.DataView.Contains(StandardDataFormats.Text))
        {
            return;
        }
        // Taken before the await: the deferral keeps the package alive, and without it the view is
        // closed by the time the id comes back.
        var deferral = args.GetDeferral();
        try
        {
            var taskId = await args.DataView.GetTextAsync();
            if (!string.IsNullOrEmpty(taskId))
            {
                await Shell.Board.MoveAsync(taskId, columnId);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>A card was tapped. The view model decides whether that opens or closes.</summary>
    private async void OnCardOpened(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string taskId)
        {
            await Shell.ToggleCardAsync(taskId);
            SyncDetailPriority();
        }
    }

    // ── The detail, in place (task 91a25b8a) ────────────────────────────────────────────────
    //
    // There is ONE detail pane. In list view it is the column beside the list; on the board it
    // is lifted out of that column and set inside the slot the board puts after the expanded
    // card, so a task opened from a card is the same fields, thread and comment box as one opened
    // from a row. A second copy of that pane for the board is how the two would come to differ.
    //
    // The slot is an item in the column's ItemsControl, so a refresh that rebuilds the columns
    // rebuilds the slot too — and Loaded fires on the new one, which is what re-homes the pane
    // after every reload rather than leaving it inside a container that is no longer on screen.

    /// <summary>The slot that currently holds the pane, when one does.</summary>
    private ContentControl? _detailHost;

    private void OnInlineDetailSlotLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is ContentControl host)
        {
            HostDetailPane(host);
        }
    }

    /// <summary>Set the pane inside a card's slot, as the expanded card's detail.</summary>
    private void HostDetailPane(ContentControl host)
    {
        if (!ReferenceEquals(_detailHost, host))
        {
            DetachDetailPane();
            host.Content = DetailPane;
            _detailHost = host;
            // A card, not a panel: every edge drawn, the corner the cards have, and the same
            // 6px gap under it the cards keep between themselves.
            DetailPane.Width = double.NaN;
            DetailPane.BorderThickness = new Thickness(1);
            DetailPane.CornerRadius = new CornerRadius(10);
            DetailPane.Margin = new Thickness(0, 0, 0, 6);
        }
        PlaceDetailPane();
    }

    /// <summary>Put the pane back beside the list.</summary>
    private void DockDetailPane()
    {
        if (_detailHost is null && RootGrid.Children.Contains(DetailPane))
        {
            return;
        }
        DetachDetailPane();
        // Grid.Column is an attached property on the element itself, so it survives the move.
        RootGrid.Children.Add(DetailPane);
        DetailPane.Width = 360;
        DetailPane.BorderThickness = new Thickness(1, 0, 0, 0);
        DetailPane.CornerRadius = new CornerRadius(0);
        DetailPane.Margin = new Thickness(0);
    }

    private void DetachDetailPane()
    {
        if (_detailHost is not null)
        {
            _detailHost.Content = null;
            _detailHost = null;
        }
        RootGrid.Children.Remove(DetailPane);
    }

    /// <summary>
    /// Where the pane belongs right now, and whether it shows.
    /// </summary>
    /// <remarks>
    /// One decision rather than a binding and a handler that could disagree. With no card
    /// expanded the pane is docked and shows whenever a task is open. With a card expanded it
    /// shows only once a slot has taken it: between the board saying which card and the slot
    /// loading, a pane still docked beside the board would flash there for a frame.
    /// </remarks>
    private void PlaceDetailPane()
    {
        if (Shell.Board.ExpandedTaskId is null)
        {
            DockDetailPane();
            DetailPane.Visibility = Shell.Detail.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        DetailPane.Visibility = Shell.Detail.IsOpen && _detailHost is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
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

    // ── The lists a task is in (task d3f3b111) ──────────────────────────────────────────────

    private async void OnListsFlyoutOpening(object sender, object args) =>
        await Shell.Detail.LoadListPicksAsync(string.Empty);

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
    private void OnDescriptionTapped(object sender, TappedRoutedEventArgs args)
    {
        if (_descriptionLinkFollowed)
        {
            _descriptionLinkFollowed = false;
            return;
        }
        Shell.Detail.BeginEditingDescription();
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
                SyncDetailPriority();
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
        SyncDetailPriority();
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
            Content = "Delete it",
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
                    new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Delete this comment?" },
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
    private void SyncDetailPriority()
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
