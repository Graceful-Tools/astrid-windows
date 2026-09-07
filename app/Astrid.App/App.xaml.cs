using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;

namespace Astrid.App;

/// <summary>
/// The process: start the core, open a window, and stop the core when the last one closes.
/// </summary>
/// <remarks>
/// <para>
/// The core is started here rather than by the window, because it owns the cache and a second
/// instance opening the same file is a corruption waiting for the first busy afternoon. One core,
/// for as long as the process lives.
/// </para>
/// <para>
/// It is started <b>before</b> the window is shown, and the window's first paint reads from the
/// cache. The alternative — show a window, then start — buys a visible empty frame in exchange for
/// nothing, because opening a SQLite file takes a millisecond.
/// </para>
/// </remarks>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();

        // A WinUI app that throws during layout dies as exit code 0xC000027B with nothing on
        // screen and nothing in the console — a "stowed exception", which tells the person running
        // it precisely nothing. Writing the exception somewhere findable is the difference between
        // a bug report that can be acted on and one that says "it just closes".
        UnhandledException += (_, args) =>
        {
            Log(args.Exception);
            // Left unhandled on purpose: swallowing it leaves the app running in whatever state
            // the failure produced, which is worse than stopping.
        };
    }

    /// <summary>Where the last crash was written.</summary>
    internal static string CrashLogPath => Path.Combine(DataDirectory(), "crash.log");

    private static void Log(Exception exception)
    {
        try
        {
            File.AppendAllText(
                CrashLogPath,
                $"{DateTimeOffset.Now:O}  {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Nothing useful to do when even the log will not write.
        }
    }

    /// <summary>The running core, for the windows to use.</summary>
    internal static AstridClient? Core { get; private set; }

    /// <summary>Why the core would not start, if it would not.</summary>
    internal static string? StartupError { get; private set; }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Core = AstridClient.Start(CachePath());
            Core.Subscribe();
        }
        catch (AstridStartupException error)
        {
            // A window that says what went wrong is far better than a process that exits silently,
            // which is what an unhandled exception here would look like to somebody double-clicking
            // an icon.
            StartupError = error.Message;
        }

        _window = new MainWindow();
        _window.Closed += (_, _) =>
        {
            Core?.Dispose();
            Core = null;
        };
        _window.Activate();
    }

    /// <summary>
    /// Where the cache lives: the roaming-free per-user local app data folder.
    /// </summary>
    /// <remarks>
    /// Local rather than roaming, deliberately. The cache is a cache — it can be rebuilt from the
    /// server in a sync — and roaming a SQLite file between machines is a good way to corrupt it
    /// while two of them have it open.
    /// </remarks>
    private static string CachePath() => Path.Combine(DataDirectory(), "astrid.db");

    private static string DataDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Astrid");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
