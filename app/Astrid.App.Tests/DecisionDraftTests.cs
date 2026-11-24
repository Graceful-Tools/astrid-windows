using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// Writing a decision with traffic lights from task details (task <c>b0f521c6</c>).
/// </summary>
/// <remarks>
/// A port of astrid-ios <c>DecisionDraftTests.swift</c> (ADEV-189, ADEV-231), case for case: the
/// draft's markdown is byte for byte what the iOS composer writes, so a decision asked for on
/// Windows reads the same in every app. The expected strings are copied, not recomputed.
/// </remarks>
public sealed class DecisionDraftTests
{
    private static DecisionDraft Draft(string question, params string[] options)
    {
        var draft = new DecisionDraft { Question = question };
        draft.Options.Clear();
        foreach (var text in options)
        {
            draft.Options.Add(new DecisionDraft.Option { Text = text });
        }
        return draft;
    }

    [Fact]
    public void A_question_and_options_write_the_decision_format_task_b0f521c6()
    {
        var d = Draft("Ship today?", "Ship today", "Wait for the review");
        d.Recommended = d.Options[0].Id;
        Assert.Equal(
            "**Decision:** Ship today?\n\n- **1.** Ship today (recommended)\n- **2.** Wait for the review",
            d.Markdown);
    }

    [Fact]
    public void A_new_draft_starts_with_two_empty_options_task_b0f521c6()
    {
        var d = new DecisionDraft();
        Assert.Equal(2, d.Options.Count);
        Assert.Equal(DecisionDraft.Problem.NoQuestion, d.CurrentProblem);
    }

    [Fact]
    public void Blank_options_are_left_out_and_the_rest_renumbered_task_b0f521c6()
    {
        var d = Draft("Where?", "Lisbon", "  ", "Cabin");
        Assert.EndsWith("- **1.** Lisbon\n- **2.** Cabin", d.Markdown);
    }

    [Fact]
    public void Rated_criteria_add_the_trade_off_grid_task_b0f521c6()
    {
        var d = Draft("Where?", "Lisbon", "Cabin");
        d.Criteria.Add(new DecisionDraft.Criterion { Name = "Cost" });
        d.Criteria.Add(new DecisionDraft.Criterion { Name = "Fun" });
        d.Set(new DecisionDraft.Cell("~", "flights"), d.Options[0].Id, d.Criteria[0].Id);
        d.Set(new DecisionDraft.Cell("++", "sun"), d.Options[0].Id, d.Criteria[1].Id);
        d.Set(new DecisionDraft.Cell("+", ""), d.Options[1].Id, d.Criteria[0].Id);
        Assert.EndsWith(
            "- **2.** Cabin\n\n| Option | Cost | Fun |\n|---|---|---|\n| 1. Lisbon | ~ flights | ++ sun |\n| 2. Cabin | + |  |",
            d.Markdown);
    }

    /// <summary>
    /// What the composer writes is what the apps draw: one set of choices, one grid — asked of the
    /// core's own renderer, which is what draws the thread and the preview.
    /// </summary>
    [Fact]
    public void The_written_decision_draws_as_a_grid_task_b0f521c6()
    {
        var d = Draft("Where?", "Lisbon", "Cabin");
        d.Recommended = d.Options[1].Id;
        d.Criteria.Add(new DecisionDraft.Criterion { Name = "Cost" });
        d.Set(new DecisionDraft.Cell("--", "dear"), d.Options[0].Id, d.Criteria[0].Id);
        d.Set(new DecisionDraft.Cell("++", "cheap | quiet"), d.Options[1].Id, d.Criteria[0].Id);

        var blocks = AstridClient.RenderMarkdown(d.Markdown);
        var grid = Assert.Single(blocks, b => b.Kind == "tradeoffGrid");
        Assert.Equal(
            new[] { "veryPoor", "veryGood" },
            grid.Rows.Select(r => Assert.Single(r.Cells).Rating));
        Assert.Contains("\\|", d.Markdown);
    }

    [Fact]
    public void Criteria_with_no_ratings_add_no_grid_task_b0f521c6()
    {
        var d = Draft("Where?", "Lisbon", "Cabin");
        d.Criteria.Add(new DecisionDraft.Criterion { Name = "Cost" });
        Assert.DoesNotContain("|", d.Markdown);
    }

    [Fact]
    public void What_stops_it_being_posted_task_b0f521c6()
    {
        Assert.Equal(DecisionDraft.Problem.NoQuestion, Draft(" ", "A", "B").CurrentProblem);
        Assert.Equal(DecisionDraft.Problem.TooFewOptions, Draft("Which?", "A", "").CurrentProblem);
        var d = Draft("Which?", "A", "B");
        Assert.Null(d.CurrentProblem);
        d.Criteria.Add(new DecisionDraft.Criterion { Name = "Cost" });
        d.Set(new DecisionDraft.Cell(null, "cheap"), d.Options[0].Id, d.Criteria[0].Id);
        Assert.Equal(DecisionDraft.Problem.ReasonWithoutRating, d.CurrentProblem);
    }

    [Fact]
    public void A_line_break_in_an_option_stays_on_its_line_task_b0f521c6()
    {
        var d = Draft("Which\nway?", "Left\nturn", "Right");
        Assert.StartsWith("**Decision:** Which way?", d.Markdown);
        Assert.Contains("- **1.** Left turn\n", d.Markdown);
    }

    [Fact]
    public void Added_to_a_description_after_what_is_there_task_b0f521c6()
    {
        var d = Draft("Which?", "A", "B");
        Assert.Equal(d.Markdown, d.AppendedTo(""));
        Assert.Equal("Notes.\n\n" + d.Markdown, d.AppendedTo("Notes.\n\n"));
    }

    // ── ADEV-231: what a criterion means, as the key under the grid ──────────────────────────

    private static DecisionDraft Rated()
    {
        var d = Draft("Where?", "Lisbon", "Cabin");
        d.Criteria.Add(new DecisionDraft.Criterion { Name = "Cost" });
        d.Criteria.Add(new DecisionDraft.Criterion { Name = "Fun" });
        d.Set(new DecisionDraft.Cell("+", "cheap flights"), d.Options[0].Id, d.Criteria[0].Id);
        return d;
    }

    [Fact]
    public void A_criterions_meaning_is_the_key_under_the_grid_task_b0f521c6()
    {
        var d = Rated();
        d.Criteria[0].Detail = "what the company pays,\nall in";
        Assert.EndsWith("| 2. Cabin |  |  |\n\n- **Cost:** what the company pays, all in", d.Markdown);
    }

    [Fact]
    public void No_meanings_no_key_task_b0f521c6()
    {
        Assert.EndsWith("| 2. Cabin |  |  |", Rated().Markdown);
    }

    [Fact]
    public void No_grid_no_key_task_b0f521c6()
    {
        var d = Rated();
        d.Set(new DecisionDraft.Cell(null, ""), d.Options[0].Id, d.Criteria[0].Id);
        d.Criteria[0].Detail = "what the company pays";
        Assert.DoesNotContain("Cost", d.Markdown);
    }

    // ── The lights ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The five lights are the core's plain tokens, worst to best, each with the rating its colour
    /// is drawn from — the emoji forms are read but not offered, as on iOS.
    /// </summary>
    [Fact]
    public void The_lights_are_the_cores_five_plain_tokens_task_b0f521c6()
    {
        var lights = DecisionDraft.Lights();
        Assert.Equal(new[] { "--", "-", "~", "+", "++" }, lights.Select(l => l.Token));
        Assert.Equal(new[] { "veryPoor", "poor", "mixed", "good", "veryGood" }, lights.Select(l => l.Rating));
    }

    [Fact]
    public void Choosing_the_lit_light_again_clears_it_and_keeps_the_note_task_b0f521c6()
    {
        var d = Rated();
        d.Toggle("+", d.Options[0].Id, d.Criteria[0].Id);
        Assert.Equal(new DecisionDraft.Cell(null, "cheap flights"), d.CellAt(d.Options[0].Id, d.Criteria[0].Id));
        d.Toggle("~", d.Options[0].Id, d.Criteria[0].Id);
        Assert.Equal(new DecisionDraft.Cell("~", "cheap flights"), d.CellAt(d.Options[0].Id, d.Criteria[0].Id));
    }

    [Fact]
    public void No_more_than_five_criteria_task_b0f521c6()
    {
        var d = Draft("Where?", "A", "B");
        for (var i = 0; i < 7; i++)
        {
            d.AddCriterion();
        }
        Assert.Equal(DecisionDraft.MaxCriteria, d.Criteria.Count);
        Assert.False(d.CanAddCriterion);
    }
}
