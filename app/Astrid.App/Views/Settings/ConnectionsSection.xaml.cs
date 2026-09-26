using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Astrid.App.Views.Settings;

/// <summary>
/// The Connections page: what can act as the account, with a way to stop each one, and the
/// developer's console for an OAuth client made by hand.
/// </summary>
public sealed partial class ConnectionsSection : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(ConnectionsSection),
        new PropertyMetadata(null, (control, _) => ((ConnectionsSection)control).OnShellChanged()));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public ConnectionsSection()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Two lines are worded here rather than bound: the review count picks one of two sentences,
    /// and the draft problem is a key the core chose, in the reader's language.
    /// </summary>
    private void OnShellChanged()
    {
        Shell.Settings.Connections.PropertyChanged += (_, changed) =>
        {
            switch (changed.PropertyName)
            {
                case nameof(ConnectionsViewModel.ReviewCount):
                    var count = Shell.Settings.Connections.ReviewCount;
                    ReviewSummary.Text = count == 1
                        ? Strings.Get("connections.review.summary_one")
                        : Strings.Get("connections.review.summary_many", count);
                    break;
                case nameof(ConnectionsViewModel.DraftProblem):
                    DraftProblemText.Text = Shell.Settings.Connections.DraftProblem is { } problem
                        ? problem.Uri is { } uri
                            ? Strings.Get($"connections.problem.{problem.Key}", uri)
                            : Strings.Get($"connections.problem.{problem.Key}")
                        : string.Empty;
                    break;
            }
        };
    }

    /// <summary>
    /// Revoke, after asking. Anything using the credential stops working immediately, and a
    /// Custom Agent goes with its credentials — so the two wordings differ by kind.
    /// </summary>
    private async void OnRevoke(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: Connection connection })
        {
            return;
        }
        var isAgent = connection.Kind == "customAgent";
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Strings.Get(isAgent ? "connections.remove_agent_title" : "connections.revoke_title"),
            Content = Strings.Get(
                isAgent ? "connections.remove_agent_confirm" : "connections.revoke_confirm",
                connection.Name),
            PrimaryButtonText = Strings.Get(isAgent ? "connections.remove_yes" : "connections.revoke_yes"),
            CloseButtonText = Strings.Get("dialog.close"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await Shell.Settings.Connections.RevokeAsync(connection);
        }
    }

    private async void OnEditClient(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: string clientId })
        {
            await Shell.Settings.Connections.BeginEditAsync(clientId);
        }
    }

    private async void OnBeginCreate(object sender, RoutedEventArgs args) =>
        await Shell.Settings.Connections.BeginCreateAsync();

    private void OnCancelEditor(object sender, RoutedEventArgs args) =>
        Shell.Settings.Connections.CancelEditor();

    /// <summary>
    /// A field changed: hand the text to the view model and ask the core again. Read off the box
    /// rather than through a two-way binding, so the check always sees what was just typed.
    /// </summary>
    private async void OnDraftTextChanged(object sender, TextChangedEventArgs args)
    {
        if (sender is not TextBox box)
        {
            return;
        }
        var connections = Shell.Settings.Connections;
        if (ReferenceEquals(box, DraftNameBox))
        {
            connections.DraftName = box.Text;
        }
        else if (ReferenceEquals(box, DraftDescriptionBox))
        {
            connections.DraftDescription = box.Text;
        }
        else if (ReferenceEquals(box, DraftRedirectUrisBox))
        {
            connections.DraftRedirectUriText = box.Text;
        }
        await connections.CheckDraftAsync();
    }

    /// <summary>A grant box was clicked: the core applies the pairing and answers with the set.</summary>
    private async void OnGrantToggled(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: string grant })
        {
            await Shell.Settings.Connections.ToggleGrantAsync(grant);
        }
    }

    private async void OnScopeToggled(object sender, RoutedEventArgs args)
    {
        if (sender is CheckBox { Tag: string scope } box)
        {
            await Shell.Settings.Connections.SetScopeAsync(scope, box.IsChecked == true);
        }
    }

    private async void OnSaveClient(object sender, RoutedEventArgs args) =>
        await Shell.Settings.Connections.SaveAsync();

    /// <summary>Both halves at once, labelled, because the pair is useless one at a time.</summary>
    private void OnCopyMintedClient(object sender, RoutedEventArgs args)
    {
        if (Shell.Settings.Connections.MintedClient is not { } minted)
        {
            return;
        }
        ClipboardText.Copy(
            $"ASTRID_OAUTH_CLIENT_ID={minted.ClientId}{Environment.NewLine}"
            + $"ASTRID_OAUTH_CLIENT_SECRET={minted.ClientSecret}");
    }
}
