using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Astrid.App.ViewModels;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// What a partner has to be able to change to produce a branded Windows build (task ac40a6ee).
/// </summary>
/// <remarks>
/// <para>
/// Windows was the closest native app to white-label-ready and still could not ship a partner
/// build: the display name, the publisher, the <c>astrid://</c> scheme and the tiles were written
/// into <c>packaging/AppxManifest.xml</c> by hand, and the server could only be chosen at runtime
/// through <c>ASTRID_SERVER_URL</c> — there was no way to bake a partner's own into the package.
/// </para>
/// <para>
/// So the manifest holds placeholders and <c>scripts/package.ps1</c> fills them from a brand
/// profile, the same <c>*.brand.json</c> shape astrid-web and the Apple apps read. The assertions
/// below are in two halves: that nothing brandable is a literal any more, and — the half that
/// matters more — that with **no** profile the values are byte for byte what they were, because
/// hosted Astrid must not change.
/// </para>
/// <para>
/// The PowerShell half is exercised by running it, not by reading it. A loader whose defaults are
/// asserted by scanning its source passes while returning something else entirely, and "the output
/// is identical when no brand is set" is exactly the claim that cannot be checked that way.
/// </para>
/// </remarks>
public sealed class BrandProfileTests
{
    /// <summary>Today's Astrid values, which a build with no brand profile must still produce.</summary>
    private static readonly (string Key, string Value)[] AstridDefaults =
    [
        ("DisplayName", "Astrid Tasks"),
        ("PublisherDisplayName", "Graceful Tools LLC"),
        ("Description", "Tasks, shared with the people you share them with."),
        ("ShortName", "Astrid"),
        ("Scheme", "astrid"),
        ("SchemeDisplayName", "Astrid"),
        ("ServerUrl", "https://astrid.cc"),
    ];

    /// <summary>
    /// Nothing brandable is a literal in the manifest any more.
    /// </summary>
    /// <remarks>
    /// Read from the parsed XML rather than the file's text, so the explanatory comments — which
    /// name Astrid and Graceful Tools, and should — are not mistaken for shipped values. Every
    /// brandable attribute and element is a <c>{PLACEHOLDER}</c>; the asset paths and the Windows
    /// App SDK dependency are not brandable and are left alone.
    /// </remarks>
    [Fact]
    public void The_manifest_states_no_brand_value_as_a_literal_task_ac40a6ee()
    {
        var manifest = XDocument.Load(ManifestPath());
        var values = new List<(string Where, string Value)>();
        foreach (var element in manifest.Descendants())
        {
            foreach (var attribute in element.Attributes())
            {
                values.Add(($"{element.Name.LocalName}/@{attribute.Name.LocalName}", attribute.Value));
            }
            var text = string.Concat(element.Nodes().OfType<XText>().Select(node => node.Value)).Trim();
            values.Add((element.Name.LocalName, text));
        }

        var leaks = values
            .Where(pair => pair.Value.Contains("Astrid", StringComparison.OrdinalIgnoreCase)
                || pair.Value.Contains("Graceful Tools", StringComparison.OrdinalIgnoreCase))
            // The executable, the Application id and the asset file names are the app's own
            // identity inside the package, not anything a reader sees. Renaming them would change
            // the AUMID for no gain.
            .Where(pair => !pair.Where.EndsWith("/@Executable", StringComparison.Ordinal)
                && !pair.Where.EndsWith("/@Id", StringComparison.Ordinal))
            .Select(pair => $"{pair.Where} = {pair.Value}")
            .ToList();

        Assert.True(leaks.Count == 0,
            "AppxManifest.xml still states brand values a partner cannot change:\n  "
            + string.Join("\n  ", leaks));
    }

    /// <summary>
    /// Every placeholder the manifest carries is one <c>package.ps1</c> actually fills.
    /// </summary>
    /// <remarks>
    /// A placeholder nobody substitutes ships as itself, and Windows only objects at install time —
    /// <c>{SCHEME}</c> as a protocol name is a package that installs and then never receives the
    /// sign-in callback. Scanned both ways round, so an abandoned substitution is caught too.
    /// </remarks>
    [Fact]
    public void Every_manifest_placeholder_is_substituted_by_the_packager_task_ac40a6ee()
    {
        var manifest = File.ReadAllText(ManifestPath());
        var packager = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "package.ps1"));

        var placeholders = Regex.Matches(manifest, @"\{[A-Z_]+\}")
            .Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
        Assert.True(placeholders.Count >= 6, $"found only {placeholders.Count} placeholders; the scan has stopped working");

        var unfilled = placeholders
            .Where(placeholder => !packager.Contains($"'{placeholder}'", StringComparison.Ordinal))
            .ToList();
        Assert.True(unfilled.Count == 0,
            "the manifest carries placeholders package.ps1 does not fill:\n  " + string.Join("\n  ", unfilled));

        var substituted = Regex.Matches(packager, @"Replace\('(\{[A-Z_]+\})'")
            .Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var orphaned = substituted.Where(name => !placeholders.Contains(name)).ToList();
        Assert.True(orphaned.Count == 0,
            "package.ps1 substitutes placeholders the manifest no longer has:\n  " + string.Join("\n  ", orphaned));
    }

    /// <summary>
    /// With no brand profile, the loader returns exactly today's Astrid values.
    /// </summary>
    /// <remarks>
    /// The whole point of the change: a hosted Astrid build has to come out of this byte for byte
    /// as it did before there was a brand profile at all.
    /// </remarks>
    [Fact]
    public void No_brand_profile_means_todays_astrid_values_task_ac40a6ee()
    {
        var profile = LoadProfile(brand: null);

        foreach (var (key, value) in AstridDefaults)
        {
            Assert.Equal(value, profile[key]);
        }
        Assert.Equal(Path.Combine(RepoRoot(), "packaging", "Assets"),
            Path.GetFullPath(profile["AssetsDirectory"]!));
    }

    /// <summary>
    /// The astrid profile, named explicitly, is the same thing as naming nothing.
    /// </summary>
    /// <remarks>
    /// astrid-web keeps <c>astrid.brand.json</c> for this reason — it proves the refactor did not
    /// change today's behaviour — and the assertion only holds if the mapping from a profile's
    /// variables onto the manifest's values is the identity for Astrid's own values.
    /// </remarks>
    [Fact]
    public void The_astrid_profile_matches_the_unbranded_default_task_ac40a6ee()
    {
        var unbranded = LoadProfile(brand: null);
        var astrid = LoadProfile(brand: "astrid");

        foreach (var (key, _) in AstridDefaults)
        {
            Assert.Equal(unbranded[key], astrid[key]);
        }
    }

    /// <summary>
    /// A partner profile moves every one of them, and leaves no Astrid value behind.
    /// </summary>
    /// <remarks>
    /// The same test astrid-web's <c>acme.brand.json</c> exists for: a profile that merely compiles
    /// is not a white-labelled build. Any value the loader forgets to map falls back to Astrid's,
    /// which is the failure this catches — a partner shipping "Astrid Tasks" in their Start menu.
    /// </remarks>
    [Fact]
    public void A_partner_profile_moves_every_brand_value_task_ac40a6ee()
    {
        var profile = LoadProfile(brand: "acme");

        Assert.Equal("Acme Tasks", profile["DisplayName"]);
        Assert.Equal("Acme Corporation", profile["PublisherDisplayName"]);
        Assert.Equal("Acme", profile["ShortName"]);
        Assert.Equal("acmetasks", profile["Scheme"]);
        Assert.Equal("https://tasks.acme.example", profile["ServerUrl"]);

        var astridLeaks = profile
            .Where(pair => pair.Value is not null && pair.Key != "AssetsDirectory"
                && (pair.Value.Contains("Astrid", StringComparison.OrdinalIgnoreCase)
                    || pair.Value.Contains("Graceful Tools", StringComparison.OrdinalIgnoreCase)))
            .Select(pair => $"{pair.Key} = {pair.Value}")
            .ToList();
        Assert.True(astridLeaks.Count == 0,
            "the acme profile still carries Astrid's values:\n  " + string.Join("\n  ", astridLeaks));
    }

    /// <summary>
    /// The app's own defaults come from the build, not from a literal in the source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ServerSelection.DefaultOrigin</c> was a <c>const</c>, so <c>ASTRID_SERVER_URL</c> was the
    /// only way to reach a partner's server and every start of a partner build without it went to
    /// astrid.cc. The default now comes from assembly metadata the build writes, and the
    /// environment variable still overrides it — which is the documented order.
    /// </para>
    /// <para>
    /// Asserted as Astrid's values because that is what this repo's own build produces; a partner
    /// build's are asserted by the loader tests above and by the package layout.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_shell_reads_its_brand_defaults_from_the_build_task_ac40a6ee()
    {
        Assert.Equal("https://astrid.cc", BrandDefaults.ServerOrigin);
        Assert.Equal("astrid", BrandDefaults.UrlScheme);
        Assert.Equal("Astrid", BrandDefaults.ShortName);
        Assert.Equal(BrandDefaults.ServerOrigin, ServerSelection.DefaultOrigin);
    }

    /// <summary>
    /// A callback on the configured scheme is found; one on another scheme is not.
    /// </summary>
    /// <remarks>
    /// <c>SignInCallback</c> held <c>"astrid://"</c> as a constant, so a partner build registering
    /// its own scheme would have dropped every sign-in it was handed. It reads the configured
    /// scheme now; the core still compares against its own <c>URL_SCHEME</c>, which is the half
    /// that has to move there.
    /// </remarks>
    [Fact]
    public void The_callback_parser_follows_the_configured_scheme_task_ac40a6ee()
    {
        var callback = $"{BrandDefaults.UrlScheme}://auth/callback?code=abc&state=xyz";

        Assert.Equal(callback, SignInCallback.FromLaunchArguments(callback));
        Assert.Null(SignInCallback.FromLaunchArguments("someoneelse://auth/callback?code=abc"));
    }

    /// <summary>
    /// Nothing in the shell writes the scheme or the server as a literal any more.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registration in <c>Program.cs</c> is the one that matters: it writes the scheme into
    /// HKCU for an unpackaged build, and a literal there would register <c>astrid://</c> for a
    /// partner's app however the manifest was built. Scanned as source because the registry calls
    /// cannot be exercised without touching the machine's own registration.
    /// </para>
    /// <para>
    /// <c>ServerSelection</c> is not scanned, and its <c>astrid.db</c> is deliberate: the cache
    /// file name is a path on disk, not anything a reader sees, and renaming it per brand would
    /// strand the cache of every install that already exists. A partner's build writes
    /// <c>astrid-&lt;their host&gt;.db</c> for the same reason it always did — one file per server.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_shell_names_no_scheme_literal_task_ac40a6ee()
    {
        var offenders = new[]
            {
                Path.Combine("Astrid.App", "Program.cs"),
                Path.Combine("Astrid.App.ViewModels", "SignInCallback.cs"),
            }
            .SelectMany(relative =>
            {
                var source = File.ReadAllText(Path.Combine(RepoRoot(), "app", relative));
                // Lines that are not comments: the XML doc comments on these types explain the
                // scheme and the server by name, and should keep doing so.
                var code = string.Join('\n', source.Split('\n')
                    .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
                // A literal that IS the scheme, or that spells out a URL on it. "Astrid.Main" —
                // the single-instance key — is neither; it is brandable for a different reason and
                // is asserted where it is defined.
                return Regex.Matches(code, @"""[^""\n]*""")
                    .Select(match => match.Value)
                    .Where(literal => literal.Equals("\"astrid\"", StringComparison.OrdinalIgnoreCase)
                        || literal.Contains("astrid://", StringComparison.OrdinalIgnoreCase)
                        || literal.Contains("astrid.cc", StringComparison.OrdinalIgnoreCase))
                    .Select(literal => $"{relative}: {literal}");
            })
            .ToList();

        Assert.True(offenders.Count == 0,
            "the shell still writes the scheme or the server as a literal:\n  "
            + string.Join("\n  ", offenders));

        // The single-instance key, which is per USER rather than per app for an unpackaged build:
        // two brands sharing it means one answers the other's sign-in callback.
        var program = File.ReadAllText(Path.Combine(RepoRoot(), "app", "Astrid.App", "Program.cs"));
        Assert.Contains("BrandDefaults.ShortName}.Main", program, StringComparison.Ordinal);
    }

    /// <summary>
    /// The packager hands the brand's scheme and server to the build, and its assets to the layout.
    /// </summary>
    /// <remarks>
    /// Without the two build properties the package carries a partner's manifest over Astrid's
    /// binaries — the Start menu says the partner's name and the first launch signs in to
    /// astrid.cc. Read as source: the assertion is about what the publish is invoked with, and
    /// running a publish per architecture to find out is a twenty-minute test.
    /// </remarks>
    [Fact]
    public void The_packager_builds_with_the_brands_server_scheme_and_assets_task_ac40a6ee()
    {
        var packager = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "package.ps1"));

        Assert.Contains("-p:AstridDefaultServerUrl=", packager, StringComparison.Ordinal);
        Assert.Contains("-p:AstridUrlScheme=", packager, StringComparison.Ordinal);
        Assert.Contains("-p:AstridBrandShortName=", packager, StringComparison.Ordinal);
        Assert.Contains("$brand.AssetsDirectory", packager, StringComparison.Ordinal);
        // The literal path is what it falls back to, inside the loader — not what the packager
        // copies, or a brand's own tiles would never reach the package.
        Assert.DoesNotContain("'packaging/Assets/*'", packager, StringComparison.Ordinal);
    }

    /// <summary>
    /// The packaging scripts parse.
    /// </summary>
    /// <remarks>
    /// Nothing else in the gate reads them: <c>package.ps1</c> is run by hand before a Store
    /// upload, so a syntax error in it waits there until the day someone needs it. Parsed rather
    /// than run — running it is a twenty-minute publish per architecture — which is enough to catch
    /// the failure this file is prone to: it is saved UTF-8 with a byte order mark, and PowerShell
    /// 5.1 reads one without as the system codepage and makes a parse error of every non-ASCII
    /// character, naming a line nowhere near the real one.
    /// </remarks>
    [Fact]
    public void The_packaging_scripts_parse_task_ac40a6ee()
    {
        foreach (var script in new[] { "package.ps1", Path.Combine("lib", "brand-profile.ps1") })
        {
            var path = Path.Combine(RepoRoot(), "scripts", script).Replace("'", "''");
            var errors = PowerShell(
                "$errors = $null; " +
                $"[System.Management.Automation.Language.Parser]::ParseFile('{path}', [ref]$null, [ref]$errors)" +
                " | Out-Null; " +
                "if ($errors) { $errors | ForEach-Object { $_.ToString() } }");

            Assert.True(errors.Trim().Length == 0, $"scripts/{script} does not parse:\n{errors}");
        }
    }

    /// <summary>The profile <c>Get-AstridBrandProfile</c> returns, as a dictionary.</summary>
    /// <remarks>
    /// Run through <c>powershell.exe</c> — Windows PowerShell 5.1, the same host
    /// <c>package.ps1</c> is run under — rather than reimplemented here. A second implementation
    /// of the mapping would be a second thing to keep in step, and the one the tests agreed with
    /// would not be the one that builds the package.
    /// </remarks>
    private static Dictionary<string, string?> LoadProfile(string? brand)
    {
        var root = RepoRoot().Replace("'", "''");
        var loader = Path.Combine(RepoRoot(), "scripts", "lib", "brand-profile.ps1").Replace("'", "''");
        var argument = brand is null ? string.Empty : $" -Brand '{brand.Replace("'", "''")}'";
        var json = PowerShell(
            $". '{loader}'; Get-AstridBrandProfile -RepoRoot '{root}'{argument} " +
            "| ConvertTo-Json -Compress -Depth 3");

        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : property.Value.GetString(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// What <paramref name="script"/> wrote to stdout, under Windows PowerShell 5.1.
    /// </summary>
    /// <remarks>
    /// <c>powershell.exe</c>, not <c>pwsh</c>: 5.1 is the host the packaging scripts are run under
    /// and the one whose quirks they are written for, and the two disagree about enough — strict
    /// mode, default encodings, <c>ConvertTo-Json</c>'s depth — to make the other a different
    /// test.
    /// </remarks>
    private static string PowerShell(string script)
    {
        var process = Process.Start(new ProcessStartInfo("powershell.exe")
        {
            ArgumentList = { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;
        var output = process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, $"powershell failed:\n{script}\n{errors}\n{output}");
        return output;
    }

    private static string ManifestPath() => Path.Combine(RepoRoot(), "packaging", "AppxManifest.xml");

    /// <summary>The repo root, from the test assembly's own location.</summary>
    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(LocalisationTests.AppDirectory(), "..", ".."));
}
