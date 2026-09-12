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
/// Quick add and the rows: adding, completing, deleting, selecting and opening a task.
/// </summary>
public sealed partial class TaskRowsView : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(TaskRowsView),
        new PropertyMetadata(null));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    /// <summary>
    /// A row was opened or the selection moved. The page repaints the detail's priority and points
    /// its arrow.
    /// </summary>
    internal event Action? RowActivated;

    public TaskRowsView()
    {
        InitializeComponent();
        AddedPrefix.Text = Strings.Get("quickadd.added");
    }

    /// <summary>Put the caret in the quick-add box.</summary>
    internal void FocusQuickAdd() => QuickAddBox.Focus(FocusState.Programmatic);

    /// <summary>The container drawing a row, if it is realised.</summary>
    internal FrameworkElement? ContainerOf(TaskRow row) => TaskRows.ContainerFromItem(row) as FrameworkElement;

    /// <summary>
    /// Call back whenever the rows scroll.
    /// </summary>
    /// <remarks>
    /// The ScrollViewer is inside the ListView's template, so it does not exist until the template
    /// is applied — which is why the page calls this once it has loaded rather than from a
    /// constructor.
    /// </remarks>
    internal void WatchScrolling(Action onScrolled)
    {
        if (ScrollViewerInside(TaskRows) is { } scroller)
        {
            scroller.ViewChanged += (_, _) => onScrolled();
        }
    }

    /// <summary>Make every live row resolve its style's theme brushes again; see <c>ShellPage.ApplyTheme</c>.</summary>
    internal void Restyle() => Restyling.Reapply(TaskRows);

    /// <summary>
    /// A row is being dragged — towards a sidebar list, which files it there (task 27cae198).
    /// </summary>
    /// <remarks>
    /// The task id travels as text on the package, as the board's cards already carry it, so one
    /// drop target reads both. Move and Copy are both offered: the sidebar answers Copy while
    /// Shift is held, which is the web's "add to this list as well".
    /// </remarks>
    private void OnRowDragStarting(object sender, DragItemsStartingEventArgs args)
    {
        if (args.Items.Count != 1 || args.Items[0] is not TaskRow row)
        {
            args.Cancel = true;
            return;
        }
        args.Data.SetText(row.Id);
        args.Data.RequestedOperation = DataPackageOperation.Move | DataPackageOperation.Copy;
    }

    private async void OnQuickAdd(object sender, RoutedEventArgs args) => await AddTypedTask();

    private async void OnQuickAddKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter)
        {
            return;
        }
        args.Handled = true;
        await AddTypedTask();
    }

    private async Task AddTypedTask()
    {
        var title = QuickAddBox.Text;
        if (await Shell.Tasks.CreateTaskAsync(title))
        {
            // Cleared only on success, so a task that could not be created is not also a task the
            // user has to retype.
            QuickAddBox.Text = string.Empty;
            QuickAddBox.Focus(FocusState.Programmatic);
            // The notice stays as long as the web's toast does. A second add in the meantime
            // restarts the clock rather than having the first one's clock take the second down.
            var ticket = ++_addedNoticeTicket;
            await Task.Delay(TimeSpan.FromSeconds(2.5));
            if (ticket == _addedNoticeTicket)
            {
                Shell.Tasks.ClearCreatedNotice();
            }
            return;
        }
        QuickAddBox.Focus(FocusState.Programmatic);
    }

    private int _addedNoticeTicket;

    /// <summary>
    /// The mark on a row, tapped.
    /// </summary>
    /// <remarks>
    /// One handler rather than the checked/unchecked pair a CheckBox gives, because the control is
    /// now an image: it draws the state the core reported and asks for the opposite. A repeating
    /// task does not finish here either way — it rolls forward — which is why this always goes
    /// through the complete command rather than writing a flag.
    /// </remarks>
    private async void OnRowMarkClicked(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: string taskId })
        {
            return;
        }
        var row = Shell.Tasks.Rows.FirstOrDefault(candidate => candidate.Id == taskId);
        if (row is { IsCopyOnly: true })
        {
            // The core drew a copy control, so the tap copies (task f6bc59e8).
            await Shell.Tasks.CopyAsync(taskId);
            return;
        }
        await Shell.Tasks.SetCompletedAsync(taskId, completed: row is null || !row.Completed);
    }

    private async Task SetCompleted(object sender, bool completed)
    {
        if (sender is FrameworkElement { Tag: string taskId })
        {
            // Always the complete command. A repeating task rolls forward rather than finishing,
            // and only that path does it — see astrid_core::services::task.
            await Shell.Tasks.SetCompletedAsync(taskId, completed);
        }
    }

    private async void OnDeleteRow(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { Tag: string taskId })
        {
            await Shell.Tasks.DeleteTaskAsync(taskId);
        }
    }

    /// <summary>
    /// Selecting a task opens it.
    /// </summary>
    /// <remarks>
    /// Selection and opening are one thing on web — a click sets <c>selectedTaskId</c>, and that
    /// is what the pane draws from, so moving with j and k opens each task in turn. This was a
    /// double-tap here, which made the pane the only screen a keyboard could not reach and asked
    /// for a gesture no other client asks for.
    /// </remarks>
    /// <summary>
    /// True while a selection change is the tail of a pointer press.
    /// </summary>
    /// <remarks>
    /// A click raises PointerPressed, then SelectionChanged, then ItemClick. Only the last of
    /// those can tell a tap on the ALREADY selected row from a tap on a new one, because tapping
    /// the selected row raises no selection change at all — so the click path owns the decision
    /// and the selection path stands down for it.
    /// </remarks>
    private bool _selectionFromPointer;

    private void OnRowsPointerPressed(object sender, PointerRoutedEventArgs args) =>
        _selectionFromPointer = true;

    /// <summary>
    /// Selection moved. Opens the task — which is how the pane is reachable from a keyboard.
    /// </summary>
    /// <remarks>
    /// Stands down for the pointer: <see cref="OnRowClicked"/> handles that, and both acting would
    /// open a task and then immediately close it again.
    /// </remarks>
    private async void OnRowSelected(object sender, SelectionChangedEventArgs args)
    {
        if (_selectionFromPointer)
        {
            _selectionFromPointer = false;
            return;
        }
        if (Shell.Tasks.Selected is { } row)
        {
            // The view model tells a moved selection from a refresh re-selecting the same task.
            await Shell.SelectRowAsync(row.Id);
            RowActivated?.Invoke();
        }
    }

    /// <summary>The ScrollViewer a ListView's template wraps its items in, if it has one yet.</summary>
    private static ScrollViewer? ScrollViewerInside(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer found)
            {
                return found;
            }
            if (ScrollViewerInside(child) is { } deeper)
            {
                return deeper;
            }
        }
        return null;
    }

    /// <summary>A row was tapped. The view model decides whether that opens or closes.</summary>
    private async void OnRowClicked(object sender, ItemClickEventArgs args)
    {
        _selectionFromPointer = false;
        if (args.ClickedItem is not TaskRow row)
        {
            return;
        }
        await Shell.OpenOrCloseTaskAsync(row.Id);
        RowActivated?.Invoke();
    }
}
