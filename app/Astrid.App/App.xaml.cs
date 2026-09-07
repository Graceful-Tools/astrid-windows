using Astrid.Core.Bindings;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Astrid.App;

/// <summary>
/// The process: start the core, open a window, and route every activation to it.
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
    private readonly AppActivationArguments? _launchActivation;
    private Window? _window;

    public App(AppActivationArguments? launchActivation = null)
    {
        _launchActivation = launchActivation;
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

    /// <summary>Raised for every activation, launch or redirected, with its URI.</summary>
    /// <remarks>
    /// A sign-in callback is an activation. It arrives as a launch when the app was closed and as a
    /// redirect when it was open, and the window has to handle both the same way.
    /// </remarks>
    internal static event Action<Uri>? UriActivated;

    /// <summary>The running core, for the windows to use.</summary>
    internal static AstridClient? Core { get; private set; }

    /// <summary>Why the core would not start, if it would not.</summary>
    internal static string? StartupError { get; private set; }

    /// <summary>Where the last crash was written.</summary>
    internal static string CrashLogPath => Path.Combine(DataDirectory(), "crash.log");

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

        // Every later activation — the browser coming back with a sign-in code, a deep link —
        // arrives here rather than as a new process, because Program.Main made this instance the
        // one that owns the app.
        AppInstance.GetCurrent().Activated += (_, activation) => Deliver(activation);

        _window = new MainWindow();
        _window.Closed += (_, _) =>
        {
            Core?.Dispose();
            Core = null;
        };
        _window.Activate();

        // The cold case: the app was started BY the callback. The window exists now, so it can be
        // told. Doing this before Activate would raise the event with nobody listening, which is
        // the difference between signing in and appearing to hang on the browser hand-off.
        if (_launchActivation is not null)
        {
            Deliver(_launchActivation);
        }
    }

    /// <summary>Pull the URI out of an activation and tell whoever is listening.</summary>
    private static void Deliver(AppActivationArguments activation)
    {
        if (activation.Kind != ExtendedActivationKind.Protocol)
        {
            return;
        }
        if (activation.Data is Windows.ApplicationModel.Activation.IProtocolActivatedEventArgs protocol)
        {
            UriActivated?.Invoke(protocol.Uri);
        }
    }

    private static void Log(Exception exception) => Log(exception.ToString());

    /// <summary>
    /// Note something that went wrong but did not stop the app.
    /// </summary>
    /// <remarks>
    /// The same file as a crash, because the question being answered is always "what happened on
    /// that machine?" and two files means finding one of them.
    /// </remarks>
    internal static void Log(string message)
    {
        try
        {
            File.AppendAllText(
                CrashLogPath,
                $"{DateTimeOffset.Now:O}  {message}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // Nothing useful to do when even the log will not write.
        }
    }

    /// <summary>
    /// Where the cache lives: the per-user local app data folder.
    /// </summary>
    /// <remarks>
    /// Local rather than roaming, deliberately. The cache is a cache — it can be rebuilt from the
    /// server in a sync — and roaming a SQLite file between machines is a good way to corrupt it
    /// while two of them have it open.
    /// </remarks>
    private static string CachePath() => Path.Combine(DataDirectory(), "astrid.db");

    /// <summary>
    /// Where the cache and the credential live.
    /// </summary>
    /// <remarks>
    /// <c>ASTRID_DATA_DIR</c> moves both. It exists for the UI smoke tests, which need a signed-in
    /// app with a known list in it and must not touch the account of whoever is running them — a
    /// test that wrote a fake session over somebody's real one would be a test nobody runs twice.
    /// It is also the honest way to try a second account without signing out of the first.
    /// </remarks>
    private static string DataDirectory()
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
}
