using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// A drag that can start.
/// </summary>
/// <remarks>
/// <para>
/// WinUI starts a drag from a <c>CanDrag="True"</c> element when the pointer moves while pressed
/// on it. It does not do that for a control that takes the press for itself — a Button, a
/// TextBox, a ToggleButton — and by design rather than by accident: a press on a button is a
/// click, and the framework does not guess which of the two the person meant. Marking one
/// <c>CanDrag</c> compiles, runs and does nothing at all.
/// </para>
/// <para>
/// That is how the board's cards were not draggable for a fortnight (task b8e42e70): the drop
/// targets, the payload and the move were all there, and the card was a Button. A control that
/// starts its own drag — the shell's <c>BoardCard</c> — is the one way to keep a card a button,
/// with everything a button gives a keyboard and a screen reader, and still drag it.
/// </para>
/// </remarks>
public sealed class DragRulesTests
{
    /// <summary>The WinUI controls that swallow the press. Not draggable, whatever the attribute says.</summary>
    private static readonly HashSet<string> TakesThePress = new(StringComparer.Ordinal)
    {
        "Button", "ToggleButton", "RepeatButton", "HyperlinkButton", "DropDownButton", "SplitButton",
        "ToggleSplitButton", "AppBarButton", "AppBarToggleButton", "CheckBox", "RadioButton",
        "TextBox", "PasswordBox", "RichEditBox", "AutoSuggestBox", "NumberBox", "Slider",
        "ScrollBar", "ComboBox",
    };

    /// <summary>
    /// <c>CanDrag</c> on a control WinUI will not drag from is a drag nobody can start
    /// (task b8e42e70).
    /// </summary>
    [Fact]
    public void A_draggable_element_is_one_WinUI_will_actually_drag_task_b8e42e70()
    {
        var app = LocalisationTests.AppDirectory();
        var inert = new List<string>();

        foreach (var path in LocalisationTests.WindowXaml(app))
        {
            foreach (var (tag, attributes) in LocalisationTests.Elements(File.ReadAllText(path)))
            {
                if (attributes.TryGetValue("CanDrag", out var canDrag)
                    && string.Equals(canDrag, "True", StringComparison.OrdinalIgnoreCase)
                    && TakesThePress.Contains(tag))
                {
                    inert.Add($"{Path.GetFileName(path)}: <{tag} CanDrag=\"True\">");
                }
            }
        }

        Assert.True(inert.Count == 0,
            "CanDrag on a control that takes the press for itself never starts a drag — "
            + "use a control that starts its own (see BoardCard):\n  " + string.Join("\n  ", inert));
    }
}
