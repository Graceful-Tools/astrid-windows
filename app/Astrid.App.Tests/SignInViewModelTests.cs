using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// What the sign-in screen does — and, mostly, what it refuses to decide.
/// </summary>
/// <remarks>
/// The PKCE verifier, the state comparison and the single-use flow all live in
/// <c>astrid_core::services::auth</c> and are tested there. These check the shell's half: which
/// screen is shown, when the browser is opened, and what happens to an activation that is not a
/// sign-in at all.
/// </remarks>
public sealed class SignInViewModelTests
{
    [Fact]
    public async Task With_no_session_the_sign_in_screen_is_what_shows()
    {
        var core = new FakeCore().AnswerOk("isSignedIn", new { signedIn = false, waitingForCallback = false });
        var view = new SignInViewModel(core);

        await view.RefreshAsync();

        Assert.True(view.NeedsSignIn);
        Assert.False(view.IsSignedIn);
    }

    [Fact]
    public async Task With_a_stored_session_the_app_shows_instead()
    {
        var core = new FakeCore().AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false });
        var view = new SignInViewModel(core);

        await view.RefreshAsync();

        Assert.False(view.NeedsSignIn);
    }

    /// <summary>
    /// The URL comes from the core, which minted the state and the verifier that go with it. A
    /// shell that built its own would be a second implementation of the hand-off.
    /// </summary>
    [Fact]
    public async Task Beginning_returns_the_url_the_core_minted()
    {
        var core = new FakeCore().AnswerOk("beginSignIn",
            new { authorizeUrl = "https://astrid.cc/auth/desktop?state=abc" });
        var view = new SignInViewModel(core);

        var url = await view.BeginAsync();

        Assert.Equal("https://astrid.cc/auth/desktop?state=abc", url);
        Assert.True(view.IsWaitingForBrowser);
    }

    [Fact]
    public async Task A_completed_callback_signs_in_and_stops_waiting()
    {
        var core = new FakeCore()
            .AnswerOk("beginSignIn", new { authorizeUrl = "https://astrid.cc/auth/desktop?state=abc" })
            .AnswerOk("completeSignIn", new { id = "u1", name = "Ada" });
        var view = new SignInViewModel(core);
        await view.BeginAsync();

        Assert.True(await view.CompleteAsync("astrid://auth/callback?code=x&state=abc"));
        Assert.True(view.IsSignedIn);
        Assert.False(view.IsWaitingForBrowser);
        Assert.Null(view.ErrorMessage);
    }

    /// <summary>
    /// Every protocol activation comes through here, and most are not sign-ins — a deep link to a
    /// task uses the same scheme. One that arrives while nothing is waiting is not an error the
    /// user should see.
    /// </summary>
    [Fact]
    public async Task An_activation_that_is_not_a_callback_is_quiet()
    {
        var core = new FakeCore().AnswerFailure("completeSignIn", AstridFailureKind.Refused,
            "no sign-in is in progress");
        var view = new SignInViewModel(core);

        Assert.False(await view.CompleteAsync("astrid://tasks/t1"));
        Assert.Null(view.ErrorMessage);
    }

    /// <summary>But a callback that fails while the user IS waiting has to say why.</summary>
    [Fact]
    public async Task A_failed_callback_during_a_sign_in_is_reported()
    {
        var core = new FakeCore()
            .AnswerOk("beginSignIn", new { authorizeUrl = "https://astrid.cc/auth/desktop?state=abc" })
            .AnswerFailure("completeSignIn", AstridFailureKind.Refused, "the code has expired");
        var view = new SignInViewModel(core);
        await view.BeginAsync();

        Assert.False(await view.CompleteAsync("astrid://auth/callback?code=x&state=abc"));
        Assert.Equal("the code has expired", view.ErrorMessage);
        Assert.False(view.IsWaitingForBrowser);
    }

    [Fact]
    public async Task Cancelling_stops_waiting_and_tells_the_core()
    {
        var core = new FakeCore()
            .AnswerOk("beginSignIn", new { authorizeUrl = "https://astrid.cc/auth/desktop" })
            .AnswerOk("cancelSignIn");
        var view = new SignInViewModel(core);
        await view.BeginAsync();

        await view.CancelAsync();

        Assert.False(view.IsWaitingForBrowser);
        Assert.Contains("cancelSignIn", core.SentKinds());
    }

    /// <summary>
    /// Signing out empties the window as well as the cache: a list left on screen after the
    /// session has gone is a list the app can no longer refresh or write to.
    /// </summary>
    [Fact]
    public async Task Signing_out_from_the_shell_clears_what_is_on_screen()
    {
        var core = new FakeCore()
            .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false })
            .AnswerOk("lists", new object[] { new { id = "l1", name = "Home" } })
            .AnswerOk("rowsForList", new { total = 0, offset = 0, rows = Array.Empty<object>() })
            .AnswerOk("outboxStats", new { hasUnsentWork = false })
            .AnswerOk("sync", new { fetched = false })
            .AnswerOk("signOut");
        using var shell = new ShellViewModel(core, work => work().GetAwaiter().GetResult());
        await shell.StartAsync();
        Assert.NotEmpty(shell.Sidebar.Lists);

        await shell.SignOutAsync();

        Assert.Empty(shell.Sidebar.Lists);
        Assert.Empty(shell.Tasks.Rows);
        Assert.True(shell.SignIn.NeedsSignIn);
    }

    /// <summary>
    /// Signed out, the app does not load the previous session's lists behind the sign-in screen.
    /// </summary>
    [Fact]
    public async Task Starting_signed_out_loads_nothing()
    {
        var core = new FakeCore()
            .AnswerOk("isSignedIn", new { signedIn = false, waitingForCallback = false });
        using var shell = new ShellViewModel(core, work => work().GetAwaiter().GetResult());

        await shell.StartAsync();

        Assert.Equal(["isSignedIn"], core.SentKinds());
        Assert.Empty(shell.Sidebar.Lists);
    }
}
