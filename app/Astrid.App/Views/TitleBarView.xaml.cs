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

    /// <summary>What <c>Window.SetTitleBar</c> is given: the strip a person drags the window by.</summary>
    internal UIElement DragRegion => Bar;
}
