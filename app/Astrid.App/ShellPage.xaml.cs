using Astrid.App.ViewModels;
using Astrid.App.Views;
using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Astrid.App;

/// <summary>
/// The app's one page: the parts of the window, and what connects them to the view model and to
/// each other.
/// </summary>
/// <remarks>
/// <para>
/// Every part of the window is a UserControl in <c>Views/</c> — the sidebar, the list header, the
/// rows, the board, the chat, the task detail, the settings pages and the overlays — and every
/// handler in them does two things: read what the user did, and call a view model. Nothing decides
/// anything — no filtering, no ordering, no rule about what completing a task means. That is rule 9
/// of <c>docs/ASTRID.md</c> §0, and a window is where it is easiest to break by accident, because
/// "just this once, in the click handler" is always the shortest path.
/// </para>
/// <para>
/// What stays on this page is what needs more than one part: the keyboard, the theme, and the one
/// detail pane that moves between the list column and a board card.
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

        // Every part binds to the one view model, handed over here — before any of them loads, so
        // their x:Bind roots are in place when their bindings first run.
        Tour.Shell = Shell;
        Palette.Shell = Shell;
        SignIn.Shell = Shell;
        Sidebar.Shell = Shell;
        Header.Shell = Shell;
        Board.Shell = Shell;
        Rows.Shell = Shell;
        Chat.Shell = Shell;
        Detail.Shell = Shell;

        // What one part has to tell another.
        Palette.RowRun += Sidebar.SyncSelectionFromViewModel;
        Rows.RowActivated += OnRowActivated;
        Board.CardOpened += Detail.SyncPriority;
        Board.DetailSlotLoaded += HostDetailPane;

        // The pane's place follows the open task and the expanded card (task 91a25b8a).
        Shell.Detail.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(TaskDetailViewModel.IsOpen))
            {
                PlaceDetailPane();
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
            Rows.FocusQuickAdd();
            return Task.CompletedTask;
        });
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        // Banners before the first load: a reminder that came due while the app was closed should
        // arrive as the window opens, not a half-minute later when the loop first ticks.
        _reminders.Start();
        // The chord the person chose, or the shipped one. From the cache, like the theme; and
        // re-registered whenever it changes, from the thread that owns the registration.
        await Shell.Settings.LoadHotkeyAsync();
        RegisterHotkey(Shell.Settings.Hotkey);
        Shell.Settings.HotkeyChanged += RegisterHotkey;
        // The look before the first paint, so the window does not flash the wrong one on the way
        // in. It comes from the cache, so this does not wait for a network.
        Shell.Settings.ThemeChanged += ApplyTheme;
        await Shell.Settings.LoadThemeAsync();
        await Shell.StartAsync();
        await Shell.RaiseRemindersAsync();
        await Shell.MaybeShowTourAsync();
        Sidebar.SyncSelectionFromViewModel();

        // The arrow has to follow the row, and a row moves when the list scrolls as well as when
        // the selection changes. The ScrollViewer is inside the ListView's template, so it does
        // not exist until the template is applied — which is why this is here and not in the
        // constructor.
        Rows.WatchScrolling(PointArrowAtSelectedRow);
        SizeChanged += (_, _) => PointArrowAtSelectedRow();
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
                    Rows.FocusQuickAdd();
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
                    Palette.TakeFocus();
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
                Rows.FocusQuickAdd();
                break;
            // The rest — editing a title in place, the description, a comment, the detail panel —
            // arrive with the task detail view. Until then the key is swallowed rather than left
            // to fall through to the list, where it would do something else entirely.
            default:
                break;
        }
    }

    // ── The rows and the detail ──────────────────────────────────────────────────────────────

    /// <summary>A row was opened or the selection moved: the detail's squares and its arrow follow.</summary>
    private void OnRowActivated()
    {
        Detail.SyncPriority();
        PointArrowAtSelectedRow();
    }

    /// <summary>
    /// Point the pane's arrow at the row it is describing, or hide it.
    /// </summary>
    /// <remarks>
    /// Hidden rather than parked at the top when there is no row to point at — a row scrolled out
    /// of view, or a task opened from search or a deep link with no row on screen at all. An arrow
    /// aimed at nothing is worse than no arrow. The measuring is the pane's (task 830e63b9).
    /// </remarks>
    private void PointArrowAtSelectedRow()
    {
        if (!Shell.Detail.IsOpen
            || Shell.IsBoardView
            || Shell.Tasks.Selected is not { } selected
            || Rows.ContainerOf(selected) is not { } container)
        {
            Detail.HideArrow();
            return;
        }
        Detail.PointArrowAt(container);
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

    // ── The look ─────────────────────────────────────────────────────────────────────────────

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
        SignIn.WearSurface(ocean
            ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AstridOceanBrush"]
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                "SolidBackgroundFillColorBaseBrush"]);

        // Rows already on screen keep the colours of the theme they were born in unless their
        // style is re-attached; see Restyling.
        Rows.Restyle();
        Sidebar.Restyle();
    }

    /// <summary>Choose a look, and wear it immediately.</summary>
    /// <remarks>
    /// Guarded against the load that fills the box in, like the other pickers here: without it,
    /// opening the flyout would write back the theme the app is already wearing.
    /// </remarks>
    /// <summary>Hold the chord the core accepted, letting go of the one before it.</summary>
    private void RegisterHotkey(Hotkey chord)
    {
        _hotkey?.Dispose();
        _hotkey = new GlobalHotkey(QuickAdd, chord);
        _hotkey.Start();
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

    /// <summary>Set the pane inside a card's slot, as the expanded card's detail.</summary>
    private void HostDetailPane(ContentControl host)
    {
        if (!ReferenceEquals(_detailHost, host))
        {
            DetachDetailPane();
            host.Content = Detail;
            _detailHost = host;
            Detail.WearAsCard();
        }
        PlaceDetailPane();
    }

    /// <summary>Put the pane back beside the list.</summary>
    private void DockDetailPane()
    {
        if (_detailHost is null && RootGrid.Children.Contains(Detail))
        {
            return;
        }
        DetachDetailPane();
        // Grid.Column is an attached property on the element itself, so it survives the move.
        RootGrid.Children.Add(Detail);
        Detail.WearAsColumn();
    }

    private void DetachDetailPane()
    {
        if (_detailHost is not null)
        {
            _detailHost.Content = null;
            _detailHost = null;
        }
        RootGrid.Children.Remove(Detail);
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
            Detail.Visibility = Shell.Detail.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        Detail.Visibility = Shell.Detail.IsOpen && _detailHost is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
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
