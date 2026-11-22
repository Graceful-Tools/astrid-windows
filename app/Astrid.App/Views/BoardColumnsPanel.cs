using Astrid.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Astrid.App.Views;

/// <summary>
/// The board's columns side by side, sharing the board's width and none narrower than
/// <see cref="MinColumnWidth"/> (astrid-core register DS34, compact; task <c>9fedf44e</c>).
/// </summary>
/// <remarks>
/// It sits inside a sideways <see cref="ScrollViewer"/>, which measures its content with an
/// infinite width, so the width to share is the scroller's viewport rather than the size it is
/// offered. The arithmetic is <see cref="BoardColumnLayout.ColumnWidth"/>, which is tested.
/// </remarks>
public sealed class BoardColumnsPanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(BoardColumnsPanel),
        new PropertyMetadata(250d, (d, _) => ((BoardColumnsPanel)d).InvalidateMeasure()));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(BoardColumnsPanel),
        new PropertyMetadata(12d, (d, _) => ((BoardColumnsPanel)d).InvalidateMeasure()));

    private ScrollViewer? _scroller;

    public BoardColumnsPanel()
    {
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
    }

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private void Attach()
    {
        Detach();
        for (var parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is ScrollViewer scroller)
            {
                _scroller = scroller;
                _scroller.SizeChanged += OnScrollerSizeChanged;
                break;
            }
        }
        InvalidateMeasure();
    }

    private void Detach()
    {
        if (_scroller is not null)
        {
            _scroller.SizeChanged -= OnScrollerSizeChanged;
            _scroller = null;
        }
    }

    private void OnScrollerSizeChanged(object sender, SizeChangedEventArgs e) => InvalidateMeasure();

    private double ColumnWidth(Size available)
    {
        var viewport = _scroller?.ActualWidth ?? 0;
        if (!(viewport > 0) && !double.IsInfinity(available.Width))
        {
            viewport = available.Width;
        }
        return BoardColumnLayout.ColumnWidth(viewport, Children.Count, MinColumnWidth, Spacing);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = ColumnWidth(availableSize);
        var height = 0d;
        foreach (var child in Children)
        {
            child.Measure(new Size(width, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        var count = Children.Count;
        var total = count == 0 ? 0 : width * count + Spacing * (count - 1);
        return new Size(total, double.IsInfinity(availableSize.Height) ? height : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = ColumnWidth(finalSize);
        var left = 0d;
        foreach (var child in Children)
        {
            child.Arrange(new Rect(left, 0, width, finalSize.Height));
            left += width + Spacing;
        }
        return finalSize;
    }
}
