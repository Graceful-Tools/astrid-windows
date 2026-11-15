namespace Astrid.Core.Bindings;

/// <summary>
/// The brush a priority is drawn in, named once.
/// </summary>
/// <remarks>
/// <para>
/// Task 204c9d98. The app drew a priority in two different colours at the same time: every row's
/// mark was one of the shared checkbox images, in which priority 1 is <b>blue</b>, while the picker
/// and the detail glyph resolved from a hardcoded switch in which priority 1 was <b>green</b>. So
/// the row said blue and the control that sets it said green, on the same screen, about the same
/// task. This class is the one table that fixed it.
/// </para>
/// <para>
/// Task 82e4ad32 changed what it holds. It used to carry four hex values sampled out of those
/// images, with a test pinning it to them — which made the images the source of truth for a colour,
/// and they never were: astrid-core publishes <c>priority-none</c>, <c>-low</c>, <c>-medium</c> and
/// <c>-high</c>, and the sampled blue (<c>#328ACC</c>) had drifted from the token (<c>#2563EB</c>)
/// with nothing able to see it. A colour living in a client's copy is the one thing the shared
/// design register forbids.
/// </para>
/// <para>
/// So this names a <b>themed brush</b> instead, and the colour sits in <c>App.xaml</c>'s theme
/// dictionaries against the vendored tokens. The indirection is not ceremony. <c>priority-low</c>
/// is an alias — <c>{brand.accent}</c> on light and ocean, <c>{brand.accent-ink-dark}</c> on dark,
/// because a mid-tone that carries meaning by itself is 2.84:1 against a dark card — so a priority
/// is not four constants and a <c>const string</c> cannot express it. Resolve a key with
/// <c>ThemeBrushes.For(element, key)</c>, which asks the theme the element is actually wearing;
/// <c>Application.Current.Resources[key]</c> answers from the light dictionary whatever the reader
/// is in.
/// </para>
/// </remarks>
public static class PriorityPalette
{
    /// <summary>No priority: the grey outline an unmarked task wears.</summary>
    public const string None = "AstridPriorityNone";

    /// <summary>Low. Blue, not green — this is the value that was wrong in task 204c9d98.</summary>
    public const string Low = "AstridPriorityLow";

    public const string Medium = "AstridPriorityMedium";

    public const string High = "AstridPriorityHigh";

    /// <summary>
    /// The themed brush key for a priority.
    /// </summary>
    /// <remarks>
    /// Anything outside 0–3 is treated as no priority. A server that grows a fourth level should
    /// draw as unmarked rather than as whichever colour a fallthrough happened to pick.
    /// </remarks>
    public static string Key(int priority) => priority switch
    {
        3 => High,
        2 => Medium,
        1 => Low,
        _ => None,
    };
}
