using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The board's capsules follow astrid-core's register (task <c>ec8cab8a</c>). This file holds the
/// Done pill, the DS33 pill in Done's meaning, and the helpers <c>BoardHeaderBadgeTests</c>
/// shares.
/// </summary>
/// <remarks>
/// The numbers are read from the vendored <c>tokens.resolved.json</c>, not typed here, so a
/// re-vendor that moves the pill's wash or padding fails this until the styles follow. The styles
/// themselves are read out of the XAML, because the shell's styles are not reachable from a
/// test without a running app.
/// </remarks>
public sealed class BoardBadgeStyleTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    internal static string AppPath(params string[] parts) =>
        Path.Combine([DesignTokenTests.RepoRoot(), "app", "Astrid.App", .. parts]);

    /// <summary>A style's setters, property → value, out of <c>Views/ShellResources.xaml</c>.</summary>
    internal static Dictionary<string, string> Setters(string key)
    {
        var style = XDocument.Load(AppPath("Views", "ShellResources.xaml"))
            .Descendants()
            .SingleOrDefault(element => element.Name.LocalName == "Style"
                && (string?)element.Attribute(Xaml + "Key") == key);
        Assert.True(style is not null, $"ShellResources.xaml has no style {key}");
        return style!.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .ToDictionary(
                setter => (string)setter.Attribute("Property")!,
                setter => (string)setter.Attribute("Value")!,
                StringComparer.Ordinal);
    }

    /// <summary>A brush stated in <c>App.xaml</c>, by key.</summary>
    private static XElement AppBrush(string key)
    {
        var brush = XDocument.Load(AppPath("App.xaml"))
            .Descendants()
            .SingleOrDefault(element => (string?)element.Attribute(Xaml + "Key") == key);
        Assert.True(brush is not null, $"App.xaml has no resource {key}");
        return brush!;
    }

    private static double Pill(string name) =>
        DesignTokenTests.Tokens().GetProperty("pill").GetProperty(name).GetDouble();

    internal static double Caption1() =>
        DesignTokenTests.Tokens().GetProperty("type").GetProperty("comfortable")
            .GetProperty("caption1").GetProperty("size").GetDouble();

    internal static string Num(double value) => value.ToString(CultureInfo.InvariantCulture);

    [Fact]
    public void TaskEc8cab8a_done_maps_to_the_accent_in_the_vendored_pill_states()
    {
        // The brushes below are the accent BECAUSE the register maps Done to it.
        var meaning = DesignTokenTests.Tokens().GetProperty("pill").GetProperty("states")
            .GetProperty("done").GetString();
        Assert.Equal("accent", meaning);
    }

    [Fact]
    public void TaskEc8cab8a_the_done_pill_wash_and_stroke_are_the_accent_at_the_pill_opacities()
    {
        var fill = AppBrush("AstridDonePillFill");
        Assert.Equal("{StaticResource AstridAccentColor}", (string?)fill.Attribute("Color"));
        Assert.Equal(Num(Pill("fill-opacity")), (string?)fill.Attribute("Opacity"));

        var stroke = AppBrush("AstridDonePillStroke");
        Assert.Equal("{StaticResource AstridAccentColor}", (string?)stroke.Attribute("Color"));
        Assert.Equal(Num(Pill("stroke-opacity")), (string?)stroke.Attribute("Opacity"));
    }

    [Fact]
    public void TaskEc8cab8a_the_done_pill_is_the_ds33_capsule()
    {
        var pill = Setters("DonePillStyle");
        // ThemeResource, not StaticResource: these live in App.xaml's own resources, which a
        // merged dictionary cannot see at parse time (the selection outline is reached the same way).
        Assert.Equal("{ThemeResource AstridDonePillFill}", pill["Background"]);
        Assert.Equal("{ThemeResource AstridDonePillStroke}", pill["BorderBrush"]);
        Assert.Equal(Num(Pill("stroke-width")), pill["BorderThickness"]);
        Assert.Equal($"{Num(Pill("padding-x"))},{Num(Pill("padding-y"))}", pill["Padding"]);
        // The largest radius, which WinUI clamps to half the height: a capsule.
        Assert.Equal("{StaticResource AstridRadiusXLarge}", pill["CornerRadius"]);
    }

    [Fact]
    public void TaskEc8cab8a_the_done_pill_label_is_the_accent_ink_in_caption1()
    {
        var label = Setters("DonePillTextStyle");
        Assert.Equal("{ThemeResource AstridAccentInk}", label["Foreground"]);
        Assert.Equal(Num(Caption1()), label["FontSize"]);
    }
}
