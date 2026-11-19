using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Xunit;
using static Astrid.App.Tests.SettingsFixtures;

namespace Astrid.App.Tests;

/// <summary>The task settings: the email defaults, subtasks and smart parsing.</summary>
public sealed class TaskSettingsViewModelTests
{
    /// <summary>
    /// Appearance reads whether smart parsing is on and where subtasks go, writes each as one
    /// field, and announces a subtask change — the rows are different now (task 6ac2639a).
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
    /// each combo, and writes one field per change, which redraws no rows (task c0f3db19). There
    /// is no layout to change any more: the core dropped the project display mode (ADEV-72).
    /// </summary>
    [Fact]
    public async Task Task_settings_read_the_defaults_and_write_one_field_task_c0f3db19()
    {
        var core = new FakeCore()
            .AnswerOk("settings", WithSmartTasks("1_week", "17:00"))
            .AnswerOk("updateSmartTaskSettings", WithSmartTasks("3_days", "17:00"))
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

        Assert.True(await view.SetSmartTaskAsync("defaultTaskDueOffset", "3_days"));
        Assert.Equal("3_days", view.SelectedDueOffset?.Value);
        Assert.Equal(0, layoutChanges);
        Assert.Contains(core.Sent, json =>
            json.Contains("\"kind\":\"updateSmartTaskSettings\"")
            && json.Contains("\"changes\":{\"defaultTaskDueOffset\":\"3_days\"}"));

        Assert.False(await view.SetSmartTaskAsync("defaultTaskDueOffset", "2_weeks"));
        Assert.Equal("Invalid defaultTaskDueOffset value", settings.ErrorMessage);
        Assert.Equal(0, layoutChanges); // a refused write changes nothing
    }
}
