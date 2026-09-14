namespace Astrid.App.ViewModels;

/// <summary>
/// Finds the <c>astrid://</c> callback in the arguments Windows started the app with.
/// </summary>
/// <remarks>
/// <para>
/// The browser hand-off comes back as <c>astrid://auth/callback?…</c>, and Windows has two ways of
/// starting the app for it. Through the App SDK's ProgId the argument is
/// <c>----ms-protocol:astrid://…</c> and the activation is a protocol one. Through the plain
/// <c>Classes\astrid</c> registration — the one the shell actually used on 2026-09-13, because the
/// URL association the ProgId depends on did not exist — the command is <c>"%1"</c>, so the app is
/// simply launched with the URL as its argument. Both shapes have to be read, or the second
/// silently loses the sign-in it was carrying.
/// </para>
/// <para>
/// Only the scheme is checked here. The core decides whether the URL is a callback at all, and
/// whether its state and code are the ones it is waiting for.
/// </para>
/// </remarks>
public static class SignInCallback
{
    private const string Scheme = "astrid://";

    /// <summary>The App SDK's marker in front of a protocol URL it passes on the command line.</summary>
    private const string ProtocolMarker = "----ms-protocol:";

    /// <summary>The URL in <paramref name="arguments"/>, or null when there is none.</summary>
    public static string? FromLaunchArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }

        // A URL has no whitespace, so splitting on it is enough to find the argument; the quotes
        // the shell wraps "%1" in are stripped from either end.
        foreach (var raw in arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim('"');
            if (token.StartsWith(ProtocolMarker, StringComparison.Ordinal))
            {
                token = token[ProtocolMarker.Length..];
            }
            if (token.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
            {
                return token;
            }
        }
        return null;
    }
}
