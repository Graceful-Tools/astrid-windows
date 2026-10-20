using System.Text.RegularExpressions;
using System.Xml.Linq;
using Astrid.App.ViewModels;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The words a reader sees name the brand the build was made for (task 8d241526).
/// </summary>
/// <remarks>
/// <para>
/// <c>BrandProfileTests</c> (task ac40a6ee) moved the package's identity — the Start menu name, the
/// publisher, the <c>astrid://</c> scheme, the server and the tiles. The copy did not move with it:
/// a partner build still said "Astrid" in its own title bar, offered
/// <c>remindme@astrid.cc</c> as the address to mail a task to, and sent a reader to
/// <c>astrid.cc</c> to finish a job the client could not. Those are the three the white-label audit
/// of 2026-10-20 named, and the ten or so other places the product's own name is written out.
/// </para>
/// <para>
/// The rule these assert is one rule: <b>no string a reader sees spells the brand</b>. A sentence
/// that has to name it carries a <c>{0}</c> and is formatted with a value from the build, so a
/// translator still owns the sentence and the packager still owns the noun. Hosted Astrid is
/// unchanged because every one of those values defaults to Astrid's.
/// </para>
/// </remarks>
public sealed class BrandCopyTests
{
    /// <summary>
    /// Nothing in the resources, the XAML or the fallback table spells Astrid.
    /// </summary>
    /// <remarks>
    /// All three together, because a word escapes through whichever of them is not watched: the
    /// resources are what a translator reads, the XAML literals are what ships when the <c>.pri</c>
    /// does not, and the fallback table is what an unpackaged build falls back to. The scan is for
    /// the bare word, so <c>astrid.cc</c> and <c>remindme@astrid.cc</c> are caught by the same test.
    /// </remarks>
    [Fact]
    public void No_word_a_reader_sees_spells_the_brand_task_8d241526()
    {
        var app = LocalisationTests.AppDirectory();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(app, "Strings"), "Resources.resw", SearchOption.AllDirectories))
        {
            var language = Path.GetFileName(Path.GetDirectoryName(file))!;
            offenders.AddRange(XDocument.Load(file).Root!.Elements("data")
                .Where(data => Names(data.Element("value")!.Value))
                .Select(data => $"Strings/{language}: {data.Attribute("name")!.Value}"
                                + $" = {data.Element("value")!.Value}"));
        }

        foreach (var file in LocalisationTests.WindowXaml(app).Append(Path.Combine(app, "MainWindow.xaml")))
        {
            offenders.AddRange(
                from element in LocalisationTests.Elements(File.ReadAllText(file))
                from property in Worded
                where element.Attributes.TryGetValue(property, out var value)
                      && !value.StartsWith('{') && Names(value)
                select $"{Path.GetFileName(file)}: {property}=\"{element.Attributes[property]}\"");
        }

        // The fallback table only — the file's doc comments explain the brand by name, and should.
        offenders.AddRange(File.ReadAllLines(Path.Combine(app, "Strings.cs"))
            .Where(line => line.TrimStart().StartsWith("[\"", StringComparison.Ordinal) && Names(line))
            .Select(line => $"Strings.cs: {line.Trim()}"));

        Assert.True(offenders.Count == 0,
            "these name the brand instead of taking it from the build — give the sentence a {0} and"
            + " format it with BrandDefaults, or bind it to BrandCopy:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The three values the audit named, plus the agent's, read back as Astrid's.
    /// </summary>
    /// <remarks>
    /// <c>Domain</c> is derived from the server rather than carried beside it: a build pointed at
    /// one host and naming another in its copy is the bug this is meant to prevent, and two
    /// properties that must agree eventually do not. The other three are their own values because
    /// none is derivable — an address can live on a different domain, and an assistant can be
    /// called something other than the product.
    /// </remarks>
    [Fact]
    public void The_brand_defaults_carry_the_whole_identity_task_8d241526()
    {
        Assert.Equal("astrid.cc", BrandDefaults.Domain);
        Assert.Equal("remindme@astrid.cc", BrandDefaults.InboundTaskEmail);
        Assert.Equal("Astrid", BrandDefaults.AgentName);
        Assert.Equal("astrid", BrandDefaults.Wordmark);
    }

    /// <summary>
    /// A partner profile moves them, and falls back to its own domain rather than to ours.
    /// </summary>
    /// <remarks>
    /// <c>acme.brand.json</c> sets no inbound address and no agent name, which is the ordinary
    /// partner profile: the fallback has to be <c>remindme@&lt;their domain&gt;</c> and their own
    /// name. astrid-web's <c>BRAND.inboundTaskEmail</c> falls back to the literal
    /// <c>remindme@astrid.cc</c> instead; a Windows build deliberately does not, because the
    /// address it prints is the one a partner's user would actually mail.
    /// </remarks>
    [Fact]
    public void A_partner_profile_moves_the_address_and_the_agent_task_8d241526()
    {
        var profile = BrandProfileTests.LoadProfile(brand: "acme");

        Assert.Equal("remindme@tasks.acme.example", profile["InboundTaskEmail"]);
        Assert.Equal("Acme", profile["AgentName"]);
        Assert.Equal("acme", profile["Wordmark"]);
    }

    /// <summary>
    /// The packager hands them to the build, as it does the scheme and the server.
    /// </summary>
    /// <remarks>
    /// A value the loader resolves and the publish never hears is a partner build that says Astrid
    /// — exactly the failure of the previous round, one layer down.
    /// </remarks>
    [Fact]
    public void The_packager_builds_with_the_brands_copy_task_8d241526()
    {
        var packager = File.ReadAllText(
            Path.Combine(LocalisationTests.AppDirectory(), "..", "..", "scripts", "package.ps1"));

        Assert.Contains("-p:AstridBrandInboundTaskEmail=", packager, StringComparison.Ordinal);
        Assert.Contains("-p:AstridBrandAgentName=", packager, StringComparison.Ordinal);
        Assert.Contains("-p:AstridBrandWordmark=", packager, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every sentence that used to name the brand now has a slot for it.
    /// </summary>
    /// <remarks>
    /// The other direction of the scan above: a sentence can be freed of the word by deleting the
    /// word, which reads as fixed and ships "Brings forward from anywhere". Each of these has to
    /// still SAY the brand — through <c>{0}</c>. The other languages are covered by
    /// <c>LocalisationTests.Every_language_carries_every_key_task_b9dd4a25</c>, which fails a
    /// translation that loses a placeholder.
    /// </remarks>
    [Fact]
    public void Every_brand_sentence_keeps_a_slot_for_the_name_task_8d241526()
    {
        var english = XDocument
            .Load(Path.Combine(LocalisationTests.AppDirectory(), "Strings", "en-US", "Resources.resw"))
            .Root!.Elements("data")
            .ToDictionary(data => data.Attribute("name")!.Value, data => data.Element("value")!.Value,
                StringComparer.Ordinal);

        string[] sentences =
        [
            "brand_board_turn_one_on", "brand_connections_web_session", "brand_tour_hotkey",
            "appearance_hotkey_hint",
            "brand_agent_model_heading", "brand_agent_model_none", "brand_agent_model_auto",
            "brand_agent_model_hint",
        ];

        var without = sentences
            .Where(key => !english.TryGetValue(key, out var value)
                          || !value.Contains("{0}", StringComparison.Ordinal))
            .ToList();

        Assert.True(without.Count == 0,
            "these have to name the brand and no longer do:\n  " + string.Join("\n  ", without));
    }

    /// <summary>The XAML properties a reader reads. <see cref="LocalisationTests"/>'s, plus a window's title.</summary>
    private static readonly string[] Worded =
    [
        "Text", "Content", "Header", "PlaceholderText", "OnContent", "OffContent", "Title",
        "ToolTipService.ToolTip", "AutomationProperties.Name",
    ];

    /// <summary>Does this spell the brand — as a word, a host or an address?</summary>
    private static bool Names(string value) =>
        Regex.IsMatch(value, "astrid", RegexOptions.IgnoreCase);
}
