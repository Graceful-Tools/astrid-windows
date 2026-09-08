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
public static class Program
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

        // Only the real instance registers the scheme, and it does so on every start rather than
        // at install time: an unpackaged app can be moved, and a registration pointing at where
        // the executable used to be silently stops the sign-in callback from arriving.
        RegisterProtocol();

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
    /// Written by hand rather than through <c>ActivationRegistrationManager</c>. That call left
    /// the scheme key behind carrying <c>URL Protocol</c> and no <c>shell\open\command</c> under
    /// it, so the browser finished a sign-in and Windows had nowhere to send the callback: the app
    /// sat waiting for something that could never arrive. A half-registered scheme is worse than
    /// an unregistered one, because it looks registered from every angle except the one that
    /// matters — and the failure was swallowed, so nothing said so.
    /// </para>
    /// <para>
    /// The layout is the documented one for an unpackaged app, under HKCU so it needs no
    /// elevation. A packaged build declares the scheme in its manifest and this is redundant
    /// there, which is why it stays best-effort: an app that will not start because it could not
    /// claim a URL scheme is worse than one whose sign-in has to be retried. But it now says when
    /// it failed, rather than leaving a sign-in that hangs with nothing in the log.
    /// </para>
    /// </remarks>
    private static void RegisterProtocol()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
        {
            return;
        }

        try
        {
            using var scheme = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                @"Software\Classes\astrid");
            scheme.SetValue(null, "URL:Astrid");
            // The marker that makes Windows treat this as a launchable scheme at all.
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
