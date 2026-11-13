using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// How one of the four squares in the priority picker is coloured (task 204c9d98).
/// </summary>
/// <remarks>
/// The web draws four squares in each priority's colour, the chosen one filled with white on top
/// and the rest outlined. The colours are <see cref="PriorityPalette"/>, the same table the row
/// marks draw from, so the picker and the row can never disagree. This is a rule, not a control, so
/// the shell paints whatever it says and a test can read it without a window.
///
/// What it answers with are themed brush KEYS, not hexes (task 82e4ad32). A priority's colour
/// depends on the theme — <c>priority-low</c> resolves to a different brand value on dark — and a
/// view-model has no theme to resolve it in. The shell does: it hands each key to
/// <c>ThemeBrushes.For(element, key)</c>, which asks the element what it is wearing.
/// </remarks>
public static class PrioritySwatch
{
    /// <summary>The label on a filled square: the <c>accent-text</c> token, white in every theme.</summary>
    public const string OnFill = "TextOnAccentFillColorPrimaryBrush";

    /// <summary>No fill: the square is outlined only.</summary>
    public const string Transparent = "transparent";

    /// <summary>The brushes for the square at <paramref name="level"/> when <paramref name="chosen"/> is lit.</summary>
    public static (string Background, string Foreground, string Border) For(int level, int chosen)
    {
        // No-priority has no colour of its own on the row — the stripe draws nothing — but a
        // square still has to be visible, so it takes the grey the unmarked checkbox is drawn in.
        var brush = PriorityPalette.Key(level);
        return level == chosen
            ? (brush, OnFill, brush)
            : (Transparent, brush, brush);
    }
}
