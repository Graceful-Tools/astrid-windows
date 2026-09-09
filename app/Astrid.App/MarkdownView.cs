using Astrid.Core.Bindings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Astrid.App;

/// <summary>
/// Draws rendered markdown blocks wherever a template needs them — a comment bubble, for one
/// (task 3271a0c5).
/// </summary>
/// <remarks>
/// A control rather than code in the page because a row template cannot call the page: the
/// blocks arrive by binding, and the control turns them into elements with the same
/// <see cref="MarkdownRenderer"/> the description uses. Where a pill or a link goes when clicked
/// is the page's business, so the page hands in the two callbacks once.
/// </remarks>
public sealed partial class MarkdownView : ContentControl
{
    public static readonly DependencyProperty BlocksProperty = DependencyProperty.Register(
        nameof(Blocks),
        typeof(IReadOnlyList<MarkdownBlock>),
        typeof(MarkdownView),
        new PropertyMetadata(null, (sender, _) => ((MarkdownView)sender).Render()));

    /// <summary>A pill was clicked: a person, a list or a task.</summary>
    public static Action<MarkdownInline>? ReferenceFollowed { get; set; }

    /// <summary>A link was clicked, with the address the core kept.</summary>
    public static Action<string>? LinkFollowed { get; set; }

    public MarkdownView()
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ActualThemeChanged += (_, _) => Render();
    }

    public IReadOnlyList<MarkdownBlock>? Blocks
    {
        get => (IReadOnlyList<MarkdownBlock>?)GetValue(BlocksProperty);
        set => SetValue(BlocksProperty, value);
    }

    private void Render()
    {
        var panel = new StackPanel { Spacing = 4 };
        if (Blocks is { Count: > 0 } blocks)
        {
            var renderer = new MarkdownRenderer(
                ThemedBrush,
                reference => ReferenceFollowed?.Invoke(reference),
                link => LinkFollowed?.Invoke(link),
                ActualTheme == ElementTheme.Dark);
            foreach (var element in renderer.Render(blocks))
            {
                panel.Children.Add(element);
            }
        }
        Content = panel;
    }

    private static Brush ThemedBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var brush) && brush is Brush themed
            ? themed
            : new SolidColorBrush(Colors.Gray);
}
