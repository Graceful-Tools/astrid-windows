using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;
using static Astrid.App.Tests.SettingsFixtures;

namespace Astrid.App.Tests;

/// <summary>The task settings: the email defaults, the layout, subtasks and smart parsing.</summary>
public sealed class TaskSettingsViewModelTests
{
    /// <summary>
    /// Appearance reads whether smart parsing is on and where subtasks go, writes each as one
    /// field, and announces a subtask change the way it announces a layout change — the rows are
    /// different now (task 6ac2639a).
    /// </summary>
    [Fact]
    public async Task Appearance_reads_smart_parsing_and_subtasks_and_a_subtask_change_redraws_rows_task_6ac2639a()
    {
        var core = new FakeCore()
            .AnswerOk("settings", WithAppearance(true, "indented"))
            .AnswerOk("updateSmartTaskSettings", WithAppearance(false, "indented"))
            .AnswerOk("updateSmartTaskSettings", WithAppearance(false, "under_parent"));
        var settings = new SettingsViewModel(core);
        var view = settings.Tasks;
        var redraws = 0;
        view.DisplayModeChanged += () => redraws++;

        await settings.LoadAsync();
        Assert.True(view.SmartParsingEnabled);
        Assert.Equal(2, view.SubtaskChoices.Count);
        Assert.Equal("smart.subtasks.indented", view.SelectedSubtaskDisplay?.TitleKey);

        Assert.True(await view.SetSmartTaskAsync("smartTaskCreationEnabled", false));
        Assert.False(view.SmartParsingEnabled);
        Assert.Equal(0, redraws);
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"updateSmartTaskSettings\"")
            && json.Contains("\"changes\":{\"smartTaskCreationEnabled\":false}"));

        Assert.True(await view.SetSmartTaskAsync("subtaskDisplay", "under_parent"));
        Assert.Equal("under_parent", view.SelectedSubtaskDisplay?.Value);
        Assert.Equal(1, redraws);
    }

    /// <summary>
    /// The Tasks page reads the core's shaped defaults and its choices, lights the current one in
    /// each combo, writes one field per change, and says so when the layout changed — because the
    /// rows have to be redrawn for that one (task c0f3db19).
    /// </summary>
    [Fact]
    public async Task Task_settings_read_the_defaults_write_one_field_and_announce_a_layout_change_task_c0f3db19()
    {
        var core = new FakeCore()
            .AnswerOk("settings", WithSmartTasks("1_week", "17:00", "list"))
            .AnswerOk("updateSmartTaskSettings", WithSmartTasks("3_days", "17:00", "list"))
            .AnswerOk("updateSmartTaskSettings", WithSmartTasks("3_days", "17:00", "project"))
            .AnswerFailure("updateSmartTaskSettings", AstridFailureKind.BadRequest, "Invalid defaultTaskDueOffset value");
        var settings = new SettingsViewModel(core);
        var view = settings.Tasks;
        var layoutChanges = 0;
        view.DisplayModeChanged += () => layoutChanges++;

        await settings.LoadAsync();
        Assert.True(view.EmailToTaskEnabled);
        Assert.Equal(4, view.DueOffsetChoices.Count);
        Assert.Equal("smart.offset.1_week", view.SelectedDueOffset?.TitleKey);
        Assert.Equal("17:00", view.SelectedDueTime?.Value);
        Assert.Equal("list", view.SelectedLayout?.Value);
        Assert.Equal("smart.layout.list_desc", view.LayoutDescriptionKey);

        Assert.True(await view.SetSmartTaskAsync("defaultTaskDueOffset", "3_days"));
        Assert.Equal("3_days", view.SelectedDueOffset?.Value);
        Assert.Equal(0, layoutChanges);
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"updateSmartTaskSettings\"")
            && json.Contains("\"changes\":{\"defaultTaskDueOffset\":\"3_days\"}"));

        Assert.True(await view.SetSmartTaskAsync("taskDisplayMode", "project"));
        Assert.Equal(1, layoutChanges);
        Assert.Equal("project", view.SelectedLayout?.Value);
        Assert.Equal("smart.layout.project_desc", view.LayoutDescriptionKey);

        Assert.False(await view.SetSmartTaskAsync("defaultTaskDueOffset", "2_weeks"));
        Assert.Equal("Invalid defaultTaskDueOffset value", settings.ErrorMessage);
        Assert.Equal(1, layoutChanges); // a refused write changes nothing
    }
}
