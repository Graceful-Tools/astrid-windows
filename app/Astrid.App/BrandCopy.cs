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
}
