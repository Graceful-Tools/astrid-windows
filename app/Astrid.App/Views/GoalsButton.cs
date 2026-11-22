using System.ComponentModel;
using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Astrid.App.Views;

/// <summary>
/// "Goals: 2 · Guardrails: 1" above a board, opening to the lists (task ec11e539).
/// </summary>
/// <remarks>
/// <para>
/// Web ADEV-179's compact control: a goal shows its measure, "Target: …" and "Now: …"; a
/// guardrail a program checks says "Enforced". Nothing is drawn when both lists are empty, unless
/// the reader may edit them, so an owner can add the first.
/// </para>
/// <para>
/// Built in code, as <see cref="LeadingPickerFlyout"/> is, because both boards carry it and a
/// flyout declared in one page's markup cannot be shared with another. Every word is a resource;
/// what it shows, and whether it may be edited, is the core's answer.
/// </para>
/// </remarks>
public sealed partial class GoalsButton : ContentControl
{
    public static readonly DependencyProperty GoalsProperty = DependencyProperty.Register(
        nameof(Goals), typeof(GoalsViewModel), typeof(GoalsButton),
        new PropertyMetadata(null, (owner, change) => ((GoalsButton)owner).Watch(
            change.OldValue as GoalsViewModel, change.NewValue as GoalsViewModel)));

    public GoalsButton()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        Visibility = Visibility.Collapsed;
    }

    public GoalsViewModel? Goals
    {
        get => (GoalsViewModel?)GetValue(GoalsProperty);
        set => SetValue(GoalsProperty, value);
    }

    private void Watch(GoalsViewModel? before, GoalsViewModel? after)
    {
        if (before is not null)
        {
            before.PropertyChanged -= OnGoalsChanged;
        }
        if (after is not null)
        {
            after.PropertyChanged += OnGoalsChanged;
        }
        Rebuild();
    }

    private void OnGoalsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(GoalsViewModel.IsShown) or nameof(GoalsViewModel.Goals)
            or nameof(GoalsViewModel.Guardrails) or nameof(GoalsViewModel.CanEdit))
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        if (Goals is not { IsShown: true } goals)
        {
            Visibility = Visibility.Collapsed;
            Content = null;
            return;
        }
        var label = goals.HasAny
            ? Strings.Get("projectMode.goals.counts", goals.Goals.Count, goals.Guardrails.Count)
            : Strings.Get("projectMode.goals.title");
        var button = new Button
        {
            Content = label,
            Flyout = new Flyout
            {
                Content = Lists(goals),
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            },
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        Content = button;
        Visibility = Visibility.Visible;
    }

    /// <summary>The flyout: the goals, the guardrails, and Edit for an owner.</summary>
    private UIElement Lists(GoalsViewModel goals)
    {
        var body = new StackPanel { Spacing = 12, MinWidth = 280, MaxWidth = 420 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            body, Strings.Get("projectMode.goals.title"));
        if (goals.Goals.Count > 0)
        {
            body.Children.Add(Heading("projectMode.goals.goalsHeading"));
            foreach (var goal in goals.Goals)
            {
                body.Children.Add(GoalRow(goal));
            }
        }
        if (goals.Guardrails.Count > 0)
        {
            body.Children.Add(Heading("projectMode.goals.guardrailsHeading"));
            foreach (var guardrail in goals.Guardrails)
            {
                body.Children.Add(GuardrailRow(guardrail));
            }
        }
        if (goals.CanEdit)
        {
            var edit = new Button { Content = Strings.Get("projectMode.goals.edit") };
            edit.Click += async (_, _) =>
            {
                if (Content is Button { Flyout: { } open })
                {
                    open.Hide();
                }
                await GoalsEditorDialog.ShowAsync(XamlRoot, goals);
            };
            body.Children.Add(edit);
        }
        return body;
    }

    private static TextBlock Heading(string key) => new()
    {
        Text = Strings.Get(key),
        FontSize = 12,
        Opacity = 0.7,
    };

    private static UIElement GoalRow(Goal goal)
    {
        var row = new StackPanel { Spacing = 2 };
        row.Children.Add(new TextBlock { Text = goal.Statement, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(goal.Measure))
        {
            parts.Add(goal.Measure);
        }
        if (!string.IsNullOrWhiteSpace(goal.Target))
        {
            parts.Add(Strings.Get("projectMode.goals.targetValue", goal.Target));
        }
        if (!string.IsNullOrWhiteSpace(goal.Current))
        {
            parts.Add(Strings.Get("projectMode.goals.currentValue", goal.Current));
        }
        if (parts.Count > 0)
        {
            row.Children.Add(Muted(string.Join(" · ", parts)));
        }
        return row;
    }

    private static UIElement GuardrailRow(Guardrail guardrail)
    {
        var row = new StackPanel { Spacing = 2 };
        row.Children.Add(new TextBlock { Text = guardrail.Statement, TextWrapping = TextWrapping.Wrap });
        if (guardrail.IsEnforced)
        {
            row.Children.Add(Muted(Strings.Get("projectMode.goals.kindEnforced")));
        }
        return row;
    }

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        // The theme the app is wearing, not the application dictionary's: the appearance is set
        // on the page, and a brush read from Application.Resources stays light in dark.
        Foreground = ThemeBrushes.For("AstridTextMuted"),
    };
}
