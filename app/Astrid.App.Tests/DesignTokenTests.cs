using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
/// There is no exception list. There was one, holding four brushes at astrid-web's older CSS with
/// the reason "DS26 names Light's two selection tints among the values that stay" — and DS26 is the
/// accent and error darkening, not the surfaces. Their row is DS3, which reads "open on Windows", so
/// task 1481f33a took the tokens and the list went with them. The priority palette was the last
/// thing outside this file — sampled from the checkbox art and pinned to those images — until task
/// 82e4ad32 moved it in here too (astrid-core DS2/DS21), which is why the table below carries four
/// <c>AstridPriority*</c> rows and <c>PriorityPaletteTests</c> now names them as well.
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
    ///
    /// <c>AstridPriorityLow</c> is the other one: <c>priority-low</c> is an alias resolving to
    /// <c>{brand.accent}</c> on light and <c>{brand.accent-ink-dark}</c> on dark, which is why the
    /// four priority colours could not stay <c>const string</c>s (task 82e4ad32).
    /// </remarks>
    internal static readonly (string Brush, string LightToken, string DarkToken)[] Brushes =
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
        ("AstridPriorityNone", "priority-none", "priority-none"),
        ("AstridPriorityLow", "priority-low", "priority-low"),
        ("AstridPriorityMedium", "priority-medium", "priority-medium"),
        ("AstridPriorityHigh", "priority-high", "priority-high"),
    ];

    /// <summary>The WinUI brushes an accent button is drawn from, and the token each one takes.</summary>
    private static readonly (string Brush, string Token)[] AccentButton =
    [
        ("AccentFillColorDefaultBrush", "accent"),
        ("AccentFillColorSecondaryBrush", "accent-hover"),
        ("TextOnAccentFillColorPrimaryBrush", "accent-text"),
    ];

    internal static string RepoRoot() =>
        Path.GetFullPath(Path.Combine(LocalisationTests.AppDirectory(), "..", ".."));

    internal static JsonElement Tokens() =>
        JsonDocument
            .Parse(File.ReadAllText(Path.Combine(RepoRoot(), "design", "tokens.resolved.json")))
            .RootElement;

    /// <summary>A token's value, following a <c>{brand.x}</c> reference to the brand it names.</summary>
    internal static string Token(JsonElement tokens, string theme, string name)
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
    internal static Dictionary<string, string> ThemeBrushes(string dictionary)
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

            var expected = Token(tokens, theme, name);
            if (brushes[brush] != expected)
            {
                wrong.Add($"{dictionary}/{brush}: {brushes[brush]}, but {theme}.{name} is {expected}");
            }
        }

        Assert.Empty(wrong);
    }

    /// <summary>
    /// A surface or a border is the token's value (astrid-core DS3), not astrid-web's older CSS.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These four sat in an exception list until task 1481f33a, excused by the reason "DS26 names
    /// Light's two selection tints among the values that stay". DS26 is the accent and error
    /// darkening; the register row that governs a surface or a border is DS3, and it reads "open on
    /// Windows". So the exception was a misread row rather than a decision, and the brushes take the
    /// tokens.
    /// </para>
    /// <para>
    /// Named separately from <see cref="Every_themed_brush_is_the_token_it_claims_task_b4807cb5"/>,
    /// which reads <see cref="Brushes"/>: this one writes the four brushes and their tokens out, so
    /// it still fails if a future edit drops a row from that table. The way these values escaped
    /// before was the covering mechanism, not the values.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Default", "light")]
    [InlineData("Light", "light")]
    [InlineData("Dark", "dark")]
    public void Surfaces_and_borders_are_the_tokens_not_webs_old_css_task_1481f33a(
        string dictionary,
        string theme)
    {
        (string Brush, string Token)[] surfaces =
        [
            ("AstridBorder", "border"),
            ("AstridBorderHover", "border-hover"),
            ("AstridBgSelected", "bg-selected"),
            ("AstridBgSelectedBorder", "bg-selected-border"),
        ];

        var tokens = Tokens();
        var brushes = ThemeBrushes(dictionary);
        var wrong = new List<string>();

        foreach (var (brush, token) in surfaces)
        {
            Assert.True(brushes.ContainsKey(brush), $"{dictionary} has no {brush}, which is {token}");

            var expected = Token(tokens, theme, token);
            if (brushes[brush] != expected)
            {
                wrong.Add($"{dictionary}/{brush}: {brushes[brush]}, but {theme}.{token} is {expected}");
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

    /// <summary>The shared checkbox art, vendored beside the tokens at the same revision.</summary>
    internal static string CheckboxArtPath() =>
        Path.Combine(RepoRoot(), "design", "AstridCheckbox.xaml");

    /// <summary>
    /// The art's own <c>body-sha256</c> header is the hash of everything below it (task 82e4ad32).
    /// </summary>
    /// <remarks>
    /// <para>
    /// astrid-core stamps every generated source file with a hash of its body, and its
    /// <c>design/README.md</c> says what the stamp is for: "A consumer checks that hash in its own
    /// gate, with no access to this repository, so a hand-edited copy fails there." This is that
    /// gate. The tokens have <see cref="The_vendored_tokens_name_their_revision_task_b4807cb5"/>,
    /// which says the copy knows which revision it came from; this says the copy has not been
    /// touched since.
    /// </para>
    /// <para>
    /// It matters more here than for the tokens. <c>tokens.resolved.json</c> is read by these tests
    /// and nothing else, so an edit to it shows up as a brush assertion failing. The art is XAML
    /// that the shell will merge and draw, where a hand-tweaked path or a retyped colour would look
    /// deliberate and never fail anything.
    /// </para>
    /// <para>
    /// <c>\r\n</c> is folded to <c>\n</c> before hashing, which is what astrid-core itself does when
    /// it hashes a file (<c>crates/xtask/src/assets.rs</c>: <c>file_hash</c>). Git checks a text
    /// file out in the platform's endings, so a byte-exact comparison would fail on Windows for a
    /// file nobody had edited — <c>.gitattributes</c> pins this one to LF, and the fold means a
    /// checkout that somehow missed that still gates the content rather than the line endings.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_vendored_checkbox_art_matches_its_declared_hash_task_82e4ad32()
    {
        var text = File.ReadAllText(CheckboxArtPath()).Replace("\r\n", "\n");
        var stamp = Regex.Match(
            text, @"^<!-- body-sha256: (?<hash>[0-9a-f]{64}) -->\n", RegexOptions.Multiline);
        Assert.True(stamp.Success, "the vendored checkbox art carries no body-sha256 header");

        var body = text[(stamp.Index + stamp.Length)..];
        var hashed = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)))
            .ToLowerInvariant();

        Assert.Equal(stamp.Groups["hash"].Value, hashed);
    }

    /// <summary>
    /// The one colour in the art that is not <c>currentColor</c> is the shared token (task 82e4ad32).
    /// </summary>
    /// <remarks>
    /// The box and the repeat frame stroke from <c>{TemplateBinding Foreground}</c>, so whatever
    /// draws them decides their colour and nothing can drift. The tick cannot: it is a fixed
    /// <c>SolidColorBrush</c> inside the dictionary, and it is therefore the one value in this file
    /// that could quietly stop agreeing with every other client. astrid-core publishes it as
    /// <c>checkbox-check</c>, in all three themes, so this reads the vendored tokens rather than
    /// repeating the hex.
    /// </remarks>
    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    [InlineData("ocean")]
    public void The_checkbox_tick_is_the_shared_token_task_82e4ad32(string theme)
    {
        var art = XDocument.Load(CheckboxArtPath()).Root!;
        var tick = art.Elements(Ui + "SolidColorBrush")
            .Single(brush => (string?)brush.Attribute(Xaml + "Key") == "AstridCheckboxCheckBrush");

        Assert.Equal(
            Token(Tokens(), theme, "checkbox-check"),
            ((string)tick.Attribute("Color")!).ToUpperInvariant());
    }

    /// <summary>A number the shell states in XAML because it cannot read the vendored JSON at runtime.</summary>
    /// <remarks>
    /// <c>ShellResources.xaml</c> is where the sizes live — the corner-radius scale is already there
    /// — and it is one of the two files <see cref="DesignMetrics"/> exempts from the literal budget,
    /// for exactly this reason: a number written down once, with something comparing it to its
    /// source, is the opposite of the 346 literal margins that budget counts.
    /// </remarks>
    private static double ShellNumber(string key)
    {
        var dictionary = XDocument
            .Load(Path.Combine(LocalisationTests.AppDirectory(), "Views", "ShellResources.xaml"))
            .Root!;
        var entry = dictionary.Elements()
            .Single(element => (string?)element.Attribute(Xaml + "Key") == key);

        return double.Parse(entry.Value.Trim(), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A selected row is outlined in the accent at the token's opacity, not filled at a hand-typed
    /// one (astrid-core DS5, task 787c7aa5).
    /// </summary>
    /// <remarks>
    /// The sidebar drew selection as a fill at <c>0.15</c>, and its hover and pressed states at
    /// <c>0.22</c> — three numbers that came from astrid-web's CSS and belonged to nothing. DS5 took
    /// the Mac's answer instead (Jon, 2026-11-10): an outline, at <c>state.selection-outline-opacity</c>.
    /// So the brush is stated once beside <c>AstridAccentBrush</c>, outside the theme dictionaries
    /// because the accent does not follow the theme, and this reads its opacity out of the vendored
    /// tokens rather than repeating the number.
    /// </remarks>
    [Fact]
    public void The_selection_outline_is_the_accent_at_the_tokens_opacity_task_787c7aa5()
    {
        var app = XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "App.xaml")).Root!;
        var outline = app.Descendants(Ui + "SolidColorBrush")
            .Single(brush => (string?)brush.Attribute(Xaml + "Key") == "AstridSelectionOutline");

        var expected = Tokens().GetProperty("state").GetProperty("selection-outline-opacity").GetDouble();
        var actual = double.Parse((string)outline.Attribute("Opacity")!, CultureInfo.InvariantCulture);

        Assert.Equal(expected, actual);

        // The accent itself, by reference rather than by a second copy of the hex: the mistake DS26
        // caught was one file moving to #2563EB while another kept #3B82F6.
        Assert.Equal("{StaticResource AstridAccentColor}", (string)outline.Attribute("Color")!);
    }

    /// <summary>
    /// The outline's width is a density lookup, and Windows is the pointer density (DS5, task 787c7aa5).
    /// </summary>
    /// <remarks>
    /// astrid-core's register reads DS5 as "1.5 wide on pointer layouts and 2.5 on touch", and the
    /// tokens carry both: <c>size.compact.selection-outline</c> and
    /// <c>size.comfortable.selection-outline</c>. This shell has no density setting today, so it
    /// takes the compact one — but it takes it from the density rather than from a constant, so the
    /// day a comfortable mode arrives the other value is already there and this test says which one
    /// the shell is using. Asserting the comfortable value is different is the part that would fail
    /// if a re-vendor collapsed the two, which is the change that would make the lookup pointless.
    /// </remarks>
    [Fact]
    public void The_selection_outline_is_the_pointer_densitys_width_task_787c7aa5()
    {
        var size = Tokens().GetProperty("size");
        var compact = size.GetProperty("compact").GetProperty("selection-outline").GetDouble();
        var comfortable = size.GetProperty("comfortable").GetProperty("selection-outline").GetDouble();

        Assert.NotEqual(compact, comfortable);
        Assert.Equal(compact, ShellNumber("AstridSelectionOutlineThickness"));
    }

    /// <summary>
    /// The sidebar states no selection colour of its own any more (DS5, task 787c7aa5).
    /// </summary>
    /// <remarks>
    /// Named apart from the two tests above because they check the value and this checks the
    /// mechanism. The three literals were overrides of WinUI's own
    /// <c>ListViewItemBackgroundSelected*</c> brushes, declared on the sidebar's panel; re-declaring
    /// any one of them would quietly beat the outline the row style now draws, and every assertion
    /// about the brush would still pass.
    /// </remarks>
    [Fact]
    public void The_sidebar_states_no_selection_colour_of_its_own_task_787c7aa5()
    {
        var sidebar = File.ReadAllText(
            Path.Combine(LocalisationTests.AppDirectory(), "Views", "SidebarView.xaml"));

        Assert.DoesNotContain("ListViewItemBackgroundSelected", sidebar, StringComparison.Ordinal);
    }

    /// <summary>
    /// Completing a task pops the mark to the token's scale, on the token's spring (DS16, task 787c7aa5).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>motion.spring</c> is a named entry with its own parameters — a damping ratio and a period —
    /// rather than a duration and an easing curve, and DS16 says to read it instead of approximating
    /// it. WinUI can express it exactly: Composition's <c>SpringVector3NaturalMotionAnimation</c>
    /// takes a <c>DampingRatio</c> and a <c>Period</c>, which are those two numbers. So nothing was
    /// substituted, and there is nothing for astrid-core's register to record.
    /// </para>
    /// <para>
    /// All three are asserted together: a pop at the right scale on the wrong spring, or the right
    /// spring at the wrong scale, is the same bug.
    /// </para>
    /// </remarks>
    [Fact]
    public void Completing_a_task_pops_the_mark_on_the_tokens_spring_task_787c7aa5()
    {
        var tokens = Tokens();
        var spring = tokens.GetProperty("motion").GetProperty("spring");

        Assert.Equal(
            tokens.GetProperty("state").GetProperty("complete-pop-scale").GetDouble(),
            ShellNumber("AstridCompletePopScale"));
        Assert.Equal(spring.GetProperty("damping").GetDouble(), ShellNumber("AstridSpringDamping"));
        Assert.Equal(spring.GetProperty("ms").GetDouble(), ShellNumber("AstridSpringMs"));
    }

    /// <summary>
    /// The mark reads those three numbers rather than carrying its own (task 787c7aa5).
    /// </summary>
    /// <remarks>
    /// A resource nobody looks up is a number that agrees with the tokens and changes nothing on
    /// screen, and the test above would pass for ever while the pop ran at whatever was typed into
    /// <c>PriorityMark</c>. The art is already resolved out of the application's resources there
    /// (<c>AstridCheckboxBoxStyle</c> and the other two), so this is the same route, not a new one.
    /// </remarks>
    [Fact]
    public void The_mark_takes_the_pop_from_the_resources_task_787c7aa5()
    {
        var mark = File.ReadAllText(Path.Combine(LocalisationTests.AppDirectory(), "PriorityMark.cs"));

        foreach (var key in new[] { "AstridCompletePopScale", "AstridSpringDamping", "AstridSpringMs" })
        {
            Assert.Contains(key, mark, StringComparison.Ordinal);
        }
    }
}
