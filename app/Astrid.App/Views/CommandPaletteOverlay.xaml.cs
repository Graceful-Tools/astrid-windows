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
/// The command palette: a box, the rows the core answers with, and the keys that move between them.
/// </summary>
public sealed partial class CommandPaletteOverlay : UserControl
{
    public static readonly DependencyProperty ShellProperty = DependencyProperty.Register(
        nameof(Shell), typeof(ShellViewModel), typeof(CommandPaletteOverlay),
        new PropertyMetadata(null));

    /// <summary>What this control binds to. <see cref="ShellPage"/> sets it before the control loads.</summary>
    public ShellViewModel Shell
    {
        get => (ShellViewModel)GetValue(ShellProperty);
        set => SetValue(ShellProperty, value);
    }

    /// <summary>A row was run. The page puts the sidebar's highlight where the view model now says.</summary>
    internal event Action? RowRun;

    public CommandPaletteOverlay()
    {
        InitializeComponent();
    }

    /// <summary>Empty the box and put the caret in it — what Ctrl+K does once the palette is shown.</summary>
    internal void TakeFocus()
    {
        PaletteBox.Text = string.Empty;
        PaletteBox.Focus(FocusState.Programmatic);
    }

    // ── The command palette ──────────────────────────────────────────────────────────────────

    private async void OnPaletteChanged(object sender, TextChangedEventArgs args)
    {
        await Shell.SearchPaletteAsync(PaletteBox.Text);
    }

    /// <summary>
    /// Enter runs the first row; Escape closes.
    /// </summary>
    /// <remarks>
    /// The first row rather than the selected one when nothing is selected, because typing three
    /// letters and pressing Enter is the whole gesture — reaching for the arrow keys first would
    /// make it slower than the sidebar it replaces.
    /// </remarks>
    private async void OnPaletteKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.Escape:
                args.Handled = true;
                await Shell.ShowPaletteAsync(false);
                break;
            case VirtualKey.Enter:
                args.Handled = true;
                var row = PaletteList.SelectedItem as PaletteRow ?? Shell.PaletteRows.FirstOrDefault();
                if (row is not null)
                {
                    await Shell.RunPaletteRowAsync(row);
                    RowRun?.Invoke();
                }
                break;
            case VirtualKey.Down:
                args.Handled = true;
                Step(1);
                break;
            case VirtualKey.Up:
                args.Handled = true;
                Step(-1);
                break;
        }
    }

    /// <summary>Move the highlight without leaving the box, so typing can continue.</summary>
    private void Step(int by)
    {
        if (Shell.PaletteRows.Count == 0)
        {
            return;
        }
        var next = PaletteList.SelectedIndex + by;
        PaletteList.SelectedIndex = Math.Clamp(next, 0, Shell.PaletteRows.Count - 1);
        PaletteList.ScrollIntoView(PaletteList.SelectedItem);
    }

    private async void OnPaletteRowChosen(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is PaletteRow row)
        {
            await Shell.RunPaletteRowAsync(row);
            RowRun?.Invoke();
        }
    }
}
