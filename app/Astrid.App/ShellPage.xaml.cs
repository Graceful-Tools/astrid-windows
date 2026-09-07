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
        Loaded += OnLoaded;
        Unloaded += (_, _) => Shell.Dispose();
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
    /// The keyboard scheme.
    /// </summary>
    /// <remarks>
    /// The bare-key shortcuts and the rule about when they are allowed to fire are a cross-platform
    /// contract, locked by <c>contracts/fixtures/shortcuts.json</c> and implemented in
    /// <c>astrid_core::keyboard</c>. What is here is the minimum until that dispatch is wired
    /// through: the two chords Windows users expect from any app, which are not part of the shared
    /// scheme and never will be.
    /// </remarks>
    private async void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        var control = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (!control)
        {
            return;
        }

        switch (args.Key)
        {
            case VirtualKey.N:
                args.Handled = true;
                QuickAddBox.Focus(FocusState.Programmatic);
                break;
            case VirtualKey.R:
                args.Handled = true;
                await Shell.SyncAsync();
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
