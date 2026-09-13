using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Astrid.App.Views;

/// <summary>
/// A button that can be dragged.
/// </summary>
/// <remarks>
/// <para>
/// A board card is two things at once: a button, so a click opens it, a keyboard reaches it and a
/// screen reader reads it; and a thing to drag between columns. WinUI gives a plain element the
/// drag for free with <c>CanDrag</c>, and refuses it — by design — to any control that takes the
/// press for itself, a Button first among them: a press on a button is a click, and the framework
/// does not guess. So a Button marked <c>CanDrag</c> is a button that never drags, which is how
/// the board's cards spent a fortnight not moving (task b8e42e70).
/// </para>
/// <para>
/// This keeps the Button and starts the drag itself: it notes where the pointer went down and,
/// once it has travelled past the drag threshold while still pressed, hands the pointer to
/// <see cref="Microsoft.UI.Xaml.UIElement.StartDragAsync"/>. From there it is the ordinary drag —
/// <c>DragStarting</c> fills the package, the column's <c>Drop</c> reads it — and the button's own
/// press ends as a capture lost rather than a click, so a drop never also opens the card.
/// </para>
/// </remarks>
public sealed partial class BoardCard : Button
{
    /// <summary>
    /// How far a pressed pointer travels before it is a drag rather than a click, in DIPs. Windows'
    /// own figure (<c>SM_CXDRAG</c>) has been 4 for as long as there has been one.
    /// </summary>
    private const double DragThreshold = 4.0;

    private Point? _pressedAt;
    private uint _pressedPointer;

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        // The primary contact only: a finger and a pen read as the left button; the right button
        // is the card's menu.
        if (CanDrag && point.Properties.IsLeftButtonPressed)
        {
            _pressedAt = point.Position;
            _pressedPointer = e.Pointer.PointerId;
        }
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pressedAt is not { } origin || e.Pointer.PointerId != _pressedPointer)
        {
            return;
        }
        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            _pressedAt = null;
            return;
        }
        if (Math.Abs(point.Position.X - origin.X) < DragThreshold
            && Math.Abs(point.Position.Y - origin.Y) < DragThreshold)
        {
            return;
        }
        _pressedAt = null;
        Drag(point);
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        _pressedAt = null;
        base.OnPointerReleased(e);
    }

    protected override void OnPointerCanceled(PointerRoutedEventArgs e)
    {
        _pressedAt = null;
        base.OnPointerCanceled(e);
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        _pressedAt = null;
        base.OnPointerCaptureLost(e);
    }

    /// <summary>
    /// Hand the pointer over. The drag runs to its end on its own; a refusal to start one — the
    /// framework will not begin a second while one is under way — is nothing to report.
    /// </summary>
    private void Drag(PointerPoint point)
    {
        _ = StartDragAsync(point).AsTask();
    }
}
