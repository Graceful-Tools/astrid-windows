using Astrid.App.ViewModels;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// <c>ASTRID_SERVER_URL</c> points the app at another deployment — the white-label partner test
/// site (tasks.gracefultools.com) had no way to be reached from Windows, because the app never
/// passed the core a base URL.
/// </summary>
public sealed class ServerSelectionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("https://astrid.cc")]
    [InlineData("https://ASTRID.cc/")]
    public void Unset_unparseable_or_Astrid_means_the_default(string? value) =>
        Assert.Null(ServerSelection.Parse(value));

    [Theory]
    [InlineData("https://tasks.gracefultools.com", "https://tasks.gracefultools.com")]
    [InlineData("https://Tasks.GracefulTools.com/some/path?x=1", "https://tasks.gracefultools.com")]
    [InlineData("http://localhost:3000", "http://localhost:3000")]
    [InlineData("http://127.0.0.1:3000/", "http://127.0.0.1:3000")]
    public void A_server_is_reduced_to_its_origin(string value, string expected) =>
        Assert.Equal(expected, ServerSelection.Parse(value));

    [Fact]
    public void Plain_http_to_a_real_host_is_refused_rather_than_sending_the_session_in_the_clear() =>
        Assert.Null(ServerSelection.Parse("http://tasks.gracefultools.com"));

    [Fact]
    public void Links_follow_the_chosen_server()
    {
        Assert.Equal("https://astrid.cc", ServerSelection.Origin(null));
        Assert.Equal("https://tasks.gracefultools.com", ServerSelection.Origin("https://tasks.gracefultools.com"));
    }

    [Fact]
    public void Each_server_gets_its_own_cache_so_two_accounts_never_merge()
    {
        Assert.Equal("astrid.db", ServerSelection.CacheFileName(null));
        Assert.Equal("astrid-tasks.gracefultools.com.db", ServerSelection.CacheFileName("https://tasks.gracefultools.com"));
        Assert.Equal("astrid-localhost-3000.db", ServerSelection.CacheFileName("http://localhost:3000"));
    }
}
