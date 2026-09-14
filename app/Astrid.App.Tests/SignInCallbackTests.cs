using Astrid.App.ViewModels;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The sign-in callback as Windows actually hands it over.
/// </summary>
/// <remarks>
/// Regression for task 64c02099 (2026-09-13, sign-in stuck on "Waiting for your browser"):
/// the shell resolves <c>astrid://</c> through the plain <c>Classes\astrid</c> registration, whose
/// command is <c>"Astrid.App.exe" "%1"</c>, so the second instance is an ordinary launch whose one
/// argument is the URL. The app only looked at protocol activations, and the sign-in it was
/// waiting for was silently dropped. The App SDK's own form, <c>----ms-protocol:</c> in front of
/// the URL, has to keep working too.
/// </remarks>
public sealed class SignInCallbackTests
{
    [Theory]
    [InlineData("astrid://auth/callback?code=abc&state=xyz")]
    [InlineData("\"astrid://auth/callback?code=abc&state=xyz\"")]
    [InlineData("----ms-protocol:astrid://auth/callback?code=abc&state=xyz")]
    [InlineData("\"----ms-protocol:astrid://auth/callback?code=abc&state=xyz\"")]
    [InlineData("  astrid://auth/callback?code=abc&state=xyz  ")]
    public void A_launch_argument_carrying_the_callback_is_recognised(string arguments)
    {
        Assert.Equal(
            "astrid://auth/callback?code=abc&state=xyz",
            SignInCallback.FromLaunchArguments(arguments));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--some-flag")]
    [InlineData("https://astrid.cc/auth/desktop?state=xyz")]
    public void Anything_else_is_not_a_callback(string? arguments)
    {
        Assert.Null(SignInCallback.FromLaunchArguments(arguments));
    }

    /// <summary>The scheme is matched, not the whole URL: the core judges the rest.</summary>
    [Theory]
    [InlineData("astrid://something/else?x=1")]
    [InlineData("ASTRID://auth/callback?code=abc")]
    public void Only_the_scheme_is_checked_here(string url)
    {
        Assert.Equal(url, SignInCallback.FromLaunchArguments(url));
    }
}
