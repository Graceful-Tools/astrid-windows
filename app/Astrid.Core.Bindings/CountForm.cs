namespace Astrid.Core.Bindings;

/// <summary>
/// What a number in a message is counting, and so which of CLDR's two sets of rules applies —
/// <c>astrid_rules::plurals::CountForm</c> (task b656efce).
/// </summary>
/// <remarks>
/// They are genuinely different rules, not two uses of one: English's <em>cardinal</em> 3 is
/// <c>other</c> ("3 weeks") while its <em>ordinal</em> 3 is <c>few</c> ("3rd"). Nothing can pick
/// the right form without being told which question is being asked, and the caller that chose the
/// key already knows.
/// </remarks>
public enum CountForm
{
    /// <summary>How many: "every 2 weeks", "10 times".</summary>
    Cardinal,

    /// <summary>Which one in order: "the 3rd Tuesday", "on the 21st".</summary>
    Ordinal,
}
