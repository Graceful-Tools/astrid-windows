using System.Text.Json;
using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The core is always told which server to talk to (task <c>7e68a733</c>).
/// </summary>
/// <remarks>
/// <para>
/// A partner build is a brand profile and nothing else: the same binary, pointed somewhere else.
/// The shell's own calls — sign-in hand-off, settings, public lists, passkeys — all go through
/// <c>App.ServerOrigin</c> and have always reached the partner's server. The core's did not. It
/// was started with <c>ServerUrl</c>, which <see cref="ServerSelection.Parse"/> answers as
/// <c>null</c> when the origin is the brand's own, and a missing <c>baseUrl</c> made the core fall
/// back to its compiled-in default — astrid.cc. So a partner's sync, Outbox and live stream went
/// to Astrid's server while everything else went to theirs, and for Astrid's own build the two
/// agreed only by coincidence.
/// </para>
/// <para>
/// The fix is structural as well as behavioural: <c>baseUrl</c> is a required parameter now, so
/// the next caller cannot leave it out. astrid-core is making the same move from its side
/// (<c>require-base-url</c>, <c>CONFIG_BASE_URL_REQUIRED</c>), and this works against every core
/// revision either way — naming the server is correct whether or not the core still has a default
/// to fall back to.
/// </para>
/// </remarks>
public sealed class BrandServerTests
{
    /// <summary>
    /// The start config always names a server (task <c>7e68a733</c>).
    /// </summary>
    /// <remarks>
    /// The key used to be dropped when it was null, alongside <c>urlScheme</c>, under the rule
    /// that "a key the shell has nothing to say about is left out so the core's default applies".
    /// That rule is right for <c>urlScheme</c> and wrong for <c>baseUrl</c>: the core's default is
    /// Astrid's server, which is the one answer a partner build must never give.
    /// </remarks>
    [Fact]
    public void The_start_config_always_names_a_server_task_7e68a733()
    {
        var config = JsonSerializer.Deserialize<Dictionary<string, string>>(
            AstridClient.BuildStartConfig(":memory:", "https://acme.example", "acme"))!;

        Assert.Equal("https://acme.example", config["baseUrl"]);
        Assert.Equal(":memory:", config["cachePath"]);
        Assert.Equal("acme", config["urlScheme"]);
    }

    /// <summary>
    /// A build that names no scheme still leaves <c>urlScheme</c> out — but never the server.
    /// </summary>
    /// <remarks>
    /// The two keys are deliberately not treated the same way any more, and this says which is
    /// which: absence is how the shell asks for the core's default scheme, and there is no
    /// corresponding way to ask for its default server.
    /// </remarks>
    [Fact]
    public void A_missing_scheme_is_still_left_out_task_7e68a733()
    {
        var config = JsonSerializer.Deserialize<Dictionary<string, string>>(
            AstridClient.BuildStartConfig(":memory:", "https://acme.example", null))!;

        Assert.False(config.ContainsKey("urlScheme"));
        Assert.Equal("https://acme.example", config["baseUrl"]);
    }

    /// <summary>
    /// With no <c>ASTRID_SERVER_URL</c>, the server the core is told about is the brand's own
    /// (task <c>7e68a733</c>).
    /// </summary>
    /// <remarks>
    /// <see cref="ServerSelection.Origin"/> is the one place that turns "nothing was chosen" into
    /// a name, and it answers the brand's origin rather than a literal. That is what makes a
    /// partner build reach the partner's server without anybody setting an environment variable,
    /// which is the whole point: a partner should not have to remember.
    /// </remarks>
    [Fact]
    public void An_unset_server_resolves_to_the_brands_own_task_7e68a733()
    {
        Assert.Null(ServerSelection.Parse(null));
        Assert.Equal(BrandDefaults.ServerOrigin, ServerSelection.Origin(ServerSelection.Parse(null)));
        Assert.Equal(BrandDefaults.ServerOrigin, ServerSelection.Origin(null));
    }

    /// <summary>
    /// And the app starts the core with that resolved origin, not with the nullable choice (task
    /// <c>7e68a733</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read out of the source, because <c>App.xaml.cs</c> is in the WinUI assembly and this test
    /// project is <c>net9.0</c> — it cannot reference it, let alone start one. A coarse test, but
    /// the bug was one identifier long and lived on exactly this line, so the thing worth guarding
    /// is which identifier is written there.
    /// </para>
    /// <para>
    /// <c>CachePath()</c> deliberately still takes <c>ServerUrl</c>: its null-versus-chosen
    /// distinction is what names the cache file — <c>astrid.db</c> against
    /// <c>astrid-&lt;host&gt;.db</c> — so a partner build and an Astrid build on one machine keep
    /// separate caches. Asserted here so that "pass the origin everywhere" does not later get
    /// applied to the line below it.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_app_starts_the_core_with_the_resolved_origin_task_7e68a733()
    {
        var source = File.ReadAllText(
            Path.Combine(LocalisationTests.AppDirectory(), "App.xaml.cs"));

        Assert.Contains("AstridClient.Start(CachePath(), ServerOrigin, BrandDefaults.UrlScheme)",
            source);
        Assert.DoesNotContain("AstridClient.Start(CachePath(), ServerUrl", source);
        Assert.Contains("ServerSelection.CacheFileName(ServerUrl)", source);
    }
}
