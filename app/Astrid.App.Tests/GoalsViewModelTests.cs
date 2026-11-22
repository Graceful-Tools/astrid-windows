using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// A board's goals and guardrails, read and written through the core (task ec11e539, following
/// web ADEV-179 and core ADEV-187).
/// </summary>
public sealed class GoalsViewModelTests
{
    private static object Answer(bool canEdit, bool empty = false) => new
    {
        listId = "l1",
        projectId = "p1",
        projectName = "Fund",
        goals = empty
            ? Array.Empty<object>()
            : new object[]
            {
                new { id = "g1", statement = "Maximise long-term returns", measure = "IRR", target = "8%", current = "6.1%" },
                new { id = "g2", statement = "Keep the books simple" },
            },
        guardrails = empty
            ? Array.Empty<object>()
            : new object[] { new { id = "r1", statement = "Break no applicable laws", kind = "enforced" } },
        canEdit,
    };

    [Fact]
    public async Task A_lists_board_shows_its_goals_and_guardrails_task_ec11e539()
    {
        var core = new FakeCore().AnswerOk("getListGoals", Answer(canEdit: false));
        var goals = new GoalsViewModel(core);

        await goals.LoadForListAsync("l1");

        Assert.Contains(core.Sent, json => json.Contains("\"kind\":\"getListGoals\"")
                                           && json.Contains("\"listId\":\"l1\""));
        Assert.True(goals.HasAny);
        Assert.Equal(["g1", "g2"], goals.Goals.Select(goal => goal.Id));
        Assert.Equal("IRR", goals.Goals[0].Measure);
        Assert.True(Assert.Single(goals.Guardrails).IsEnforced);
        Assert.False(goals.CanEdit);
        Assert.Equal("p1", goals.ProjectId);
    }

    /// <summary>Both lists empty draws nothing at all — not "Goals: 0 · Guardrails: 0".</summary>
    [Fact]
    public async Task A_board_with_no_goals_draws_nothing_task_ec11e539()
    {
        var core = new FakeCore().AnswerOk("getProjectGoals", Answer(canEdit: true, empty: true));
        var goals = new GoalsViewModel(core);

        await goals.LoadForProjectAsync("p1");

        Assert.False(goals.HasAny);
        // An owner can still add the first one, which is why the editor is offered on CanEdit.
        Assert.True(goals.CanEdit);
    }

    /// <summary>A list on no board, or a read the core refused, is nothing to show and no error.</summary>
    [Fact]
    public async Task A_refused_read_is_nothing_to_show_task_ec11e539()
    {
        var core = new FakeCore().AnswerFailure("getListGoals", AstridFailureKind.NotFound, "gone");
        var goals = new GoalsViewModel(core);

        await goals.LoadForListAsync("l1");

        Assert.False(goals.HasAny);
        Assert.False(goals.CanEdit);
    }

    /// <summary>
    /// Saving PUTs the whole value: a new row goes without an id so the server assigns one, and
    /// the server's echo — with its ids — is what is drawn afterwards.
    /// </summary>
    [Fact]
    public async Task Saving_sends_the_whole_value_and_draws_the_echo_task_ec11e539()
    {
        var core = new FakeCore().AnswerOk("getProjectGoals", Answer(canEdit: true));
        var goals = new GoalsViewModel(core);
        await goals.LoadForProjectAsync("p1");

        core.AnswerOk("setProjectGoals", new
        {
            projectId = "p1",
            projectName = "Fund",
            goals = new object[] { new { id = "g3", statement = "Grow the fund" } },
            guardrails = Array.Empty<object>(),
            canEdit = true,
        });
        var saved = await goals.SaveAsync(
            [new GoalDraft(null, "Grow the fund", null, null, null)],
            []);

        Assert.True(saved);
        var sent = core.Sent.Last(json => json.Contains("\"kind\":\"setProjectGoals\""));
        Assert.Contains("\"projectId\":\"p1\"", sent);
        Assert.Contains("\"statement\":\"Grow the fund\"", sent);
        Assert.DoesNotContain("\"id\":", sent);
        Assert.Contains("\"guardrails\":[]", sent);
        Assert.Equal("g3", Assert.Single(goals.Goals).Id);
        Assert.Empty(goals.Guardrails);
    }

    /// <summary>A refused save keeps what was on screen and says why.</summary>
    [Fact]
    public async Task A_refused_save_keeps_the_old_value_and_says_so_task_ec11e539()
    {
        var core = new FakeCore().AnswerOk("getProjectGoals", Answer(canEdit: true));
        var goals = new GoalsViewModel(core);
        await goals.LoadForProjectAsync("p1");

        core.AnswerFailure("setProjectGoals", AstridFailureKind.Refused, "not an owner");
        var saved = await goals.SaveAsync([], []);

        Assert.False(saved);
        Assert.Equal(2, goals.Goals.Count);
        Assert.NotNull(goals.ErrorMessage);
    }
}
