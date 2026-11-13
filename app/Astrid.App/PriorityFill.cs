using Astrid.Core.Bindings;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Astrid.App;

/// <summary>
/// Fills a square with a priority's colour, in the theme it is drawn in (task 82e4ad32).
/// </summary>
/// <remarks>
/// <para>
/// The mark for somebody else's task, and for nobody's, is their initial in a priority-coloured
/// square rather than a checkbox (PRODUCT_CONTRACT.md §4). That fill used to come from
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
/// Priority 0 stays transparent here, as it did in the converter: no priority draws nothing at all
/// rather than grey, because a stripe on every row is a stripe that says nothing. The picker's
/// square, which needs to be visible whatever it means, asks <c>PrioritySwatch</c> for the grey.
/// </para>
/// </remarks>
public static class PriorityFill
{
    public static int GetPriority(DependencyObject element) =>
        (int)element.GetValue(PriorityProperty);

    public static void SetPriority(DependencyObject element, int value) =>
        element.SetValue(PriorityProperty, value);

    public static readonly DependencyProperty PriorityProperty =
        DependencyProperty.RegisterAttached(
            "Priority", typeof(int), typeof(PriorityFill), new PropertyMetadata(0, OnChanged));

    /// <summary>Whether this element's theme changes are already being listened to.</summary>
    private static readonly DependencyProperty ListeningProperty =
        DependencyProperty.RegisterAttached(
            "Listening", typeof(bool), typeof(PriorityFill), new PropertyMetadata(false));

    private static void OnChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not Border border)
        {
            return;
        }

        // A row's Border is recycled by the ListView rather than rebuilt, so the value changes many
        // times over one element's life; the handler is attached once.
        if (!(bool)border.GetValue(ListeningProperty))
        {
            border.SetValue(ListeningProperty, true);
            border.ActualThemeChanged += (sender, _) => Paint((Border)sender);
        }

        Paint(border);
    }

    private static void Paint(Border border) =>
        border.Background = GetPriority(border) is 1 or 2 or 3
            ? ThemeBrushes.For(border, PriorityPalette.Key(GetPriority(border)))
            : new SolidColorBrush(Colors.Transparent);
}
