using Astrid.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace Astrid.App.Views;

/// <summary>
/// What a list being dragged looks like on the clipboard (task f8b844dd).
/// </summary>
/// <remarks>
/// A private format rather than text, and that is the whole point of it. The board's cells and
/// every sidebar row accept <see cref="StandardDataFormats.Text"/> and read whatever arrives as a
/// task id — so a list dragged as text would be accepted by both as a task that does not exist,
/// and the write would fail somewhere far from the gesture. A format nobody else asks for is
/// rejected by all of them without one line being changed in any.
/// </remarks>
internal static class ListDrag
{
    /// <summary>The clipboard format a dragged list travels under.</summary>
    internal const string Format = "astrid/list-id";

    /// <summary>Put a list on a drag.</summary>
    internal static void Set(DataPackage data, string listId)
    {
        data.SetData(Format, listId);
        // Copy rather than Move: the list is not being taken out of the sidebar, and a Move
        // cursor over the board would promise that it was.
        data.RequestedOperation = DataPackageOperation.Copy;
    }

    /// <summary>The list on a drag, or null when the drag is not carrying one.</summary>
    internal static async Task<string?> ReadAsync(DataPackageView view) =>
        view.Contains(Format) ? await view.GetDataAsync(Format) as string : null;
}

/// <summary>
/// The project board: swimlanes, the cards dragged between their cells, and the three ways a row
/// is chosen (task f8b844dd).
/// </summary>
/// <remarks>
/// It renders and dispatches. Which lists are rows, which could be added, which column a card is
/// in and whether a move needs a condition are all answered in the core — see
/// <see cref="ProjectBoardViewModel"/>.
/// </remarks>
public sealed partial class ProjectBoardView : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(ProjectBoardView),
        new PropertyMetadata(null));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before it loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    public ProjectBoardView()
    {
        InitializeComponent();
        Loaded += (_, _) => WatchColumns();
        // A board drawn while hidden has no width to scroll; it is opened when it is first shown.
        BoardScroller.SizeChanged += (_, _) => StartAtTheInbox();
    }

    // ── Opening at the inbox ────────────────────────────────────────────────────────────────────

    /// <summary>The project whose board has been opened at its inbox column; null until one has.</summary>
    private string? _startedAtInboxFor;

    private ProjectBoardViewModel? _watched;

    private void WatchColumns()
    {
        if (Shell is null || ReferenceEquals(_watched, Shell.ProjectBoard))
        {
            return;
        }
        _watched = Shell.ProjectBoard;
        _watched.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName is nameof(ProjectBoardViewModel.Columns))
            {
                // Queued at Low, as on the list board: the columns are raised before the strip
                // has laid them out, and a column not yet realised cannot be measured.
                DispatcherQueue.TryEnqueue(
                    Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                    StartAtTheInbox);
            }
        };
        StartAtTheInbox();
    }

    /// <summary>
    /// Open a project board scrolled to its inbox column when it does not fit (task 7d1436a5).
    /// </summary>
    /// <remarks>
    /// The same rule as <see cref="BoardView"/>'s (task 8c543a42): Waiting leads every board since
    /// core ADEV-128, so on a window too narrow for every column the board starts at the inbox and
    /// Waiting is a scroll back. Once per project opened, never on a refresh: a reader who
    /// scrolled back to Waiting stays there. The header strip and the cells are one scrolling
    /// surface, so moving it moves both; the row heads are pinned (<see cref="PinRowHeads"/>).
    /// </remarks>
    private void StartAtTheInbox()
    {
        if (Shell is null || Shell.ProjectBoard.ProjectId == _startedAtInboxFor
            || BoardScroller.ActualWidth <= 0 || Shell.ProjectBoard.Columns.Count == 0)
        {
            return;
        }
        var inbox = Shell.ProjectBoard.Columns.FirstOrDefault(column => column.Kind == "inbox");
        if (inbox is null || HeadStrip.ContainerFromItem(inbox) is not FrameworkElement container
            || container.ActualWidth <= 0)
        {
            return;
        }
        _startedAtInboxFor = Shell.ProjectBoard.ProjectId;
        if (BoardScroller.ScrollableWidth <= 0)
        {
            return;
        }
        var left = container.TransformToVisual(HeadStrip).TransformPoint(default).X;
        BoardScroller.ChangeView(Math.Min(left, BoardScroller.ScrollableWidth), null, null, true);
    }

    // ── Pinning the row heads ───────────────────────────────────────────────────────────────────

    /// <summary>The row heads on screen, each moved by the board's horizontal offset.</summary>
    private readonly HashSet<FrameworkElement> _rowHeads = [];

    private void OnRowHeadLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement head)
        {
            _rowHeads.Add(head);
            Pin(head);
        }
    }

    private void OnRowHeadUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement head)
        {
            _rowHeads.Remove(head);
        }
    }

    private void OnBoardScrolled(object? sender, ScrollViewerViewChangedEventArgs args) =>
        PinRowHeads();

    /// <summary>
    /// Keep every row head at the board's left edge, whichever column is scrolled to it.
    /// </summary>
    /// <remarks>
    /// A render transform rather than a layout change: the head moves with the view, and the
    /// cells under it — which are what scroll — keep their places, so rows stay level.
    /// </remarks>
    private void PinRowHeads()
    {
        foreach (var head in _rowHeads)
        {
            Pin(head);
        }
    }

    private void Pin(FrameworkElement head)
    {
        if (head.RenderTransform is not TranslateTransform shift)
        {
            shift = new TranslateTransform();
            head.RenderTransform = shift;
        }
        shift.X = BoardScroller.HorizontalOffset;
    }

    // ── Dragging a card between cells ───────────────────────────────────────────────────────────

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

    /// <summary>A cell will take a card, and nothing else.</summary>
    /// <remarks>
    /// A list dragged from the sidebar travels under its own format, so it is refused here and
    /// lands on the board behind instead — which is where a new row is added.
    /// </remarks>
    private void OnCardDragOver(object sender, DragEventArgs args)
    {
        args.AcceptedOperation = args.DataView.Contains(StandardDataFormats.Text)
            ? DataPackageOperation.Move
            : DataPackageOperation.None;
    }

    /// <summary>
    /// A card was dropped on a cell.
    /// </summary>
    /// <remarks>
    /// The cell carries both ids — which column it is, and which row it is in — and both are
    /// needed: a column id alone does not say which list's card is being moved, and on a board of
    /// several lists that is the difference between moving the card and moving somebody else's.
    /// What the move WRITES is the core's decision; this hands over ids.
    /// </remarks>
    private async void OnCardDropped(object sender, DragEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not ProjectBoardColumnView cell
            || !args.DataView.Contains(StandardDataFormats.Text))
        {
            return;
        }
        // Taken before the await, as on the single-list board: the deferral keeps the package
        // alive, and without it the view is gone by the time the id comes back.
        var deferral = args.GetDeferral();
        try
        {
            var taskId = await args.DataView.GetTextAsync();
            if (!string.IsNullOrEmpty(taskId))
            {
                await Shell.ProjectBoard.MoveAsync(taskId, cell.Id, cell.RowListId);
                await AskForWaitConditionAsync();
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>A card was tapped: open its task in the side pane.</summary>
    /// <remarks>
    /// Not in place, as the single-list board opens one. A swimlane is one card tall, so a detail
    /// grown inside a row would push every other row off the screen — the thing a board is for is
    /// seeing the rows at once.
    /// </remarks>
    /// <summary>
    /// DS16's pop for the card whose task was just completed (task 27c773f5) — the same memory
    /// <see cref="BoardView"/> keeps, for the same reason: a rebuilt swimlane draws the finished
    /// task with a new mark that was ticked before it loaded.
    /// </summary>
    private void OnCardMarkLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is PriorityMark { DataContext: Astrid.Core.Bindings.TaskRow row } mark)
        {
            _completionPops.Drawn(row, mark);
        }
    }

    private readonly CompletionPopTracker _completionPops = new();

    private async void OnCardOpened(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string taskId)
        {
            await Shell.OpenTaskAsync(taskId);
        }
    }

    /// <summary>
    /// Moving a card by menu, which is the way a keyboard can do it.
    /// </summary>
    /// <remarks>
    /// The single-list board learned this the hard way (task b8e42e70): a card that can only be
    /// dragged cannot be moved by anybody using a keyboard or a screen reader, so the menu is the
    /// one that has to exist and the drag is the one everybody expects. The columns are the
    /// board's own, and the row is found by walking up to the cell this card is drawn in — a card
    /// on two row lists is a card in two rows, so which row it was clicked in is a fact about the
    /// element and not about the task.
    /// </remarks>
    private void OnCardRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (sender is not FrameworkElement card
            || card.Tag is not string taskId
            || CellOf(card) is not { } cell)
        {
            return;
        }
        var menu = new MenuFlyout();
        foreach (var column in Shell.ProjectBoard.Columns)
        {
            var item = new MenuFlyoutItem { Text = column.Name, Tag = column.Id };
            item.Click += async (_, _) =>
            {
                await Shell.ProjectBoard.MoveAsync(taskId, column.Id, cell.RowListId);
                await AskForWaitConditionAsync();
            };
            menu.Items.Add(item);
        }
        menu.ShowAt(card, args.GetPosition(card));
    }

    /// <summary>The cell a card is drawn in, by walking up from the card.</summary>
    /// <remarks>
    /// The card's own template has no access to its row: it is bound to a <c>TaskRow</c>, which
    /// is a task and knows nothing about which swimlane drew it. The cell above it does.
    /// </remarks>
    private static ProjectBoardColumnView? CellOf(DependencyObject element)
    {
        for (var at = element; at is not null; at = VisualTreeHelper.GetParent(at))
        {
            if (at is FrameworkElement { DataContext: ProjectBoardColumnView cell })
            {
                return cell;
            }
        }
        return null;
    }

    /// <summary>
    /// Ask what the task is waiting for, if the core said the move needs it.
    /// </summary>
    /// <remarks>
    /// Called after a move rather than from the columns this view thinks are Waiting columns:
    /// which columns need a condition is the core's decision, and this asks only because it was
    /// told to. Cancelling leaves the card where it was — nothing was written.
    /// </remarks>
    private async Task AskForWaitConditionAsync()
    {
        if (Shell.ProjectBoard.WaitPrompt is not { } prompt)
        {
            return;
        }
        var condition = await WaitConditionDialog.AskAsync(
            XamlRoot, prompt, query => Shell.SearchTasksAsync(query));
        if (condition is null)
        {
            Shell.ProjectBoard.CancelWaitPrompt();
            return;
        }
        await Shell.ProjectBoard.ResolveWaitAsync(condition);
    }

    // ── Choosing the rows ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fill "Add list" when it opens, from the candidates the core named.
    /// </summary>
    /// <remarks>
    /// Built on opening rather than bound, because the set changes with every row added or
    /// removed and a menu bound to it would be rebuilt while it was on screen. In the core's
    /// order: it sorts by name with case folded so that every client offers one order.
    /// </remarks>
    private void OnAddRowMenuOpening(object sender, object args)
    {
        AddRowMenu.Items.Clear();
        foreach (var list in Shell.ProjectBoard.AddListCandidates)
        {
            var item = new MenuFlyoutItem { Text = list.Name, Tag = list.Id };
            item.Click += async (_, _) => await Shell.ProjectBoard.AddRowAsync(list.Id);
            AddRowMenu.Items.Add(item);
        }
    }

    /// <summary>The cross on a row header.</summary>
    private async void OnRemoveRow(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is string listId)
        {
            await Shell.ProjectBoard.RemoveRowAsync(listId);
        }
    }

    /// <summary>The board will take a list dragged out of the sidebar, and nothing else.</summary>
    private void OnListDragOver(object sender, DragEventArgs args)
    {
        args.AcceptedOperation = args.DataView.Contains(ListDrag.Format)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
    }

    /// <summary>
    /// A list was dropped on the board: it becomes a row, at the end.
    /// </summary>
    /// <remarks>
    /// Anywhere on the board, not a particular spot. The core appends — <c>board_rows::add</c>
    /// puts a row at the end and adds it once — so a drop that aimed between two rows and a drop
    /// on empty space are the same write, and pretending otherwise would be the shell inventing a
    /// rule about ordering.
    /// </remarks>
    private async void OnListDropped(object sender, DragEventArgs args)
    {
        if (!args.DataView.Contains(ListDrag.Format))
        {
            return;
        }
        var deferral = args.GetDeferral();
        try
        {
            if (await ListDrag.ReadAsync(args.DataView) is { Length: > 0 } listId)
            {
                await Shell.ProjectBoard.AddRowAsync(listId);
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
