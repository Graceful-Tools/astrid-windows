using Astrid.App.ViewModels;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// What a trade-off grid's rows, columns and cells are called (task <c>cca2483d</c>).
/// </summary>
public sealed class TradeoffGridLabelsTests
{
    /// <summary>
    /// A criterion's letter, including past Z (task <c>cca2483d</c>).
    /// </summary>
    /// <remarks>
    /// The cases are the ones <c>astrid_rules::decisions::column_letter</c> is specified by, and
    /// this is a port of it rather than a call to it — the core exposes it as a plain function and
    /// this repository has no binding to the rules door. The interesting ones are at the
    /// boundaries: bijective base 26 has no zero digit, so <c>Z</c> is followed by <c>AA</c>
    /// rather than by <c>BA</c>, and an ordinary base-26 loop gets that wrong.
    /// </remarks>
    [Theory]
    [InlineData(0, "A")]
    [InlineData(1, "B")]
    [InlineData(10, "K")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(27, "AB")]
    [InlineData(51, "AZ")]
    [InlineData(52, "BA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void A_criterion_is_lettered_by_position_task_cca2483d(int index, string expected) =>
        Assert.Equal(expected, TradeoffGridLabels.ColumnLetter(index));

    /// <summary>An option is numbered from one, with the stop the grid draws.</summary>
    [Theory]
    [InlineData(0, "1.")]
    [InlineData(9, "10.")]
    public void An_option_is_numbered_by_position_task_cca2483d(int index, string expected) =>
        Assert.Equal(expected, TradeoffGridLabels.RowNumber(index));

    /// <summary>
    /// Every rating the core can send has a colour, an ink and a word (task <c>cca2483d</c>).
    /// </summary>
    /// <remarks>
    /// The five are listed here rather than read from the helper, so that a rating added to the
    /// core and wired into <c>TradeoffGridLabels</c> without a <c>.resw</c> entry or a brush is
    /// caught by this test failing to find them rather than by a blank cell on a screen.
    /// </remarks>
    [Theory]
    [InlineData("veryPoor", "AstridTradeoffVeryPoor", "tasks.tradeoffRatings.veryPoor")]
    [InlineData("poor", "AstridTradeoffPoor", "tasks.tradeoffRatings.poor")]
    [InlineData("mixed", "AstridTradeoffMixed", "tasks.tradeoffRatings.mixed")]
    [InlineData("good", "AstridTradeoffGood", "tasks.tradeoffRatings.good")]
    [InlineData("veryGood", "AstridTradeoffVeryGood", "tasks.tradeoffRatings.veryGood")]
    public void A_rating_has_a_fill_an_ink_and_a_word_task_cca2483d(string rating, string fill,
        string labelKey)
    {
        Assert.Equal(fill, TradeoffGridLabels.FillBrush(rating));
        Assert.Equal(fill + "Ink", TradeoffGridLabels.InkBrush(rating));
        Assert.Equal(labelKey, TradeoffGridLabels.RatingLabelKey(rating));
    }

    /// <summary>
    /// An unrated cell is not painted, and neither is a rating this client does not know (task
    /// <c>cca2483d</c>).
    /// </summary>
    /// <remarks>
    /// Null rather than a neutral fill: "nothing recorded against this criterion" must not read as
    /// a rating of its own. A rating the core adds later takes the same path, so a newer core
    /// costs an unpainted cell rather than a crash.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("catastrophic")]
    public void An_unknown_or_absent_rating_is_not_painted_task_cca2483d(string? rating)
    {
        Assert.Null(TradeoffGridLabels.FillBrush(rating));
        Assert.Null(TradeoffGridLabels.InkBrush(rating));
        Assert.Null(TradeoffGridLabels.RatingLabelKey(rating));
    }

    /// <summary>A key keeps the core's worst-to-best order and drops what it cannot draw.</summary>
    [Fact]
    public void A_key_keeps_its_order_and_drops_the_unknown_task_cca2483d() =>
        Assert.Equal(
            new[] { "poor", "mixed", "veryGood" },
            TradeoffGridLabels.KnownRatings(["poor", "mixed", "catastrophic", "veryGood"]));
}
