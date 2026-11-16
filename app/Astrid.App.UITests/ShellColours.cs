using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Astrid.App.UITests;

/// <summary>
/// The colour the shell declares for a key, in the theme that is on screen (task 1fae8c54).
/// </summary>
/// <remarks>
/// <para>
/// Five of these suites compare a pixel they captured to a colour the shell states, and each had
/// its own copy of this lookup reading <c>App.xaml</c>'s theme dictionaries. Task 1fae8c54 deleted
/// those sixty-six hand-written brushes — <c>App.xaml</c> now merges astrid-core's generated
/// <c>design/AstridTokens.xaml</c> — so all five copies went looking in a file that no longer
/// answers, and four of them failed in under a millisecond with "the Light dictionary has no
/// AstridPriorityNone". One lookup, in one place, is what stops the next move costing five edits.
/// </para>
/// <para>
/// It reads the shell's own declared value rather than restating hexes here on purpose: these
/// tests assert that what was PAINTED is what was DECLARED, and a literal would only ever prove
/// the literal. <c>DesignTokenTests</c> is the other half of that chain — it holds the declared
/// value to astrid-core's tokens — so between them the pixel on screen is tied to the register.
/// </para>
/// </remarks>
internal static class ShellColours
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The nearest directory above the test binary that holds <c>design/</c>.</summary>
    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "design")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName
            ?? throw new InvalidOperationException("no repository root above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// The colour <paramref name="key"/> names, in <paramref name="theme"/>, as a 0xRRGGBB int.
    /// </summary>
    /// <remarks>
    /// The generated dictionary first, because that is where every themed brush lives. Then
    /// anywhere in <c>App.xaml</c>, which still states the values that cannot be themed —
    /// <c>AstridAccentColor</c>, <c>AstridSelectionOutline</c> and the accent ramp — and which the
    /// register deliberately does not carry (<c>design/README.md</c>).
    /// </remarks>
    internal static int Brush(string theme, string key)
    {
        var root = RepositoryRoot();
        var wanted = theme == "dark" ? "Dark" : "Light";

        var tokens = XDocument.Load(Path.Combine(root, "design", "AstridTokens.xaml"));
        var themed = tokens.Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "ResourceDictionary"
                && (string?)element.Attribute(Xaml + "Key") == wanted)
            ?? throw new InvalidOperationException(
                $"the vendored token dictionary has no '{wanted}' theme");

        var app = XDocument.Load(Path.Combine(root, "app", "Astrid.App", "App.xaml"));
        var stated = themed.Elements()
                .FirstOrDefault(element => (string?)element.Attribute(Xaml + "Key") == key)
            ?? app.Descendants()
                .FirstOrDefault(element => (string?)element.Attribute(Xaml + "Key") == key)
            ?? throw new InvalidOperationException(
                $"neither the vendored token dictionary's {wanted} theme nor App.xaml states {key}");

        var value = (string?)stated.Attribute("Color") ?? stated.Value;
        return Convert.ToInt32(value.Trim().TrimStart('#'), 16);
    }
}
