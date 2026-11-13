using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Astrid.App.Views;

/// <summary>
/// Asks what a task entering Waiting is waiting for (task 7462618e).
/// </summary>
/// <remarks>
/// <para>
/// Three controls and nothing else: a date, a task, and whether to keep the assignee. It does not
/// judge the answer. Whether a date is far enough ahead to count, and whether a blocker already on
/// the task counts instead, are <c>astrid_rules::waiting</c>'s; the core is free to refuse what is
/// typed here, in which case the view model puts this dialog back up.
/// </para>
/// <para>
/// Built in code rather than as a page, which is what a one-off <see cref="ContentDialog"/> costs
/// least as — the same shape <see cref="ListSettingsFlyout"/> uses for its transfer confirmation.
/// Every string is a resource; none is a literal.
/// </para>
/// </remarks>
internal static class WaitConditionDialog
{
    /// <summary>
    /// Put the question, and answer it.
    /// </summary>
    /// <param name="prompt">What is going into which column — the core named both.</param>
    /// <param name="search">
    /// Finds candidate tasks to wait on. A delegate rather than a core handle: this dialog can
    /// search and can do nothing else.
    /// </param>
    /// <returns>The answer, or null when it was cancelled — which leaves the task where it was.</returns>
    public static async Task<WaitCondition?> AskAsync(
        XamlRoot? xamlRoot,
        WaitPrompt prompt,
        Func<string, Task<IReadOnlyList<TaskRow>>> search)
    {
        var date = new CalendarDatePicker
        {
            PlaceholderText = Strings.Get("wait.until_placeholder"),
            // Today cannot be a condition — core counts a date only when it is strictly ahead —
            // so the calendar does not offer a day that would come straight back as a refusal.
            MinDate = DateTimeOffset.Now.Date.AddDays(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var task = new AutoSuggestBox
        {
            PlaceholderText = Strings.Get("wait.on_placeholder"),
            TextMemberPath = nameof(TaskRow.Title),
            DisplayMemberPath = nameof(TaskRow.Title),
            QueryIcon = new SymbolIcon(Symbol.Find),
        };
        var keepAssigned = new ToggleSwitch
        {
            Header = Strings.Get("wait.keep_assigned"),
            IsOn = false,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        TaskRow? chosen = null;
        task.TextChanged += async (sender, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }
            // Typing again abandons the last choice: a suggestion left selected behind an edited
            // query is how the wrong task becomes the blocker.
            chosen = null;
            var query = sender.Text?.Trim() ?? string.Empty;
            sender.ItemsSource = query.Length == 0 ? null : await search(query);
        };
        task.SuggestionChosen += (_, args) =>
        {
            chosen = args.SelectedItem as TaskRow;
        };

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            Text = Strings.Get("wait.explanation", prompt.ColumnName),
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock { Text = Strings.Get("wait.until_label") });
        body.Children.Add(date);
        body.Children.Add(new TextBlock { Text = Strings.Get("wait.on_label") });
        body.Children.Add(task);
        body.Children.Add(keepAssigned);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = Strings.Get("wait.title"),
            Content = body,
            PrimaryButtonText = Strings.Get("wait.confirm"),
            CloseButtonText = Strings.Get("dialog.cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        // An all-day date: the user picked a day, not an instant, and core stores such a date at
        // midnight UTC and counts it until that day starts.
        var picked = date.Date;
        return new WaitCondition(
            DueDateTime: picked?.UtcDateTime.Date.ToString("yyyy-MM-ddTHH:mm:ssZ",
                System.Globalization.CultureInfo.InvariantCulture),
            IsAllDay: picked is null ? null : true,
            WaitOnTaskId: chosen?.Id,
            KeepAssignee: keepAssigned.IsOn);
    }
}
