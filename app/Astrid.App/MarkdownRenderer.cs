using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace Astrid.App;

/// <summary>
/// Draws the blocks the core rendered a description into (task 11cfaf6d).
/// </summary>
/// <remarks>
/// <para>
/// Presentation only. What <c>**bold**</c> means, where a link points, which <c>![…](…)</c> is a
/// task and which a picture — all of that was decided in <c>astrid_core::markdown</c>, mirrored
/// from the web's <c>renderMarkdownWithLinks</c>, and arrives here as blocks of runs. This class
/// turns those into the paragraphs, hyperlinks and panels a WinUI window can show, and chooses
/// nothing else.
/// </para>
/// <para>
/// A code block is the one with a rule of its own: it scrolls sideways rather than widening the
/// pane, which is what the web's <c>.prose pre</c> does and what a long line would otherwise do
/// to a 360px column.
/// </para>
/// </remarks>
internal sealed class MarkdownRenderer
{
    private readonly Func<string, Brush> _brush;
    private readonly Action<MarkdownInline> _onReference;
    private readonly Action<string> _onLink;
    private readonly bool _dark;

    /// <param name="brush">A themed brush by its resource key.</param>
    /// <param name="onReference">A pill was clicked: a person, a list or a task.</param>
    /// <param name="onLink">A link was clicked, with the address the core kept.</param>
    /// <param name="dark">Whether the pills take their dark-theme colours.</param>
    public MarkdownRenderer(Func<string, Brush> brush, Action<MarkdownInline> onReference,
        Action<string> onLink, bool dark)
    {
        _brush = brush;
        _onReference = onReference;
        _onLink = onLink;
        _dark = dark;
    }

    /// <summary>One element per block, top to bottom.</summary>
    public IReadOnlyList<UIElement> Render(IReadOnlyList<MarkdownBlock> blocks) =>
        blocks.Select(Block).ToList();

    private UIElement Block(MarkdownBlock block) => block.Kind switch
    {
        "heading" => Paragraph(block.Inlines, HeadingSize(block.Level), FontWeights.SemiBold,
            new Thickness(0, block.Level <= 2 ? 6 : 4, 0, 0)),
        "code" => CodeBlock(block.Text),
        "list" => List(block),
        "quote" => Quote(block.Blocks),
        "rule" => new Border
        {
            Height = 1,
            Margin = new Thickness(0, 6, 0, 6),
            Background = _brush("AstridBorder"),
        },
        "table" => Table(block),
        "tradeoffGrid" => TradeoffGrid(block),
        _ => Paragraph(block.Inlines, 14, FontWeights.Normal, new Thickness(0)),
    };

    private static double HeadingSize(int level) => level switch
    {
        1 => 20,
        2 => 17,
        3 => 15,
        _ => 14,
    };

    private RichTextBlock Paragraph(IReadOnlyList<MarkdownInline> inlines, double size,
        FontWeight weight, Thickness margin)
    {
        var paragraph = new Paragraph();
        foreach (var inline in inlines)
        {
            paragraph.Inlines.Add(Inline(inline));
        }
        return new RichTextBlock
        {
            FontSize = size,
            FontWeight = weight,
            Margin = margin,
            TextWrapping = TextWrapping.Wrap,
            // A tap on the text opens the editor, as on the web; selection would eat the tap.
            IsTextSelectionEnabled = false,
            Blocks = { paragraph },
        };
    }

    private Inline Inline(MarkdownInline inline)
    {
        switch (inline.Kind)
        {
            case "lineBreak":
                return new LineBreak();
            case "reference":
                return Pill(inline);
        }

        var run = new Run { Text = inline.Text };
        if (inline.Bold)
        {
            run.FontWeight = FontWeights.SemiBold;
        }
        if (inline.Italic)
        {
            run.FontStyle = FontStyle.Italic;
        }
        if (inline.Strike)
        {
            run.TextDecorations = TextDecorations.Strikethrough;
        }
        if (inline.Code)
        {
            // The web's inline code is a chip on a tinted ground. A run cannot carry a background,
            // so the monospace face and the secondary colour are what mark it out.
            run.FontFamily = new FontFamily("Consolas");
            run.Foreground = _brush("AstridTextSecondary");
        }
        if (inline.Link is { } link)
        {
            // The web's link blue, not the theme's accent: a link should read as a link.
            var hyperlink = new Hyperlink { Inlines = { run }, Foreground = _brush("AstridAccentInk") };
            hyperlink.Click += (_, _) => _onLink(link);
            return hyperlink;
        }
        return run;
    }

    /// <summary>A person, a list or a task, in the web's three pill colours.</summary>
    private Hyperlink Pill(MarkdownInline inline)
    {
        var (sigil, light, dark) = inline.Reference switch
        {
            "user" => ("@", "#1D4ED8", "#93C5FD"),
            "list" => ("#", "#15803D", "#86EFAC"),
            _ => ("!", "#B45309", "#FCD34D"),
        };
        var hyperlink = new Hyperlink
        {
            Foreground = new SolidColorBrush(Colours.Parse(_dark ? dark : light) ?? Colors.Gray),
            UnderlineStyle = UnderlineStyle.None,
            Inlines =
            {
                new Run { Text = sigil + inline.Label, FontWeight = FontWeights.SemiBold },
            },
        };
        hyperlink.Click += (_, _) => _onReference(inline);
        return hyperlink;
    }

    private UIElement CodeBlock(string text) => new Border
    {
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10, 8, 10, 8),
        Margin = new Thickness(0, 2, 0, 2),
        Background = _brush("AstridSurfaceHover"),
        Child = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
            Content = new TextBlock
            {
                Text = text.TrimEnd('\n'),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.NoWrap,
                IsTextSelectionEnabled = true,
                Foreground = _brush("AstridTextPrimary"),
            },
        },
    };

    private UIElement List(MarkdownBlock block)
    {
        var panel = new StackPanel { Spacing = 2 };
        var number = block.Start;
        foreach (var item in block.Items)
        {
            var marker = item.Checked switch
            {
                true => "☑",
                false => "☐",
                null => block.Ordered ? $"{number}." : "•",
            };
            number++;

            var row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bullet = new TextBlock
            {
                Text = marker,
                FontSize = 14,
                TextAlignment = TextAlignment.Right,
                Foreground = _brush("AstridTextSecondary"),
            };
            Grid.SetColumn(bullet, 0);
            row.Children.Add(bullet);

            var body = new StackPanel { Spacing = 2 };
            foreach (var child in item.Blocks)
            {
                body.Children.Add(Block(child));
            }
            Grid.SetColumn(body, 1);
            row.Children.Add(body);
            panel.Children.Add(row);
        }
        return panel;
    }

    private UIElement Quote(IReadOnlyList<MarkdownBlock> blocks)
    {
        var body = new StackPanel { Spacing = 4 };
        foreach (var child in blocks)
        {
            body.Children.Add(Block(child));
        }
        return new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = _brush("AstridBorderHover"),
            Padding = new Thickness(10, 0, 0, 0),
            Margin = new Thickness(0, 2, 0, 2),
            Child = body,
        };
    }

    private UIElement Table(MarkdownBlock block)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
        var columns = Math.Max(block.Header.Count,
            block.Rows.Count > 0 ? block.Rows.Max(r => r.Cells.Count) : 0);
        for (var i = 0; i < columns; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }
        var rowIndex = 0;
        if (block.Header.Count > 0)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < block.Header.Count; c++)
            {
                var cell = Paragraph(block.Header[c], 13, FontWeights.SemiBold, new Thickness(0));
                cell.TextAlignment = Alignment(block.Alignments, c);
                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
            rowIndex++;
        }
        foreach (var row in block.Rows)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < row.Cells.Count; c++)
            {
                var cell = Paragraph(row.Cells[c].Inlines, 13, FontWeights.Normal, new Thickness(0));
                cell.TextAlignment = Alignment(block.Alignments, c);
                Grid.SetRow(cell, rowIndex);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
            rowIndex++;
        }
        // Wide tables scroll, for the same reason code blocks do.
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = grid,
        };
    }

    /// <summary>
    /// A trade-off grid: the options down the side, the criteria across the top, each cell painted
    /// by its rating, and a key underneath (task <c>cca2483d</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Drawn as a table rather than as a <c>table</c> block because the shape is different: a
    /// grid's first column is the option's name, which the block keeps apart from the cells, and
    /// every cell carries a rating that decides its colour. Sharing <see cref="Table"/> would have
    /// meant teaching it about ratings it never has.
    /// </para>
    /// <para>
    /// Row numbers and column letters are not in the block — see
    /// <see cref="TradeoffGridLabels"/>, where deciding what they are lives so it can be tested.
    /// This method only draws.
    /// </para>
    /// </remarks>
    private UIElement TradeoffGrid(MarkdownBlock block)
    {
        var grid = new Grid { ColumnSpacing = 3, RowSpacing = 3 };
        var criteria = Math.Max(block.Header.Count - 1,
            block.Rows.Count > 0 ? block.Rows.Max(row => row.Cells.Count) : 0);

        // The option column takes what it needs; the criteria share the rest evenly, which is how
        // a grid is read — one cell against the cell beside it.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var c = 0; c < criteria; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var rowIndex = 0;
        if (block.Header.Count > 0)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(grid, Paragraph(block.Header[0], 13, FontWeights.SemiBold, new Thickness(0)), rowIndex, 0);
            for (var c = 0; c < criteria; c++)
            {
                var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
                heading.Children.Add(new TextBlock
                {
                    Text = TradeoffGridLabels.ColumnLetter(c),
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Opacity = 0.6,
                    Foreground = _brush("AstridTextSecondary"),
                });
                if (c + 1 < block.Header.Count)
                {
                    heading.Children.Add(Paragraph(block.Header[c + 1], 13, FontWeights.SemiBold,
                        new Thickness(0)));
                }
                Place(grid, heading, rowIndex, c + 1);
            }
            rowIndex++;
        }

        for (var r = 0; r < block.Rows.Count; r++)
        {
            var row = block.Rows[r];
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var option = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            option.Children.Add(new TextBlock
            {
                Text = TradeoffGridLabels.RowNumber(r),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Opacity = 0.6,
                Foreground = _brush("AstridTextSecondary"),
            });
            if (row.Name is { Count: > 0 })
            {
                option.Children.Add(Paragraph(row.Name, 13, FontWeights.SemiBold, new Thickness(0)));
            }
            Place(grid, option, rowIndex, 0);

            for (var c = 0; c < row.Cells.Count; c++)
            {
                Place(grid, Cell(row.Cells[c]), rowIndex, c + 1);
            }
            rowIndex++;
        }

        var body = new StackPanel { Spacing = 6 };
        // Wide grids scroll, for the same reason tables and code blocks do.
        body.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = grid,
        });

        var key = Key(block.Key);
        if (key is not null)
        {
            body.Children.Add(key);
        }
        return body;
    }

    /// <summary>One cell of a grid: its reason, on its rating's colour.</summary>
    /// <remarks>
    /// An unrated cell is left unpainted rather than painted in a neutral — "nothing recorded"
    /// should not look like a rating of its own.
    /// </remarks>
    private UIElement Cell(MarkdownCell cell)
    {
        var text = Paragraph(cell.Inlines, 13, FontWeights.Normal, new Thickness(0));
        text.TextAlignment = TextAlignment.Center;

        var fill = TradeoffGridLabels.FillBrush(cell.Rating);
        var ink = TradeoffGridLabels.InkBrush(cell.Rating);
        if (ink is not null)
        {
            text.Foreground = _brush(ink);
        }

        return new Border
        {
            // `markdown/tradeoff-cell-radius` and `tradeoff-cell-min`/`-max` in the design tokens.
            CornerRadius = new CornerRadius(4),
            MinWidth = 84,
            MaxWidth = 150,
            Padding = new Thickness(6, 4, 6, 4),
            Background = fill is null ? null : _brush(fill),
            Child = text,
        };
    }

    /// <summary>
    /// The key underneath a grid: a swatch and a word per rating it used, worst to best.
    /// </summary>
    /// <remarks>
    /// Null when the block lists no ratings this client knows — a grid of entirely unrated cells
    /// has nothing to explain, and an empty key would be a row of blank space under it.
    /// </remarks>
    private UIElement? Key(IReadOnlyList<string> ratings)
    {
        var known = TradeoffGridLabels.KnownRatings(ratings);
        if (known.Count == 0)
        {
            return null;
        }

        var key = new StackPanel { Spacing = 2 };
        foreach (var rating in known)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            item.Children.Add(new Border
            {
                // `markdown/tradeoff-swatch` in the design tokens.
                Width = 10,
                Height = 10,
                CornerRadius = new CornerRadius(3),
                Background = _brush(TradeoffGridLabels.FillBrush(rating)!),
                VerticalAlignment = VerticalAlignment.Center,
            });
            item.Children.Add(new TextBlock
            {
                Text = Strings.Get(TradeoffGridLabels.RatingLabelKey(rating)!),
                FontSize = 13,
                Foreground = _brush("AstridTextSecondary"),
            });
            key.Children.Add(item);
        }
        return key;
    }

    private static void Place(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static TextAlignment Alignment(IReadOnlyList<string> alignments, int column) =>
        column < alignments.Count
            ? alignments[column] switch
            {
                "center" => TextAlignment.Center,
                "right" => TextAlignment.Right,
                _ => TextAlignment.Left,
            }
            : TextAlignment.Left;
}
