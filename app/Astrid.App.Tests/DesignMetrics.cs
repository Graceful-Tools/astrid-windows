using System.Text.RegularExpressions;

namespace Astrid.App.Tests;

/// <summary>
/// Counts the hand-written colours, sizes and strings in the shell (task <c>d7e381a7</c>).
/// </summary>
/// <remarks>
/// <para>
/// One implementation, two readers: <see cref="DesignMetricsTests"/> ratchets the counts in the
/// gate, and <c>scripts/report-design-metrics.ps1</c> prints them and rewrites the budget. A
/// second counter beside this one would drift from it, which is the failure the whole task is
/// about — so the script runs the test rather than counting for itself.
/// </para>
/// <para>
/// Each count is a number that must not rise, not a rule about any one site. That is deliberate:
/// 346 literal margins cannot be forbidden outright today, so the budget records where the shell
/// is and the gate stops it growing, the same shape as astrid-web's ratchet.
/// </para>
/// </remarks>
internal static class DesignMetrics
{
    /// <summary>A hex colour written into a view instead of taken from the token brushes.</summary>
    private static readonly Regex HexColour = new(@"#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?\b");

    /// <summary>
    /// An attribute whose value is a literal — <c>Margin="8"</c> and not
    /// <c>Margin="{StaticResource …}"</c> or a binding, which are the forms that DO come from the
    /// design system and must not be counted against it.
    /// </summary>
    private static Regex Literal(string property) =>
        new(property + @"=""(?<value>[^""{][^""]*)""");

    /// <summary>The metrics, in the order the report prints them.</summary>
    internal static readonly string[] Names =
    [
        "xaml.hex-colours",
        "xaml.corner-radius",
        "xaml.font-size",
        "xaml.spacing",
        "code.strings",
    ];

    /// <summary>
    /// The views the budget covers: every XAML file the window is made of, less the two the task
    /// exempts — <c>App.xaml</c> and <c>Views/ShellResources.xaml</c>, which are where a colour,
    /// a radius and a size are SUPPOSED to be written down.
    /// </summary>
    internal static IEnumerable<string> Views(string app) =>
        LocalisationTests.WindowXaml(app)
            .Where(path => !Path.GetFileName(path).Equals("ShellResources.xaml", StringComparison.Ordinal)
                && !Path.GetFileName(path).Equals("App.xaml", StringComparison.Ordinal));

    /// <summary>Every C# file of the shell. The view-models are where a string is built.</summary>
    internal static IEnumerable<string> Code(string app) =>
        Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.EndsWith(".g.cs", StringComparison.Ordinal)
                && !path.EndsWith(".g.i.cs", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal);

    /// <summary>
    /// The counts, and for each one the sites behind it so a rise says what raised it.
    /// </summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> Sites(string app)
    {
        var sites = Names.ToDictionary(name => name, _ => (IReadOnlyList<string>)new List<string>(), StringComparer.Ordinal);

        void Add(string metric, string site) => ((List<string>)sites[metric]).Add(site);

        foreach (var path in Views(app))
        {
            var file = Path.GetFileName(path);
            var text = File.ReadAllText(path);

            foreach (Match match in HexColour.Matches(text))
            {
                Add("xaml.hex-colours", $"{file}: {match.Value}");
            }
            foreach (Match match in Literal("CornerRadius").Matches(text))
            {
                Add("xaml.corner-radius", $"{file}: CornerRadius=\"{match.Groups["value"].Value}\"");
            }
            foreach (Match match in Literal("FontSize").Matches(text))
            {
                Add("xaml.font-size", $"{file}: FontSize=\"{match.Groups["value"].Value}\"");
            }
            foreach (var property in (string[])["Margin", "Padding", "Spacing"])
            {
                foreach (Match match in Literal(property).Matches(text))
                {
                    Add("xaml.spacing", $"{file}: {property}=\"{match.Groups["value"].Value}\"");
                }
            }
        }

        foreach (var path in Code(app))
        {
            var file = Path.GetFileName(path);
            foreach (var literal in StringLiterals(File.ReadAllText(path)).Where(IsProse))
            {
                Add("code.strings", $"{file}: \"{literal}\"");
            }
        }

        return sites;
    }

    internal static IReadOnlyDictionary<string, int> Counts(string app) =>
        Sites(app).ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal);

    /// <summary>
    /// Every string literal in a C# file, skipping comments — scanned rather than matched with a
    /// regex because the two mislead each other: a <c>//</c> inside a URL literal is not a comment,
    /// and the prose in a <c>///</c> summary is not a string.
    /// </summary>
    private static IEnumerable<string> StringLiterals(string code)
    {
        for (var index = 0; index < code.Length; index++)
        {
            var character = code[index];

            if (character == '/' && index + 1 < code.Length && code[index + 1] == '/')
            {
                while (index < code.Length && code[index] != '\n')
                {
                    index++;
                }
                continue;
            }

            if (character == '/' && index + 1 < code.Length && code[index + 1] == '*')
            {
                index = code.IndexOf("*/", index + 2, StringComparison.Ordinal);
                if (index < 0)
                {
                    yield break;
                }
                index++;
                continue;
            }

            if (character == '\'')
            {
                index++;
                while (index < code.Length && code[index] != '\'')
                {
                    index += code[index] == '\\' ? 2 : 1;
                }
                continue;
            }

            // A verbatim string: `@"…"`, where `""` is an escaped quote and `\` is not an escape.
            if (character == '@' && index + 1 < code.Length && code[index + 1] == '"')
            {
                var start = index + 2;
                index = start;
                while (index < code.Length)
                {
                    if (code[index] == '"')
                    {
                        if (index + 1 < code.Length && code[index + 1] == '"')
                        {
                            index += 2;
                            continue;
                        }
                        break;
                    }
                    index++;
                }
                yield return code[start..Math.Min(index, code.Length)];
                continue;
            }

            if (character == '"')
            {
                var start = index + 1;
                index = start;
                while (index < code.Length && code[index] != '"' && code[index] != '\n')
                {
                    index += code[index] == '\\' ? 2 : 1;
                }
                yield return code[start..Math.Min(index, code.Length)];
            }
        }
    }

    /// <summary>
    /// A literal that reads as a sentence a user sees, rather than a key, a glyph, a path or a
    /// format id. Two runs of letters with a space between them is the whole test: an identifier
    /// does not have spaces, and prose does.
    /// </summary>
    /// <remarks>
    /// A heuristic, and knowingly so — it is a number to hold down, not a verdict on a line. It
    /// errs toward counting: a false positive makes the budget one higher and costs nothing, while
    /// a false negative is a string that could be hand-written for free.
    /// </remarks>
    private static bool IsProse(string literal)
    {
        if (literal.Length < 4 || !char.IsAsciiLetter(literal[0]) || !literal.Contains(' '))
        {
            return false;
        }
        if (literal.Contains("://", StringComparison.Ordinal) || literal.Contains('\\'))
        {
            return false;
        }
        return Regex.Matches(literal, "[A-Za-z]{2,}").Count >= 2;
    }
}
