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

    /// <summary>
    /// No view writes, by hand, a uniform margin or padding that is a step of the space scale
    /// (task 2bd2ea4f): since ADEV-215 the dictionary emits each step as a Thickness too.
    /// </summary>
    /// <remarks>
    /// Only the one-number form. A <c>"0,8,0,0"</c> has no token to name, and off-scale numbers
    /// are left alone here as they are in the gap and radius checks above.
    /// </remarks>
    [Fact]
    public void No_view_restates_a_uniform_inset_task_2bd2ea4f()
    {
        var space = SpaceSteps();
        var restated = new List<string>();
        foreach (var (file, text) in ViewTexts().Append(("ShellResources.xaml",
                     File.ReadAllText(Path.Combine(LocalisationTests.AppDirectory(), "Views", "ShellResources.xaml")))))
        {
            foreach (Match match in Regex.Matches(text, @"\b(?<property>Margin|Padding)=""(?<value>[0-9.]+)""|<Setter Property=""(?<property>Margin|Padding)"" Value=""(?<value>[0-9.]+)"""))
            {
                if (space.TryGetValue(match.Groups["value"].Value, out var key))
                {
                    restated.Add($"{file}: {match.Value} is {{StaticResource {key.Replace("Space", "Inset")}}}");
                }
            }
        }
        Assert.True(restated.Count == 0, string.Join(Environment.NewLine, restated));
    }

    /// <summary>
    /// The general compact type steps — label, detail body, row meta, row title — by size, each
    /// with the generated key a view names instead (task 737b31a8).
    /// </summary>
    /// <remarks>
    /// Not the Markdown headings: a 15 in a view is not an h2, and naming it one would be a lie the
    /// next re-vendor acts on. Where two steps share a size (label and row meta are both 11) the
    /// label is the general one. Row title's 14 is left out on purpose: every 14 in the shell today
    /// is a glyph or a heading the register gives no step, so mapping it would only mislabel them.
    /// </remarks>
    private static Dictionary<string, string> TypeSteps()
    {
        var compact = DesignTokenTests.Tokens().GetProperty("type").GetProperty("compact");
        string Size(string step) =>
            compact.GetProperty(step).GetProperty("size").GetDouble().ToString(CultureInfo.InvariantCulture);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Size("label")] = "AstridTypeCompactLabelSize",
            [Size("detail-body")] = "AstridTypeCompactDetailBodySize",
        };
    }

    /// <summary>
    /// No text in a view writes, by hand, a size that is a general compact type step (task 737b31a8).
    /// </summary>
    /// <remarks>
    /// A glyph's size is an icon's, not type: a <c>FontIcon</c>, a <c>FontIcon</c> style's setter,
    /// and a text element in the symbol font are skipped. An off-scale size (a 12, a 15) is left for
    /// a by-eye pass, as the radius and space checks above leave theirs.
    /// </remarks>
    [Fact]
    public void No_text_restates_a_compact_type_step_task_737b31a8()
    {
        var steps = TypeSteps();
        var restated = new List<string>();
        foreach (var (file, text) in ViewTexts().Append(("ShellResources.xaml",
                     File.ReadAllText(Path.Combine(LocalisationTests.AppDirectory(), "Views", "ShellResources.xaml")))))
        {
            foreach (Match tag in Regex.Matches(text, @"<(?<name>[\w:.]+)\b(?<attrs>[^>]*?)>", RegexOptions.Singleline))
            {
                var attrs = tag.Groups["attrs"].Value;
                if (tag.Groups["name"].Value == "FontIcon" || Regex.IsMatch(attrs, "SymbolThemeFontFamily|Segoe Fluent Icons|Segoe MDL2"))
                {
                    continue;
                }
                var size = Regex.Match(attrs, @"\bFontSize=""(?<value>[0-9.]+)""");
                if (size.Success && steps.TryGetValue(size.Groups["value"].Value, out var key))
                {
                    restated.Add($"{file}: <{tag.Groups["name"].Value} {size.Value}> is {{StaticResource {key}}}");
                }
            }
            foreach (Match style in Regex.Matches(text, @"<Style\b[^>]*?TargetType=""(?<target>[^""]+)""[^>]*>.*?</Style>", RegexOptions.Singleline))
            {
                if (style.Groups["target"].Value.Contains("FontIcon", StringComparison.Ordinal))
                {
                    continue;
                }
                foreach (Match setter in Regex.Matches(style.Value, @"<Setter Property=""FontSize"" Value=""(?<value>[0-9.]+)"""))
                {
                    if (steps.TryGetValue(setter.Groups["value"].Value, out var key))
                    {
                        restated.Add($"{file}: {setter.Value} is {{StaticResource {key}}}");
                    }
                }
            }
        }
        Assert.True(restated.Count == 0, string.Join(Environment.NewLine, restated.Distinct()));
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
        // The type steps too (task 737b31a8): the same load-order reason, the same two rules.
        var layout = LayoutKeys()
            .Concat(GeneratedKeys().Where(key => key.StartsWith("AstridType", StringComparison.Ordinal)
                || key.StartsWith("AstridInset", StringComparison.Ordinal)))
            .ToHashSet(StringComparer.Ordinal);

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
