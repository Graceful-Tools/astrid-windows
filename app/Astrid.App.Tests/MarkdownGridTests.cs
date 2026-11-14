using System.Text.Json;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// Reading the core's <c>tradeoffGrid</c> markdown block (task <c>cca2483d</c>).
/// </summary>
/// <remarks>
/// <para>
/// The fixtures these payloads are copied from are astrid-core's
/// <c>contracts/fixtures/markdown.json</c>, the <c>grid*</c> cases, which is what pins the shape
/// across the clients. They are inlined rather than read from a sibling checkout because this test
/// has to run wherever the gate does, and a fixture that is only present on a developer's machine
/// is a test that quietly stops asking.
/// </para>
/// <para>
/// A grid sends its rows under the same <c>rows</c> key a plain table uses, with a different
/// shape: a table's row is an array of cells, a grid's row is an object with the option's
/// <c>name</c> and <c>cells</c> that each carry a <c>rating</c>. Both therefore have to read, and
/// that is what most of this file is about.
/// </para>
/// </remarks>
public sealed class MarkdownGridTests
{
    /// <summary>
    /// The <c>grid</c> fixture case: two options, two criteria, every cell rated.
    /// </summary>
    private const string Grid = """
        {
          "kind": "tradeoffGrid",
          "alignments": ["none", "none", "none"],
          "header": [
            [{"kind": "text", "text": "Option"}],
            [{"kind": "text", "text": "Cost"}],
            [{"kind": "text", "text": "Time"}]
          ],
          "rows": [
            {
              "name": [{"kind": "text", "text": "Lisbon"}],
              "cells": [
                {"rating": "veryGood", "inlines": [{"kind": "text", "text": "cheap"}]},
                {"rating": "poor", "inlines": [{"kind": "text", "text": "long flight"}]}
              ]
            },
            {
              "name": [{"kind": "text", "text": "Porto"}],
              "cells": [
                {"rating": "mixed", "inlines": [{"kind": "text", "text": "fair"}]},
                {"rating": "good", "inlines": [{"kind": "text", "text": "short"}]}
              ]
            }
          ],
          "key": ["poor", "mixed", "good", "veryGood"]
        }
        """;

    /// <summary>The <c>grid-empty-cell</c> case: an unrated cell is <c>rating: null</c>.</summary>
    private const string GridWithEmptyCell = """
        {
          "kind": "tradeoffGrid",
          "alignments": ["none", "none"],
          "header": [
            [{"kind": "text", "text": "Option"}],
            [{"kind": "text", "text": "Cost"}]
          ],
          "rows": [
            {
              "name": [{"kind": "text", "text": "Lisbon"}],
              "cells": [{"rating": null, "inlines": []}]
            }
          ],
          "key": []
        }
        """;

    /// <summary>A plain table, which sends the same <c>rows</c> key in the other shape.</summary>
    private const string Table = """
        {
          "kind": "table",
          "alignments": ["left", "right"],
          "header": [
            [{"kind": "text", "text": "Fruit"}],
            [{"kind": "text", "text": "Count"}]
          ],
          "rows": [
            [
              [{"kind": "text", "text": "Apples"}],
              [{"kind": "text", "text": "3"}]
            ]
          ]
        }
        """;

    /// <summary>
    /// A grid reads: the option names, the ratings, the reasons and the key (task
    /// <c>cca2483d</c>).
    /// </summary>
    /// <remarks>
    /// RED before the fix, and not in the way the task predicted. The task expected a grid to draw
    /// as an empty paragraph, because <c>MarkdownRenderer.Block()</c> falls through to
    /// <c>Paragraph(block.Inlines, …)</c> for a kind it does not know and a grid has no
    /// <c>inlines</c>. It never reaches the renderer: <c>MarkdownBlock.Rows</c> was a table's rows
    /// all the way down, so an object where an array was expected threw and the whole markdown
    /// answer failed to parse — losing the entire description or comment, not just its table.
    /// </remarks>
    [Fact]
    public void A_tradeoff_grid_reads_its_options_ratings_and_key_task_cca2483d()
    {
        var block = JsonSerializer.Deserialize<MarkdownBlock>(Grid, CommandJson.Options);

        Assert.NotNull(block);
        Assert.Equal("tradeoffGrid", block!.Kind);
        Assert.Equal(3, block.Header.Count);
        Assert.Equal(new[] { "poor", "mixed", "good", "veryGood" }, block.Key);

        Assert.Equal(2, block.Rows.Count);

        var lisbon = block.Rows[0];
        Assert.NotNull(lisbon.Name);
        Assert.Equal("Lisbon", Assert.Single(lisbon.Name!).Text);
        Assert.Equal(new[] { "veryGood", "poor" }, lisbon.Cells.Select(cell => cell.Rating));
        Assert.Equal("cheap", Assert.Single(lisbon.Cells[0].Inlines).Text);
        Assert.Equal("long flight", Assert.Single(lisbon.Cells[1].Inlines).Text);

        var porto = block.Rows[1];
        Assert.Equal("Porto", Assert.Single(porto.Name!).Text);
        Assert.Equal(new[] { "mixed", "good" }, porto.Cells.Select(cell => cell.Rating));
    }

    /// <summary>An unrated cell is empty rather than absent (task <c>cca2483d</c>).</summary>
    /// <remarks>
    /// <c>rating: null</c> is the core saying "this option has nothing recorded against this
    /// criterion", which is different from a cell that is rated and happens to have no reason
    /// written. The renderer has to be able to tell them apart to leave the cell unpainted.
    /// </remarks>
    [Fact]
    public void An_unrated_cell_reads_as_null_rather_than_as_a_rating_task_cca2483d()
    {
        var block = JsonSerializer.Deserialize<MarkdownBlock>(GridWithEmptyCell, CommandJson.Options);

        var cell = Assert.Single(Assert.Single(block!.Rows).Cells);
        Assert.Null(cell.Rating);
        Assert.Empty(cell.Inlines);
    }

    /// <summary>
    /// And a plain table still reads, through the same <c>rows</c> key (task <c>cca2483d</c>).
    /// </summary>
    /// <remarks>
    /// The whole risk of this change is here. One key carries two shapes, so the type that reads
    /// it has to accept both — and a table is the shape that already worked, drawn in every task
    /// description that has one. A row of a table has no option name and no ratings.
    /// </remarks>
    [Fact]
    public void A_plain_table_still_reads_through_the_same_rows_key_task_cca2483d()
    {
        var block = JsonSerializer.Deserialize<MarkdownBlock>(Table, CommandJson.Options);

        Assert.Equal("table", block!.Kind);
        var row = Assert.Single(block.Rows);
        Assert.Null(row.Name);
        Assert.Equal(2, row.Cells.Count);
        Assert.Equal("Apples", Assert.Single(row.Cells[0].Inlines).Text);
        Assert.Equal("3", Assert.Single(row.Cells[1].Inlines).Text);
        Assert.All(row.Cells, cell => Assert.Null(cell.Rating));
    }

    /// <summary>
    /// A grid inside a whole answer does not take the rest of the message down with it (task
    /// <c>cca2483d</c>).
    /// </summary>
    /// <remarks>
    /// This is the regression as a user met it: the grid is one block of a description that also
    /// has prose around it, and before the fix the paragraph either side vanished too, because the
    /// failure was in parsing rather than in drawing.
    /// </remarks>
    [Fact]
    public void A_grid_between_paragraphs_does_not_lose_the_paragraphs_task_cca2483d()
    {
        var json = "[{\"kind\":\"paragraph\",\"inlines\":[{\"kind\":\"text\",\"text\":\"Before\"}]},"
                   + Grid
                   + ",{\"kind\":\"paragraph\",\"inlines\":[{\"kind\":\"text\",\"text\":\"After\"}]}]";

        var blocks = JsonSerializer.Deserialize<List<MarkdownBlock>>(json, CommandJson.Options);

        Assert.NotNull(blocks);
        Assert.Equal(3, blocks!.Count);
        Assert.Equal("Before", Assert.Single(blocks[0].Inlines).Text);
        Assert.Equal("tradeoffGrid", blocks[1].Kind);
        Assert.Equal("After", Assert.Single(blocks[2].Inlines).Text);
    }
}
