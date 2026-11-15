using Astrid.App.ViewModels;

namespace Astrid.App;

/// <summary>
/// The sentences that name the brand, with the brand's own name in them (task 8d241526).
/// </summary>
/// <remarks>
/// <para>
/// Every word on screen comes from <see cref="Strings"/>, and nearly all of them are applied by
/// <c>x:Uid</c> — the resource system writes the string straight onto the element and nothing gets
/// a chance to touch it. That is exactly wrong for the handful of lines that have to say what this
/// build is called: the resource is the translator's, the noun is the packager's, and only one of
/// them can win an <c>x:Uid</c>.
/// </para>
/// <para>
/// So these lines carry a <c>{0}</c> in the resource, are formatted here with a value from
/// <see cref="BrandDefaults"/>, and are bound in XAML as <c>{x:Bind local:BrandCopy.…}</c> —
/// which is also what takes them out of <c>LocalisationTests</c>'s literal scan, since a binding
/// is not an English literal. The same shape <see cref="Brand"/> uses for the images.
/// </para>
/// <para>
/// Read once, at type load. None of these can change while the window is open: the brand is the
/// build's, and the language is chosen before the first page is parsed.
/// </para>
/// </remarks>
internal static class BrandCopy
{
    /// <summary>The product's name, as the title bar and the sign-in card show it.</summary>
    /// <remarks>
    /// Not a resource: a brand name is not translated, and a <c>app_name</c> string per language
    /// was three copies of "Astrid" that a partner had to find and change.
    /// </remarks>
    public static string AppName { get; } = BrandDefaults.ShortName;

    /// <summary>The address a task can be mailed to, on the Tasks settings page.</summary>
    public static string InboundTaskEmail { get; } = BrandDefaults.InboundTaskEmail;

    /// <summary>What an exported file is called, before the date and the extension.</summary>
    /// <remarks>
    /// <para>
    /// Not copy in the sense the rest of this file is — it is a file name — but it is a brand value
    /// the user sees, and it was the last literal <c>Astrid</c> the shell put in front of one
    /// (task 5bf1f1dc): a partner's user saved <c>astrid-export-2026-11-12.json</c> out of an app
    /// that is not called Astrid.
    /// </para>
    /// <para>
    /// Lower-cased and stripped the way <c>scripts/lib/brand-profile.ps1</c> strips the package
    /// stem, for the same reason: a short name is prose and is free to carry a space or a comma,
    /// and the save dialog rejects a suggested name that holds either. A name with nothing usable
    /// left falls back to <c>export</c> rather than to Astrid's — our name has no business on a
    /// partner's file.
    /// </para>
    /// </remarks>
    public static string ExportFileStem { get; } = BuildExportFileStem();

    private static string BuildExportFileStem()
    {
        var stem = new string(BrandDefaults.ShortName.ToLowerInvariant()
            .Select(character => character is ' ' ? '-' : character)
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        return stem.Length == 0 ? "export" : $"{stem}-export";
    }

    /// <summary>Where to turn a board on, when a list has none.</summary>
    /// <remarks>
    /// This one named <c>astrid.app</c> — a host the product has never been on, and so a line that
    /// was wrong for hosted Astrid too. It says the build's own server now.
    /// </remarks>
    public static string BoardTurnOneOn { get; } =
        Strings.Get("brand.board_turn_one_on", BrandDefaults.Domain);

    /// <summary>Where to go when the server wants a browser session this client cannot give it.</summary>
    public static string ConnectionsWebSession { get; } =
        Strings.Get("brand.connections_web_session", BrandDefaults.Domain);

    /// <summary>The tour's line about the quick-add shortcut.</summary>
    public static string TourHotkey { get; } =
        Strings.Get("brand.tour_hotkey", BrandDefaults.ShortName);

    /// <summary>The same thing said again under the shortcut box in Appearance.</summary>
    public static string HotkeyHint { get; } =
        Strings.Get("appearance.hotkey_hint", BrandDefaults.ShortName);

    /// <summary>The heading over the assistant's model choice.</summary>
    /// <remarks>The handle, not the name: it is written as a mention.</remarks>
    public static string AgentModelHeading { get; } =
        Strings.Get("brand.agent_model_heading", BrandDefaults.Wordmark);

    /// <summary>What that section says when no model can power the assistant yet.</summary>
    public static string AgentModelNone { get; } =
        Strings.Get("brand.agent_model_none", BrandDefaults.AgentName);

    /// <summary>The "let it choose for itself" option.</summary>
    public static string AgentModelAuto { get; } =
        Strings.Get("brand.agent_model_auto", BrandDefaults.AgentName);

    /// <summary>The line under the model list.</summary>
    public static string AgentModelHint { get; } =
        Strings.Get("brand.agent_model_hint", BrandDefaults.AgentName);

    /// <summary>
    /// What the sign-in card promises works in the browser (task 28348df8).
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Passkeys, Google and magic links all work there" was a literal, so every partner build made
    /// the same promise about methods its deployment may not have — and hid the ones it does, since
    /// GitHub and single sign-on are reached only by listing them. The methods come from the brand
    /// now, through <see cref="BrandDefaults.AuthProviders"/>.
    /// </para>
    /// <para>
    /// Here rather than on the element, and for the usual reason: the list has to be composed
    /// before it can be shown, and an <c>x:Uid</c> writes the resource straight onto the element
    /// with nothing in between. The sentence, the separator, the conjunction and every method's
    /// name are all resources — only the choosing and the joining happen here.
    /// </para>
    /// <para>
    /// A magic link is always last and is never a provider: astrid-web offers no switch for it, and
    /// it is the method a deployment falls back to when the rest are unavailable.
    /// </para>
    /// </remarks>
    public static string SignInMethods { get; } = BuildSignInMethods();

    private static string BuildSignInMethods()
    {
        var names = BrandDefaults.AuthProviders
            .Select(provider => Strings.Get($"auth.method_{provider}"))
            .Append(Strings.Get("auth.method_magic_link"))
            .ToList();
        var list = AuthMethods.List(names, Strings.Get("list.separator"), Strings.Get("list.conjunction"));
        return Strings.Get("brand.sign_in_methods", list);
    }
}
