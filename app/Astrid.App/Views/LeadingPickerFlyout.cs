using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Astrid.App.Views;

/// <summary>
/// The popover a leading mark opens when the core answered <c>openPicker</c> (task 3c0dfb73).
/// </summary>
/// <remarks>
/// <para>
/// Priority, who it goes to, and — when <see cref="LeadingPickerViewModel.ShowsProjectState"/> —
/// the board column, with "Mark as done" at the foot. Web's options sheet behind
/// <c>leadingControlOpensOptions</c> is the same control; iOS's is the quick changer.
/// </para>
/// <para>
/// Built in code rather than as XAML, the way <see cref="WaitConditionDialog"/> is, for one
/// reason: a list row and a board card open the same picker, and a <see cref="Flyout"/> declared
/// in a page's markup cannot be shared by two pages — it would have had to be written twice, which
/// is exactly how a row and a card come to disagree about one task. Every word is a resource.
/// </para>
/// <para>
/// It writes nothing itself. Each control calls the view model, which calls the core; what the
/// picker offers at all was decided before it opened.
/// </para>
/// </remarks>
internal static class LeadingPickerFlyout
{
    /// <summary>The four levels, highest last, as the quick-add settings draw them.</summary>
    private static readonly (int Level, string Glyph, string Key)[] Priorities =
    [
        (0, "○", "priority.none"),
        (1, "!", "priority.low"),
        (2, "!!", "priority.medium"),
        (3, "!!!", "priority.high"),
    ];

    /// <summary>
    /// Put the picker up against the mark that was clicked.
    /// </summary>
    /// <param name="anchor">The mark itself, so the popover points at what was tapped.</param>
    /// <param name="picker">Already opened on a row — its options are loaded or loading.</param>
    public static void ShowAt(FrameworkElement anchor, LeadingPickerViewModel picker)
    {
        var body = new StackPanel { Spacing = 10, MinWidth = 240 };
        body.Children.Add(PriorityRow(picker));
        body.Children.Add(Heading("picker.assignee"));
        body.Children.Add(AssigneeList(picker));
        if (picker.ShowsProjectState && picker.StatusChoices.Count > 0)
        {
            body.Children.Add(Heading("picker.status"));
            body.Children.Add(StatusList(picker));
        }
        body.Children.Add(picker.AsksBeforeCompleting
            ? CompleteQuestion(picker)
            : CompleteButton(picker));

        var flyout = new Flyout
        {
            Content = body,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
        };
        // The view model holds the open task, and a popover that has gone does not. Closing it
        // here is what keeps a stale task id from receiving the next pick.
        flyout.Closed += (_, _) => picker.Close();
        // Named for the reader, not for the task: "Options for Water roses" is what the mark
        // itself now says, so the two agree.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            body, Strings.Get("picker.options_for", picker.Title));
        flyout.ShowAt(anchor);

        void Close() => flyout.Hide();
        foreach (var button in Buttons(body))
        {
            button.Click += (_, _) => Close();
        }
    }

    private static TextBlock Heading(string key) => new()
    {
        Text = Strings.Get(key),
        FontSize = 12,
        Opacity = 0.7,
    };

    private static UIElement PriorityRow(LeadingPickerViewModel picker)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            row, Strings.Get("picker.priority"));
        foreach (var (level, glyph, key) in Priorities)
        {
            var choice = new Button
            {
                Content = glyph,
                Tag = level,
                // The one already set reads as chosen, which a row of four identical buttons
                // otherwise cannot say.
                IsEnabled = level != picker.Priority,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choice, Strings.Get(key));
            choice.Click += async (_, _) => await picker.PickPriorityAsync(level);
            row.Children.Add(choice);
        }
        return row;
    }

    private static UIElement AssigneeList(LeadingPickerViewModel picker)
    {
        var people = new StackPanel { Spacing = 2 };
        foreach (var option in picker.Assignees)
        {
            var name = option.Name is { Length: > 0 } written
                ? written
                : Strings.Get("assignee.unassigned");
            var choice = new Button
            {
                Content = name,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choice, name);
            var userId = option.UserId;
            choice.Click += async (_, _) => await picker.PickAssigneeAsync(userId);
            people.Children.Add(choice);
        }
        return people;
    }

    private static UIElement StatusList(LeadingPickerViewModel picker)
    {
        var columns = new StackPanel { Spacing = 2 };
        foreach (var column in picker.StatusChoices)
        {
            var choice = new Button
            {
                // The column's own name, which the board's owner typed — not a word of ours.
                Content = column.Name,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                IsEnabled = !column.IsCurrent,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(choice, column.Name);
            var columnId = column.Id;
            choice.Click += async (_, _) => await picker.PickStatusAsync(columnId);
            columns.Children.Add(choice);
        }
        return columns;
    }

    /// <summary>
    /// "Mark as done", or "Mark as to-do", beside the task's own checkbox (task 8cf2d10d).
    /// </summary>
    /// <remarks>
    /// ADEV-146/148: a plain row rather than an accent button, and its glyph is the row's own
    /// checkbox, not a bare tick: the task's priority colour, its repeat arrows, unticked beside
    /// "Mark as done" and ticked beside "Mark as to-do". So the row shows what the click will
    /// leave behind on the list, the way web's <c>MarkDoneQuickSetRow</c> and iOS's do.
    /// </remarks>
    private static UIElement CompleteButton(LeadingPickerViewModel picker)
    {
        const double markSize = 32; // the list row's own mark, TaskRowsView.xaml
        var word = Strings.Get(picker.CompletionKey);
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        content.Children.Add(new PriorityMark
        {
            Priority = picker.Priority,
            IsRepeating = picker.IsRepeating,
            ShowTick = picker.Completed,
            Width = markSize,
            Height = markSize,
        });
        content.Children.Add(new TextBlock { Text = word, VerticalAlignment = VerticalAlignment.Center });
        var finish = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(finish, word);
        finish.Click += async (_, _) => await picker.CompleteAsync();
        return finish;
    }

    /// <summary>
    /// Someone else's task, or nobody's: the completion row is the question itself — who holds it,
    /// then Cancel and Complete (task d02d76e4, web's <c>MarkDoneQuickSetRow</c> with
    /// <c>asksBeforeCompleting</c>, ADEV-71).
    /// </summary>
    /// <remarks>
    /// Cancel writes nothing; like every button here it closes the popover. Whether to ask is
    /// <see cref="LeadingPickerViewModel.AsksBeforeCompleting"/>, the core's answer.
    /// </remarks>
    private static UIElement CompleteQuestion(LeadingPickerViewModel picker)
    {
        var question = new StackPanel { Spacing = 8 };
        var holder = picker.HolderName is { } name
            ? Strings.Get(picker.HolderLineKey, name)
            : Strings.Get(picker.HolderLineKey);
        question.Children.Add(new TextBlock
        {
            Text = holder,
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
        });

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancelWord = Strings.Get("common.cancel");
        var cancel = new Button { Content = cancelWord };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(cancel, cancelWord);
        var completeWord = Strings.Get("common.complete");
        var complete = new Button
        {
            Content = completeWord,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(complete, completeWord);
        complete.Click += async (_, _) => await picker.CompleteAsync();
        buttons.Children.Add(cancel);
        buttons.Children.Add(complete);
        question.Children.Add(buttons);
        return question;
    }

    /// <summary>Every button in the popover, so one pick closes it.</summary>
    private static IEnumerable<Button> Buttons(DependencyObject root)
    {
        if (root is Button button)
        {
            yield return button;
            yield break;
        }
        if (root is not Panel panel)
        {
            yield break;
        }
        foreach (var child in panel.Children)
        {
            foreach (var found in Buttons(child))
            {
                yield return found;
            }
        }
    }
}
