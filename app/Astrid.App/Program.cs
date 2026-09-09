using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Astrid.App;

/// <summary>
/// The entry point, hand-written so the app can be single-instance.
/// </summary>
/// <remarks>
/// <para>
/// The generated <c>Main</c> starts a second copy of the app for every launch, which is wrong for
/// this app twice over. Once because a task list is a document you have one of, and twice because
/// the sign-in callback arrives as a <b>launch</b>: the browser opens
/// <c>astrid://auth/callback?code=…</c>, Windows starts the app to handle it, and that second copy
/// has none of the flow state the first one is waiting with. Redirecting the activation to the
/// instance that started the sign-in is what makes the hand-off work at all.
/// </para>
/// <para>
/// This is the M0 spike named in <c>docs/M0_NOTES.md</c> — "does <c>astrid://auth/callback</c>
/// reach the app both cold and already-running?" — with the fallback that spike nominated
/// (single-instance redirection) adopted as the design rather than kept in reserve, because cold
/// and warm need different handling either way.
/// </para>
/// </remarks>
public static partial class Program
{
    /// <summary>
    /// The key every copy of the app registers under. One key, so the second launch finds the
    /// first.
    /// </summary>
    private const string InstanceKey = "Astrid.Main";

    [STAThread]
    public static int Main(string[] args)
    {
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var main = AppInstance.FindOrRegisterForKey(InstanceKey);

        if (!main.IsCurrent)
        {
            // Somebody else owns the app. Hand them the activation and stop — this copy exists
            // only to carry the URL across, and doing that asynchronously while the runtime is
            // still starting is what the redirect API is for.
            RedirectAndExit(main, activation);
            return 0;
        }

        // Only the real instance registers the scheme, and only when this build has to.
        //
        // A PACKAGED build declares astrid:// in AppxManifest.xml, so the installer registers it
        // and the uninstaller removes it. Writing the same key into HKCU as well would be both
        // redundant and a leak: MSIX cannot clean up a key the app wrote outside its own package,
        // so uninstalling would leave a dead scheme pointing at an executable that is gone.
        if (!IsPackaged())
        {
            RegisterProtocol();
        }

        // The callback parameter is named rather than discarded: `_` would shadow the discard on
        // the last line, and `_ = new App(...)` would then assign to the parameter instead.
        Microsoft.UI.Xaml.Application.Start(parameters =>
        {
            // The synchronization context is what makes `await` in a view model come back to the
            // UI thread. `Application.Start` does not install one, and without it every
            // ObservableCollection mutation after an await happens on a pool thread — which is the
            // crash documented in Astrid.App.ViewModels/ObservableObject.cs.
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(activation);
        });
        return 0;
    }

    /// <summary>
    /// Hand an activation to the instance that owns the app.
    /// </summary>
    /// <remarks>
    /// The redirect has to complete before this process exits, and it needs a message pump to do
    /// it — hence the event and the wait rather than a bare <c>await</c>, which would return to a
    /// thread that is about to end.
    /// </remarks>
    private static void RedirectAndExit(AppInstance main, AppActivationArguments activation)
    {
        var redirected = new ManualResetEvent(false);
        _ = Task.Run(async () =>
        {
            try
            {
                await main.RedirectActivationToAsync(activation);
            }
            finally
            {
                redirected.Set();
            }
        });
        // Bounded: if the other instance is wedged, this copy still exits rather than hanging
        // around as a process with no window.
        redirected.WaitOne(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Register <c>astrid://</c> so Windows knows which executable to launch for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both ways round, because Windows has two and prefers the one that is easy to miss. The
    /// App SDK call writes a generated ProgId — <c>App.&lt;hash of the path&gt;.Protocol</c> — and
    /// points the user's URL association at it; that association is what the shell actually
    /// resolves. The plain <c>Classes\astrid\shell\open\command</c> underneath is the fallback for
    /// when there is no association at all.
    /// </para>
    /// <para>
    /// Registering on every start rather than at install time is deliberate: an unpackaged app can
    /// be moved, and the ProgId is derived from the path, so a registration pointing at where the
    /// executable used to be silently stops the sign-in callback from arriving.
    /// </para>
    /// <para>
    /// It stays best-effort — an app that will not start because it could not claim a URL scheme
    /// is worse than one whose sign-in has to be retried — but it now says so in the log instead of
    /// leaving a sign-in that hangs with no explanation anywhere.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Whether this process is running from an MSIX package.
    /// </summary>
    /// <remarks>
    /// Asked of the OS rather than of a build constant, because the same binaries are laid into a
    /// package by <c>scripts/package.ps1</c> — so "was this compiled for the Store" and "is this
    /// running packaged right now" are different questions, and only the second one is the truth
    /// at the moment it matters.
    ///
    /// <c>GetCurrentPackageFullName</c> answers <c>APPMODEL_ERROR_NO_PACKAGE</c> for a process
    /// with no package identity, which is the documented way to ask.
    /// </remarks>
    private static bool IsPackaged()
    {
        // 15700 — APPMODEL_ERROR_NO_PACKAGE.
        const int NoPackage = 15700;
        var length = 0;
        return GetCurrentPackageFullName(ref length, null) != NoPackage;
    }

    [System.Runtime.InteropServices.LibraryImport(
        "kernel32.dll",
        EntryPoint = "GetCurrentPackageFullName",
        StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
    private static partial int GetCurrentPackageFullName(ref int packageFullNameLength,
        char[]? packageFullName);

    private static void RegisterProtocol()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            return;
        }

        try
        {
            ActivationRegistrationManager.RegisterForProtocolActivation(
                scheme: "astrid",
                logo: $"{executable},1",
                displayName: "Astrid",
                exePath: executable);
        }
        catch (Exception error)
        {
            App.Log($"could not register astrid:// through the App SDK: {error.Message}");
        }

        try
        {
            // The direct registration, under HKCU so it needs no elevation. Harmless beside the
            // ProgId — the shell prefers the association when there is one — and the only thing
            // there is to resolve when there is not.
            using var scheme = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\astrid");
            scheme.SetValue(null, "URL:Astrid");
            scheme.SetValue("URL Protocol", string.Empty);

            using var icon = scheme.CreateSubKey("DefaultIcon");
            icon.SetValue(null, $"{executable},1");

            // "%1" is the callback URL. Without it the app is launched with no arguments, the
            // activation carries nothing, and the sign-in it was holding is lost.
            using var command = scheme.CreateSubKey(@"shell\open\command");
            command.SetValue(null, $"\"{executable}\" \"%1\"");
        }
        catch (Exception error)
        {
            App.Log($"could not register astrid:// — sign-in will not come back: {error.Message}");
        }
    }
}
