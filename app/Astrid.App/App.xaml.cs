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
    /// <summary>
    /// The one window.
    /// </summary>
    /// <remarks>
    /// Static because the global hotkey has to reach it from a thread that has no reference to the
    /// application object, and this app is single-instance by construction — there is never a
    /// second one to confuse it with.
    /// </remarks>
    private static Window? _window;

    public App(AppActivationArguments? launchActivation = null)
    {
        _launchActivation = launchActivation;
        // Before the first XAML, so the first word asked for is already in the right language.
        PrepareResources();
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

    /// <summary>The window a file picker should belong to.</summary>
    internal static IntPtr MainWindowHandle { get; private set; }

    /// <summary>
    /// Put the window in front of whatever the user was doing.
    /// </summary>
    /// <remarks>
    /// `Activate` alone is not enough from a background thread: Windows only lets the foreground
    /// application steal focus, so a window restored this way can end up flashing in the taskbar
    /// instead of appearing. Restoring it first, then activating, is what actually brings it up.
    /// </remarks>
    internal static void BringToFront()
    {
        if (_window is null)
        {
            return;
        }
        if (_window.AppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.Restore();
        }
        _window.Activate();
        // And the Win32 way as well. `Activate` asks XAML to activate the window; only
        // `SetForegroundWindow` asks Windows to put it in front of whatever the person was using,
        // which is the whole point of a global hotkey. It is allowed here because the process that
        // owns the hotkey is granted foreground rights when it fires.
        SetForegroundWindow(MainWindowHandle);
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr window);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Core = AstridClient.Start(CachePath());
            Core.Subscribe();
        }
        catch (Exception error) when (error is AstridStartupException or BadImageFormatException
            or DllNotFoundException)
        {
            // A window that says what went wrong is far better than a process that exits silently,
            // which is what an unhandled exception here would look like to somebody double-clicking
            // an icon.
            //
            // The two loader failures are caught for the same reason. They were escaping as
            // unhandled — the app died before it drew anything, and the only trace was a stack in
            // the log — and they are the two that happen when the native core beside the
            // executable does not match the process that is trying to load it.
            StartupError = error.Message;
            Log($"the core did not load: {error}{Environment.NewLine}{Environment.NewLine}{Where()}");
        }

        // Every later activation — the browser coming back with a sign-in code, a deep link —
        // arrives here rather than as a new process, because Program.Main made this instance the
        // one that owns the app.
        AppInstance.GetCurrent().Activated += (_, activation) => Deliver(activation);

        _window = new MainWindow();
        // Kept because the WinRT pickers need it: an unpackaged app has to tell a picker which
        // window it belongs to, and without that it throws rather than opening — a failure that
        // looks like the button doing nothing at all.
        MainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
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

    /// <summary>
    /// Where this process is, and what it is.
    /// </summary>
    /// <remarks>
    /// Written beside a startup failure because "the core did not load" has one interesting cause
    /// — the process and the native library disagreeing about the architecture — and none of that
    /// is visible in a managed stack trace. It matters most for a launch nobody watched: the
    /// sign-in callback starts the app from the browser, and the environment there is not the one
    /// anybody tested by double-clicking.
    /// </remarks>
    private static string Where() =>
        string.Join(Environment.NewLine, [
            $"process:     {Environment.ProcessPath}",
            $"process arch:{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}",
            $"os arch:     {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}",
            $"base dir:    {AppContext.BaseDirectory}",
            $"working dir: {Environment.CurrentDirectory}",
            $"command line:{Environment.CommandLine}",
        ]);

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
    /// The resources beside the executable: every word <see cref="Strings.Get(string)"/> answers.
    /// </summary>
    /// <remarks>
    /// Null when there is no <c>.pri</c> there, in which case the XAML literals and the table in
    /// <c>Strings.cs</c> answer, in English.
    /// </remarks>
    internal static Microsoft.Windows.ApplicationModel.Resources.ResourceManager? ResourceStore { get; private set; }

    /// <summary>
    /// The language <see cref="Strings.Get(string)"/> answers in when the environment chose one;
    /// null for the machine's own.
    /// </summary>
    internal static Microsoft.Windows.ApplicationModel.Resources.ResourceContext? ChosenLanguage { get; private set; }

    /// <summary>
    /// Open the resources, and take the language the environment names if it names one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The app takes its language from Windows, the way every Windows app does; nobody sets it
    /// here. <c>ASTRID_LANGUAGE</c> exists for the UI smoke tests, which drive the app's words in
    /// a second language to prove that a language is a folder (task b9dd4a25) without changing
    /// the display language of whoever is running them.
    /// </para>
    /// <para>
    /// It reaches what a process can reach. The .NET culture is what the quick-add box sends the
    /// core, so "morgen" is read by the German keyword table; the resource context is what
    /// <c>Strings.Get</c> looks words up with, so the answer comes back "Morgen". It does not
    /// reach the <c>x:Uid</c> literals: WinUI resolves those with a context of its own whose
    /// language it takes from <c>ApplicationLanguages.Languages</c>
    /// (<c>dxaml/xcp/components/mrt/ModernResourceProvider.cpp</c>,
    /// <c>UpdateLanguageAndLayoutDirectionQualifiers</c>), and without package identity that
    /// list is the user's and <c>PrimaryLanguageOverride</c> throws. So in the unpackaged build
    /// the chrome follows Windows, whatever this says — which for a person is exactly right.
    /// </para>
    /// </remarks>
    private static void PrepareResources()
    {
        try
        {
            ResourceStore = new Microsoft.Windows.ApplicationModel.Resources.ResourceManager();
        }
        catch (Exception)
        {
            // No resource file beside the executable. English, from the literals and the table.
            ResourceStore = null;
        }

        var chosen = Environment.GetEnvironmentVariable("ASTRID_LANGUAGE");
        if (string.IsNullOrWhiteSpace(chosen))
        {
            return;
        }
        try
        {
            var culture = new System.Globalization.CultureInfo(chosen);
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = culture;
            System.Globalization.CultureInfo.CurrentCulture = culture;
            System.Globalization.CultureInfo.CurrentUICulture = culture;
            if (ResourceStore is not null)
            {
                var context = ResourceStore.CreateResourceContext();
                context.QualifierValues[Microsoft.Windows.ApplicationModel.Resources.KnownResourceQualifierName.Language] = chosen;
                ChosenLanguage = context;
            }
        }
        catch (Exception error)
        {
            // Logged rather than fatal: an app in the wrong language is still an app.
            Log($"the language override '{chosen}' was not applied: {error}");
            ChosenLanguage = null;
        }
    }

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
