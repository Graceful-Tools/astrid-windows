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
/// What this deliberately does not reach. A <c>Margin</c> or <c>Padding</c> is a <c>Thickness</c>,
/// and the generator emits the space scale only as doubles, so there is nothing for one to
/// reference; and the <c>layout.*</c> tokens (row height, board card and column, detail-panel
/// radius, subtask indent) are in <c>tokens.resolved.json</c> but not emitted for Windows at all.
/// Both are astrid-core's to add. An off-scale literal (a 10, a 14) is left alone here too:
/// snapping it moves pixels, and several are those layout tokens in disguise.
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
}
