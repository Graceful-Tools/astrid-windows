using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// Every word the window shows can be translated.
/// </summary>
/// <remarks>
/// <para>
/// The mechanism is <c>x:Uid</c>: an element whose <c>Text</c>, <c>Content</c>, <c>Header</c>
/// and so on is an English literal carries an <c>x:Uid</c>, and <c>Resources.resw</c> holds
/// <c>uid.Property</c> for each. The literal stays in the XAML as the fallback for a build whose
/// <c>.pri</c> did not make it beside the executable, and as what a reader of the XAML sees; the
/// resource wins when it is there, which is what lets a translator add a folder and nothing else.
/// </para>
/// <para>
/// About half the shell's strings bypassed this until 2026-09-11 (docs/ASTRID.md §0 rule 10). These
/// tests are what stops the number climbing back: a new literal with no <c>x:Uid</c>, or an
/// <c>x:Uid</c> with no resource behind it, fails the build.
/// </para>
/// </remarks>
public sealed class LocalisationTests
{
    private static readonly string[] Properties =
    [
        "Text", "Content", "Header", "PlaceholderText", "OnContent", "OffContent", "Title",
        "ToolTipService.ToolTip", "AutomationProperties.Name",
    ];

    private static readonly Dictionary<string, string> ResourceProperty = new(StringComparer.Ordinal)
    {
        ["ToolTipService.ToolTip"] = "[using:Microsoft.UI.Xaml.Controls]ToolTipService.ToolTip",
        ["AutomationProperties.Name"] = "[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
    };

    // A start tag, outside comments: `<Name attrs>` or `<Name attrs/>`.
    private static readonly Regex Tag = new(
        @"<([A-Za-z][A-Za-z0-9_.:]*)((?:\s+[^<>""]*?(?:""[^""]*"")?)*?)\s*/?>", RegexOptions.Singleline);

    private static readonly Regex Attribute = new(@"([A-Za-z_][A-Za-z0-9_.:]*)\s*=\s*""([^""]*)""");

    private static readonly Regex Comment = new(@"<!--.*?-->", RegexOptions.Singleline);

    private static string AppDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Astrid.App", "ShellPage.xaml")))
        {
            directory = directory.Parent;
        }
        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "Astrid.App");
    }

    private static IEnumerable<(string Tag, IReadOnlyDictionary<string, string> Attributes)> Elements(string xaml)
    {
        var stripped = Comment.Replace(xaml, string.Empty);
        foreach (Match match in Tag.Matches(stripped))
        {
            var attributes = Attribute.Matches(match.Groups[2].Value)
                .ToDictionary(a => a.Groups[1].Value, a => a.Groups[2].Value, StringComparer.Ordinal);
            yield return (match.Groups[1].Value, attributes);
        }
    }

    /// <summary>
    /// A word, not a binding and not a glyph code such as <c>&amp;#xE8BD;</c> — a Segoe icon is
    /// the same in every language.
    /// </summary>
    private static bool IsLiteral(string value) =>
        value.Length > 0 && value[0] != '{'
        && System.Net.WebUtility.HtmlDecode(value).Any(char.IsAsciiLetter);

    private static HashSet<string> ResourceNames(string app)
    {
        var resw = XDocument.Load(Path.Combine(app, "Strings", "en-US", "Resources.resw"));
        return resw.Root!.Elements("data")
            .Select(data => data.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// A literal on an element with no <c>x:Uid</c> is a word no translator can reach.
    /// </summary>
    [Fact]
    public void Every_literal_in_the_window_can_be_translated()
    {
        var app = AppDirectory();
        var xaml = File.ReadAllText(Path.Combine(app, "ShellPage.xaml"));
        var resources = ResourceNames(app);
        var untranslatable = new List<string>();
        var unbacked = new List<string>();

        foreach (var (tag, attributes) in Elements(xaml))
        {
            var literals = Properties.Where(p => attributes.TryGetValue(p, out var v) && IsLiteral(v)).ToList();
            if (literals.Count == 0)
            {
                continue;
            }
            if (!attributes.TryGetValue("x:Uid", out var uid))
            {
                untranslatable.Add($"<{tag} {literals[0]}=\"{attributes[literals[0]]}\">");
                continue;
            }
            foreach (var property in literals)
            {
                var name = $"{uid}.{ResourceProperty.GetValueOrDefault(property, property)}";
                if (!resources.Contains(name))
                {
                    unbacked.Add(name);
                }
            }
        }

        Assert.True(untranslatable.Count == 0,
            "elements with an English literal and no x:Uid — give each one and add the resource:\n  "
            + string.Join("\n  ", untranslatable));
        Assert.True(unbacked.Count == 0,
            "x:Uid properties with no entry in Resources.resw:\n  " + string.Join("\n  ", unbacked));
    }

    /// <summary>
    /// The other direction: a resource with nothing to apply to is a translator's wasted hour,
    /// and an <c>x:Uid</c> naming a property the element does not have throws when the page loads.
    /// </summary>
    [Fact]
    public void Every_uid_resource_has_an_element_to_apply_to()
    {
        var app = AppDirectory();
        var xaml = File.ReadAllText(Path.Combine(app, "ShellPage.xaml"));
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, attributes) in Elements(xaml))
        {
            if (!attributes.TryGetValue("x:Uid", out var uid))
            {
                continue;
            }
            foreach (var property in Properties.Where(attributes.ContainsKey))
            {
                wanted.Add($"{uid}.{ResourceProperty.GetValueOrDefault(property, property)}");
            }
        }

        var orphaned = ResourceNames(app)
            .Where(name => name.Contains('.', StringComparison.Ordinal) && !wanted.Contains(name))
            .ToList();
        Assert.True(orphaned.Count == 0,
            "x:Uid resources with no element in ShellPage.xaml:\n  " + string.Join("\n  ", orphaned));
    }
}
