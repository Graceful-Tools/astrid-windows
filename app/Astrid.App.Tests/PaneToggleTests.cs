using Astrid.App.ViewModels;
using Xunit;

namespace Astrid.App.Tests;

/// <summary>
/// The sidebar and the messages pane each hide and show, and each device remembers which
/// (task ab4cb9ee — the Windows half of AWTD-1178).
/// </summary>
/// <remarks>
/// The board already kept both panes beside it; what was missing was the pair of controls and the
/// memory behind them. astrid-web's two hooks are the shape being matched: separate keys, a 48px
/// rail holding only the show button, and nothing on the server.
/// </remarks>
public sealed class PaneToggleTests
{
    private static readonly Action<Func<Task>> RunInline = work => work().GetAwaiter().GetResult();

    /// <summary>A device that remembers, without a file.</summary>
    private sealed class Remembered : IDevicePreferences
    {
        public Dictionary<string, bool> Flags { get; } = new(StringComparer.Ordinal);

        public bool Flag(string key, bool fallback) =>
            Flags.TryGetValue(key, out var value) ? value : fallback;

        public void SetFlag(string key, bool value) => Flags[key] = value;
    }

    private static FakeCore SignedIn() =>
        new FakeCore()
            .AnswerOk("isSignedIn", new { signedIn = true, waitingForCallback = false });

    /// <summary>
    /// Both panes start beside the work on a device that has never said otherwise — the sidebar
    /// because it always was, the messages pane because the board is a three-column view.
    /// </summary>
    [Fact]
    public void Both_panes_open_on_a_device_that_has_not_said_task_ab4cb9ee()
    {
        using var shell = new ShellViewModel(SignedIn(), RunInline, new Remembered());

        Assert.True(shell.IsSidebarOpen);
        Assert.True(shell.IsChatOpen);
    }

    /// <summary>Hiding the sidebar is remembered under its own key, and shows again.</summary>
    [Fact]
    public void Hiding_the_sidebar_is_remembered_task_ab4cb9ee()
    {
        var device = new Remembered();
        using var shell = new ShellViewModel(SignedIn(), RunInline, device);

        shell.ShowSidebar(false);

        Assert.False(shell.IsSidebarOpen);
        Assert.False(device.Flags[DevicePreferences.SidebarOpen]);

        shell.ShowSidebar(true);

        Assert.True(shell.IsSidebarOpen);
        Assert.True(device.Flags[DevicePreferences.SidebarOpen]);
    }

    /// <summary>
    /// The next launch on that device opens where it was left, rather than resetting to the
    /// default every time — which is what <c>IsChatOpen</c> did before this task.
    /// </summary>
    [Fact]
    public async Task A_hidden_pane_is_still_hidden_next_launch_task_ab4cb9ee()
    {
        var device = new Remembered();
        using (var first = new ShellViewModel(SignedIn(), RunInline, device))
        {
            first.ShowSidebar(false);
            await first.ShowChatAsync(false);
        }

        using var next = new ShellViewModel(SignedIn(), RunInline, device);

        Assert.False(next.IsSidebarOpen);
        Assert.False(next.IsChatOpen);
    }

    /// <summary>
    /// The two are remembered separately, as they are on web: hiding the messages pane must not
    /// take the sidebar with it.
    /// </summary>
    [Fact]
    public async Task The_two_panes_are_remembered_separately_task_ab4cb9ee()
    {
        var device = new Remembered();
        using var shell = new ShellViewModel(SignedIn(), RunInline, device);

        await shell.ShowChatAsync(false);

        Assert.True(shell.IsSidebarOpen);
        Assert.False(shell.IsChatOpen);
        // The messages pane's key was written; the sidebar's was not touched at all, which is the
        // stronger statement — one toggle must not express an opinion about the other pane.
        Assert.False(device.Flags[DevicePreferences.ChatOpen]);
        Assert.DoesNotContain(DevicePreferences.SidebarOpen, device.Flags.Keys);
    }

    /// <summary>
    /// A device that remembers nothing is a device with both panes open, not a window that will
    /// not start: <see cref="NoDevicePreferences"/> is the default the view model takes.
    /// </summary>
    [Fact]
    public void A_device_that_remembers_nothing_still_opens_task_ab4cb9ee()
    {
        using var shell = new ShellViewModel(SignedIn(), RunInline, NoDevicePreferences.Instance);

        shell.ShowSidebar(false);

        Assert.False(shell.IsSidebarOpen);

        using var next = new ShellViewModel(SignedIn(), RunInline, NoDevicePreferences.Instance);

        Assert.True(next.IsSidebarOpen);
    }

    /// <summary>
    /// The file-backed store survives a round trip and an unreadable file, since the window must
    /// open either way.
    /// </summary>
    [Fact]
    public void The_file_backed_store_round_trips_and_tolerates_rubbish_task_ab4cb9ee()
    {
        var directory = Path.Combine(Path.GetTempPath(), "astrid-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var store = DevicePreferences.Open(directory);
        Assert.True(store.Flag(DevicePreferences.ChatOpen, true));
        store.SetFlag(DevicePreferences.ChatOpen, false);

        Assert.False(DevicePreferences.Open(directory).Flag(DevicePreferences.ChatOpen, true));

        File.WriteAllText(Path.Combine(directory, "window.json"), "{ not json");
        Assert.True(DevicePreferences.Open(directory).Flag(DevicePreferences.ChatOpen, true));
    }
}
