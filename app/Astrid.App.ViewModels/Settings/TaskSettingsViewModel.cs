using System.Collections.ObjectModel;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The task settings (task c0f3db19): email-to-task and the defaults for tasks created by email,
/// the task-detail layout, where subtasks go, and whether quick add reads <c>#list</c> tags.
/// </summary>
/// <remarks>
/// One server setting, one write, shown on two pages: the Tasks page draws the email defaults, and
/// the Appearance page draws the layout, the subtasks and smart parsing (task 6ac2639a). They are
/// one view model because they are one blob — a page that held half of it would have to be told
/// when the other half changed.
/// </remarks>
public sealed class TaskSettingsViewModel : ObservableObject
{
    private readonly SettingsSession _session;
    private SmartTaskSettings _smartTasks = new();

    public TaskSettingsViewModel(SettingsSession session)
    {
        _session = session;
        _session.AccountRead += account =>
        {
            ReplaceChoices(DueOffsetChoices, "defaultTaskDueOffset", account.DueOffsetChoices);
            ReplaceChoices(DueTimeChoices, "defaultDueTime", account.DueTimeChoices);
            ReplaceChoices(LayoutChoices, "taskDisplayMode", account.LayoutChoices);
            ReplaceChoices(SubtaskChoices, "subtaskDisplay", account.SubtaskChoices);
            SmartTasks = account.SmartTasks;
        };
    }

    /// <summary>
    /// The task defaults and the task-detail layout. Shaped by the core, so a server that has
    /// never stored them reads as the web's defaults here too.
    /// </summary>
    public SmartTaskSettings SmartTasks
    {
        get => _smartTasks;
        private set
        {
            if (Set(ref _smartTasks, value))
            {
                Raise(nameof(EmailToTaskEnabled));
                Raise(nameof(SelectedDueOffset));
                Raise(nameof(SelectedDueTime));
                Raise(nameof(SelectedLayout));
                Raise(nameof(LayoutDescriptionKey));
                Raise(nameof(SmartParsingEnabled));
                Raise(nameof(SelectedSubtaskDisplay));
            }
        }
    }

    public bool EmailToTaskEnabled => SmartTasks.EmailToTaskEnabled;

    /// <summary>
    /// Whether the quick-add box's <c>#list</c> tags file the task (task 6ac2639a). The rule is
    /// the core's; this is only whether the account has it on.
    /// </summary>
    public bool SmartParsingEnabled => SmartTasks.SmartTaskCreationEnabled;

    /// <summary>The two subtask layouts, for the Appearance page.</summary>
    public ObservableCollection<DefaultChoice> SubtaskChoices { get; } = [];

    public DefaultChoice? SelectedSubtaskDisplay =>
        SubtaskChoices.FirstOrDefault(choice => choice.Value == SmartTasks.SubtaskDisplay);

    /// <summary>The due-date offsets the Tasks page offers, in the core's order.</summary>
    public ObservableCollection<DefaultChoice> DueOffsetChoices { get; } = [];

    /// <summary>The due times the Tasks page offers.</summary>
    public ObservableCollection<DefaultChoice> DueTimeChoices { get; } = [];

    /// <summary>The two task-detail layouts, for the Appearance page.</summary>
    public ObservableCollection<DefaultChoice> LayoutChoices { get; } = [];

    public DefaultChoice? SelectedDueOffset =>
        DueOffsetChoices.FirstOrDefault(choice => choice.Value == SmartTasks.DefaultTaskDueOffset);

    public DefaultChoice? SelectedDueTime =>
        DueTimeChoices.FirstOrDefault(choice => choice.Value == SmartTasks.DefaultDueTime);

    public DefaultChoice? SelectedLayout =>
        LayoutChoices.FirstOrDefault(choice => choice.Value == SmartTasks.TaskDisplayMode);

    /// <summary>The line under the layout combo, as a key: what the chosen layout does.</summary>
    public string LayoutDescriptionKey => $"smart.layout.{SmartTasks.TaskDisplayMode}_desc";

    /// <summary>
    /// Raised when the task-detail layout or the subtask layout changes, so the shell can redraw
    /// the rows and the open task: what a row is, and what its leading control does, are different
    /// now.
    /// </summary>
    public event Action? DisplayModeChanged;

    /// <summary>
    /// Change one task setting. The core merges it and refuses what the server would refuse; the
    /// screen redraws from the answer.
    /// </summary>
    public async Task<bool> SetSmartTaskAsync(string field, object? value,
        CancellationToken cancellationToken = default)
    {
        var before = (SmartTasks.TaskDisplayMode, SmartTasks.SubtaskDisplay);
        var response = await _session.Core.CallAsync(
            Commands.UpdateSmartTaskSettings(new Dictionary<string, object?> { [field] = value }),
            cancellationToken);
        if (!_session.Read(response))
        {
            return false;
        }
        if ((SmartTasks.TaskDisplayMode, SmartTasks.SubtaskDisplay) != before)
        {
            DisplayModeChanged?.Invoke();
        }
        return true;
    }

    /// <summary>
    /// Refill a combo's choices only when they differ, so a combo bound to them does not lose its
    /// selection on every settings answer.
    /// </summary>
    private static void ReplaceChoices(ObservableCollection<DefaultChoice> target, string field,
        IReadOnlyList<SettingChoice> choices)
    {
        if (target.Count == choices.Count
            && target.Zip(choices).All(pair => pair.First.Value == pair.Second.Value))
        {
            return;
        }
        target.Clear();
        foreach (var choice in choices)
        {
            target.Add(new DefaultChoice(field, choice.Value, choice.TitleKey, null, false));
        }
    }
}
