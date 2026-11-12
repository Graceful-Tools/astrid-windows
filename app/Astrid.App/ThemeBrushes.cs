using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace Astrid.App;

/// <summary>
/// A themed brush by key, resolved for the theme an element is actually wearing (task b4807cb5).
/// </summary>
/// <remarks>
/// <para>
/// <c>Application.Current.Resources[key]</c> looks like the way to read a brush from code, and for
/// a key in the root dictionary it is. For a key in <c>ThemeDictionaries</c> it is not: it resolves
/// against <c>Application.RequestedTheme</c>, which this app never sets. The appearance is applied
/// by setting <c>RequestedTheme</c> on ShellPage's root — the only way to change it after startup —
/// so every XAML <c>ThemeResource</c> follows the element while every lookup from code goes on
/// answering with the <c>Default</c> dictionary, which is the light one.
/// </para>
/// <para>
/// It is a quiet failure: in a light theme the two agree and nothing looks wrong. In a dark theme
/// the caller gets a light brush, and what it paints is a light shape on a dark surface. Found by
/// looking at the running app rather than by a test — the chat bubbles draw white text on the light
/// theme's near-white <c>AstridBgSelected</c> and cannot be read at all.
/// </para>
/// <para>
/// So the lookup takes the element and asks its <c>ActualTheme</c>, which is the question the
/// caller meant. A key outside the theme dictionaries — <c>AstridAccentBrush</c>, a priority — is
/// found in the root dictionary as before.
/// </para>
/// <para>
/// A converter has no element to ask: <c>IValueConverter.Convert</c> is handed a value and nothing
/// else (task 770625f8). <see cref="Follow"/> is for them — the page registers the one element it
/// sets the appearance on, and <see cref="For(string)"/> asks that. Deliberately a reference to the
/// element rather than a copy of its theme: a stored <c>ElementTheme</c> is a second copy of the
/// truth, and goes stale the moment anything sets <c>RequestedTheme</c> without telling it. The
/// element cannot go stale, because it is what the appearance is set on.
/// </para>
/// </remarks>
internal static class ThemeBrushes
{
    /// <summary>
    /// The element that wears the app's appearance, for callers with no element of their own.
    /// </summary>
    /// <remarks>
    /// Weak, because this is static for the life of the process and the tree it points into is
    /// not: holding the page's content strongly would outlive the window.
    /// </remarks>
    private static WeakReference<FrameworkElement>? _appearance;

    /// <summary>Register the element the appearance is set on. <c>ShellPage.ApplyTheme</c> calls it.</summary>
    public static void Follow(FrameworkElement element) =>
        _appearance = new WeakReference<FrameworkElement>(element);

    /// <summary>The brush <paramref name="key"/> names in the theme <paramref name="element"/> wears.</summary>
    public static Brush For(FrameworkElement element, string key) =>
        Resolve(element.ActualTheme, key);

    /// <summary>
    /// The brush <paramref name="key"/> names in the theme the app is wearing.
    /// </summary>
    /// <remarks>
    /// For a converter, which has no element. Before <see cref="Follow"/> has been called — which
    /// is only possible before the page has applied a theme at all — the light dictionary answers,
    /// which is what WinUI's own default would have been.
    /// </remarks>
    public static Brush For(string key) =>
        Resolve(
            _appearance is not null && _appearance.TryGetTarget(out var element)
                ? element.ActualTheme
                : ElementTheme.Light,
            key);

    private static Brush Resolve(ElementTheme theme, string key)
    {
        var dictionaries = Application.Current.Resources.ThemeDictionaries;
        var wanted = theme == ElementTheme.Dark ? "Dark" : "Default";

        if (dictionaries.TryGetValue(wanted, out var entry)
            && entry is ResourceDictionary themed
            && themed.TryGetValue(key, out var found)
            && found is Brush brush)
        {
            return brush;
        }

        // Not a themed key, or a theme that carries no entry for it: the root dictionary answers.
        return Application.Current.Resources.TryGetValue(key, out var fallback) && fallback is Brush root
            ? root
            : new SolidColorBrush(Colors.Gray);
    }
}
