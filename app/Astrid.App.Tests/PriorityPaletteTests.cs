using System.Globalization;
using System.Runtime.Versioning;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// A priority is one colour, and the row agrees with the control that sets it (task 204c9d98).
/// </summary>
/// <remarks>
/// The app drew priority 1 as blue on every row — the shared checkbox image — and green in the
/// picker, which resolved from a hardcoded switch. These tests read the images the app actually
/// ships and assert the palette matches them, so the two cannot drift apart again: swapping an
/// asset without updating the palette fails here rather than on somebody's screen.
/// </remarks>
public sealed class PriorityPaletteTests
{
    /// <summary>Where the shipped marks live, relative to the test binary.</summary>
    private static string AssetPath(int priority)
    {
        var root = AppContext.BaseDirectory;
        for (var depth = 0; depth < 8 && root is not null; depth++)
        {
            var candidate = Path.Combine(
                root, "app", "Astrid.App", "Assets", "Checkboxes", $"check_box_{priority}.png");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            root = Directory.GetParent(root)?.FullName;
        }
        throw new FileNotFoundException($"check_box_{priority}.png was not found above the test binary");
    }

    /// <summary>
    /// The first opaque pixel across the middle of the image: the border stroke, which is the
    /// colour a reader sees as "this task's priority".
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static string BorderHex(int priority)
    {
        using var image = System.Drawing.Image.FromFile(AssetPath(priority));
        using var bitmap = new System.Drawing.Bitmap(image);
        var y = bitmap.Height / 2;
        for (var x = 0; x < bitmap.Width; x++)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.A > 200)
            {
                return $"#{pixel.R:X2}{pixel.G:X2}{pixel.B:X2}";
            }
        }
        throw new InvalidOperationException($"check_box_{priority}.png has no opaque border pixel");
    }

    /// <summary>
    /// The bug: the row said blue and the picker said green, about the same task, at the same time.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [SupportedOSPlatform("windows")]
    public void The_palette_matches_the_mark_drawn_on_every_row(int priority)
    {
        Assert.Equal(
            BorderHex(priority),
            PriorityPalette.Hex(priority).ToUpperInvariant());
    }

    /// <summary>Low is blue. Naming it, because green is the value that was wrong.</summary>
    [Fact]
    public void Low_priority_is_blue()
    {
        Assert.Equal("#328ACC", PriorityPalette.Low);
        Assert.NotEqual("#10B981", PriorityPalette.Low);
    }

    /// <summary>
    /// A level this build does not know draws as unmarked, rather than as whichever colour a
    /// fallthrough happened to land on.
    /// </summary>
    [Fact]
    public void An_unknown_level_draws_as_no_priority()
    {
        Assert.Equal(PriorityPalette.None, PriorityPalette.Hex(4));
        Assert.Equal(PriorityPalette.None, PriorityPalette.Hex(-1));
    }

    /// <summary>Every value is a colour a parser can read, so a typo fails here.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Every_entry_is_a_readable_hex_colour(int priority)
    {
        var hex = PriorityPalette.Hex(priority);
        Assert.StartsWith("#", hex);
        Assert.Equal(7, hex.Length);
        Assert.True(int.TryParse(
            hex[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _));
    }
}
