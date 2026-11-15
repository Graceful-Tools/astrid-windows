namespace Astrid.App.ViewModels;

/// <summary>
/// What a trade-off grid's rows, columns and cells are called and coloured (task
/// <c>cca2483d</c>).
/// </summary>
/// <remarks>
/// <para>
/// The core's <c>tradeoffGrid</c> block carries neither row numbers nor column letters — its own
/// comment says so — because they come from a row's or a column's position rather than from
/// anything the writer typed. Producing them is therefore the client's job by design, not a rule
/// that leaked out of the core.
/// </para>
/// <para>
/// They live here rather than in <c>MarkdownRenderer</c> so they can be tested. The renderer is in
/// the WinUI assembly, which the test project cannot reference, and "the eleventh criterion is
/// called K" is exactly the kind of claim that should be checked rather than looked at.
/// </para>
/// </remarks>
public static class TradeoffGridLabels
{
    /// <summary>
    /// The ratings the core can send, worst to best. Anything else is a rating this client does
    /// not know and is drawn unpainted rather than guessed at.
    /// </summary>
    private static readonly string[] Ratings = ["veryPoor", "poor", "mixed", "good", "veryGood"];

    /// <summary>
    /// A criterion's letter, from its position: <c>A</c>, <c>B</c>, … <c>Z</c>, <c>AA</c>,
    /// <c>AB</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A faithful port of <c>astrid_rules::decisions::column_letter</c> at the pinned core
    /// revision, which is the specification. That function is a plain public Rust function rather
    /// than a <c>Rule</c> variant on the stateless door, and this repository has no binding to
    /// that door at all, so there is nothing to call — see <c>docs/CONTRACTS.md</c>.
    /// </para>
    /// <para>
    /// It is bijective base 26, not ordinary base 26: there is no zero digit, so <c>Z</c> is
    /// followed by <c>AA</c> rather than by <c>BA</c>. The <c>/ 26 - 1</c> is what makes that
    /// happen, and it is the only interesting line.
    /// </para>
    /// </remarks>
    /// <param name="index">The criterion's position, counting from zero.</param>
    public static string ColumnLetter(int index)
    {
        if (index < 0)
        {
            return string.Empty;
        }

        var name = string.Empty;
        for (long i = index; i >= 0; i = (i / 26) - 1)
        {
            name = (char)('A' + (i % 26)) + name;
        }
        return name;
    }

    /// <summary>An option's number, from its position: <c>1.</c>, <c>2.</c>, …</summary>
    /// <remarks>
    /// The writer's own marker — the "A." of "A. Lisbon" — has already been dropped by the core,
    /// so what a row is called here never disagrees with what it is called in a reply.
    /// </remarks>
    public static string RowNumber(int index) => $"{index + 1}.";

    /// <summary>
    /// The resource key for a rating's word, or null for a rating this client does not know.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These are the core's own keys — <c>astrid_rules::decisions</c>'s <c>RATING_KEY_PREFIX</c>
    /// and <c>RATING_LABEL_KEYS</c>, which <c>astrid_core::copy_keys()</c> names — so they are
    /// spelt the way the registry spells them and <c>Strings.Get</c> does the flatten to
    /// <c>tasks_tradeoffRatings_veryGood</c>, as it does for every other core key. This shell used
    /// to invent its own name here (<c>tradeoff_very_good</c>), which held the right word under a
    /// spelling no other client could have guessed; task b63d8679 aligned it, and
    /// <c>copy_catalog_contract.rs</c> now fails if it drifts back.
    /// </para>
    /// <para>
    /// The mapping is stated rather than computed, so a rating added to the core shows up as a
    /// missing case rather than as a key that quietly does not resolve.
    /// </para>
    /// </remarks>
    public static string? RatingLabelKey(string? rating) => rating switch
    {
        "veryPoor" => "tasks.tradeoffRatings.veryPoor",
        "poor" => "tasks.tradeoffRatings.poor",
        "mixed" => "tasks.tradeoffRatings.mixed",
        "good" => "tasks.tradeoffRatings.good",
        "veryGood" => "tasks.tradeoffRatings.veryGood",
        _ => null,
    };

    /// <summary>
    /// The brush key a rated cell is painted with, or null for an unrated cell.
    /// </summary>
    /// <remarks>
    /// Null means "do not paint", which is how an unrated cell (<c>rating: null</c>) is told from
    /// a rated one whose reason happens to be empty.
    /// </remarks>
    public static string? FillBrush(string? rating) =>
        IsKnown(rating) ? "AstridTradeoff" + Pascal(rating!) : null;

    /// <summary>The brush key a rated cell's text takes, or null for an unrated cell.</summary>
    /// <remarks>
    /// Each fill carries its own ink because the five are a fixed palette rather than a theme: the
    /// two dark fills take white and the three light ones take near-black, which is the contrast
    /// decision already made for these exact colours.
    /// </remarks>
    public static string? InkBrush(string? rating) =>
        IsKnown(rating) ? "AstridTradeoff" + Pascal(rating!) + "Ink" : null;

    /// <summary>The ratings a key lists, worst to best, keeping only the ones this client knows.</summary>
    /// <remarks>
    /// The core already sends <c>key</c> worst to best and already drops the ratings the grid does
    /// not use, so this only guards against a rating added to the core before it is added here.
    /// </remarks>
    public static IReadOnlyList<string> KnownRatings(IReadOnlyList<string> key) =>
        key.Where(IsKnown).ToList();

    private static bool IsKnown(string? rating) =>
        rating is not null && Array.IndexOf(Ratings, rating) >= 0;

    private static string Pascal(string rating) =>
        char.ToUpperInvariant(rating[0]) + rating[1..];
}
