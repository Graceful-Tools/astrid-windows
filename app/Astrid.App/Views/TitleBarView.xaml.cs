using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Astrid.App.Views;

/// <summary>
/// The title bar: the mark, the wordmark, and the drag region the window hands to Windows.
/// </summary>
public sealed partial class TitleBarView : UserControl
{
    public TitleBarView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// What <c>Window.SetTitleBar</c> is given: the strip a person drags the window by.
    /// </summary>
    /// <remarks>
    /// The run of the strip to the RIGHT of the brand header, not the whole bar. Windows eats the
    /// input over whatever it is handed, so the header's search control has to sit outside it —
    /// inside, the button draws and does nothing at all (task 20dcc235).
    /// </remarks>
    internal UIElement DragRegion => DragStrip;

    /// <summary>Someone asked for search from the header. <c>ShellPage</c> owns what that means.</summary>
    internal event Action? SearchRequested;

    private void OnSearch(object sender, RoutedEventArgs args) => SearchRequested?.Invoke();
}
