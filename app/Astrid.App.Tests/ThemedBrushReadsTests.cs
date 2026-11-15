using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// A brush read from code asks the element, never the application (task 770625f8).
/// </summary>
/// <remarks>
/// <para>
/// <c>Application.Current.Resources[key]</c> resolves a key in <c>ThemeDictionaries</c> against
/// <c>Application.RequestedTheme</c>, which this app never sets — the appearance is applied by
/// setting <c>RequestedTheme</c> on ShellPage's root, the only way to change it after startup. So
/// every XAML <c>ThemeResource</c> follows the element while every lookup from code goes on
/// answering with the <c>Default</c> dictionary, which is the light one. <c>ThemeBrushes</c> is the
/// lookup that asks the right question; see its own remarks.
/// </para>
/// <para>
/// This is a source-level check because there is nothing else available. The test project is plain
/// <c>net9.0</c> and does not reference <c>Astrid.App</c> — see its csproj: a test project that has
/// to start the Windows App SDK is one that gets skipped on the machine where it matters — so a
/// converter cannot be instantiated here and a dark-theme assertion cannot be made at all. It is
/// the same reason <see cref="DesignTokenTests"/> and <see cref="LocalisationTests"/> read the XAML
/// as XML.
/// </para>
/// <para>
/// It is inverted on purpose: safe by default, rather than a list of the keys known to be themed.
/// A themed key read from code fails quietly — in a light theme the two answers agree and nothing
/// looks wrong, which is how the chat bubbles drew white text on the light theme's near-white
/// <c>AstridBgSelected</c> for as long as they did. A list of offenders would have to be extended
/// by whoever added the next brush to a theme dictionary, which is exactly the person not thinking
/// about this. So every key is an offender until <see cref="SafeToReadFromTheApp"/> says why it is
/// not.
/// </para>
/// </remarks>
public sealed class ThemedBrushReadsTests
{
    private static readonly XNamespace Ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// The resources the shell may read straight off the application, and why each one is safe.
    /// </summary>
    /// <remarks>
    /// A resource belongs here when it cannot differ between appearances: it is outside
    /// <c>App.xaml</c>'s <c>ThemeDictionaries</c> and nothing in a theme dictionary shadows it. A
    /// brush that is one value in both themes today but might not be tomorrow does not belong
    /// here — it belongs in the theme dictionaries, stated twice, and read through
    /// <c>ThemeBrushes</c>.
    /// </remarks>
    private static readonly Dictionary<string, string> SafeToReadFromTheApp = new()
    {
        ["AstridAccentBrush"] = "App.xaml's root dictionary: the brand accent, one value in every appearance",
        ["AstridOceanBrush"] = "App.xaml's root dictionary: the Ocean wash, which is what Ocean IS",
        ["AccentButtonStyle"] = "a WinUI Style, not a brush; its own setters resolve per theme",
    };

    /// <summary>Every <c>.cs</c> file the shell is built from, skipping the generated trees.</summary>
    private static IEnumerable<string> ShellSources() =>
        Directory
            .EnumerateFiles(LocalisationTests.AppDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relative = Path.GetRelativePath(LocalisationTests.AppDirectory(), path);
                var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return !parts.Contains("obj") && !parts.Contains("bin");
            });

    /// <summary>Every key in <c>App.xaml</c>, and whether it sits in a theme dictionary.</summary>
    private static (HashSet<string> Themed, HashSet<string> Root) AppKeys()
    {
        var app = XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "App.xaml")).Root!;
        var dictionaries = app.Descendants(Ui + "ResourceDictionary.ThemeDictionaries").Single();
        var themed = dictionaries
            .Descendants()
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .OfType<string>()
            .ToHashSet();

        // Everything else keyed anywhere in App.xaml, minus the theme dictionaries' own names.
        var root = app
            .Descendants()
            .Where(element => !element.Ancestors().Contains(dictionaries) && element != dictionaries)
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .OfType<string>()
            .Where(key => !themed.Contains(key))
            .ToHashSet();

        return (themed, root);
    }

    /// <summary>
    /// No shell code reads a resource off the application unless it is one that cannot be themed.
    /// </summary>
    [Fact]
    public void A_brush_read_from_code_never_asks_the_application_task_770625f8()
    {
        var offenders = new List<string>();

        foreach (var path in ShellSources())
        {
            var lines = File.ReadAllLines(path);
            var text = string.Join("\n", lines);

            // The read, and everything up to the closing bracket — which covers the ternary form,
            // `Resources[mine ? "A" : "B"]`, where two keys hide behind one lookup.
            foreach (var read in Regex.Matches(text, @"Application\.Current\.Resources\[(?<keys>[^\]]*)\]"))
            {
                var match = (Match)read;
                var line = text[..match.Index].Count(c => c == '\n') + 1;
                foreach (var literal in Regex.Matches(match.Groups["keys"].Value, @"""(?<key>[^""]+)"""))
                {
                    var key = ((Match)literal).Groups["key"].Value;
                    if (!SafeToReadFromTheApp.ContainsKey(key))
                    {
                        offenders.Add(
                            $"{Path.GetFileName(path)}:{line} reads \"{key}\" from the application; "
                            + "ask the element instead (ThemeBrushes), or say in SafeToReadFromTheApp why it cannot be themed");
                    }
                }
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// Every Astrid brush key named in shell code is one <c>App.xaml</c> actually defines.
    /// </summary>
    /// <remarks>
    /// An unknown key does not throw: <c>ThemeBrushes</c> hands back gray, and a misspelling shows
    /// up as a grey shape somebody has to notice. XAML cannot make this mistake — an unresolvable
    /// <c>ThemeResource</c> fails the build — so only code needs saying.
    /// </remarks>
    [Fact]
    public void Every_brush_key_named_in_code_is_one_App_xaml_defines_task_770625f8()
    {
        var (themed, root) = AppKeys();
        var unknown = new List<string>();

        foreach (var path in ShellSources())
        {
            if (Path.GetFileName(path) == "ThemeBrushes.cs")
            {
                // Its own remarks name keys as examples, and it is the thing being described.
                continue;
            }

            foreach (var literal in Regex.Matches(File.ReadAllText(path), @"""(?<key>Astrid[A-Za-z]+)"""))
            {
                var key = ((Match)literal).Groups["key"].Value;
                if (!themed.Contains(key) && !root.Contains(key))
                {
                    unknown.Add($"{Path.GetFileName(path)} names \"{key}\", which App.xaml does not define");
                }
            }
        }

        Assert.Empty(unknown);
    }
}
