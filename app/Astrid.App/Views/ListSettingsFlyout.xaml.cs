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
/// The list's settings, sharing and members. Every handler reads what the user did and calls the view model.
/// </summary>
public sealed partial class ListSettingsFlyout : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(ListSettingsFlyout),
        new PropertyMetadata(null));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public ListSettingsFlyout()
    {
        InitializeComponent();
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
}
