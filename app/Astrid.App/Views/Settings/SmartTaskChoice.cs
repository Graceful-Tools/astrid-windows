using Astrid.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Astrid.App.Views.Settings;

/// <summary>
/// One handler for the smart-task combos on the Tasks and Appearance pages: each row knows which
/// field it is a value of, so the control does not have to.
/// </summary>
internal static class SmartTaskChoice
{
    internal static async Task ChosenAsync(TaskSettingsViewModel settings, object sender)
    {
        if (sender is not ComboBox { SelectedItem: DefaultChoice choice })
        {
            return;
        }
        var current = choice.Field switch
        {
            "defaultTaskDueOffset" => settings.SmartTasks.DefaultTaskDueOffset,
            "defaultDueTime" => settings.SmartTasks.DefaultDueTime,
            "taskDisplayMode" => settings.SmartTasks.TaskDisplayMode,
            "subtaskDisplay" => settings.SmartTasks.SubtaskDisplay,
            _ => null,
        };
        if (choice.Value is null || choice.Value == current)
        {
            return;
        }
        await settings.SetSmartTaskAsync(choice.Field, choice.Value);
    }
}
