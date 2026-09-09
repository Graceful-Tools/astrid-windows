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
            var hyperlink = new Hyperlink { Inlines = { run }, Foreground = _brush("AstridAccentBrush") };
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
        var columns = Math.Max(block.Header.Count, block.Rows.Count > 0 ? block.Rows.Max(r => r.Count) : 0);
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
            for (var c = 0; c < row.Count; c++)
            {
                var cell = Paragraph(row[c], 13, FontWeights.Normal, new Thickness(0));
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
