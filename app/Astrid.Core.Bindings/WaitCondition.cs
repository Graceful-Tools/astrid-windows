namespace Astrid.Core.Bindings;

/// <summary>
/// What a move into Waiting carries: the condition it waits on, and whether to keep the assignee.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>astrid_core::app::command::WaitOptions</c>, which is flattened into every command
/// that can put a task in Waiting, so these four keys sit beside each command's own rather than
/// nested under a <c>wait</c> object. All four are optional and a command that sends none behaves
/// as it always did, except that entering Waiting now needs a condition.
/// </para>
/// <para>
/// This carries the answer; it does not judge it. Whether a date is far enough ahead to count, and
/// whether a blocker already on the task counts instead, are <c>astrid_rules::waiting</c>'s —
/// which is why <see cref="NamesACondition"/> asks only whether the user typed something, and the
/// core is still free to refuse what they typed.
/// </para>
/// </remarks>
/// <param name="DueDateTime">
/// ISO-8601. Wait until then, and write it to the task's due date in the same operation.
/// </param>
/// <param name="IsAllDay">Whether <paramref name="DueDateTime"/> is a day rather than an instant.</param>
/// <param name="WaitOnTaskId">Wait on this task; it is attached as a blocker before the move.</param>
/// <param name="KeepAssignee">
/// Entering Waiting clears the assignee unless this is true. Off by default on purpose: a task
/// nobody is waiting for is nobody's, and that is the decision of 2026-11-12.
/// </param>
public sealed record WaitCondition(
    string? DueDateTime = null,
    bool? IsAllDay = null,
    string? WaitOnTaskId = null,
    bool KeepAssignee = false)
{
    /// <summary>The refusal code core answers with when Waiting was asked for without a condition.</summary>
    public const string NeedsConditionCode = "WAITING_NEEDS_CONDITION";

    /// <summary>Whether the user named a condition at all.</summary>
    /// <remarks>
    /// With neither a date nor a task there is nothing to send: core would refuse the command
    /// again for exactly the reason it refused the first one, and the round trip would only make
    /// the prompt flicker.
    /// </remarks>
    public bool NamesACondition => DueDateTime is not null || WaitOnTaskId is not null;

    /// <summary>
    /// <c>keepAssignee</c> as it goes on the wire, or null when it is the default.
    /// </summary>
    /// <remarks>
    /// Null rather than <c>false</c> so <c>DefaultIgnoreCondition = WhenWritingNull</c> drops the
    /// key, and a move that says nothing about the assignee keeps the wire shape it has always had.
    /// </remarks>
    internal bool? KeepAssigneeOrNull => KeepAssignee ? true : null;
}
