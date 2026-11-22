using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// A board's goals and guardrails, for the compact control above a board (task ec11e539).
/// </summary>
/// <remarks>
/// <para>
/// Web ADEV-179 and core ADEV-187: what a project is for and what it must not do, read by every
/// person and agent working on it. The core caches the answer and journals the write through the
/// Outbox when offline; this holds what it answered and nothing it worked out itself.
/// </para>
/// <para>
/// One class for both boards, because they show the same thing: a list's board asks
/// <c>getListGoals</c> (the board that list is on), a project board asks <c>getProjectGoals</c>.
/// Editing is offered on the server's <see cref="CanEdit"/> and writes the whole value, the way
/// the route takes it.
/// </para>
/// </remarks>
public sealed class GoalsViewModel : ObservableObject
{
    private readonly IAstridCore _core;
    private ProjectGoals _value = new();
    private string? _errorMessage;

    public GoalsViewModel(IAstridCore core) => _core = core;

    /// <summary>The board these belong to; null for a list on no board, or before a load.</summary>
    public string? ProjectId => _value.ProjectId;

    public IReadOnlyList<Goal> Goals => _value.Goals;

    public IReadOnlyList<Guardrail> Guardrails => _value.Guardrails;

    /// <summary>Whether there is anything to draw. Both lists empty draws nothing at all.</summary>
    public bool HasAny => Goals.Count > 0 || Guardrails.Count > 0;

    /// <summary>The server's answer: an owner or admin, and not an agent.</summary>
    public bool CanEdit => _value.CanEdit && _value.ProjectId is not null;

    /// <summary>Whether the control is drawn: something to read, or an owner who can add the first.</summary>
    public bool IsShown => HasAny || CanEdit;

    /// <summary>Why the last save did not land, in the core's words; null when it did.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => Set(ref _errorMessage, value);
    }

    /// <summary>The goals of the board a list is on.</summary>
    public Task LoadForListAsync(string listId, CancellationToken cancellationToken = default) =>
        LoadAsync(Commands.GetListGoals(listId), cancellationToken);

    /// <summary>A board's own goals.</summary>
    public Task LoadForProjectAsync(string projectId, CancellationToken cancellationToken = default) =>
        LoadAsync(Commands.GetProjectGoals(projectId), cancellationToken);

    /// <summary>Forget the last board, for a list that has none.</summary>
    public void Clear() => Show(new ProjectGoals());

    /// <summary>
    /// Replace the board's goals and guardrails with these, and draw what the server echoes.
    /// </summary>
    /// <remarks>
    /// A refusal (not an owner, a limit, a duplicate id) leaves what was on screen and says why;
    /// nothing is drawn as saved that the server did not take.
    /// </remarks>
    public async Task<bool> SaveAsync(IReadOnlyList<GoalDraft> goals, IReadOnlyList<GuardrailDraft> guardrails,
        CancellationToken cancellationToken = default)
    {
        if (_value.ProjectId is not { } projectId)
        {
            return false;
        }
        var response = await _core.CallAsync(
            Commands.SetProjectGoals(projectId, goals, guardrails), cancellationToken);
        if (!response.Ok)
        {
            ErrorMessage = response.Error?.Message;
            return false;
        }
        ErrorMessage = null;
        // The echo answers for a project, so it carries no list id; keep the one this was opened on.
        Show((response.Read<ProjectGoals>() ?? new ProjectGoals()) with { ListId = _value.ListId });
        return true;
    }

    private async Task LoadAsync(object command, CancellationToken cancellationToken)
    {
        var response = await _core.CallAsync(command, cancellationToken);
        // A list on no board, or a board this reader cannot see, is nothing to show — not an error.
        Show((response.Ok ? response.Read<ProjectGoals>() : null) ?? new ProjectGoals());
    }

    private void Show(ProjectGoals value)
    {
        _value = value;
        Raise(nameof(ProjectId));
        Raise(nameof(Goals));
        Raise(nameof(Guardrails));
        Raise(nameof(HasAny));
        Raise(nameof(CanEdit));
        Raise(nameof(IsShown));
    }
}
