using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The question the shell must ask before a task can enter Waiting (task 7462618e).
/// </summary>
/// <remarks>
/// Only what the picker has to draw: which column was asked for, and what is going into it. The
/// command to re-send is not here — it is a closure the view model kept, so the dialog cannot
/// assemble a command of its own.
/// </remarks>
/// <param name="ColumnId">The column the move was headed for.</param>
/// <param name="ColumnName">Its name, for the prompt's title. The core named it; this does not.</param>
/// <param name="TaskId">The task being moved, or null when a new card is being typed into Waiting.</param>
/// <param name="Title">The new card's title, when that is what this is. Null for a move.</param>
public sealed record WaitPrompt(string ColumnId, string ColumnName, string? TaskId, string? Title);

/// <summary>
/// A move into Waiting that is waiting on an answer.
/// </summary>
/// <remarks>
/// <para>
/// Held by composition in both <see cref="BoardViewModel"/> and <see cref="TaskDetailViewModel"/>:
/// dragging a card to Waiting and choosing Waiting from the Status menu are the same move, refused
/// by the same rule, and two copies of the ask-and-resend dance would be two chances to forget to
/// clear the prompt.
/// </para>
/// <para>
/// The retry is a closure rather than a command, so the only thing that can be sent again is the
/// command that was already refused, with the answer added. Nothing here decides whether the
/// answer is good enough — <c>astrid_rules::waiting</c> does, and is free to refuse a second time,
/// in which case the prompt simply stays up.
/// </para>
/// </remarks>
internal sealed class PendingWait
{
    private readonly Action _changed;
    private Func<WaitCondition, CancellationToken, Task<bool>>? _retry;

    /// <param name="changed">
    /// Raises the owner's change for its <c>WaitPrompt</c> property, which is computed from this
    /// rather than stored — so <c>Set</c> cannot raise it and something has to.
    /// </param>
    public PendingWait(Action changed)
    {
        _changed = changed;
    }

    /// <summary>The question to put, or null when nothing is waiting on an answer.</summary>
    public WaitPrompt? Prompt { get; private set; }

    /// <summary>Remember the refused move, and what to send once it has a condition.</summary>
    public void Ask(WaitPrompt prompt, Func<WaitCondition, CancellationToken, Task<bool>> retry)
    {
        Prompt = prompt;
        _retry = retry;
        _changed();
    }

    /// <summary>Forget it. Nothing was written, so there is nothing to undo.</summary>
    public void Cancel()
    {
        if (Prompt is null)
        {
            return;
        }
        Prompt = null;
        _retry = null;
        _changed();
    }

    /// <summary>
    /// Send the refused move again, carrying <paramref name="condition"/>.
    /// </summary>
    /// <remarks>
    /// An answer naming neither a date nor a task is not sent: core would refuse it for the same
    /// reason, and the only visible effect would be the prompt blinking. The prompt stays up.
    /// </remarks>
    public async Task<bool> ResolveAsync(WaitCondition condition, CancellationToken cancellationToken)
    {
        if (_retry is null || !condition.NamesACondition)
        {
            return false;
        }

        var retry = _retry;
        // Cleared before the call, not after: the retry refreshes the view, and a prompt still
        // standing while the board redraws is a dialog over a board that has already moved on. If
        // the core refuses the answer too, the retry puts the prompt back on its way through.
        Prompt = null;
        _retry = null;
        _changed();
        return await retry(condition, cancellationToken);
    }
}
