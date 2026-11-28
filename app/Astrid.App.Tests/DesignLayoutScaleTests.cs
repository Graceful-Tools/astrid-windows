using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The views take a gap or a radius from the generated dictionary whenever the dictionary has it
/// (task <c>a7f56c04</c>).
/// </summary>
/// <remarks>
/// <para>
/// <c>design/AstridTokens.xaml</c> is merged into <c>App.xaml</c> and carries the radius scale
/// (<c>AstridRadiusSmall</c>…<c>XLarge</c>) and the space scale (<c>AstridSpace2</c>…<c>48</c>,
/// as <c>x:Double</c>). A view that writes <c>Spacing="8"</c> is restating a step of that scale
/// by hand, which is the copy that drifts the day the register moves a step. So: a literal that IS
/// a step must name the step.
/// </para>
/// <para>
/// What this deliberately does not reach: a <c>Margin</c> or <c>Padding</c> literal. The generator
/// has emitted the space scale as <c>Thickness</c> (<c>AstridInset*</c>) and the <c>layout.*</c>
/// tokens since ADEV-215; the layout tokens are held by the two task <c>5988cf90</c> tests at the
/// foot of this file, and moving the remaining uniform paddings onto <c>AstridInset*</c> is
/// follow-up work. An off-scale literal (a 10, a 14) is left alone here too: snapping it moves
/// pixels.
/// </para>
/// <para>
/// The scales are read from <c>tokens.resolved.json</c>, not typed here, so a re-vendor that adds
/// a step makes every literal of it a failure without this file changing.
/// </para>
/// </remarks>
public sealed class DesignLayoutScaleTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The space scale: value → the generated key that holds it.</summary>
    private static Dictionary<string, string> SpaceSteps() =>
        DesignTokenTests.Tokens()
            .GetProperty("space")
            .EnumerateArray()
            .Select(step => step.GetDouble().ToString(CultureInfo.InvariantCulture))
            .ToDictionary(value => value, value => $"AstridSpace{value}", StringComparer.Ordinal);

    /// <summary>The radius scale: value → the generated key that holds it.</summary>
    private static Dictionary<string, string> RadiusSteps()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sm"] = "AstridRadiusSmall",
            ["md"] = "AstridRadiusMedium",
            ["lg"] = "AstridRadiusLarge",
            ["xl"] = "AstridRadiusXLarge",
        };
        return DesignTokenTests.Tokens()
            .GetProperty("radius")
            .EnumerateObject()
            .ToDictionary(
                step => step.Value.GetDouble().ToString(CultureInfo.InvariantCulture),
                step => names[step.Name],
                StringComparer.Ordinal);
    }

    /// <summary>Every key the generated dictionary declares, outside its theme dictionaries.</summary>
    private static HashSet<string> GeneratedKeys() =>
        XDocument.Load(DesignTokenTests.TokenDictionaryPath())
            .Root!
            .Elements()
            .Select(element => (string?)element.Attribute(Xaml + "Key"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A number as a view writes it: a literal, or a <c>{StaticResource …}</c> naming one of the
    /// generated dictionary's doubles — so a test summing gaps reads the value the page will get.
    /// </summary>
    internal static double Number(string value)
    {
        var reference = Regex.Match(value, @"^\{StaticResource (?<key>\w+)\}$");
        if (!reference.Success)
        {
            return double.Parse(value, CultureInfo.InvariantCulture);
        }
        var key = reference.Groups["key"].Value;
        var declared = XDocument.Load(DesignTokenTests.TokenDictionaryPath())
            .Root!
            .Elements()
            .SingleOrDefault(element => (string?)element.Attribute(Xaml + "Key") == key);
        Assert.True(declared is not null, $"{key} is not in the generated dictionary");
        return double.Parse(declared!.Value, CultureInfo.InvariantCulture);
    }

    private static IEnumerable<(string File, string Text)> ViewTexts() =>
        DesignMetrics.Views(LocalisationTests.AppDirectory())
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)));

    /// <summary>
    /// No view writes, by hand, a gap or a radius that is a step of the generated scale.
    /// </summary>
    [Fact]
    public void No_view_restates_a_step_of_the_space_or_radius_scale_task_a7f56c04()
    {
        var space = SpaceSteps();
        var radius = RadiusSteps();
        var restated = new List<string>();

        foreach (var (file, text) in ViewTexts())
        {
            foreach (Match match in Regex.Matches(text, @"\b(?<property>Spacing|ColumnSpacing|RowSpacing)=""(?<value>[0-9.]+)"""))
            {
                if (space.TryGetValue(match.Groups["value"].Value, out var key))
                {
                    restated.Add($"{file}: {match.Value} is {{StaticResource {key}}}");
                }
            }
            foreach (Match match in Regex.Matches(text, @"\bCornerRadius=""(?<value>[0-9.]+)"""))
            {
                if (radius.TryGetValue(match.Groups["value"].Value, out var key))
                {
                    restated.Add($"{file}: {match.Value} is {{StaticResource {key}}}");
                }
            }
        }

        Assert.True(restated.Count == 0, string.Join(Environment.NewLine, restated));
    }

    /// <summary>
    /// Every scale key a view names is one the generated dictionary declares — a misspelt
    /// <c>AstridSpace7</c> compiles, and fails only when the page loads.
    /// </summary>
    [Fact]
    public void Every_scale_key_a_view_names_is_generated_task_a7f56c04()
    {
        var declared = GeneratedKeys();
        var missing = ViewTexts()
            .SelectMany(view => Regex
                .Matches(view.Text, @"\{StaticResource (?<key>Astrid(?:Space|Radius)\w*)\}")
                .Select(match => (view.File, Key: match.Groups["key"].Value)))
            .Where(use => !declared.Contains(use.Key))
            .Select(use => $"{use.File}: {use.Key}")
            .Distinct()
            .ToList();

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// <c>ShellResources.xaml</c> still declares the radius scale itself — its own styles need it
    /// at load, before the application's merged dictionaries resolve — so it must say exactly what
    /// the generated dictionary says.
    /// </summary>
    [Fact]
    public void The_shells_radius_scale_is_the_generated_one_task_a7f56c04()
    {
        var generated = XDocument.Load(DesignTokenTests.TokenDictionaryPath()).Root!;
        var shell = XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "Views", "ShellResources.xaml")).Root!;

        string? Value(XElement root, string key) =>
            root.Elements()
                .FirstOrDefault(element => element.Name.LocalName == "CornerRadius"
                    && (string?)element.Attribute(Xaml + "Key") == key)
                ?.Value;

        foreach (var key in RadiusSteps().Values)
        {
            Assert.NotNull(Value(generated, key));
            Assert.Equal(Value(generated, key), Value(shell, key));
        }
    }

    /// <summary>The generated <c>layout.*</c> keys: rows, board, detail panel, subtask indent.</summary>
    private static IEnumerable<string> LayoutKeys() =>
        GeneratedKeys().Where(key => Regex.IsMatch(key, "^Astrid(Board|Row|Detail|Subtask)"));

    /// <summary>
    /// The layout tokens the shell has no place for yet, each with why — a list somebody has to
    /// edit, so a token that stops being used is a reviewable act rather than a silent one.
    /// </summary>
    private static readonly Dictionary<string, string> UnusedLayoutKeys = new(StringComparer.Ordinal)
    {
        ["AstridBoardPadding"] =
            "the board is inset 16 on three sides and 0 on top, where the header already gives the "
            + "gap: AstridBoardInset, which a uniform 16 cannot say",
        ["AstridDetailIconColumn"] =
            "Windows' detail pane lays its fields out as headed controls, with no icon column to size",
        ["AstridDetailPanelMinWidth"] =
            "the callout's width is the core's placement answer (rows::callout::place), which this "
            + "shell must not second-guess with a floor of its own",
    };

    /// <summary>
    /// Every <c>layout.*</c> token the generated dictionary emits is drawn somewhere, or listed
    /// above with the reason it is not (task 5988cf90).
    /// </summary>
    /// <remarks>
    /// astrid-core began emitting these for Windows in ADEV-215, and the shell's rows, board,
    /// callout and indent had been writing the same numbers by hand — some of them different
    /// numbers (a 64 row against the token's 52, a 10 card corner against its 6). A token nothing
    /// reads agrees with the register for ever, which is the failure this catches.
    /// </remarks>
    [Fact]
    public void Every_layout_token_is_drawn_somewhere_task_5988cf90()
    {
        var app = LocalisationTests.AppDirectory();
        var sources = Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".xaml", StringComparison.Ordinal)
                || path.EndsWith(".cs", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText)
            .ToList();

        var unused = LayoutKeys()
            .Where(key => !UnusedLayoutKeys.ContainsKey(key))
            .Where(key => !sources.Any(text =>
                text.Contains($"{{StaticResource {key}}}", StringComparison.Ordinal)
                || text.Contains($"\"{key}\"", StringComparison.Ordinal)))
            .ToList();

        Assert.True(unused.Count == 0,
            "layout tokens nothing draws — use them where the shell writes the same thing by hand, "
            + "or list them with a reason: " + string.Join(", ", unused));
        Assert.All(UnusedLayoutKeys.Keys, key => Assert.Contains(key, LayoutKeys()));
    }

    /// <summary>
    /// <c>ShellResources.xaml</c> re-declares a layout token only for its own styles, and then with
    /// the generated value (task 5988cf90).
    /// </summary>
    /// <remarks>
    /// Its styles resolve at load, before the application's merged dictionaries do, so a style
    /// there that wants <c>AstridBoardColumnRadius</c> needs its own copy — the same reason it
    /// carries the radius scale. A view does not: it reaches the generated dictionary through the
    /// application. So a copy nobody in the file uses is a second definition that can only drift,
    /// and a copy that is used has to say exactly what the generated one says.
    /// </remarks>
    [Fact]
    public void The_shell_redeclares_a_layout_token_only_for_its_own_styles_task_5988cf90()
    {
        var generated = XDocument.Load(DesignTokenTests.TokenDictionaryPath()).Root!;
        var path = Path.Combine(LocalisationTests.AppDirectory(), "Views", "ShellResources.xaml");
        var shell = XDocument.Load(path).Root!;
        var text = File.ReadAllText(path);
        var layout = LayoutKeys().ToHashSet(StringComparer.Ordinal);

        var wrong = new List<string>();
        foreach (var copy in shell.Elements())
        {
            if ((string?)copy.Attribute(Xaml + "Key") is not { } key || !layout.Contains(key))
            {
                continue;
            }
            var original = generated.Elements()
                .Single(element => (string?)element.Attribute(Xaml + "Key") == key);
            if (copy.Name.LocalName != original.Name.LocalName || copy.Value.Trim() != original.Value.Trim())
            {
                wrong.Add($"{key} is {copy.Name.LocalName} {copy.Value} here and {original.Name.LocalName} {original.Value} generated");
            }
            if (!text.Contains($"{{StaticResource {key}}}", StringComparison.Ordinal))
            {
                wrong.Add($"{key} is declared here and used by none of this file's styles — the views read the generated one");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
}
