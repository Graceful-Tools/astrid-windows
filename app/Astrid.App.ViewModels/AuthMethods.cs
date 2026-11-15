namespace Astrid.App.ViewModels;

/// <summary>
/// Which ways of signing in this build offers, and how to say them in one sentence (task 28348df8).
/// </summary>
/// <remarks>
/// <para>
/// The sign-in card said "Passkeys, Google and magic links all work there" whatever the build was
/// made for, so a partner whose deployment offers GitHub and single sign-on — and no Google —
/// promised their users two methods that are not there and hid the two that are. The methods are a
/// brand value like the server and the scheme, and they arrive the same way: from the profile,
/// through <c>package.ps1</c>, as assembly metadata <see cref="BrandDefaults"/> reads back.
/// </para>
/// <para>
/// The ids are astrid-web's, from <c>lib/brand/auth-providers.ts</c> — one ordered list, because
/// that file's rules are what the server actually offers and a second vocabulary here would be a
/// second thing to keep in step. <b>The order is the brand's</b>, and it survives: a deployment
/// that leads with single sign-on says so first.
/// </para>
/// <para>
/// The eventual refinement is <c>GET /api/v1/capabilities</c>, which answers this for the server
/// the app is actually talking to. It is not what this reads, for the same reason
/// <see cref="BrandDefaults.ServerOrigin"/> is baked in rather than discovered: the card is drawn
/// before there is a session, often before there is a network, and a sentence that has to wait for
/// a round trip is a sentence that is blank when it is read.
/// </para>
/// </remarks>
public static class AuthMethods
{
    /// <summary>The provider ids astrid-web knows, which are the only ones a profile may name.</summary>
    /// <remarks>
    /// Validated rather than trusted, the same as the scheme and the server: the value comes from a
    /// command line, and a typo should cost a partner one missing method rather than a sentence
    /// with "goolge" in it.
    /// </remarks>
    public static readonly IReadOnlyList<string> Known = ["github", "google", "apple", "passkey", "sso"];

    /// <summary>
    /// The ids in <paramref name="configured"/>, or <paramref name="fallback"/> when it names none.
    /// </summary>
    /// <remarks>
    /// Unknown names and repeats are dropped and the rest keep their order — astrid-web's
    /// <c>resolveAuthProviders</c> with an explicit list, which is the only form that reaches a
    /// Windows build: the legacy <c>ENABLE_AUTH_*</c> switches are resolved into this list by
    /// <c>scripts/lib/brand-profile.ps1</c>, so the rule has one home on this side of the wire.
    /// </remarks>
    public static IReadOnlyList<string> Parse(string? configured, IReadOnlyList<string> fallback)
    {
        if (configured is null)
        {
            return fallback;
        }

        var resolved = new List<string>();
        foreach (var name in configured.Split(','))
        {
            var id = name.Trim().ToLowerInvariant();
            if (Known.Contains(id) && !resolved.Contains(id))
            {
                resolved.Add(id);
            }
        }
        return resolved.Count == 0 ? fallback : resolved;
    }

    /// <summary>
    /// <paramref name="words"/> as a list a sentence can carry: "Passkeys, Google and magic links".
    /// </summary>
    /// <remarks>
    /// <para>
    /// The separator and the conjunction are handed in rather than written here, because both are
    /// the translator's: German joins with "und", and a language that does not use a comma says so
    /// in its own <c>Resources.resw</c>. The conjunction is a format string with two places, so a
    /// language that puts the last item first can.
    /// </para>
    /// <para>
    /// The first word is capitalised because the sentence begins with it, and the words themselves
    /// cannot be: "magic links" is lower case in the middle of a list and upper case at the start
    /// of one, and only the composition knows which it is. A brand name in the list — "GitHub" —
    /// is unaffected, since it is already capital.
    /// </para>
    /// </remarks>
    public static string List(IReadOnlyList<string> words, string separator, string conjunction)
    {
        var sentence = words.Count switch
        {
            0 => string.Empty,
            1 => words[0],
            _ => string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                conjunction,
                string.Join(separator, words.Take(words.Count - 1)),
                words[^1]),
        };
        return sentence.Length == 0
            ? sentence
            : char.ToUpper(sentence[0], System.Globalization.CultureInfo.CurrentCulture) + sentence[1..];
    }
}
