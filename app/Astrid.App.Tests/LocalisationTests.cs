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

    /// <summary>
    /// Every XAML file the window's content is made of: the page, and the UserControls and the
    /// shared resource dictionary under <c>Views/</c>. A resource applies to every element
    /// carrying its uid whichever file that element is in, so the rules below hold across all of
    /// them at once. The frame (<c>MainWindow.xaml</c>) carries only the product name.
    /// </summary>
    private static IEnumerable<string> WindowXaml(string app) =>
        Directory.EnumerateFiles(Path.Combine(app, "Views"), "*.xaml", SearchOption.AllDirectories)
            .Prepend(Path.Combine(app, "ShellPage.xaml"))
            .OrderBy(path => path, StringComparer.Ordinal);

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
        var resources = ResourceNames(app);
        var untranslatable = new List<string>();
        var unbacked = new List<string>();

        foreach (var (tag, attributes) in WindowXaml(app).SelectMany(path => Elements(File.ReadAllText(path))))
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
    /// A language is a folder (task b9dd4a25): every folder beside <c>en-US</c> carries every key
    /// <c>en-US</c> has, and nothing else.
    /// </summary>
    /// <remarks>
    /// A key a translator missed falls back to English on a page that is otherwise German, which
    /// nobody notices until a screenshot; a key <c>en-US</c> no longer has is a translation nobody
    /// sees. Placeholders survive too — a <c>{0}</c> lost in translation throws at the moment the
    /// string is formatted. And the two the task singled out: the unassigned mark is one letter,
    /// and every ordinal still has its number.
    /// </remarks>
    [Fact]
    public void Every_language_carries_every_key_task_b9dd4a25()
    {
        var app = AppDirectory();
        var strings = Path.Combine(app, "Strings");
        var english = Words(Path.Combine(strings, "en-US"));
        var folders = Directory.EnumerateDirectories(strings)
            .Where(folder => !Path.GetFileName(folder).Equals("en-US", StringComparison.OrdinalIgnoreCase))
            .OrderBy(folder => folder, StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(folders);

        foreach (var folder in folders)
        {
            var tag = Path.GetFileName(folder);
            var words = Words(folder);

            var missing = english.Keys.Except(words.Keys).OrderBy(key => key, StringComparer.Ordinal).ToList();
            Assert.True(missing.Count == 0, $"{tag} is missing:\n  " + string.Join("\n  ", missing));
            var extra = words.Keys.Except(english.Keys).OrderBy(key => key, StringComparer.Ordinal).ToList();
            Assert.True(extra.Count == 0, $"{tag} has keys en-US does not:\n  " + string.Join("\n  ", extra));

            var empty = words.Where(word => string.IsNullOrWhiteSpace(word.Value) && !string.IsNullOrWhiteSpace(english[word.Key]))
                .Select(word => word.Key).ToList();
            Assert.True(empty.Count == 0, $"{tag} leaves these empty:\n  " + string.Join("\n  ", empty));

            var lostPlaceholders = english
                .SelectMany(word => new[] { "{0}", "{1}", "{2}" }
                    .Where(placeholder => word.Value.Contains(placeholder, StringComparison.Ordinal)
                                          && !words[word.Key].Contains(placeholder, StringComparison.Ordinal))
                    .Select(placeholder => $"{word.Key} lost {placeholder}"))
                .ToList();
            Assert.True(lostPlaceholders.Count == 0, $"{tag}:\n  " + string.Join("\n  ", lostPlaceholders));

            Assert.Equal(1, new System.Globalization.StringInfo(words["tasks_unassigned_mark"]).LengthInTextElements);
            foreach (var ordinal in new[] { "ordinal_st", "ordinal_nd", "ordinal_rd", "ordinal_th" })
            {
                Assert.Contains("{0}", words[ordinal]);
            }
        }
    }

    private static Dictionary<string, string> Words(string folder) =>
        XDocument.Load(Path.Combine(folder, "Resources.resw")).Root!.Elements("data")
            .ToDictionary(
                data => data.Attribute("name")!.Value,
                data => data.Element("value")!.Value,
                StringComparer.Ordinal);

    /// <summary>
    /// The other direction: a resource with nothing to apply to is a translator's wasted hour,
    /// and an <c>x:Uid</c> naming a property the element does not have throws when the page loads.
    /// </summary>
    /// <remarks>
    /// The second half is the one that bit. The framework applies <em>every</em> <c>uid.*</c>
    /// resource to <em>every</em> element carrying the uid, so a <c>TextBlock</c> (Text) and a
    /// <c>ListViewItem</c> (Content) sharing <c>account</c> were each handed the other's
    /// property, and the window threw on load — "Unable to resolve property 'Text' … for Uid
    /// 'account'". So each element carrying a uid must have every property the uid's resources
    /// name, not merely the union across elements.
    /// </remarks>
    [Fact]
    public void Every_uid_resource_has_an_element_to_apply_to()
    {
        var app = AppDirectory();
        var resources = ResourceNames(app);
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        var unsettable = new List<string>();
        foreach (var (tag, attributes) in WindowXaml(app).SelectMany(path => Elements(File.ReadAllText(path))))
        {
            if (!attributes.TryGetValue("x:Uid", out var uid))
            {
                continue;
            }
            var mine = Properties.Where(attributes.ContainsKey)
                .Select(property => $"{uid}.{ResourceProperty.GetValueOrDefault(property, property)}")
                .ToHashSet(StringComparer.Ordinal);
            wanted.UnionWith(mine);
            foreach (var name in resources.Where(name => name.StartsWith(uid + ".", StringComparison.Ordinal)))
            {
                if (!mine.Contains(name))
                {
                    unsettable.Add($"{name} would be applied to <{tag} x:Uid=\"{uid}\">, which does not set it");
                }
            }
        }

        var orphaned = resources
            .Where(name => name.Contains('.', StringComparison.Ordinal) && !wanted.Contains(name))
            .ToList();
        Assert.True(orphaned.Count == 0,
            "x:Uid resources with no element in ShellPage.xaml:\n  " + string.Join("\n  ", orphaned));
        Assert.True(unsettable.Count == 0,
            "a uid shared by elements with different properties throws when the page loads:\n  "
            + string.Join("\n  ", unsettable));
    }
}
