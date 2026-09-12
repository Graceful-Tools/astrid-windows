using System.IO;
using System.Threading;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Astrid.App.UITests;

/// <summary>
/// The app under test: launched into a data directory of its own, and driven through the
/// accessibility tree.
/// </summary>
/// <remarks>
/// <para>
/// <c>ASTRID_DATA_DIR</c> points the cache and the credential at a temporary folder, so a test run
/// cannot touch the account of whoever is running it. The session written there is not a real one —
/// nothing here talks to a server — which is exactly right for a smoke test: the app is offline,
/// every write goes to the Outbox, and what is being checked is that the shell, the boundary and
/// the core work together at all.
/// </para>
/// <para>
/// Everything is found by its automation name, which is the same name a screen reader reads. A
/// control these tests cannot reach is a control somebody using Narrator cannot reach either.
/// </para>
/// </remarks>
public sealed class AstridApp : IDisposable
{
    private readonly Process _process;

    private AstridApp(Process process, string dataDirectory, AutomationElement window)
    {
        _process = process;
        DataDirectory = dataDirectory;
        Window = window;
    }

    public string DataDirectory { get; }

    public AutomationElement Window { get; }

    /// <summary>Where the crash log would be, if the app wrote one.</summary>
    public string CrashLogPath => Path.Combine(DataDirectory, "crash.log");

    /// <summary>Launch the app with an empty cache, signed in or signed out.</summary>
    /// <param name="signedIn">Whether a session is written first.</param>
    /// <param name="language">
    /// A BCP-47 tag for the app's words, or null for the machine's own. Set through the app's
    /// <c>ASTRID_LANGUAGE</c> hook, so the machine's display language is left alone (task
    /// b9dd4a25). The x:Uid chrome follows Windows regardless — see <c>App.PrepareResources</c>.
    /// </param>
    public static AstridApp Launch(bool signedIn, string? language = null)
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "astrid-uitests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(directory);
        if (signedIn)
        {
            WriteSession(directory);
        }

        var start = new ProcessStartInfo(ExecutablePath()) { UseShellExecute = false };
        start.Environment["ASTRID_DATA_DIR"] = directory;
        if (language is not null)
        {
            start.Environment["ASTRID_LANGUAGE"] = language;
        }
        var process = Process.Start(start)
            ?? throw new InvalidOperationException("the app did not start");

        var window = WaitForWindow(process.Id)
            ?? throw new InvalidOperationException("the app started but never showed a window");
        // The first paint is a cache read, but the window appears before it lands.
        Thread.Sleep(1500);
        var app = new AstridApp(process, directory, window);

        // Every launch here is a first run in a fresh data directory, so the tour is up. A person
        // presses "Got it" before doing anything else, and a test that reached past it would be
        // testing something nobody sees.
        if (app.Find("Got it", 1500) is not null)
        {
            app.Invoke("Got it");
        }
        return app;
    }

    /// <summary>Find one element by the name a screen reader would read.</summary>
    public AutomationElement? Find(string name, int timeoutMilliseconds = 4000)
    {
        var condition = new PropertyCondition(AutomationElement.NameProperty, name);
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        do
        {
            var found = Window.FindFirst(TreeScope.Descendants, condition);
            if (found is not null)
            {
                return found;
            }
            Thread.Sleep(200);
        }
        while (DateTime.UtcNow < deadline);
        return null;
    }

    public AutomationElement Require(string name) =>
        Find(name)
        // With what IS on screen: a missing control is nearly always the app showing a different
        // screen than the test expected, and the names say which.
        ?? throw new InvalidOperationException(
            $"no element named '{name}' on screen. Saw: {string.Join(", ", Names())}");

    /// <summary>Press a button.</summary>
    public void Invoke(string name)
    {
        var pattern = (InvokePattern)Require(name).GetCurrentPattern(InvokePattern.Pattern);
        pattern.Invoke();
        Thread.Sleep(600);
    }

    /// <summary>Type into a box, replacing what is there.</summary>
    public void Type(string name, string text)
    {
        var element = Require(name);
        var pattern = (ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern);
        pattern.SetValue(text);
        Thread.Sleep(200);
    }

    /// <summary>Every name on screen. What a failure message should show.</summary>
    public IReadOnlyList<string> Names() =>
        Window.FindAll(TreeScope.Descendants, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Select(element => element.Current.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();

    /// <summary>Wait until something with this name is on screen.</summary>
    public bool Sees(string name, int timeoutMilliseconds = 4000) =>
        Find(name, timeoutMilliseconds) is not null;

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
        _process.Dispose();

        try
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A file still held open by the process that just died. A temp folder is not worth
            // failing a test over.
        }
    }

    /// <summary>
    /// A stored session, so the app draws its shell rather than the sign-in screen.
    /// </summary>
    /// <remarks>
    /// Not a real one, and it does not have to be: the credential file only has to decrypt and
    /// carry a non-empty cookie for the app to consider itself signed in. Everything these tests
    /// then do is local, which is the point.
    /// </remarks>
    private static void WriteSession(string directory)
    {
        var plain = System.Text.Encoding.UTF8.GetBytes(
            """{"astrid.session-cookie":"ui-smoke-test-not-a-real-session"}""");
        var sealedBytes = System.Security.Cryptography.ProtectedData.Protect(
            plain, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path.Combine(directory, "astrid.credentials"), sealedBytes);
    }

    /// <summary>
    /// The built executable for this architecture.
    /// </summary>
    /// <remarks>
    /// Found by walking up to the repository root rather than by a relative path from the test
    /// binary, so the tests run the same from a solution build, a single-project build and the
    /// gate.
    /// </remarks>
    /// <summary>The executable these tests launch, for a test that needs to name it.</summary>
    public static string ExecutableUnderTest() => ExecutablePath();

    private static string ExecutablePath()
    {
        var overridden = Environment.GetEnvironmentVariable("ASTRID_APP_EXE");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "app")))
        {
            directory = directory.Parent;
        }
        if (directory is null)
        {
            throw new InvalidOperationException("could not find the repository root");
        }

        var runtime = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "win-arm64"
            : "win-x64";
        var candidates = new[] { "Debug", "Release" }
            .Select(configuration => Path.Combine(
                directory.FullName,
                "app", "Astrid.App", "bin", configuration,
                "net9.0-windows10.0.19041.0", runtime, "Astrid.App.exe"))
            .Where(File.Exists)
            .ToList();

        return candidates.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Astrid.App.exe was not found — build the app before running the UI tests");
    }

    private static AutomationElement? WaitForWindow(int processId)
    {
        var condition = new PropertyCondition(AutomationElement.ProcessIdProperty, processId);
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var window = AutomationElement.RootElement.FindFirst(TreeScope.Children, condition);
            if (window is not null)
            {
                return window;
            }
            Thread.Sleep(500);
        }
        return null;
    }
}
