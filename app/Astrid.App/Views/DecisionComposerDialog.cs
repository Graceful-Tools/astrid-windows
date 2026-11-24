using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Astrid.App.Views;

/// <summary>
/// "Ask for a decision" from a task's details (task b0f521c6): a question, two or more options,
/// optional criteria rated with five traffic lights, and where to post it.
/// </summary>
/// <remarks>
/// <para>
/// The form is astrid-ios <c>DecisionComposerView.swift</c>'s. It decides nothing: what the text
/// says and what stops it being posted are <see cref="DecisionDraft"/>'s, and the preview is the
/// core's renderer drawing that text through the same <see cref="MarkdownRenderer"/> the thread
/// uses — so what is previewed is what will be drawn.
/// </para>
/// <para>
/// Built in code, like <see cref="WaitConditionDialog"/>. The lists of options and criteria are
/// rebuilt when one is added or removed; typing only redraws the lights and the preview, so a text
/// box never loses its caret under the writer.
/// </para>
/// </remarks>
internal static class DecisionComposerDialog
{
    /// <summary>What was written, and where it goes.</summary>
    public readonly record struct Answer(DecisionDraft Draft, bool InDescription);

    /// <param name="postsAsComment">
    /// False where the board has nowhere for a new comment to go (<c>supports.comments</c>): the
    /// decision can still go into the description.
    /// </param>
    public static async Task<Answer?> AskAsync(XamlRoot? xamlRoot, Func<string, Brush> brush,
        bool postsAsComment)
    {
        var draft = new DecisionDraft();
        var lights = DecisionDraft.Lights();
        var renderer = new MarkdownRenderer(brush, _ => { }, _ => { });

        var optionsPanel = new StackPanel { Spacing = 6 };
        var criteriaPanel = new StackPanel { Spacing = 6 };
        var lightsPanel = new StackPanel { Spacing = 12 };
        var previewPanel = new StackPanel { Spacing = 6 };
        var optionsProblem = Hint(Strings.Get("decision_composer.needs_two_options"), brush, problem: true);
        var reasonProblem = Hint(Strings.Get("decision_composer.reason_needs_colour"), brush, problem: true);
        var addCriterion = new Button { Content = Strings.Get("decision_composer.add_criterion") };

        var asComment = new RadioButton
        {
            Content = Strings.Get("decision_composer.as_comment"),
            GroupName = "decision-post-to",
            IsChecked = postsAsComment,
            IsEnabled = postsAsComment,
        };
        var inDescription = new RadioButton
        {
            Content = Strings.Get("decision_composer.in_description"),
            GroupName = "decision-post-to",
            IsChecked = !postsAsComment,
        };
        var postHint = Hint(string.Empty, brush);

        ContentDialog? dialog = null;

        void Refresh()
        {
            var problem = draft.CurrentProblem;
            optionsProblem.Visibility = problem == DecisionDraft.Problem.TooFewOptions
                ? Visibility.Visible : Visibility.Collapsed;
            reasonProblem.Visibility = problem == DecisionDraft.Problem.ReasonWithoutRating
                ? Visibility.Visible : Visibility.Collapsed;
            postHint.Text = Strings.Get(inDescription.IsChecked == true
                ? "decision_composer.description_hint" : "decision_composer.comment_hint");
            addCriterion.IsEnabled = draft.CanAddCriterion;
            previewPanel.Children.Clear();
            foreach (var element in renderer.Render(AstridClient.RenderMarkdown(draft.Markdown)))
            {
                previewPanel.Children.Add(element);
            }
            if (dialog is not null)
            {
                dialog.IsPrimaryButtonEnabled = problem is null;
            }
        }

        void RebuildLights()
        {
            lightsPanel.Children.Clear();
            foreach (var criterion in draft.Criteria.Where(c => c.Name.Trim().Length > 0))
            {
                var section = new StackPanel { Spacing = 4 };
                section.Children.Add(new TextBlock
                {
                    Text = criterion.Name.Trim(),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                });
                foreach (var option in draft.Options.Where(o => o.Text.Trim().Length > 0))
                {
                    section.Children.Add(CellRow(criterion, option));
                }
                lightsPanel.Children.Add(section);
            }
        }

        UIElement CellRow(DecisionDraft.Criterion criterion, DecisionDraft.Option option)
        {
            var cell = draft.CellAt(option.Id, criterion.Id);
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(new TextBlock { Text = option.Text.Trim(), TextWrapping = TextWrapping.Wrap });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var light in lights)
            {
                var lit = cell.Rating == light.Token;
                var word = TradeoffGridLabels.RatingLabelKey(light.Rating) is { } key ? Strings.Get(key) : light.Token;
                var button = new Button
                {
                    MinWidth = 40,
                    Padding = new Thickness(6, 2, 6, 2),
                    Content = new TextBlock { Text = light.Token },
                    Background = TradeoffGridLabels.FillBrush(light.Rating) is { } fill ? brush(fill) : null,
                    Opacity = lit ? 1.0 : 0.4,
                    BorderThickness = new Thickness(lit ? 2 : 0),
                    BorderBrush = brush("AstridTextPrimary"),
                };
                if (TradeoffGridLabels.InkBrush(light.Rating) is { } ink)
                {
                    ((TextBlock)button.Content).Foreground = brush(ink);
                }
                AutomationProperties.SetName(button, $"{option.Text.Trim()}, {criterion.Name.Trim()}: {word}");
                ToolTipService.SetToolTip(button, word);
                var token = light.Token;
                button.Click += (_, _) =>
                {
                    draft.Toggle(token, option.Id, criterion.Id);
                    RebuildLights();
                    Refresh();
                };
                buttons.Children.Add(button);
            }
            row.Children.Add(buttons);
            var note = new TextBox
            {
                Text = cell.Text,
                PlaceholderText = Strings.Get("decision_composer.reason_placeholder"),
            };
            note.TextChanged += (_, _) =>
            {
                draft.SetNote(note.Text, option.Id, criterion.Id);
                Refresh();
            };
            row.Children.Add(note);
            return row;
        }

        void RebuildOptions()
        {
            optionsPanel.Children.Clear();
            foreach (var option in draft.Options)
            {
                var line = new Grid { ColumnSpacing = 6 };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var starred = draft.Recommended == option.Id;
                var star = new Button
                {
                    Content = new FontIcon { Glyph = starred ? "" : "", FontSize = 14 },
                    Padding = new Thickness(6),
                };
                AutomationProperties.SetName(star, Strings.Get("decision.recommended"));
                ToolTipService.SetToolTip(star, Strings.Get("decision.recommended"));
                star.Click += (_, _) =>
                {
                    draft.ToggleRecommended(option.Id);
                    RebuildOptions();
                    Refresh();
                };
                line.Children.Add(star);

                var text = new TextBox
                {
                    Text = option.Text,
                    PlaceholderText = Strings.Get("decision_composer.option_placeholder"),
                };
                text.TextChanged += (_, _) =>
                {
                    option.Text = text.Text;
                    RebuildLights();
                    Refresh();
                };
                Grid.SetColumn(text, 1);
                AutomationProperties.SetAutomationId(text, $"DecisionOption{draft.Options.IndexOf(option)}");
                line.Children.Add(text);

                if (draft.Options.Count > 2)
                {
                    var remove = RemoveButton(() => draft.RemoveOption(option.Id));
                    Grid.SetColumn(remove, 2);
                    line.Children.Add(remove);
                }
                optionsPanel.Children.Add(line);
            }
        }

        void RebuildCriteria()
        {
            criteriaPanel.Children.Clear();
            foreach (var criterion in draft.Criteria)
            {
                var line = new Grid { ColumnSpacing = 6, RowSpacing = 4 };
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.RowDefinitions.Add(new RowDefinition());
                line.RowDefinitions.Add(new RowDefinition());

                var name = new TextBox
                {
                    Text = criterion.Name,
                    PlaceholderText = Strings.Get("decision_composer.criterion_placeholder"),
                };
                name.TextChanged += (_, _) =>
                {
                    criterion.Name = name.Text;
                    RebuildLights();
                    Refresh();
                };
                line.Children.Add(name);
                AutomationProperties.SetAutomationId(name, $"DecisionCriterion{draft.Criteria.IndexOf(criterion)}");

                var detail = new TextBox
                {
                    Text = criterion.Detail,
                    PlaceholderText = Strings.Get("decision_composer.criterion_detail_placeholder"),
                };
                detail.TextChanged += (_, _) =>
                {
                    criterion.Detail = detail.Text;
                    Refresh();
                };
                Grid.SetRow(detail, 1);
                line.Children.Add(detail);

                var remove = RemoveButton(() => draft.RemoveCriterion(criterion.Id));
                Grid.SetColumn(remove, 1);
                line.Children.Add(remove);
                criteriaPanel.Children.Add(line);
            }
        }

        Button RemoveButton(Action remove)
        {
            var button = new Button
            {
                Content = new FontIcon { Glyph = "", FontSize = 12 },
                Padding = new Thickness(8),
                VerticalAlignment = VerticalAlignment.Top,
            };
            AutomationProperties.SetName(button, Strings.Get("decision_composer.remove"));
            ToolTipService.SetToolTip(button, Strings.Get("decision_composer.remove"));
            button.Click += (_, _) =>
            {
                remove();
                RebuildAll();
            };
            return button;
        }

        void RebuildAll()
        {
            RebuildOptions();
            RebuildCriteria();
            RebuildLights();
            Refresh();
        }

        var question = new TextBox
        {
            PlaceholderText = Strings.Get("decision_composer.question_placeholder"),
            TextWrapping = TextWrapping.Wrap,
        };
        question.TextChanged += (_, _) =>
        {
            draft.Question = question.Text;
            Refresh();
        };

        // Ids for the UI test, which drives this form in any language.
        AutomationProperties.SetAutomationId(question, "DecisionQuestion");
        AutomationProperties.SetAutomationId(addCriterion, "DecisionAddCriterion");
        var addOption = new Button { Content = Strings.Get("decision_composer.add_option") };
        addOption.Click += (_, _) =>
        {
            draft.AddOption();
            RebuildAll();
        };
        addCriterion.Click += (_, _) =>
        {
            draft.AddCriterion();
            RebuildAll();
        };
        asComment.Checked += (_, _) => Refresh();
        inDescription.Checked += (_, _) => Refresh();

        var body = new StackPanel { Spacing = 8, MinWidth = 420 };
        body.Children.Add(Heading(Strings.Get("decision_composer.question")));
        body.Children.Add(question);
        body.Children.Add(Heading(Strings.Get("decision_composer.options")));
        body.Children.Add(optionsPanel);
        body.Children.Add(addOption);
        body.Children.Add(optionsProblem);
        body.Children.Add(Hint(Strings.Get("decision_composer.recommend_hint"), brush));
        body.Children.Add(Heading(Strings.Get("decision_composer.criteria")));
        body.Children.Add(Hint(Strings.Get("decision_composer.criteria_hint"), brush));
        body.Children.Add(criteriaPanel);
        body.Children.Add(addCriterion);
        body.Children.Add(lightsPanel);
        body.Children.Add(reasonProblem);
        body.Children.Add(Heading(Strings.Get("decision_composer.post_to")));
        var postTo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        postTo.Children.Add(asComment);
        postTo.Children.Add(inDescription);
        body.Children.Add(postTo);
        body.Children.Add(postHint);
        body.Children.Add(Heading(Strings.Get("decision_composer.preview")));
        body.Children.Add(new Border
        {
            Child = previewPanel,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = brush("AstridBorder"),
        });

        dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = Strings.Get("decision_composer.title"),
            Content = new ScrollViewer { Content = body, MaxHeight = 640 },
            PrimaryButtonText = Strings.Get("decision_composer.post"),
            CloseButtonText = Strings.Get("dialog.cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        RebuildAll();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || draft.CurrentProblem is not null)
        {
            return null;
        }
        return new Answer(draft, inDescription.IsChecked == true);
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(0, 8, 0, 0),
    };

    /// <param name="problem">
    /// A problem: drawn in the error ink, and hidden until it applies.
    /// </param>
    private static TextBlock Hint(string text, Func<string, Brush> brush, bool problem = false) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Foreground = brush(problem ? "AstridErrorInk" : "AstridTextMuted"),
        Visibility = problem ? Visibility.Collapsed : Visibility.Visible,
    };
}
