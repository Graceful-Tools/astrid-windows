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
/// The sign-in screen. It covers the app rather than replacing it.
/// </summary>
public sealed partial class SignInOverlay : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(SignInOverlay),
        new PropertyMetadata(null));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public SignInOverlay()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The ground behind the card, set by the page's theme.
    /// </summary>
    /// <remarks>
    /// This screen covers everything, including the Ocean surface, so it wears the wash itself
    /// rather than showing a plain panel on the one screen a first-time user actually sees.
    /// </remarks>
    internal void WearSurface(Brush ground) => SignInLayer.Background = ground;

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
}
