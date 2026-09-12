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
