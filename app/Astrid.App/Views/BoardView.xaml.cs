using System.Diagnostics;
using Astrid.App.ViewModels;
using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Astrid.App.Views;

/// <summary>
/// The board: dragging a card between columns, opening one in place, and the menu that moves one by keyboard.
/// </summary>
public sealed partial class BoardView : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(BoardView),
        new PropertyMetadata(null));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    /// <summary>A card was opened or closed. The page repaints the detail's priority.</summary>
    internal event Action? CardOpened;

    /// <summary>
    /// The callout's host has loaded. The page moves its one detail pane into it.
    /// </summary>
    /// <remarks>
    /// Still one pane, as it was when this was an inline slot (task eb2684bb): the board shows the
    /// same detail the list does — fields, thread, comment box — without a second copy of any of
    /// it. Only where it is hosted changed.
    /// </remarks>
    internal event Action<ContentControl>? DetailSlotLoaded;

    public BoardView()
    {
        InitializeComponent();

        // The callout follows the open card, and follows the window (task eb2684bb). A placement
        // made against a viewport that has since changed is a callout hanging off the window edge,
        // which is the case the core's clamping exists to prevent — so a resize re-asks.
        SizeChanged += (_, _) => PlaceCardCallout();
        Loaded += (_, _) => WatchExpandedCard();
    }

    private ShellViewModel? _watched;

    private void WatchExpandedCard()
    {
        if (ReferenceEquals(_watched, Shell) || Shell is null)
        {
            return;
        }
        _watched = Shell;
        Shell.Board.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName is nameof(BoardViewModel.ExpandedTaskId)
                or nameof(BoardViewModel.Columns))
            {
                // Queued, not immediate: the board raises Columns before the new cards have been
                // laid out, and a card that is not yet realised cannot be measured. Low priority
                // puts this after that layout pass.
                DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    PlaceCardCallout);
            }
        };
        PlaceCardCallout();
    }

    /// <summary>
    /// Start dragging a card.
    /// </summary>
    /// <remarks>
    /// The task id travels as text on the clipboard package, which is how WinUI carries a drag. It
    /// is also what makes a card draggable out of the app into a text field — harmless, and better
    /// than a private format nothing else can read.
    /// </remarks>
    private void OnCardDragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not string taskId)
        {
            args.Cancel = true;
            return;
        }
        args.Data.SetText(taskId);
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    /// <summary>A column will take a card.</summary>
    private void OnCardDragOver(object sender, DragEventArgs args)
    {
        args.AcceptedOperation = args.DataView.Contains(StandardDataFormats.Text)
            ? DataPackageOperation.Move
            : DataPackageOperation.None;
    }

    /// <summary>
    /// A card was dropped on a column.
    /// </summary>
    /// <remarks>
    /// What the move writes is the core's decision — a role, a completion, or neither — so this
    /// hands over two ids and nothing else. Dropping a card back where it came from is a no-op
    /// there rather than a write nobody asked for.
    /// </remarks>
    private async void OnCardDropped(object sender, DragEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not string columnId
            || !args.DataView.Contains(StandardDataFormats.Text))
        {
            return;
        }
        // Taken before the await: the deferral keeps the package alive, and without it the view is
        // closed by the time the id comes back.
        var deferral = args.GetDeferral();
        try
        {
            var taskId = await args.DataView.GetTextAsync();
            if (!string.IsNullOrEmpty(taskId))
            {
                await Shell.Board.MoveAsync(taskId, columnId);
                await AskForWaitConditionAsync();
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>A card was tapped. The view model decides whether that opens or closes.</summary>
    private async void OnCardOpened(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string taskId)
        {
            await Shell.ToggleCardAsync(taskId);
            CardOpened?.Invoke();
        }
    }

    /// <summary>
    /// A card's mark, which does what a row's does (task 6979e694): copies a task the reader
    /// cannot edit, and otherwise does whatever the core said this control does.
    /// </summary>
    /// <remarks>
    /// Which, on a board, is almost always "open the picker" — a card's mark sets the task's
    /// column, and finishing it outright from here is the trapdoor task 3c0dfb73 closed.
    /// </remarks>
    private async void OnCardMarkClicked(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: string taskId } mark)
        {
            return;
        }
        if (await Shell.Board.ActivateLeadingAsync(taskId) == LeadingOutcome.OpenPicker)
        {
            LeadingPickerFlyout.ShowAt(mark, Shell.Board.LeadingPicker);
        }
    }

    /// <summary>
    /// Enter in a column's add field adds the card (task 95c7a68f).
    /// </summary>
    /// <remarks>
    /// The field's own text is read here rather than left to the binding: a <c>TextBox.Text</c>
    /// two-way binding does not push until focus leaves, and Enter does not move focus, so the
    /// first card typed into a column would have been created with an empty title. The view model
    /// trims it, ignores an empty one, and clears the draft before the write.
    /// </remarks>
    private async void OnAddCardKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter
            || sender is not TextBox field
            || field.DataContext is not BoardColumnView column)
        {
            return;
        }
        args.Handled = true;
        column.Draft = field.Text;
        await column.AddCardAsync();
        field.Text = column.Draft;
        await AskForWaitConditionAsync();
    }

    /// <summary>
    /// Ask what the task is waiting for, if the core said the move needs it (task 7462618e).
    /// </summary>
    /// <remarks>
    /// Called after every board write that can put a task in Waiting, rather than from the columns
    /// the view thinks are Waiting columns: which columns need a condition is the core's decision,
    /// and this asks only because it was told to. Cancelling drops the prompt and leaves the task
    /// where it was — nothing was written.
    /// </remarks>
    private async Task AskForWaitConditionAsync()
    {
        if (Shell.Board.WaitPrompt is not { } prompt)
        {
            return;
        }
        var condition = await WaitConditionDialog.AskAsync(
            XamlRoot, prompt, query => Shell.SearchTasksAsync(query));
        if (condition is null)
        {
            Shell.Board.CancelWaitPrompt();
            return;
        }
        await Shell.Board.ResolveWaitAsync(condition);
    }

    /// <summary>
    /// Moving a card. A menu rather than a drag, for now.
    /// </summary>
    /// <remarks>
    /// A drag is the gesture people expect from a board and it will come; a card that can ONLY be
    /// dragged is a card a keyboard cannot move at all, so the menu is the one that has to exist.
    /// The columns come from the board rather than from a list typed here — a board can be renamed
    /// and can have columns of its own.
    /// </remarks>
    private void OnCardRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (sender is not FrameworkElement card || card.Tag is not string taskId)
        {
            return;
        }
        var menu = new MenuFlyout();
        foreach (var column in Shell.Board.Columns)
        {
            var item = new MenuFlyoutItem { Text = column.Name, Tag = column.Id };
            item.Click += async (_, _) =>
            {
                await Shell.Board.MoveAsync(taskId, column.Id);
                await AskForWaitConditionAsync();
            };
            menu.Items.Add(item);
        }
        menu.ShowAt(card, args.GetPosition(card));
    }

    // ── The column add field's pickers (task 71ad20a0) ──────────────────────────────────────
    //
    // The flyout lives inside the column DataTemplate, so there is no named field for it: every
    // column has its own instance. Which column a pick belongs to, and which flyout to dismiss,
    // are both taken from the settings button that opened it.

    private Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase? _openColumnFlyout;
    private BoardColumnView? _openColumn;

    /// <summary>
    /// Ask the core what a card born in this column would look like, before anything is picked.
    /// </summary>
    /// <remarks>
    /// So the mark is right the first time the column is drawn rather than only after a pick. The
    /// column's own defaults — the list's priority and assignee — are an answer, not an absence.
    /// </remarks>
    private async void OnColumnAddSettingsLoaded(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is BoardColumnView column)
        {
            await column.LoadQuickAddDefaultsAsync();
        }
    }

    /// <summary>Remember whose pickers these are, and which flyout a pick should close.</summary>
    private void OnColumnAddSettingsOpened(object sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: BoardColumnView column } button)
        {
            _openColumn = column;
            _openColumnFlyout = button.Flyout;
        }
    }

    /// <summary>The priority the next card in this column is born with.</summary>
    private async void OnColumnPriorityPicked(object sender, RoutedEventArgs args)
    {
        _openColumnFlyout?.Hide();
        if (_openColumn is { } column
            && sender is FrameworkElement { Tag: string tag }
            && int.TryParse(tag, out var priority))
        {
            await column.PickQuickAddPriorityAsync(priority);
        }
    }

    /// <summary>Who it goes to. A null tag is the unassigned row.</summary>
    private async void OnColumnAssigneeChosen(object sender, RoutedEventArgs args)
    {
        _openColumnFlyout?.Hide();
        if (_openColumn is { } column && sender is FrameworkElement element)
        {
            await column.PickQuickAddAssigneeAsync(element.Tag as string);
        }
    }

    // ── The open card's callout (task eb2684bb) ─────────────────────────────────────────────
    //
    // The board used to splice an `InlineDetailSlot` into the column's item list and widen the
    // column to hold it, so detail arrived as another card-shaped block pushed in between cards.
    // There was no side to choose and no arrow to aim. It opens a real callout now, placed by
    // `astrid_rules::rows::callout::place` — the same rule the Mac, the iPad and the Android
    // tablet ask, so four clients cannot drift apart on where a callout goes.
    //
    // What is the shell's and not the core's: the callout's SIZE, and measuring the card. Where a
    // row sits on screen is geometry the view model cannot know, and how big a detail pane wants
    // to be is a design decision per platform — so this measures, then asks.

    /// <summary>
    /// How wide the callout is, and how tall it would like to be.
    /// </summary>
    /// <remarks>
    /// The pane's card width plus its border, and a height that shows a description and the top of
    /// the thread without reaching for the whole window. The core clamps both into the viewport, so
    /// these are a preference rather than a promise — a short window gets a shorter callout.
    /// </remarks>
    private const double CalloutWidth = 400;
    private const double CalloutHeight = 560;

    /// <summary>
    /// Half the arrow's 16px square: a rotated square is positioned by its corner, and the point
    /// the core named is where its MIDDLE belongs.
    /// </summary>
    private const double ArrowHalf = 8;

    /// <summary>
    /// The inset the frame carries on both sides, which is the room the arrow is drawn in.
    /// </summary>
    /// <remarks>
    /// A Popup clips its child to that child's own bounds, so the arrow cannot hang off the frame
    /// on a negative margin — it is laid out and reported to UI Automation but never drawn. The
    /// frame is inset instead, and the popup is opened this much to the left of where the core put
    /// the callout so that the FRAME lands on the core's answer.
    /// </remarks>
    private const double ArrowGutter = 12;

    /// <summary>
    /// The narrowest the callout is allowed to shrink to before it stops being worth opening
    /// beside the card.
    /// </summary>
    /// <remarks>
    /// Below this the pane's own rows — a title, an assignee chip, the four priority squares —
    /// start wrapping into something nobody can read, and a callout that covers its card is the
    /// better of two bad answers. So this is a floor, not a target: a board narrow enough to hit
    /// it gets the core's clamp, which is the behaviour as it was.
    /// </remarks>
    private const double MinCalloutWidth = 220;

    /// <summary>
    /// The box to ask the core to place: the panel plus the arrow's reach, narrowed to what will
    /// actually fit beside the card (task 914fe805).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The core decides the SIDE and then clamps the box into the viewport; it does not resize it,
    /// and should not — a rule that silently changed the size it was given would be deciding a
    /// thing each platform wants differently. So the shell has to hand it a size that can be
    /// placed, and the room beside the card is what that means: whichever side has more, less the
    /// clearance the core keeps from the window edge.
    /// </para>
    /// <para>
    /// The same comparison the core makes for the side, deliberately — if this measured room
    /// differently it could ask for a width that fits the side the core does not choose.
    /// </para>
    /// </remarks>
    private static double FittingWidth(CalloutRect anchor, CalloutRect viewport)
    {
        var onTheRight = viewport.Width - (anchor.Left + anchor.Width);
        var onTheLeft = anchor.Left;
        var room = Math.Max(onTheRight, onTheLeft) - ViewportGutter;
        return Math.Clamp(room, MinCalloutWidth + ArrowGutter, CalloutWidth + ArrowGutter);
    }

    /// <summary>
    /// The clearance the core keeps between a callout and the window's edge — its `ARROW_MARGIN`
    /// sibling, `contracts/fixtures/callout-placement.json`'s 8.
    /// </summary>
    /// <remarks>
    /// Written here because the width asked for has to leave it; the core is still the one that
    /// applies it. If the fixture's value moves, this is the second place to follow it, which is
    /// why it is named rather than an 8 in the middle of an expression.
    /// </remarks>
    private const double ViewportGutter = 8;

    private void OnCardCalloutHostLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is ContentControl host)
        {
            DetailSlotLoaded?.Invoke(host);
        }
    }

    /// <summary>
    /// Open, move or close the callout to match whichever card is expanded.
    /// </summary>
    /// <remarks>
    /// Called whenever the board says a different card is open, and again when the control is
    /// resized — a callout placed against a viewport that has since changed is a callout hanging
    /// off the window, which is the case the core's clamping exists to prevent.
    /// </remarks>
    private void PlaceCardCallout()
    {
        var taskId = Shell?.Board?.ExpandedTaskId;
        if (taskId is null)
        {
            CardCallout.IsOpen = false;
            return;
        }

        if (FindCardElement(taskId) is not { } card)
        {
            // The card is not realised — a column scrolled away, or the board still loading. Not
            // an error: the next placement pass finds it.
            CardCallout.IsOpen = false;
            return;
        }

        var viewport = new CalloutRect(0, 0, ActualWidth, ActualHeight);
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return;
        }

        var anchor = MeasureAgainstBoard(card);
        var height = Math.Min(CalloutHeight, Math.Max(0, viewport.Height - 16));
        // The width asked for INCLUDES the arrow's reach, because the core now puts the box's
        // near edge ON the card's edge (astrid-ios AITD-679, core ed8380c) — the 8px gutter is
        // only the clearance from the window edges. Ask for the panel's width alone and the box
        // that comes back is the panel's, so an arrow drawn outside it lands inside the card.
        //
        // And it is the width that FITS, which is the shell's decision and not the core's: the
        // core places a box of the size it is given and clamps it into the viewport, so a box
        // too wide for the room beside the card is clamped back OVER the card — correctly, and
        // uselessly. With the chat pane open this board is about 540 logical wide and a card
        // takes 270 of it, so a 412-wide callout had nowhere to go and covered the task it was
        // opened from. Asking for what fits is what makes "beside the card" true (task 914fe805).
        var placement = AstridClient.CalloutPlacement(
            anchor, viewport, FittingWidth(anchor, viewport), height);

        CardCalloutFrame.Width = Math.Max(0, placement.Width - ArrowGutter);
        CardCalloutFrame.Height = placement.Height;
        // Inset on the CARD's side only, which is the gutter the arrow is drawn in. Both sides
        // were inset while the width asked for was the panel's alone; now that the box carries
        // the arrow's reach, insetting the far side as well would make the panel a gutter
        // narrower than it asked to be. Set here rather than in the XAML because ArrowGutter is
        // the number, and writing 12 there too would be one constant living in two files.
        CardCalloutFrame.Margin = placement.OpensLeft
            ? new Thickness(0, 0, ArrowGutter, 0)
            : new Thickness(ArrowGutter, 0, 0, 0);

        // The arrow sits on the edge FACING the anchor, which is the opposite edge from the side
        // the callout opened on: a callout to the left of its card points rightward, at the card.
        CardCalloutArrow.HorizontalAlignment = placement.OpensLeft
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
        CardCalloutArrow.Margin = placement.OpensLeft
            ? new Thickness(0, 0, ArrowGutter - ArrowHalf, 0)
            : new Thickness(ArrowGutter - ArrowHalf, 0, 0, 0);
        // Which two sides of the rotated square are drawn: the two that face the anchor.
        CardCalloutArrow.BorderThickness = placement.OpensLeft
            ? new Thickness(0, 1, 1, 0)
            : new Thickness(1, 0, 0, 1);
        CardCalloutArrowOffset.Y = placement.ArrowOffset - ArrowHalf;
        CardCalloutArrow.Visibility = Visibility.Visible;

        // Exactly where the rule put it. The popup's box IS the core's box now — the panel plus
        // the arrow's reach — so its near edge sits on the card's edge and the arrow inside it is
        // what meets the card, rather than the panel being docked against the card with its arrow
        // laid over the top.
        CardCallout.HorizontalOffset = placement.Left;
        CardCallout.VerticalOffset = placement.Top;
        CardCallout.IsOpen = true;
    }

    /// <summary>
    /// The card's rectangle in the board's own coordinates — which is the space the viewport is
    /// described in, so the core compares like with like.
    /// </summary>
    /// <remarks>
    /// Logical pixels. WinUI lays out in those already, which is why nothing here divides by a
    /// scale factor: the core deliberately does not take one, because a rule that took a scale
    /// factor is a rule that can get it wrong.
    /// </remarks>
    private CalloutRect MeasureAgainstBoard(FrameworkElement card)
    {
        try
        {
            var corner = card.TransformToVisual(this).TransformPoint(new Windows.Foundation.Point(0, 0));
            return new CalloutRect(corner.X, corner.Y, card.ActualWidth, card.ActualHeight);
        }
        catch (Exception)
        {
            // TransformToVisual throws when either element is not in the tree yet, which happens on
            // the first layout pass. An anchor at the origin is placed rather than crashed; the
            // next pass measures it properly.
            return new CalloutRect(0, 0, card.ActualWidth, card.ActualHeight);
        }
    }

    /// <summary>The element drawing the card with this id, if it is realised.</summary>
    /// <remarks>
    /// Walked rather than asked of the ItemsControl, because the cards are inside a column template
    /// inside the board's own ItemsControl — two generators deep, and the inner one is reachable
    /// only once its column's container exists. A walk answers the same question without depending
    /// on how many templates are between here and there.
    /// </remarks>
    private FrameworkElement? FindCardElement(string taskId) => FindCardElement(this, taskId);

    private static FrameworkElement? FindCardElement(DependencyObject root, string taskId)
    {
        var children = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < children; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is BoardCard card && card.Tag as string == taskId)
            {
                return card;
            }
            if (FindCardElement(child, taskId) is { } found)
            {
                return found;
            }
        }
        return null;
    }

}
