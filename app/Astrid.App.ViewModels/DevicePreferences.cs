using System.Text.Json;

namespace Astrid.App.ViewModels;

/// <summary>
/// Flags this one device remembers about its own window.
/// </summary>
/// <remarks>
/// <para>
/// Which panes are open is a property of a screen, not of an account: a wide desktop and a laptop
/// want different answers, and syncing one over the other would be a setting that changes itself.
/// astrid-web keeps the same two flags in that browser's local storage
/// (<c>astrid.sidebarCollapsed</c>, <c>astrid.chatPaneCollapsed</c>) rather than on the server, and
/// this is the same decision taken the same way.
/// </para>
/// <para>
/// So it is deliberately NOT a core command. The core's own per-device memory — whether the tour
/// has been seen — goes through <c>hasSeenTour</c>/<c>tourSeen</c>, and adding a command here would
/// mean a change in astrid-core and a pin bump for a window's idea of its own furniture. Nothing
/// about a hidden pane is a rule, so nothing about it belongs in the rules.
/// </para>
/// </remarks>
public interface IDevicePreferences
{
    /// <summary>What this device last said, or <paramref name="fallback"/> if it has never said.</summary>
    bool Flag(string key, bool fallback);

    /// <summary>Remember an answer for next launch.</summary>
    void SetFlag(string key, bool value);
}

/// <summary>Remembers nothing. The default, so a test or a tool needs no files to run.</summary>
public sealed class NoDevicePreferences : IDevicePreferences
{
    public static readonly NoDevicePreferences Instance = new();

    public bool Flag(string key, bool fallback) => fallback;

    public void SetFlag(string key, bool value)
    {
    }
}

/// <summary>
/// The flags, in a small JSON file beside the core's own data.
/// </summary>
/// <remarks>
/// <b>Nothing here throws.</b> A window that would not open because a preferences file was
/// half-written, read-only or owned by another user would be a worse bug than any it remembers, so
/// every failure degrades to the fallback and the pane simply opens where it always did. The file
/// is small and written whole on each change: there is no partial-update case to get wrong, and a
/// torn write costs one forgotten preference rather than a corrupt store.
/// </remarks>
public sealed class DevicePreferences : IDevicePreferences
{
    /// <summary>Whether the list sidebar is beside the work. Open on a device that has not said.</summary>
    public const string SidebarOpen = "sidebarOpen";

    /// <summary>Whether the messages pane is beside the work.</summary>
    public const string ChatOpen = "chatOpen";

    private readonly string _path;
    private readonly Dictionary<string, bool> _flags;

    private DevicePreferences(string path, Dictionary<string, bool> flags)
    {
        _path = path;
        _flags = flags;
    }

    /// <summary>
    /// The directory this device keeps its data in.
    /// </summary>
    /// <remarks>
    /// <c>ASTRID_DATA_DIR</c> moves it, which is what lets the UI smoke tests run against a known
    /// account without writing over whoever is running them. One definition, used by the core's
    /// data directory as well, because two would drift and the tests would start passing against a
    /// profile the app does not use.
    /// </remarks>
    public static string DataDirectory()
    {
        var chosen = Environment.GetEnvironmentVariable("ASTRID_DATA_DIR");
        var directory = string.IsNullOrWhiteSpace(chosen)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Astrid")
            : chosen;
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Open this device's flags, reading what it said last time.</summary>
    public static DevicePreferences Open(string directory)
    {
        var path = Path.Combine(directory, "window.json");
        return new DevicePreferences(path, Read(path));
    }

    /// <summary>Open the flags for the device the app is running on.</summary>
    public static DevicePreferences ForThisDevice()
    {
        try
        {
            return Open(DataDirectory());
        }
        catch (Exception)
        {
            // No data directory means no memory, not no window.
            return Open(Path.GetTempPath());
        }
    }

    public bool Flag(string key, bool fallback) =>
        _flags.TryGetValue(key, out var remembered) ? remembered : fallback;

    public void SetFlag(string key, bool value)
    {
        _flags[key] = value;
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_flags));
        }
        catch (Exception)
        {
            // Remembered for this session; forgotten by the next. See the class remarks.
        }
    }

    private static Dictionary<string, bool> Read(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(path))
                   ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
