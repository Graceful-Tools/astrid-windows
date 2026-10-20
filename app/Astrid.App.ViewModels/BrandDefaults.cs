using System.Reflection;

namespace Astrid.App.ViewModels;

/// <summary>
/// The brand this build was made for: Astrid, unless <c>scripts/package.ps1 -Brand</c> said otherwise.
/// </summary>
/// <remarks>
/// <para>
/// Three values the shell cannot read off the manifest. Windows reads <c>AppxManifest.xml</c>; the
/// running app does not, and an unpackaged build has no manifest at all — so a package carrying a
/// partner's name over a default build would have said the partner's name in Start and then signed
/// in to astrid.cc on the <c>astrid://</c> scheme. They arrive as assembly metadata, written by the
/// build from the same brand profile the manifest is filled from, which is what keeps the two
/// halves of a branded package in step.
/// </para>
/// <para>
/// Each falls back to Astrid's value, and a value the build does not set is the ordinary case: a
/// plain <c>dotnet build</c> of this repo is an Astrid build. <c>ASTRID_SERVER_URL</c> still
/// overrides the server at runtime — the baked-in value is the default, not a lock — which is how
/// a partner build is pointed at a staging host.
/// </para>
/// <para>
/// Validated here rather than trusted, because the properties come from a command line: a build
/// run with a typo for a server or a scheme should be an Astrid build rather than a broken one.
/// </para>
/// </remarks>
public static class BrandDefaults
{
    /// <summary>Hosted Astrid's values, which every unset property falls back to.</summary>
    private const string AstridOrigin = "https://astrid.cc";
    private const string AstridScheme = "astrid";
    private const string AstridShortName = "Astrid";

    /// <summary>The server a build with no <c>ASTRID_SERVER_URL</c> talks to.</summary>
    public static string ServerOrigin { get; } = ReadOrigin();

    /// <summary>
    /// The URL scheme this build is registered on, without <c>://</c>.
    /// </summary>
    /// <remarks>
    /// The installer registers it from the manifest for a packaged build and
    /// <c>Program.RegisterProtocol</c> writes it into HKCU for an unpackaged one. Both have to
    /// agree with the scheme the sign-in hand-off comes back on, which astrid-web takes from
    /// <c>NEXT_PUBLIC_BRAND_APP_URL_SCHEME</c> — the same field of the same brand profile.
    /// </remarks>
    public static string UrlScheme { get; } = ReadScheme();

    /// <summary>The short brand name: the tile's, and the name a scheme registration shows.</summary>
    public static string ShortName { get; } = Read("AstridBrandShortName") ?? AstridShortName;

    /// <summary>
    /// The host a reader is sent to when the client cannot finish a job — "…at astrid.cc".
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="ServerOrigin"/> rather than carried beside it. A build that talks to
    /// one host and names another in its copy sends a partner's user to our sign-in page, and two
    /// properties that have to agree eventually do not. The baked-in server, not the
    /// <c>ASTRID_SERVER_URL</c> one: a developer pointed at a staging host is still reading the
    /// product's own documentation.
    /// </remarks>
    public static string Domain { get; } = new Uri(ServerOrigin).Host;

    /// <summary>The address a task can be mailed to, as the Tasks settings page prints it.</summary>
    /// <remarks>
    /// Its own value, because an inbound address need not live on the web domain — a partner
    /// routing mail through their own provider is the ordinary case. The fallback is this brand's
    /// own domain and never ours: a partner who sets nothing should print an address that at worst
    /// bounces, rather than one that files their user's task in someone else's account.
    /// </remarks>
    public static string InboundTaskEmail { get; } = ReadInboundTaskEmail();

    /// <summary>The assistant's name: "Let Astrid choose".</summary>
    /// <remarks>
    /// astrid-web's <c>BRAND.agentIdentityName</c>, which falls back to the brand name for the same
    /// reason this does — most deployments call the assistant after the product, and the ones that
    /// do not say so once.
    /// </remarks>
    public static string AgentName { get; } = Read("AstridBrandAgentName") ?? ShortName;

    /// <summary>The handle the assistant is mentioned by: "The model behind @astrid".</summary>
    /// <remarks>
    /// astrid-web's <c>BRAND.wordmark</c>. Lower case by default because a mention is, and
    /// <see cref="AgentName"/> is not: "@Astrid" is not the handle the server would match.
    /// </remarks>
    public static string Wordmark { get; } =
        Read("AstridBrandWordmark")?.ToLowerInvariant() ?? ShortName.ToLowerInvariant();

    /// <summary>
    /// The origin the build named, if it is one this app would accept at runtime.
    /// </summary>
    /// <remarks>
    /// Through <see cref="ServerSelection.ParseOrigin"/>, so a baked-in server is held to exactly
    /// the rule an <c>ASTRID_SERVER_URL</c> one is: https anywhere, http on loopback only. A
    /// partner whose profile says <c>http://tasks.partner.example</c> gets Astrid rather than a
    /// build that sends its session over the wire in the clear.
    /// </remarks>
    /// <remarks>
    /// <see cref="ServerSelection.ParseOrigin"/> and not <see cref="ServerSelection.Parse"/>: the
    /// latter compares against <see cref="ServerSelection.DefaultOrigin"/>, which is this property,
    /// and a static initialiser that re-enters itself reads the half-built value — null — rather
    /// than recursing. <c>ParseOrigin</c> holds no static state of its own, so the two can depend
    /// on each other this way round only.
    /// </remarks>
    private static string ReadOrigin()
    {
        var configured = Read("AstridDefaultServerUrl");
        return configured is null ? AstridOrigin : ServerSelection.ParseOrigin(configured) ?? AstridOrigin;
    }

    /// <summary>
    /// The scheme the build named, if it is one Windows can register.
    /// </summary>
    /// <remarks>
    /// The same shape <c>brand-profile.ps1</c> enforces, repeated rather than trusted: this
    /// assembly can be built by <c>dotnet build -p:AstridUrlScheme=…</c> without the packager
    /// having looked at the value. A scheme with a space or a slash in it registers nothing and
    /// loses every sign-in, silently.
    /// </remarks>
    private static string ReadScheme()
    {
        var configured = Read("AstridUrlScheme")?.ToLowerInvariant();
        if (configured is null || configured.Length == 0 || !char.IsAsciiLetterLower(configured[0]))
        {
            return AstridScheme;
        }
        return configured.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-')
            ? configured
            : AstridScheme;
    }

    /// <summary>
    /// The inbound address the build named, if it is one a reader could actually mail.
    /// </summary>
    /// <remarks>
    /// Held to the same "validated, not trusted" rule as the server and the scheme, and for the
    /// same reason: the value comes from a command line. A local part and a host, no spaces — not
    /// RFC 5322, which would accept a quoted string with a comma in it and tell a reader nothing.
    /// A build with a typo prints this brand's own default rather than a line that cannot be mailed.
    /// </remarks>
    private static string ReadInboundTaskEmail()
    {
        var fallback = $"remindme@{Domain}";
        var configured = Read("AstridBrandInboundTaskEmail");
        if (configured is null)
        {
            return fallback;
        }
        var at = configured.IndexOf('@');
        var usable = at > 0 && at == configured.LastIndexOf('@') && at < configured.Length - 1
            && !configured.Any(char.IsWhiteSpace);
        return usable ? configured : fallback;
    }

    /// <summary>
    /// One <c>AssemblyMetadata</c> value from this assembly, or null when it is absent or blank.
    /// </summary>
    /// <remarks>
    /// This assembly's own, not the entry assembly's: under the test runner the entry assembly is
    /// the test host, which carries none of these, so reading it would make the defaults untestable
    /// — and the project reference is what carries a <c>dotnet publish -p:…</c> property down here
    /// in the first place.
    /// </remarks>
    private static string? Read(string key)
    {
        var value = typeof(BrandDefaults).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)
            ?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
