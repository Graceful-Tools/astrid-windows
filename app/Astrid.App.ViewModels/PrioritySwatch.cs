using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// How one of the four squares in the priority picker is coloured (task 204c9d98).
/// </summary>
/// <remarks>
/// The web draws four squares in each priority's colour, the chosen one filled with white on top
/// and the rest outlined. The colours are <see cref="PriorityPalette"/>, the table pinned to the
/// row marks, so the picker and the row can never disagree. This is a rule, not a control, so the
/// shell paints whatever it says and a test can read it without a window.
/// </remarks>
public static class PrioritySwatch
{
    public const string White = "#FFFFFF";

    /// <summary>No fill: the square is outlined only.</summary>
    public const string Transparent = "transparent";

    /// <summary>The colours for the square at <paramref name="level"/> when <paramref name="chosen"/> is lit.</summary>
    public static (string Background, string Foreground, string Border) For(int level, int chosen)
    {
        // No-priority has no colour of its own on the row — the stripe draws nothing — but a
        // square still has to be visible, so it takes the grey the unmarked checkbox is drawn in.
        var colour = level == 0 ? PriorityPalette.None : PriorityPalette.Hex(level);
        return level == chosen
            ? (colour, White, colour)
            : (Transparent, colour, colour);
    }
}
