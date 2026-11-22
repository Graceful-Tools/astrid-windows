using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Astrid.App.Views;

/// <summary>
/// Edit a board's goals and guardrails, and save the whole value (task ec11e539).
/// </summary>
/// <remarks>
/// <para>
/// Web ADEV-179's project-settings editor, offered here from the goals control because this shell
/// has no project settings screen. Same fields, same words: a goal's statement, measure, target and
/// current value; a guardrail's statement and whether it is stated or enforced.
/// </para>
/// <para>
/// It decides nothing. Rows keep the ids they arrived with, a new row has none and the server
/// assigns one, and a blank row is sent as it is for the server to drop. A refused save keeps the
/// dialog open with the reason, so nothing typed is lost.
/// </para>
/// </remarks>
internal static class GoalsEditorDialog
{
    private sealed record GoalFields(string? Id, TextBox Statement, TextBox Measure, TextBox Target, TextBox Current);

    private sealed record GuardrailFields(string? Id, TextBox Statement, ComboBox Kind);

    /// <summary>The two kinds, in web's order, with their words.</summary>
    private static readonly (string Kind, string Key)[] Kinds =
    [
        ("stated", "projectMode.goals.kindStated"),
        ("enforced", "projectMode.goals.kindEnforced"),
    ];

    public static async Task ShowAsync(XamlRoot xamlRoot, GoalsViewModel goals)
    {
        var goalRows = new List<GoalFields>();
        var guardrailRows = new List<GuardrailFields>();
        var goalPanel = new StackPanel { Spacing = 10 };
        var guardrailPanel = new StackPanel { Spacing = 10 };

        void AddGoal(Goal? goal)
        {
            var fields = new GoalFields(goal?.Id,
                Field("projectMode.goals.goalPlaceholder", goal?.Statement),
                Field("projectMode.goals.measurePlaceholder", goal?.Measure),
                Field("projectMode.goals.targetPlaceholder", goal?.Target),
                Field("projectMode.goals.currentPlaceholder", goal?.Current));
            goalRows.Add(fields);
            var numbers = new Grid { ColumnSpacing = 6 };
            foreach (var (box, column) in new[] { (fields.Measure, 0), (fields.Target, 1), (fields.Current, 2) })
            {
                numbers.ColumnDefinitions.Add(new ColumnDefinition());
                Grid.SetColumn(box, column);
                numbers.Children.Add(box);
            }
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(fields.Statement);
            row.Children.Add(numbers);
            row.Children.Add(Remove(() =>
            {
                goalRows.Remove(fields);
                goalPanel.Children.Remove(row);
            }));
            goalPanel.Children.Add(row);
        }

        void AddGuardrail(Guardrail? guardrail)
        {
            var kind = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                kind, Strings.Get("projectMode.goals.kindLabel"));
            foreach (var (_, key) in Kinds)
            {
                kind.Items.Add(Strings.Get(key));
            }
            kind.SelectedIndex = guardrail?.IsEnforced == true ? 1 : 0;
            var fields = new GuardrailFields(guardrail?.Id,
                Field("projectMode.goals.guardrailPlaceholder", guardrail?.Statement), kind);
            guardrailRows.Add(fields);
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(fields.Statement);
            row.Children.Add(kind);
            row.Children.Add(Remove(() =>
            {
                guardrailRows.Remove(fields);
                guardrailPanel.Children.Remove(row);
            }));
            guardrailPanel.Children.Add(row);
        }

        foreach (var goal in goals.Goals)
        {
            AddGoal(goal);
        }
        foreach (var guardrail in goals.Guardrails)
        {
            AddGuardrail(guardrail);
        }

        var addGoal = new Button { Content = Strings.Get("projectMode.goals.addGoal") };
        addGoal.Click += (_, _) => AddGoal(null);
        var addGuardrail = new Button { Content = Strings.Get("projectMode.goals.addGuardrail") };
        addGuardrail.Click += (_, _) => AddGuardrail(null);
        var failure = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Foreground = ThemeBrushes.For("AstridError"),
        };

        var body = new StackPanel { Spacing = 12, MinWidth = 420 };
        body.Children.Add(Wrapped(Strings.Get("projectMode.goals.intro")));
        body.Children.Add(Heading("projectMode.goals.goalsHeading"));
        body.Children.Add(goalPanel);
        body.Children.Add(addGoal);
        body.Children.Add(Heading("projectMode.goals.guardrailsHeading"));
        body.Children.Add(Wrapped(Strings.Get("projectMode.goals.kindHint")));
        body.Children.Add(guardrailPanel);
        body.Children.Add(addGuardrail);
        body.Children.Add(failure);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = Strings.Get("projectMode.goals.title"),
            Content = new ScrollViewer { Content = body, MaxHeight = 520 },
            PrimaryButtonText = Strings.Get("account.save"),
            CloseButtonText = Strings.Get("dialog.cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                var saved = await goals.SaveAsync(
                    goalRows.Select(row => new GoalDraft(row.Id, row.Statement.Text,
                        Optional(row.Measure), Optional(row.Target), Optional(row.Current))).ToList(),
                    guardrailRows.Select(row => new GuardrailDraft(row.Id, row.Statement.Text,
                        Kinds[Math.Max(0, row.Kind.SelectedIndex)].Kind)).ToList());
                if (!saved)
                {
                    // Kept open: what was typed is still there to try again.
                    args.Cancel = true;
                    failure.Text = Strings.Get("projectMode.goals.saveFailed");
                    failure.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
    }

    private static TextBox Field(string placeholderKey, string? text)
    {
        var placeholder = Strings.Get(placeholderKey);
        var box = new TextBox { Text = text ?? string.Empty, PlaceholderText = placeholder };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, placeholder);
        return box;
    }

    /// <summary>An empty optional field is sent as absent, not as an empty string.</summary>
    private static string? Optional(TextBox box) =>
        string.IsNullOrWhiteSpace(box.Text) ? null : box.Text;

    private static Button Remove(Action remove)
    {
        var button = new Button { Content = Strings.Get("projectMode.goals.remove") };
        button.Click += (_, _) => remove();
        return button;
    }

    private static TextBlock Heading(string key) => new()
    {
        Text = Strings.Get(key),
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
    };

    private static TextBlock Wrapped(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
        Foreground = ThemeBrushes.For("AstridTextMuted"),
    };
}
