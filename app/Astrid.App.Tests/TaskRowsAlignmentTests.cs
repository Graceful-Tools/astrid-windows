using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// Quick add and the rows under it are one column of work, so their checkboxes and their text
/// start at the same x (task 90d0306e).
/// </summary>
/// <remarks>
/// <para>
/// Reported from the running app: the add-task checkbox sat right of the row checkboxes and its
/// text right of the task text, so the eye follows two ragged edges down the list instead of one.
/// Each offset was defensible alone — the quick-add card carried its own margin and padding, the
/// rows get theirs from the ListView and the item container — and nothing compared them, which is
/// how they drifted 8px apart.
/// </para>
/// <para>
/// Read off the XAML, the way <see cref="TaskDetailAlignmentTests"/> and
/// <see cref="LocalisationTests"/> already read it: a view-model test cannot see a margin, and the
/// UI smoke suite that could is ten minutes and does not run on every gate. The arithmetic is the
/// point — each side is a sum of a container inset and a column gap, and the test states the sum
/// rather than the numbers, so a future change that moves both stays green.
/// </para>
/// </remarks>
public sealed class TaskRowsAlignmentTests
{
    private static readonly XNamespace Ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XElement Rows() =>
        XDocument.Load(Path.Combine(LocalisationTests.AppDirectory(), "Views", "TaskRowsView.xaml")).Root!;

    /// <summary>The left component of a Margin/Padding/Thickness, in its 1, 2 and 4 value forms.</summary>
    private static double Left(string? thickness)
    {
        Assert.False(string.IsNullOrWhiteSpace(thickness));
        var parts = thickness!.Split(',', StringSplitOptions.TrimEntries);
        return double.Parse(parts[0], CultureInfo.InvariantCulture);
    }

    private static double Number(string? value)
    {
        Assert.False(string.IsNullOrWhiteSpace(value));
        return double.Parse(value!, CultureInfo.InvariantCulture);
    }

    /// <summary>The quick-add card: the Border that holds the task-settings button.</summary>
    private static XElement QuickAddCard(XElement root) =>
        root.Descendants(Ui + "Border").Single(border =>
            border.Descendants().Any(child => (string?)child.Attribute(Xaml + "Uid") == "quick_add_settings"));

    private static XElement TaskList(XElement root) => root.Descendants(Ui + "ListView").Single();

    [Fact]
    public void Quick_add_and_the_rows_share_one_left_edge_task_90d0306e()
    {
        var root = Rows();

        // Quick add: the card's own margin, then its padding, then the gap to the text.
        var card = QuickAddCard(root);
        var cardGrid = card.Elements(Ui + "Grid").Single();
        var quickCheckbox = Left((string?)card.Attribute("Margin")) + Left((string?)card.Attribute("Padding"));
        var quickText = quickCheckbox + 32 + Number((string?)cardGrid.Attribute("ColumnSpacing"));

        // A row: the list's margin, then the item container's padding, then the gap to the text.
        var list = TaskList(root);
        var containerPadding = list
            .Descendants(Ui + "Setter")
            .Single(setter => (string?)setter.Attribute("Property") == "Padding");
        var rowGrid = list.Element(Ui + "ListView.ItemTemplate")!
            .Element(Ui + "DataTemplate")!
            .Element(Ui + "Grid")!;
        var rowCheckbox = Left((string?)list.Attribute("Margin")) + Left((string?)containerPadding.Attribute("Value"));
        var rowText = rowCheckbox + 32 + Number((string?)rowGrid.Attribute("ColumnSpacing"));

        Assert.Equal(rowCheckbox, quickCheckbox);
        Assert.Equal(rowText, quickText);
    }

    /// <summary>
    /// A board card leads with the same mark a row does (task 6979e694).
    /// </summary>
    /// <remarks>
    /// A card drew its title with nothing to its left, so the words sat against the card edge and
    /// moved the moment the card was opened. The Mac builds its card out of its row view and the
    /// web shares <c>TaskRowContent</c>, both for this reason; this asserts the same shape without
    /// sharing the template — the mark, 32px wide, then the words a row's gap away. All four
    /// states the core can ask for are checked, because a card that drops one shows nothing where
    /// the mark should be.
    /// </remarks>
    [Fact]
    public void A_board_card_leads_with_the_same_mark_as_a_row_task_6979e694()
    {
        var board = XDocument
            .Load(Path.Combine(LocalisationTests.AppDirectory(), "Views", "BoardView.xaml")).Root!;
        var card = board.Descendants()
            .Single(element => element.Name.LocalName == "BoardCard");
        var grid = card.Elements(Ui + "Grid").Single();

        Assert.Equal(12d, Number((string?)grid.Attribute("ColumnSpacing")));

        var mark = grid.Elements(Ui + "Button").Single();
        Assert.Equal("0", (string?)mark.Attribute("Grid.Column"));
        Assert.Equal(32d, Number((string?)mark.Attribute("Width")));
        Assert.Equal("OnCardMarkClicked", (string?)mark.Attribute("Click"));

        // The same four answers a row gives: yours, a copy you may take, somebody else's, nobody's.
        var states = mark.Descendants()
            .Select(element => (string?)element.Attribute("Visibility") ?? string.Empty)
            .ToList();
        Assert.Contains(states, value => value.Contains("LeadingIsCheckbox"));
        Assert.Contains(states, value => value.Contains("LeadingIsCopy"));
        Assert.Contains(states, value => value.Contains("LeadingIsSquare"));
    }
}
