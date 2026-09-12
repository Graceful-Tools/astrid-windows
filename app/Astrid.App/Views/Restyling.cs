using Microsoft.UI.Xaml.Controls;

namespace Astrid.App.Views;

/// <summary>
/// Re-attach a list's item container style, so rows already on screen take the current theme.
/// </summary>
/// <remarks>
/// A ThemeResource in a Style SETTER is resolved when the style is applied to a container and
/// never again, so rows already on screen keep the colours of the theme they were born in.
/// Switching to dark left white cards carrying white text — unreadable until a restart.
/// Re-attaching the style makes every live container resolve its setters again.
/// </remarks>
internal static class Restyling
{
    internal static void Reapply(ListView view)
    {
        var style = view.ItemContainerStyle;
        view.ItemContainerStyle = null;
        view.ItemContainerStyle = style;
    }
}
