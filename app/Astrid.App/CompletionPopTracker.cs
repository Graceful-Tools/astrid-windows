using System.Collections.Generic;
using Astrid.Core.Bindings;

namespace Astrid.App;

/// <summary>
/// DS16's pop for the mark of a task that was just completed, wherever its mark is rebuilt
/// (tasks 787c7aa5, 4717a2fd, 27c773f5).
/// </summary>
/// <remarks>
/// <para>
/// The mark cannot tell on its own. The list replaces a completed row's item and the boards rebuild
/// their columns, so the completed task arrives with a <b>new</b> mark that is built already ticked
/// and never sees the tick arrive (see <see cref="PriorityMark.PopWhenLoaded"/>).
/// </para>
/// <para>
/// What tells a finished task from a done card scrolling into view or a view reloading is what this
/// view last drew for that task: unfinished then and finished now is the one that pops. A task this
/// view has never drawn does not, and the re-sort or second realisation that follows sees it done
/// already, so it pops once. One tracker per view, because "last drawn" is that view's own memory.
/// </para>
/// </remarks>
internal sealed class CompletionPopTracker
{
    private readonly Dictionary<string, bool> _lastSeenCompleted = new();

    /// <summary>Note that <paramref name="mark"/> now draws <paramref name="row"/>, and pop it if
    /// the task was unfinished the last time this view drew it.</summary>
    public void Drawn(TaskRow row, PriorityMark mark)
    {
        var wasOpen = _lastSeenCompleted.TryGetValue(row.Id, out var was) && !was;
        _lastSeenCompleted[row.Id] = row.Completed;
        if (wasOpen && row.Completed)
        {
            mark.PopWhenLoaded();
        }
    }
}
