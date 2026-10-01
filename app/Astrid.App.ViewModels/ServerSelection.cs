namespace Astrid.App.ViewModels;

/// <summary>
/// Which deployment this app talks to: Astrid, unless <c>ASTRID_SERVER_URL</c> names another.
/// </summary>
/// <remarks>
/// <para>
/// The core always accepted a base URL (<c>AstridClient.Start(cachePath, baseUrl)</c>) and the app
/// never passed one, so a Windows build could only ever reach astrid.cc. A partner deployment —
/// the white-label test site at tasks.gracefultools.com runs its own server, database and domain —
/// was unreachable from Windows. Sign-in needs nothing more: it is the browser hand-off, built
/// from the same base URL, and passkeys happen on the partner's own site.
/// </para>
/// <para>
/// A different server gets a different cache file. The cache holds one account's tasks and its
/// sync ledger; pointing an Astrid cache at a partner server would merge two accounts' data.
/// </para>
/// <para>
/// Like <c>ASTRID_DATA_DIR</c>, this is an environment variable rather than a setting: it is for
/// testing a deployment and for partner builds, and a field in Settings that silently moves a
/// user's account to another server is not a feature anyone should stumble on.
/// </para>
/// </remarks>
public static class ServerSelection
{
    public const string DefaultOrigin = "https://astrid.cc";

    /// <summary>
    /// The origin <paramref name="value"/> names, or null for the default. Accepts https anywhere
    /// and http only on loopback (a local dev server). Anything else is ignored rather than
    /// trusted: an http origin would send the session over the wire in the clear.
    /// </summary>
    public static string? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return null;
        var secure = uri.Scheme == Uri.UriSchemeHttps;
        var loopback = uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;
        if (!secure && !loopback) return null;
        var origin = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        return origin == DefaultOrigin ? null : origin;
    }

    /// <summary>The origin links should open: the chosen server, or Astrid.</summary>
    public static string Origin(string? chosen) => chosen ?? DefaultOrigin;

    /// <summary>
    /// <c>astrid.db</c> for Astrid; <c>astrid-&lt;host&gt;.db</c> for anything else, so two servers'
    /// accounts never share a cache.
    /// </summary>
    public static string CacheFileName(string? chosen)
    {
        if (chosen is null) return "astrid.db";
        var uri = new Uri(chosen);
        var host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}-{uri.Port}";
        return $"astrid-{host}.db";
    }
}
