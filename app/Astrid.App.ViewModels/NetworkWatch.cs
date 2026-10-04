namespace Astrid.App.ViewModels;

/// <summary>
/// When to tell the core the network came back: on the edge from offline to online, once.
/// </summary>
/// <remarks>
/// The core no longer retries a write made with no network (astrid-core 126596f); it waits for
/// <c>networkRestored</c>, a sign-in or a wake. Windows raises <c>NetworkStatusChanged</c> for
/// every adapter change — Wi-Fi roaming, a VPN coming up — so only a real offline→online edge is
/// worth a command; repeated "still online" events are ignored.
/// </remarks>
public sealed class NetworkWatch
{
    private bool _online;

    public NetworkWatch(bool startedOnline) => _online = startedOnline;

    /// <summary>Record the current state; true when it is the moment to send networkRestored.</summary>
    public bool Observe(bool isOnline)
    {
        var restored = !_online && isOnline;
        _online = isOnline;
        return restored;
    }
}
