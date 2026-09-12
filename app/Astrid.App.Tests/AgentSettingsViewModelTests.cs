using Astrid.App.ViewModels;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>The AI agents page: the hub, the credentials, the webhook.</summary>
public sealed class AgentSettingsViewModelTests
{
    /// <summary>
    /// The mode arrives in a map beside the agents rather than on them, and joining the two in one
    /// place keeps every control that shows an agent from doing it again.
    /// </summary>
    [Fact]
    public async Task An_agents_mode_is_joined_from_the_map_beside_it()
    {
        var core = new FakeCore().AnswerOk("agents", new
        {
            agents = new[]
            {
                new { id = "astrid", name = "Astrid", description = (string?)"The one that answers" },
                new { id = "claude", name = "Claude", description = (string?)null },
            },
            modes = new Dictionary<string, string> { ["astrid"] = "api", ["claude"] = "webhook" },
            credentials = new[]
            {
                new { serviceId = "openai", name = "OpenAI", configured = true },
                new { serviceId = "anthropic", name = "Anthropic", configured = false },
            },
        });
        var view = new SettingsViewModel(core).Agents;

        await view.LoadAgentsAsync();

        Assert.Equal("api", view.Agents[0].Mode);
        Assert.False(view.Agents[0].NeedsOwnCredential);
        Assert.Equal("webhook", view.Agents[1].Mode);
        Assert.True(view.Agents[1].NeedsOwnCredential);
        Assert.True(view.Credentials[0].Configured);
    }

    /// <summary>An agent the modes map does not mention is off, not unknown.</summary>
    [Fact]
    public async Task An_agent_with_no_mode_is_off()
    {
        var core = new FakeCore().AnswerOk("agents", new
        {
            agents = new[] { new { id = "astrid", name = "Astrid" } },
            modes = new Dictionary<string, string>(),
            credentials = Array.Empty<object>(),
        });
        var view = new SettingsViewModel(core).Agents;

        await view.LoadAgentsAsync();

        Assert.Equal("off", view.Agents[0].Mode);
    }

    /// <summary>
    /// The model behind @astrid (task 810e1876): the options and the choice come from the core;
    /// choosing sends the agent's id, choosing "let Astrid choose" sends none, and the page
    /// redraws from what the core answers.
    /// </summary>
    [Fact]
    public async Task The_model_behind_astrid_is_listed_chosen_and_cleared_task_810e1876()
    {
        var core = new FakeCore()
            .AnswerOk("astridModel", new
            {
                options = new[]
                {
                    new { id = "claude-agent", name = "Claude", service = "claude", serviceLabel = "claude", isSelected = true },
                    new { id = "own", name = "Mine", service = "openclaw", serviceLabel = "Custom Agent", isSelected = false },
                },
                selected = "claude-agent",
            })
            .AnswerOk("setAstridModel", new
            {
                options = new[]
                {
                    new { id = "claude-agent", name = "Claude", service = "claude", serviceLabel = "claude", isSelected = false },
                    new { id = "own", name = "Mine", service = "openclaw", serviceLabel = "Custom Agent", isSelected = true },
                },
                selected = "own",
            })
            .AnswerOk("setAstridModel", new
            {
                options = new[]
                {
                    new { id = "claude-agent", name = "Claude", service = "claude", serviceLabel = "claude", isSelected = false },
                },
                selected = (string?)null,
            });
        var view = new SettingsViewModel(core).Agents;
        Assert.False(view.NoAstridModels, "not asked yet is not none");

        await view.LoadAstridModelAsync();
        Assert.Equal(2, view.AstridModels.Count);
        Assert.True(view.HasAstridModels);
        Assert.Equal("claude-agent", view.SelectedAstridModelId);
        Assert.False(view.LetsAstridChoose);
        Assert.Equal("Custom Agent", view.AstridModels[1].ServiceLabel);

        Assert.True(await view.ChooseAstridModelAsync("own"));
        Assert.Contains(core.Sent, json => json.Contains("\"kind\":\"setAstridModel\"") && json.Contains("\"agentId\":\"own\""));
        Assert.Equal("own", view.SelectedAstridModelId);
        Assert.True(view.AstridModels[1].IsSelected);

        Assert.True(await view.ChooseAstridModelAsync(null));
        Assert.Null(view.SelectedAstridModelId);
        Assert.True(view.LetsAstridChoose);
        Assert.Single(view.AstridModels);
    }

    /// <summary>With nothing that could power it, the page says what to do rather than showing an empty list.</summary>
    [Fact]
    public async Task With_no_agent_to_choose_the_page_says_so_task_810e1876()
    {
        var core = new FakeCore().AnswerOk("astridModel", new { options = Array.Empty<object>(), selected = (string?)null });
        var view = new SettingsViewModel(core).Agents;

        await view.LoadAstridModelAsync();

        Assert.True(view.NoAstridModels);
        Assert.False(view.HasAstridModels);
    }

    /// <summary>A blank box is not a key.</summary>
    [Fact]
    public async Task An_empty_key_is_not_sent()
    {
        var core = new FakeCore();
        var view = new SettingsViewModel(core).Agents;

        Assert.False(await view.SaveCredentialAsync("openai", "   "));
        Assert.Empty(core.Sent);
    }
}
