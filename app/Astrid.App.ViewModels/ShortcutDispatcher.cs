using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// A key press, turned into something the app does.
/// </summary>
/// <remarks>
/// <para>
/// The <b>meaning</b> of a key is decided in <c>astrid_core::keyboard</c>, which is locked against
/// web's own table by a generated fixture — including the guard about when a key may fire at all,
/// which is the half that is easiest to get wrong and impossible to see in a list of shortcuts.
/// This class asks what the key means and then does it.
/// </para>
/// <para>
/// The scheme is bare-key and modifier-less, Gmail-style, so muscle memory transfers between web,
/// Mac and Windows unchanged. Ctrl chords are additive shell concerns and are handled separately —
/// a bare key from the shared table must never be shadowed by a Windows accelerator.
/// </para>
/// </remarks>
public sealed class ShortcutDispatcher
{
    private readonly IAstridCore _core;
    private readonly ShellViewModel _shell;

    public ShortcutDispatcher(IAstridCore core, ShellViewModel shell)
    {
        _core = core;
        _shell = shell;
    }

    /// <summary>Raised when an action needs the window rather than the view models.</summary>
    /// <remarks>
    /// Focusing a text box, opening a sheet: things a view model has no business doing. The window
    /// subscribes and does them.
    /// </remarks>
    public event Action<string>? ShellActionRequested;

    /// <summary>
    /// Handle a key press.
    /// </summary>
    /// <returns><c>true</c> when the key was used and the event should stop here.</returns>
    public async Task<bool> HandleAsync(string key, bool isTextFieldFocused, bool isModalPresented,
        CancellationToken cancellationToken = default)
    {
        var response = await _core.CallAsync(
            Commands.ResolveShortcut(key, _shell.Tasks.HasSelection, isTextFieldFocused, isModalPresented),
            cancellationToken);
        if (!response.Ok
            || !response.Value.TryGetProperty("action", out var element)
            || element.ValueKind == System.Text.Json.JsonValueKind.Null)
        {
            return false;
        }

        var action = element.GetString();
        if (action is null)
        {
            return false;
        }

        var selected = _shell.Tasks.Selected;
        switch (action)
        {
            case "selectNext":
                _shell.Tasks.MoveSelection(1);
                return true;
            case "selectPrevious":
                _shell.Tasks.MoveSelection(-1);
                return true;

            case "completeTask":
                if (selected is not null)
                {
                    // The complete command, never an update: a repeating task rolls forward.
                    await _shell.Tasks.SetCompletedAsync(selected.Id, !selected.Completed, cancellationToken);
                }
                return true;

            case "deleteTask":
                if (selected is not null)
                {
                    await _shell.Tasks.DeleteTaskAsync(selected.Id, cancellationToken);
                }
                return true;

            case "priorityNone":
            case "priorityLow":
            case "priorityMedium":
            case "priorityHigh":
                if (selected is not null)
                {
                    await _shell.Tasks.SetPriorityAsync(selected.Id, PriorityOf(action), cancellationToken);
                }
                return true;

            case "removeDueDate":
                if (selected is not null)
                {
                    await _shell.Tasks.ClearDueDateAsync(selected.Id, cancellationToken);
                }
                return true;

            case "newTask":
            case "showShortcuts":
            case "editTitle":
            case "editDescription":
            case "addComment":
            case "togglePanel":
                // The window's business: focus something, open something.
                ShellActionRequested?.Invoke(action);
                return true;

            default:
                // A shortcut this build has a name for but no behaviour yet. Swallowed rather than
                // passed on: letting it fall through to the ListView would make a bare key that is
                // *supposed* to do something instead do something else, which is worse than
                // nothing happening.
                return true;
        }
    }

    private static int PriorityOf(string action) => action switch
    {
        "priorityHigh" => 3,
        "priorityMedium" => 2,
        "priorityLow" => 1,
        _ => 0,
    };
}
