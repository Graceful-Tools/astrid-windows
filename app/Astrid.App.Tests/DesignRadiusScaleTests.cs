using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The corner-radius scale, and the rule that the task detail pane takes every radius from it
/// (task <c>77d17dc8</c>).
/// </summary>
/// <remarks>
/// <para>
/// Jon asked for the Windows design to read more like the Mac's, and most of what separates them
/// turns on which client is the reference: Windows' <c>Astrid*</c> brushes track astrid-web's
/// <c>styles/themes/*.css</c> exactly and it is the Mac's <c>Theme.swift</c> that drifted, so
/// moving Windows' colours toward the Mac would move them away from the design system they were
/// deliberately matched to. The radius scale is the one axis where that argument does not apply.
/// Windows had NO radius scale — not one <c>CornerRadius</c> key in <c>App.xaml</c> or
/// <c>ShellResources.xaml</c> — so there was nothing to diverge from, and web's own base
/// <c>--radius</c> is 8px, which is the scale's middle step either way.
/// </para>
/// <para>
/// The values are the Mac's <c>radiusSmall/Medium/Large/XLarge</c>. Adopting them snapped the two
/// off-scale radii the detail pane had grown — the Won't-do chip's 4 and the comment bubble's 10
/// — onto the nearest step, which is the visible part of this change and the point of having a
/// scale at all: fourteen hand-typed numbers across one file is how a pane drifts, and it is the
/// same failure <see cref="TaskDetailAlignmentTests"/> was written for after the alignment sum
/// had been retyped eleven times.
/// </para>
/// <para>
/// Read off the XAML rather than asserted against a rendered control, for the reason that file
/// gives: a view-model test cannot see a hard-coded radius, and the UI smoke suite that could is
/// ten minutes and does not run on every gate.
/// </para>
/// </remarks>
public sealed class DesignRadiusScaleTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The scale, by key, in the Mac's values.</summary>
    private static readonly (string Key, double Value)[] Scale =
    [
        ("AstridRadiusSmall", 6d),
        ("AstridRadiusMedium", 8d),
        ("AstridRadiusLarge", 12d),
        ("AstridRadiusXLarge", 16d),
    ];

    private static XElement Resources() =>
        XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "Views", "ShellResources.xaml")).Root!;

    private static string PaneText() =>
        File.ReadAllText(Path.Combine(LocalisationTests.AppDirectory(), "Views", "TaskDetailPane.xaml"));

    /// <summary>
    /// The four steps are declared once each, and carry the Mac's values.
    /// </summary>
    [Fact]
    public void The_radius_scale_is_declared_once_in_the_macs_values_task_77d17dc8()
    {
        var declarations = Resources()
            .Descendants()
            .Where(element => element.Name.LocalName == "CornerRadius")
            .ToList();

        foreach (var (key, value) in Scale)
        {
            var matches = declarations
                .Where(element => (string?)element.Attribute(Xaml + "Key") == key)
                .ToList();
            Assert.Single(matches);
            Assert.Equal(value, double.Parse(matches[0].Value, CultureInfo.InvariantCulture));
        }

        // Nothing else is smuggled into the scale's namespace.
        Assert.Equal(
            Scale.Length,
            declarations.Count(element =>
                ((string?)element.Attribute(Xaml + "Key"))?.StartsWith("AstridRadius", StringComparison.Ordinal) == true));
    }

    /// <summary>
    /// The detail pane states no radius of its own; every one references the scale.
    /// </summary>
    /// <remarks>
    /// The companion to the test above, and the half that actually holds the line: declaring a
    /// scale nobody references would leave the fourteen literals exactly where they were.
    /// </remarks>
    [Fact]
    public void The_detail_pane_takes_every_radius_from_the_scale_task_77d17dc8()
    {
        var literals = Regex
            .Matches(PaneText(), "CornerRadius=\"(?<value>[^\"]*)\"")
            .Select(match => match.Groups["value"].Value)
            .Where(value => !value.Contains("StaticResource", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(literals);
    }
}
