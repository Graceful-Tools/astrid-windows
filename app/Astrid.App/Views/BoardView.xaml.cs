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
    /// The slot after the expanded card has loaded. The page moves its one detail pane into it.
    /// </summary>
    internal event Action<ContentControl>? DetailSlotLoaded;

    public BoardView()
    {
        InitializeComponent();
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

    private void OnInlineDetailSlotLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is ContentControl host)
        {
            DetailSlotLoaded?.Invoke(host);
        }
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
            item.Click += async (_, _) => await Shell.Board.MoveAsync(taskId, column.Id);
            menu.Items.Add(item);
        }
        menu.ShowAt(card, args.GetPosition(card));
    }
}
