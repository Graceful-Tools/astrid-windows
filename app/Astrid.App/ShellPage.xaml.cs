using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        _shortcuts = new ShortcutDispatcher(core, Shell);
        _shortcuts.ShellActionRequested += OnShellAction;
        Loaded += OnLoaded;
        // Every protocol activation, launch or redirected, arrives here. The core decides which
        // are sign-in callbacks; a deep link to a task uses the same scheme.
        App.UriActivated += OnUriActivated;
        Unloaded += (_, _) =>
        {
            App.UriActivated -= OnUriActivated;
            Shell.Dispose();
        };
    }

    /// <summary>What the window binds to.</summary>
    public ShellViewModel Shell { get; }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        await Shell.StartAsync();
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
