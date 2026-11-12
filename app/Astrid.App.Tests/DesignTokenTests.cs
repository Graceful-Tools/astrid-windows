using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// Every themed brush in <c>App.xaml</c> is a design token, and says which one (task b4807cb5).
/// </summary>
/// <remarks>
/// <para>
/// astrid-core's <c>design/tokens.json</c> is the one source for how Astrid looks, and each client
/// takes a value by re-vendoring at a revision — "a value never changes in a client's copy". This
/// shell had no copy to change: the brushes were hand-transcribed from astrid-web's
/// <c>styles/themes/*.css</c> with a comment saying so and nothing comparing them. Which is how
/// the accent sat at <c>#3B82F6</c> here after iOS, Mac and the shared tokens had all moved to
/// <c>#2563EB</c> (DS26: a fill that carries a label is dark enough for it).
/// </para>
/// <para>
/// So <c>design/tokens.resolved.json</c> is vendored beside <c>design/REVISION</c>, and this reads
/// it. There is no Windows emitter in astrid-core yet; its <c>design/README.md</c> names exactly
/// this arrangement until there is. The failure message carries the token name and both values,
/// because the point is that the next re-vendor is a diff to read rather than a hunt.
/// </para>
/// <para>
/// <see cref="Exceptions"/> is the honest part. Four brushes still follow web's CSS rather than the
/// tokens, and the priority palette follows the checkbox art; each is there with a reason, and a
/// fifth cannot appear without somebody writing one.
/// </para>
/// </remarks>
public sealed class DesignTokenTests
{
    private static readonly XNamespace Ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The brush in <c>App.xaml</c>, and the token in each theme it is drawn from.</summary>
    /// <remarks>
    /// <c>AstridSurface</c> and <c>AstridSurfaceHover</c> are the card and the card's hover, which
    /// are different tokens in a light theme and a dark one — astrid-core's DS9: the card is
    /// <c>bg-hover</c> on light, <c>bg-secondary</c> on dark.
    /// </remarks>
    private static readonly (string Brush, string LightToken, string DarkToken)[] Brushes =
    [
        ("AstridSurface", "bg-hover", "bg-secondary"),
        ("AstridSurfaceHover", "bg-tertiary", "bg-tertiary"),
        ("AstridBorder", "border", "border"),
        ("AstridBorderHover", "border-hover", "border-hover"),
        ("AstridBgSelected", "bg-selected", "bg-selected"),
        ("AstridBgSelectedBorder", "bg-selected-border", "bg-selected-border"),
        ("AstridBgHover", "bg-hover", "bg-hover"),
        ("AstridTextPrimary", "text-primary", "text-primary"),
        ("AstridTextSecondary", "text-secondary", "text-secondary"),
        ("AstridTextMuted", "text-muted", "text-muted"),
        ("AstridAccentHover", "accent-hover", "accent-hover"),
        ("AstridAccentInk", "accent-ink", "accent-ink"),
        ("AstridBorderFocus", "border-focus", "border-focus"),
        ("AstridError", "error", "error"),
        ("AstridErrorInk", "error-ink", "error-ink"),
    ];

    /// <summary>The WinUI brushes an accent button is drawn from, and the token each one takes.</summary>
    private static readonly (string Brush, string Token)[] AccentButton =
    [
        ("AccentFillColorDefaultBrush", "accent"),
        ("AccentFillColorSecondaryBrush", "accent-hover"),
        ("TextOnAccentFillColorPrimaryBrush", "accent-text"),
    ];

    /// <summary>
    /// The brushes that do not follow the token, and the reason each one does not.
    /// </summary>
    /// <remarks>
    /// All of them are astrid-web's values, transcribed before this file existed and still
    /// agreeing with web to the byte. That is web-to-token drift, not Windows-to-web drift, and it
    /// is web's to resolve when it vendors; DS26 names Light's two selection tints among the values
    /// that stay. Keeping them listed means the brushes that DO follow are checked.
    /// </remarks>
    private static readonly Dictionary<(string Theme, string Brush), string> Exceptions = new()
    {
        [("light", "AstridBorder")] = "web's --theme-border: 235, 237, 240",
        [("dark", "AstridBorder")] = "web's --theme-border: 55, 65, 78",
        [("light", "AstridBorderHover")] = "web's --theme-border-hover: 215, 218, 224",
        [("dark", "AstridBorderHover")] = "web's --theme-border-hover: 90, 100, 114",
        [("light", "AstridBgSelected")] = "web's --theme-bg-selected: 243, 248, 255; a tint, DS26 leaves it",
        [("light", "AstridBgSelectedBorder")] = "web's --theme-bg-selected-border: 200, 224, 255; a tint",
    };

    private static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(LocalisationTests.AppDirectory(), "..", ".."));

    private static JsonElement Tokens() =>
        JsonDocument
            .Parse(File.ReadAllText(Path.Combine(RepoRoot(), "design", "tokens.resolved.json")))
            .RootElement;

    /// <summary>A token's value, following a <c>{brand.x}</c> reference to the brand it names.</summary>
    private static string Token(JsonElement tokens, string theme, string name)
    {
        var value = tokens.GetProperty("themes").GetProperty(theme).GetProperty("colors")
            .GetProperty(name).GetString()!;
        var reference = Regex.Match(value, @"^\{brand\.(?<key>[a-z-]+)\}$");
        var resolved = reference.Success
            ? tokens.GetProperty("brand").GetProperty(reference.Groups["key"].Value).GetString()!
            : value;
        return resolved.ToUpperInvariant();
    }

    /// <summary>Every <c>SolidColorBrush</c> in one of App.xaml's theme dictionaries.</summary>
    private static Dictionary<string, string> ThemeBrushes(string dictionary)
    {
        var app = XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "App.xaml")).Root!;
        var themed = app.Descendants(Ui + "ResourceDictionary.ThemeDictionaries").Single();
        var wanted = themed.Elements(Ui + "ResourceDictionary")
            .Single(entry => (string?)entry.Attribute(Xaml + "Key") == dictionary);
        return wanted.Elements(Ui + "SolidColorBrush")
            .ToDictionary(
                brush => (string)brush.Attribute(Xaml + "Key")!,
                brush => ((string)brush.Attribute("Color")!).ToUpperInvariant());
    }

    /// <summary>
    /// <c>Default</c> and <c>Light</c> are the same dictionary twice — WinUI resolves
    /// <c>Default</c> when the system reports no preference — so both are checked against the
    /// light theme.
    /// </summary>
    [Theory]
    [InlineData("Default", "light")]
    [InlineData("Light", "light")]
    [InlineData("Dark", "dark")]
    public void Every_themed_brush_is_the_token_it_claims_task_b4807cb5(string dictionary, string theme)
    {
        var tokens = Tokens();
        var brushes = ThemeBrushes(dictionary);
        var wrong = new List<string>();

        foreach (var (brush, lightToken, darkToken) in Brushes)
        {
            var name = theme == "dark" ? darkToken : lightToken;
            Assert.True(brushes.ContainsKey(brush), $"{dictionary} has no {brush}, which should be {name}");

            if (Exceptions.ContainsKey((theme, brush)))
            {
                continue;
            }

            var expected = Token(tokens, theme, name);
            if (brushes[brush] != expected)
            {
                wrong.Add($"{dictionary}/{brush}: {brushes[brush]}, but {theme}.{name} is {expected}");
            }
        }

        Assert.Empty(wrong);
    }

    /// <summary>
    /// A brush that drifted is one thing; a brush nobody noticed is another. Everything in a theme
    /// dictionary is a token or an accent-button override, never an invention.
    /// </summary>
    [Theory]
    [InlineData("Default")]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void No_themed_brush_is_outside_the_token_table_task_b4807cb5(string dictionary)
    {
        var known = Brushes.Select(entry => entry.Brush)
            .Concat(AccentButton.Select(entry => entry.Brush))
            .ToHashSet(StringComparer.Ordinal);

        var unknown = ThemeBrushes(dictionary).Keys.Where(brush => !known.Contains(brush)).ToList();

        Assert.Empty(unknown);
    }

    /// <summary>
    /// The accent fill is the same in every theme (DS26), so it is stated once outside the theme
    /// dictionaries — and it is the fill, not the old blue and not the ink.
    /// </summary>
    [Fact]
    public void The_accent_brush_is_the_brands_accent_task_b4807cb5()
    {
        var app = XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "App.xaml")).Root!;
        var accent = app.Descendants(Ui + "SolidColorBrush")
            .Single(brush => (string?)brush.Attribute(Xaml + "Key") == "AstridAccentBrush");
        var colour = app.Descendants(Ui + "Color")
            .Single(entry => (string?)entry.Attribute(Xaml + "Key") == "AstridAccentColor");

        var expected = Tokens().GetProperty("brand").GetProperty("accent").GetString()!.ToUpperInvariant();
        Assert.Equal(expected, ((string)accent.Attribute("Color")!).ToUpperInvariant());

        // The same value as a Color as well, because a tint needs one: a SolidColorBrush takes an
        // opacity off a colour, not off another brush.
        Assert.Equal(expected, colour.Value.Trim().ToUpperInvariant());
    }

    /// <summary>
    /// WinUI would otherwise draw an accent button from the machine's accent and, on a dark theme,
    /// as a pale fill with a near-black label — the thing DS26 exists to stop.
    /// </summary>
    [Theory]
    [InlineData("Default", "light")]
    [InlineData("Light", "light")]
    [InlineData("Dark", "dark")]
    public void An_accent_button_is_the_tokens_fill_and_its_label_is_white_task_b4807cb5(
        string dictionary,
        string theme)
    {
        var tokens = Tokens();
        var brushes = ThemeBrushes(dictionary);

        foreach (var (brush, token) in AccentButton)
        {
            Assert.True(brushes.ContainsKey(brush), $"{dictionary} has no {brush}, which should be {token}");
            Assert.Equal(Token(tokens, theme, token), brushes[brush]);
        }
    }

    /// <summary>
    /// The accent fill is never drawn as text or as a line (astrid-core DS25, DS26).
    /// </summary>
    /// <remarks>
    /// This is the rule that is easy to lose: `AstridAccentBrush` is the right answer for a button
    /// and the wrong one for a label, a glyph or a focus ring, and the two are one character apart.
    /// Web found it the expensive way — reading the single mid-tone as text dropped its links to
    /// 3.68:1 — so the shell is checked rather than trusted. A `Foreground` wants
    /// `AstridAccentInk`; a focus line wants `AstridBorderFocus`, which goes lighter on a dark
    /// theme where the fill is 2.84:1 against a card.
    /// </remarks>
    [Fact]
    public void The_accent_fill_is_never_text_or_a_line_task_b4807cb5()
    {
        var app = LocalisationTests.AppDirectory();
        var misread = new List<string>();

        // The curated source list, not every *.xaml under the project: a recursive sweep also
        // finds the copies under obj/ and bin/, which carry whatever the last build saw and
        // reported three files that do not contain the string at all.
        foreach (var path in LocalisationTests.WindowXaml(app).Append(Path.Combine(app, "App.xaml")))
        {
            var xaml = File.ReadAllText(path);

            foreach (var property in new[] { "Foreground", "FocusVisualPrimaryBrush", "BorderBrush" })
            {
                // Set on the element, and set through a Setter in a Style — the second form is how
                // the focus ring is set, so a check that only knew the first would have passed
                // while every focus line in the shell was still the fill.
                var reference = @"""\{\w+Resource\s+AstridAccentBrush\}""";
                var patterns = new[]
                {
                    $@"(?<![\w.]){property}\s*=\s*{reference}",
                    $@"Property\s*=\s*""{property}""\s+Value\s*=\s*{reference}",
                };

                if (patterns.Any(pattern => Regex.IsMatch(xaml, pattern)))
                {
                    misread.Add($"{Path.GetFileName(path)}: {property} is the accent fill, not an ink");
                }
            }
        }

        Assert.Empty(misread);
    }

    /// <summary>
    /// The vendored copy says which revision it is, as a full sha. A short one is ambiguous as
    /// astrid-core's history grows, which is the rule its own CONSUMERS.md states for the pins.
    /// </summary>
    [Fact]
    public void The_vendored_tokens_name_their_revision_task_b4807cb5()
    {
        var revision = File.ReadAllText(Path.Combine(RepoRoot(), "design", "REVISION")).Trim();

        Assert.Matches("^[0-9a-f]{40}$", revision);
    }
}
