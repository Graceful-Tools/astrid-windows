using Astrid.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Astrid.App.Views;

/// <summary>
/// One public list, opened by its id and read (task 6fc92134). It covers the window the way the
/// settings do; see the XAML for why it is read-only.
/// </summary>
public sealed partial class PublicListView : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(PublicListView),
        new PropertyMetadata(null));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public PublicListView()
    {
        InitializeComponent();
    }

    private void OnClose(object sender, RoutedEventArgs args) => Shell.ClosePublicList();

    /// <summary>
    /// File the report on the web.
    /// </summary>
    /// <remarks>
    /// The one write this screen offers, and it leaves the app to do it: the core does not cache a
    /// public list, and whether the Outbox may hold a write against an uncached list is still open
    /// there. The address is built from the list on screen and the origin this window is signed in
    /// to — no list id is held in this shell, which is the bug task a4352b30 fixed.
    /// </remarks>
    private async void OnReportOnTheWeb(object sender, RoutedEventArgs args)
    {
        if (Shell.PublicList.ListId is not { Length: > 0 } listId)
        {
            return;
        }
        var origin = App.ServerOrigin.TrimEnd('/');
        await Windows.System.Launcher.LaunchUriAsync(
            new Uri($"{origin}/lists/{Uri.EscapeDataString(listId)}"));
    }
}
