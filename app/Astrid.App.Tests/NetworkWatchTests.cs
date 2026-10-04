using Astrid.App.ViewModels;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// networkRestored goes on the offline→online edge only — Windows raises NetworkStatusChanged for
/// every adapter change, and the core needs one signal per return, not one per event.
/// </summary>
public sealed class NetworkWatchTests
{
    [Fact]
    public void Coming_back_online_signals_once()
    {
        var watch = new NetworkWatch(startedOnline: true);
        Assert.False(watch.Observe(true));   // roaming while online: nothing to send
        Assert.False(watch.Observe(false));  // going offline: nothing to send
        Assert.True(watch.Observe(true));    // back: send networkRestored
        Assert.False(watch.Observe(true));   // still online: not again
    }

    [Fact]
    public void Starting_offline_signals_when_the_network_arrives() =>
        Assert.True(new NetworkWatch(startedOnline: false).Observe(true));
}
