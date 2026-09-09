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
        }
        QuickAddBox.Focus(FocusState.Programmatic);
    }

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
            await Shell.OpenTaskAsync(row.Id);
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
        ShowSettingsSection(section);

        // Loaded when its page is opened rather than when the flyout is: it is a network round
        // trip for a page most opens never reach, and the account flyout already makes four.
        if (section == "ApiAccess")
        {
            await Shell.Settings.LoadApiAccessAsync();
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
            "Appearance" => "Appearance",
            "Agents" => "AI agents",
            "ApiAccess" => "API access",
            "Integrations" => "Integrations",
            "Data" => "Your data",
            _ => "Account",
        };

        AccountSection.Visibility = Visible("Account");
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
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }
        args.Handled = true;
        await SendCommentAsync();
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

        void Paint(Button button, int level)
        {
            // Same colours as the row stripe and every other client, read from the one converter
            // that owns them rather than restated here.
            var colour = (Brush)PriorityColours.Convert(level, typeof(Brush), null!, string.Empty);
            // No-priority has no colour of its own in the STRIPE — that deliberately draws nothing
            // — but a swatch still has to be visible, so it takes the grey the unmarked checkbox
            // image is drawn in rather than a muted text colour that matches nothing on screen.
            if (level == 0)
            {
                colour = new SolidColorBrush(
                    Colours.Parse(PriorityPalette.None) ?? Colors.Gray);
            }

            button.BorderBrush = colour;
            var on = level == chosen;
            button.Background = on ? colour : new SolidColorBrush(Colors.Transparent);
            button.Foreground = on
                ? new SolidColorBrush(Colors.White)
                : colour;
        }
    }

    /// <summary>The one place the priority colours are decided, borrowed for the squares.</summary>
    private static readonly PriorityBrushConverter PriorityColours = new();

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
