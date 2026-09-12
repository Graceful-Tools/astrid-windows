using Microsoft.UI.Xaml;

namespace Astrid.App.Views;

/// <summary>
/// The converters, styles and shared templates every view in the window draws with.
/// </summary>
/// <remarks>
/// A class-backed dictionary, because a <c>DataTemplate</c> with <c>x:Bind</c> in it has to be
/// compiled against something. Merged from <c>App.xaml</c>.
/// </remarks>
public sealed partial class ShellResources : ResourceDictionary
{
    public ShellResources()
    {
        InitializeComponent();
    }
}
