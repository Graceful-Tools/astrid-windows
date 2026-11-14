using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Astrid.App;

/// <summary>
/// Draws a square mark in its priority's colour, in the theme it is drawn in (task 82e4ad32):
/// the outline on the square, and the glyph inside it (task f1a8ff9f).
/// </summary>
/// <remarks>
/// <para>
/// The mark for somebody else's task, and for nobody's, is their initial in a priority-coloured
/// square rather than a checkbox (PRODUCT_CONTRACT.md §4). That colour used to come from
/// <c>PriorityBrushConverter</c>, which built a brush out of a hex.
/// </para>
/// <para>
/// A converter cannot do it any more, and the reason is not a detail: <c>priority-low</c> resolves
/// to a different brand value on a dark theme, and an <c>IValueConverter</c> is handed a value and a
/// target type but never the element, so it has no theme to resolve in. An attached property does —
/// it is set ON the element — so it can ask <c>ThemeBrushes</c> and listen for the appearance
/// changing under it.
/// </para>
/// <para>
/// It was a fill until task f1a8ff9f, and the fill is what made priority 0 disappear: no priority
/// painted the square transparent, deliberately, and the glyph on top of it was a literal
/// <c>White</c> that only ever worked against a fill — so an unassigned task with no priority, the
/// ordinary case on a fresh board, drew nothing at all. astrid-web draws both square marks as a 2px
/// border in the priority colour with the glyph in the same colour and no background
/// (<c>components/task-leading-control.tsx</c>), and astrid-core's design register says it from the
/// other side: DS6 calls the repeat arrows "the photo's border, in place of the priority outline".
/// Windows was the only client filling. Drawing the line instead needs no absence: every priority,
/// 0 included, is a colour <c>PriorityPalette.Key</c> can name.
/// </para>
/// <para>
/// Set it on the square and on the glyph inside it, rather than having the square reach down for
/// its child: a template's child is attached after its parent's attributes are set, so the first
/// paint would find nothing there.
/// </para>
/// </remarks>
public static class PriorityOutline
{
    public static int GetPriority(DependencyObject element) =>
        (int)element.GetValue(PriorityProperty);

    public static void SetPriority(DependencyObject element, int value) =>
        element.SetValue(PriorityProperty, value);

    public static readonly DependencyProperty PriorityProperty =
        DependencyProperty.RegisterAttached(
            "Priority", typeof(int), typeof(PriorityOutline), new PropertyMetadata(0, OnChanged));

    /// <summary>Whether this element's theme changes are already being listened to.</summary>
    private static readonly DependencyProperty ListeningProperty =
        DependencyProperty.RegisterAttached(
            "Listening", typeof(bool), typeof(PriorityOutline), new PropertyMetadata(false));

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not FrameworkElement target)
        {
            return;
        }

        // A row's elements are recycled by the ListView rather than rebuilt, so the value changes
        // many times over one element's life; the handler is attached once.
        if (!(bool)target.GetValue(ListeningProperty))
        {
            target.SetValue(ListeningProperty, true);
            target.ActualThemeChanged += (sender, _) => Paint((FrameworkElement)sender);
        }

        Paint(target);
    }

    private static void Paint(FrameworkElement target)
    {
        var brush = ThemeBrushes.For(target, PriorityPalette.Key(GetPriority(target)));
        switch (target)
        {
            case Border square:
                square.BorderBrush = brush;
                break;
            case TextBlock mark:
                mark.Foreground = brush;
                break;
        }
    }
}
