using Microsoft.UI.Xaml.Media.Imaging;

namespace Astrid.App;

/// <summary>
/// The character, as the window draws it: astrid-web's <c>BRAND.icon</c> and <c>BRAND.iconSmall</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two files beside the executable, written by <c>scripts/make-icons.ps1</c> from the same master
/// as the taskbar icon and the Store tiles. XAML binds to these rather than naming the files:
/// <c>Source="ms-appx:///Assets/…"</c> written straight into XAML brings the unpackaged build
/// down at the moment the page is parsed — 0xC000027B inside Microsoft.UI.Xaml.dll, no managed
/// exception to catch, nothing in the log — while a source handed over as an object is fine.
/// The checkbox images have always gone that way, through a binding; these do too.
/// </para>
/// <para>
/// One image object per size, shared by every element that shows it; the decoded bitmap is
/// shared with it.
/// </para>
/// </remarks>
internal static class Brand
{
    /// <summary>The 512px mark: the sign-in card, the empty list.</summary>
    public static BitmapImage Mark { get; } = Load("Astrid-512.png");

    /// <summary>The 96px mark: the title bar, beside the agent's name.</summary>
    public static BitmapImage SmallMark { get; } = Load("Astrid-96.png");

    private static BitmapImage Load(string name) =>
        new(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", name)));
}
