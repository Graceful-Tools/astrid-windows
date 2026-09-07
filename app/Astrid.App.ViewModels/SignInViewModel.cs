using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// Signing in through the browser.
/// </summary>
/// <remarks>
/// <para>
/// The app does not ask for a password. Astrid's sign-in is passkeys, Google and a magic link, and
/// the browser the person is already signed into does all three better than a window inside a task
/// app could. So this view model has two jobs: get a URL from the core and hand it to the shell to
/// open, and pass the callback back when Windows delivers it.
/// </para>
/// <para>
/// Everything security-relevant — the PKCE verifier, the state comparison, the single-use flow —
/// is in <c>astrid_core::services::auth</c>. Nothing here validates anything, because a check
/// written here would be a second implementation of a rule that already exists.
/// </para>
/// </remarks>
public sealed class SignInViewModel : ObservableObject
{
    private readonly IAstridCore _core;
    private bool _isSignedIn;
    private bool _isWaitingForBrowser;
    private string? _errorMessage;

    public SignInViewModel(IAstridCore core)
    {
        _core = core;
    }

    /// <summary>Whether there is a stored session.</summary>
    public bool IsSignedIn
    {
        get => _isSignedIn;
        private set
        {
            if (Set(ref _isSignedIn, value))
            {
                Raise(nameof(NeedsSignIn));
            }
        }
    }

    /// <summary>Whether to show the sign-in screen instead of the app.</summary>
    public bool NeedsSignIn => !IsSignedIn;

    /// <summary>
    /// True between opening the browser and the callback arriving.
    /// </summary>
    /// <remarks>
    /// Shown as "waiting for your browser" rather than a spinner with no explanation: the app is
    /// idle on purpose and the next thing to happen is in another window entirely.
    /// </remarks>
    public bool IsWaitingForBrowser
    {
        get => _isWaitingForBrowser;
        private set => Set(ref _isWaitingForBrowser, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    /// <summary>Ask the core whether we are signed in. Called before the first paint.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(Commands.IsSignedIn(), cancellationToken);
        IsSignedIn = response.Ok
            && response.Value.TryGetProperty("signedIn", out var element)
            && element.GetBoolean();
        IsWaitingForBrowser = response.Ok
            && response.Value.TryGetProperty("waitingForCallback", out var waiting)
            && waiting.GetBoolean();
    }

    /// <summary>
    /// Start signing in.
    /// </summary>
    /// <returns>The URL to open in the browser, or null if the core would not start a flow.</returns>
    public async Task<string?> BeginAsync(CancellationToken cancellationToken = default)
    {
        ErrorMessage = null;
        var response = await _core.CallAsync(Commands.BeginSignIn(), cancellationToken);
        if (!response.Ok || !response.Value.TryGetProperty("authorizeUrl", out var element))
        {
            ErrorMessage = response.Error?.Message ?? "sign-in could not be started";
            return null;
        }

        IsWaitingForBrowser = true;
        return element.GetString();
    }

    /// <summary>
    /// Finish signing in from the URL Windows activated the app with.
    /// </summary>
    /// <remarks>
    /// Called for every protocol activation, not only the ones that are callbacks. The core
    /// decides which are — a deep link to a task uses the same scheme — so a rejection here is
    /// ordinary and is not shown to the user unless a sign-in was actually in progress.
    /// </remarks>
    public async Task<bool> CompleteAsync(string callbackUrl, CancellationToken cancellationToken = default)
    {
        var wasWaiting = IsWaitingForBrowser;
        var response = await _core.CallAsync(Commands.CompleteSignIn(callbackUrl), cancellationToken);
        IsWaitingForBrowser = false;

        if (!response.Ok)
        {
            ErrorMessage = wasWaiting ? response.Error?.Message : null;
            return false;
        }

        ErrorMessage = null;
        IsSignedIn = true;
        return true;
    }

    /// <summary>The user closed the browser without finishing.</summary>
    public async Task CancelAsync(CancellationToken cancellationToken = default)
    {
        await _core.CallAsync(Commands.CancelSignIn(), cancellationToken);
        IsWaitingForBrowser = false;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await _core.CallAsync(Commands.SignOut(), cancellationToken);
        IsSignedIn = false;
        IsWaitingForBrowser = false;
    }
}
