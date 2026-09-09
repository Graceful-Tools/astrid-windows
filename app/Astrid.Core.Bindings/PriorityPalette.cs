namespace Astrid.Core.Bindings;

/// <summary>
/// The colour a priority is drawn in, stated once.
/// </summary>
/// <remarks>
/// <para>
/// Task 204c9d98. The app drew a priority in two different colours at the same time. Every row's
/// mark is one of the shared checkbox images copied from astrid-web, and priority 1 in those is
/// <b>blue</b>; the picker and the detail glyph resolved their colour from a hardcoded switch in
/// which priority 1 was <b>green</b>. So the row said blue and the control that sets it said
/// green, on the same screen, about the same task.
/// </para>
/// <para>
/// These values are sampled from the images the app actually ships — see
/// <c>app/Astrid.App/Assets/Checkboxes/check_box_{0..3}.png</c>, and the test beside this class
/// pins them. The images win because they are pixels this app cannot restyle: a swatch that
/// disagrees with them is simply wrong, whatever a table elsewhere says.
/// </para>
/// <para>
/// The previous values claimed in a comment to match Apple's <c>Task.Priority.color</c>, and were
/// hardcoded in the converter rather than read from the brushes the same comment pointed at. Two
/// copies and a doc that described neither.
/// </para>
/// </remarks>
public static class PriorityPalette
{
    /// <summary>No priority: the grey outline an unmarked task wears.</summary>
    public const string None = "#B3B6BA";

    /// <summary>Low. Blue, not green — this is the value that was wrong.</summary>
    public const string Low = "#328ACC";

    public const string Medium = "#F5AF39";

    public const string High = "#D5392E";

    /// <summary>
    /// The colour for a priority, as <c>#rrggbb</c>.
    /// </summary>
    /// <remarks>
    /// Anything outside 0–3 is treated as no priority. A server that grows a fourth level should
    /// draw as unmarked rather than as whichever colour a fallthrough happened to pick.
    /// </remarks>
    public static string Hex(int priority) => priority switch
    {
        3 => High,
        2 => Medium,
        1 => Low,
        _ => None,
    };
}
