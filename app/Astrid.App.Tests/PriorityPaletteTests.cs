using System.Text.RegularExpressions;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// A priority is the shared token's colour, in the theme the reader is in (task 82e4ad32).
/// </summary>
/// <remarks>
/// <para>
/// These tests used to read <c>Assets/Checkboxes/check_box_{0..3}.png</c> and assert that
/// <see cref="PriorityPalette"/>'s four hexes matched the border pixel. That was the right test for
/// task 204c9d98, where the bug was the picker saying green while every row said blue, and the
/// images were the thing neither side could restyle. It is the wrong test now: the images were
/// never a design token, their four values disagree with astrid-core's (<c>#328ACC</c> against
/// <c>priority-low</c>'s <c>#2563EB</c>), and pinning to them pinned the drift in place.
/// </para>
/// <para>
/// So the palette names a themed <b>brush</b> rather than carrying a colour, and the colour lives
/// in <c>App.xaml</c>'s theme dictionaries where the vendored tokens can be compared to it. That
/// indirection is not ceremony: <c>priority-low</c> is an alias resolving to <c>{brand.accent}</c>
/// on light and ocean but <c>{brand.accent-ink-dark}</c> on dark, so a priority is not four
/// constants and a <c>const string</c> cannot express it.
/// </para>
/// </remarks>
public sealed class PriorityPaletteTests
{
    /// <summary>Each priority, the brush it draws in, and the token that brush takes.</summary>
    private static readonly (int Level, string Brush, string Token)[] Priorities =
    [
        (0, "AstridPriorityNone", "priority-none"),
        (1, "AstridPriorityLow", "priority-low"),
        (2, "AstridPriorityMedium", "priority-medium"),
        (3, "AstridPriorityHigh", "priority-high"),
    ];

    /// <summary>
    /// The four colours are the vendored tokens, per theme.
    /// </summary>
    /// <remarks>
    /// Written out here rather than left to <c>DesignTokenTests.Brushes</c>, which also covers them:
    /// the way these values escaped before was that nothing named them, so this fails even if a
    /// future edit drops their rows from that table.
    /// </remarks>
    [Theory]
    [InlineData("Default", "light")]
    [InlineData("Light", "light")]
    [InlineData("Dark", "dark")]
    public void Priority_colours_are_the_shared_tokens_per_theme_task_82e4ad32(
        string dictionary,
        string theme)
    {
        var tokens = DesignTokenTests.Tokens();
        var brushes = DesignTokenTests.ThemeBrushes(dictionary);
        var wrong = new List<string>();

        foreach (var (_, brush, token) in Priorities)
        {
            Assert.True(brushes.ContainsKey(brush), $"{dictionary} has no {brush}, which is {token}");

            var expected = DesignTokenTests.Token(tokens, theme, token);
            if (brushes[brush] != expected)
            {
                wrong.Add($"{dictionary}/{brush}: {brushes[brush]}, but {theme}.{token} is {expected}");
            }
        }

        Assert.Empty(wrong);
    }

    /// <summary>
    /// Low is a different colour in a dark theme, which is the whole reason for the indirection.
    /// </summary>
    /// <remarks>
    /// The mid-tone accent is 2.84:1 against a dark card — the same reason <c>AstridBorderFocus</c>
    /// goes lighter there (DS25). A test that only checked light would pass with the dark value
    /// wrong and the mark unreadable, which is the failure this class exists to stop.
    /// </remarks>
    [Fact]
    public void Low_priority_is_lighter_on_a_dark_theme_task_82e4ad32()
    {
        var light = DesignTokenTests.ThemeBrushes("Light")["AstridPriorityLow"];
        var dark = DesignTokenTests.ThemeBrushes("Dark")["AstridPriorityLow"];

        Assert.NotEqual(light, dark);
        Assert.Equal(DesignTokenTests.Token(DesignTokenTests.Tokens(), "light", "priority-low"), light);
        Assert.Equal(DesignTokenTests.Token(DesignTokenTests.Tokens(), "dark", "priority-low"), dark);
    }

    /// <summary>The palette answers with a brush key, so the theme decides the colour.</summary>
    [Theory]
    [InlineData(0, "AstridPriorityNone")]
    [InlineData(1, "AstridPriorityLow")]
    [InlineData(2, "AstridPriorityMedium")]
    [InlineData(3, "AstridPriorityHigh")]
    public void The_palette_names_a_themed_brush_task_82e4ad32(int priority, string brush)
    {
        Assert.Equal(brush, PriorityPalette.Key(priority));
    }

    /// <summary>
    /// A level this build does not know draws as unmarked, rather than as whichever colour a
    /// fallthrough happened to land on.
    /// </summary>
    [Fact]
    public void An_unknown_level_draws_as_no_priority()
    {
        Assert.Equal(PriorityPalette.None, PriorityPalette.Key(4));
        Assert.Equal(PriorityPalette.None, PriorityPalette.Key(-1));
    }

    /// <summary>
    /// No colour is written in the palette at all (task 82e4ad32).
    /// </summary>
    /// <remarks>
    /// The task's own wording: "with no sampled hex left in <c>PriorityPalette.cs</c>". A hex
    /// retyped into that file would be a value living in a client's copy, which astrid-core's
    /// <c>design/README.md</c> forbids outright — and it would pass every other test here, because
    /// the brushes it checks are in <c>App.xaml</c>. So the file is read.
    /// </remarks>
    [Fact]
    public void No_colour_is_written_in_the_palette_task_82e4ad32()
    {
        var source = File.ReadAllText(Path.Combine(
            DesignTokenTests.RepoRoot(), "app", "Astrid.Core.Bindings", "PriorityPalette.cs"));

        // The doc comments explain the move and name the old values, so only code is read.
        var code = string.Join(
            "\n",
            source.Split('\n').Where(line => !line.TrimStart().StartsWith("///", StringComparison.Ordinal)));

        Assert.DoesNotMatch(new Regex("#[0-9A-Fa-f]{6}"), code);
    }
}
