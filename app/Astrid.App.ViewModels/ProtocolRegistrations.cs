namespace Astrid.App.ViewModels;

/// <summary>
/// Which of the app's own <c>astrid://</c> registrations belong to some other copy of it.
/// </summary>
/// <remarks>
/// <para>
/// An unpackaged build registers <c>astrid://</c> through the App SDK on every start, and the
/// registration is keyed by the executable's path. So every build flavour that has ever run on a
/// machine — Debug, Release, the portable folder, the same path spelt with a different drive-letter
/// case — leaves its own registration behind, each named "Astrid". Windows then has several apps
/// claiming the scheme and, with no default chosen, hands the URL to its "How do you want to open
/// this?" picker; the first choice made there sticks, and it was the wrong build on 2026-09-13
/// (task 64c02099): a September build started as its own instance, swallowed the sign-in code, and
/// the app that had asked for it waited forever.
/// </para>
/// <para>
/// The documented rule is "whichever ran last wins". This is the pure half of making that true:
/// given the registrations that claim the scheme, say which executables are not this one so the
/// caller can unregister them. Registry access stays in the shell.
/// </para>
/// </remarks>
public static class ProtocolRegistrations
{
    /// <summary>One registration: its name under <c>RegisteredApplications</c> and its open command.</summary>
    public readonly record struct Registration(string Name, string? Command);

    /// <summary>
    /// The executables of every registration that is not <paramref name="thisExecutable"/>, in
    /// the order given, without duplicates.
    /// </summary>
    public static IReadOnlyList<string> StaleExecutables(
        IEnumerable<Registration> registrations, string thisExecutable)
    {
        var mine = Normalise(thisExecutable);
        var stale = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations)
        {
            var executable = ExecutableOf(registration.Command);
            if (executable is null)
            {
                continue;
            }
            var key = Normalise(executable);
            if (string.Equals(key, mine, StringComparison.OrdinalIgnoreCase) || !seen.Add(key))
            {
                continue;
            }
            stale.Add(executable);
        }
        return stale;
    }

    /// <summary>
    /// The executable in an open command, as the App SDK writes it: either
    /// <c>"C:\path\Astrid.App.exe" "----ms-protocol:%1"</c> or, unquoted,
    /// <c>C:\path\Astrid.App.exe "----ms-protocol:%1"</c>.
    /// </summary>
    public static string? ExecutableOf(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }
        var trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            var closing = trimmed.IndexOf('"', 1);
            return closing > 1 ? trimmed[1..closing] : null;
        }
        // Unquoted: the executable runs up to the argument, which the App SDK always quotes.
        var argument = trimmed.IndexOf(" \"", StringComparison.Ordinal);
        var executable = argument > 0 ? trimmed[..argument] : trimmed;
        return executable.Length > 0 ? executable : null;
    }

    private static string Normalise(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }
}
