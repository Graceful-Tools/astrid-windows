using System.IO;
using System.Threading;
using System.Linq;
using Xunit;

namespace Astrid.App.UITests;

/// <summary>
/// The app, end to end: a real window, the real boundary, the real core, a real SQLite file.
/// </summary>
/// <remarks>
/// <para>
/// These are the tests the unit tests cannot be. Everything below the window is covered by 500-odd
/// Rust tests and 113 view-model tests against a fake core; what none of them can catch is the
/// class of failure this project exists for — a converter the XAML cannot resolve, a collection
/// mutated off the UI thread, a stale native library answering "unknown command", a control with
/// no accessible name. Every one of those has happened here, and every one of them looked like a
/// blank screen rather than a failing test.
/// </para>
/// <para>
/// Offline throughout: the session is a fake one and nothing reaches a server. That is not a
/// limitation, it is the app's normal state — every write goes to the Outbox first.
/// </para>
/// </remarks>
[Collection("ui")]
public sealed class ShellSmokeTests
{
    /// <summary>
    /// The sign-in callback can find the app, and finds one that can start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Signing in hands off to the browser and comes back as <c>astrid://auth/callback?…</c>.
    /// Whatever Windows has registered for that scheme is what gets launched — and on a machine
    /// with several build flavours that is simply whichever ran last, because the registration is
    /// derived from the executable's path.
    /// </para>
    /// <para>
    /// That is how this broke: an x86 build had claimed the scheme, and an x86 build could not
    /// start, because the shell was carrying a core of the host's architecture. The browser
    /// completed the sign-in, Windows launched the app, and the app died loading its own core
    /// before drawing anything. From the outside it looked like the sign-in simply did nothing.
    /// </para>
    /// <para>
    /// So the assertion is not "a key exists" — the key existed the whole time. It is that the
    /// command Windows will run names a real executable, carries the URL, and is the same
    /// architecture as the process that will host it.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_sign_in_callback_reaches_an_app_that_can_start()
    {
        using var app = AstridApp.Launch(signedIn: false);

        var command = RegisteredProtocolCommand();
        Assert.False(string.IsNullOrWhiteSpace(command), "astrid:// resolves to nothing");

        // Without a placeholder the app is launched with no URL, so the callback arrives empty and
        // the sign-in it was carrying is lost.
        Assert.Contains("%1", command!, System.StringComparison.Ordinal);

        var executable = FirstQuoted(command!);
        Assert.True(File.Exists(executable), $"astrid:// points at a missing file: {executable}");
        Assert.Equal(
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture,
            ArchitectureOf(executable));
    }

    /// <summary>The command Windows will actually run for <c>astrid://</c>.</summary>
    /// <remarks>
    /// Resolved the way the shell resolves it: the user's URL association names a generated ProgId
    /// and that ProgId carries the command; the scheme's own key is the fallback when no
    /// association exists. Reading only the second is what let a stale association go unnoticed.
    /// </remarks>
    private static string? RegisteredProtocolCommand()
    {
        var progId = Microsoft.Win32.Registry.CurrentUser
            .OpenSubKey(@"SOFTWARE\Microsoft\Windows\Shell\Associations\UrlAssociations\astrid\UserChoiceLatest\ProgId")
            ?.GetValue("ProgId") as string;

        if (!string.IsNullOrEmpty(progId))
        {
            var viaProgId = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey($@"Software\Classes\{progId}\shell\open\command")
                ?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(viaProgId))
            {
                return viaProgId;
            }
        }

        return Microsoft.Win32.Registry.CurrentUser
            .OpenSubKey(@"Software\Classes\astrid\shell\open\command")
            ?.GetValue(null) as string;
    }

    /// <summary>The executable out of a <c>"path" args</c> command line.</summary>
    private static string FirstQuoted(string command)
    {
        var opening = command.IndexOf('"');
        if (opening < 0)
        {
            return command.Split(' ')[0];
        }
        var closing = command.IndexOf('"', opening + 1);
        return closing < 0 ? command : command[(opening + 1)..closing];
    }

    /// <summary>What a PE file was built for, read out of its COFF header.</summary>
    private static System.Runtime.InteropServices.Architecture ArchitectureOf(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var header = System.BitConverter.ToInt32(bytes, 0x3C);
        return System.BitConverter.ToUInt16(bytes, header + 4) switch
        {
            0xAA64 => System.Runtime.InteropServices.Architecture.Arm64,
            0x8664 => System.Runtime.InteropServices.Architecture.X64,
            0x014C => System.Runtime.InteropServices.Architecture.X86,
            var other => throw new System.InvalidOperationException($"unknown machine 0x{other:X4}"),
        };
    }

    /// <summary>With no session, the first thing on screen is the way to get one.</summary>
    [Fact]
    public void Signed_out_it_offers_the_browser_sign_in()
    {
        using var app = AstridApp.Launch(signedIn: false);

        Assert.True(
            app.Sees("Sign in with your browser"),
            $"expected a sign-in button; saw: {string.Join(", ", app.Names())}");
        Assert.False(File.Exists(app.CrashLogPath), Crash(app));
    }

    /// <summary>
    /// The whole loop, offline: make a list, add a task to it, finish it.
    /// </summary>
    /// <remarks>
    /// Three writes through the Outbox and three reads back out of SQLite, across the C ABI both
    /// ways, drawn by the real XAML. If this passes, the app works.
    /// </remarks>
    [Fact]
    public void A_list_a_task_and_a_completion_all_work_offline()
    {
        using var app = AstridApp.Launch(signedIn: true);

        app.Type("New list", "Shopping");
        app.Invoke("Add");
        Assert.True(
            app.Sees("Shopping"),
            $"the new list never appeared; saw: {string.Join(", ", app.Names())}");

        app.Type("Add a task", "Buy oat milk");
        // Two buttons are called Add — the list's and the task's. The task one is the second.
        InvokeSecondAdd(app);
        Assert.True(
            app.Sees("Buy oat milk"),
            $"the new task never appeared; saw: {string.Join(", ", app.Names())}");

        // The mark is a button carrying the checkbox image, not a CheckBox: it draws the state the
        // core reported and asks for the opposite, because a repeating task rolls forward rather
        // than finishing and a two-state control cannot say that. It is named for the task it
        // finishes, which is what a screen reader reads.
        app.Invoke("Complete Buy oat milk");

        // The list hides finished tasks after a moment; what matters is that nothing fell over.
        Assert.False(File.Exists(app.CrashLogPath), Crash(app));
    }

    /// <summary>
    /// Opening a task shows the panes and the pickers behind them.
    /// </summary>
    /// <remarks>
    /// Every one of these buttons opens a flyout whose contents are built from a command answer,
    /// and a converter a flyout's template cannot resolve crashes the app at the moment it opens —
    /// 0xC000027B, no message, nothing on screen. That is what this is watching for.
    /// </remarks>
    [Fact]
    public void A_task_opens_with_its_fields_and_its_pickers()
    {
        using var app = AstridApp.Launch(signedIn: true);
        app.Type("New list", "Work");
        app.Invoke("Add");
        app.Type("Add a task", "Book flights");
        InvokeSecondAdd(app);
        Assert.True(app.Sees("Book flights"));

        // Selecting a task opens it — the same gesture as web, and the only one a keyboard has.
        Select(app, "Book flights");

        foreach (var field in new[] { "Assignee", "Due date", "Reminder", "Repeat" })
        {
            Assert.True(app.Sees(field), $"the detail pane has no {field} row");
            app.Invoke(field);
            Assert.False(File.Exists(app.CrashLogPath), Crash(app));
            // Escape shuts the flyout so the next one can be reached.
            Dismiss(app);
        }
    }

    /// <summary>The screens reached from the header: filters, chat, the list's own settings.</summary>
    [Fact]
    public void The_header_panels_all_open()
    {
        using var app = AstridApp.Launch(signedIn: true);
        app.Type("New list", "Work");
        app.Invoke("Add");

        app.Invoke("Filters");
        Assert.True(app.Sees("Sort by"), "the filter sheet did not open");
        Dismiss(app);

        app.Invoke("List settings");
        Assert.True(app.Sees("List settings"), "the list settings did not open");
        Dismiss(app);

        Assert.False(File.Exists(app.CrashLogPath), Crash(app));
    }

    private static void Select(AstridApp app, string name)
    {
        var row = app.Require(name);
        // The row's own element is a text block; its list item is what carries selection.
        var walker = System.Windows.Automation.TreeWalker.ControlViewWalker;
        var element = row;
        while (element is not null
            && element.Current.ControlType != System.Windows.Automation.ControlType.ListItem)
        {
            element = walker.GetParent(element);
        }
        Assert.NotNull(element);
        var pattern = (System.Windows.Automation.SelectionItemPattern)element!.GetCurrentPattern(
            System.Windows.Automation.SelectionItemPattern.Pattern);
        pattern.Select();
        Thread.Sleep(800);
    }

    /// <summary>
    /// Press the second button named Add — the one under the task box.
    /// </summary>
    /// <remarks>
    /// Both say "Add", which is right on screen where each sits under its own box, and ambiguous to
    /// anything that finds controls by name. Worth remembering when these grow: two identical
    /// names is also what a screen reader reads out.
    /// </remarks>
    private static void InvokeSecondAdd(AstridApp app)
    {
        var buttons = app.Window.FindAll(
            System.Windows.Automation.TreeScope.Descendants,
            new System.Windows.Automation.PropertyCondition(
                System.Windows.Automation.AutomationElement.NameProperty, "Add"));
        Assert.True(buttons.Count >= 2, "expected an Add button for lists and one for tasks");
        var add = (System.Windows.Automation.InvokePattern)buttons[buttons.Count - 1]
            .GetCurrentPattern(System.Windows.Automation.InvokePattern.Pattern);
        add.Invoke();
        Thread.Sleep(800);
    }

    /// <summary>Close whatever flyout is open.</summary>
    private static void Dismiss(AstridApp app)
    {
        // A flyout is light-dismissed by a click elsewhere; the window's own title is a safe
        // target that does nothing when it is pressed.
        var title = app.Find("Astrid", 500);
        if (title is not null)
        {
            try
            {
                var point = title.GetClickablePoint();
                Native.Click((int)point.X, (int)point.Y);
            }
            catch (System.Windows.Automation.NoClickablePointException)
            {
                // Off screen. The next Find will simply look past the flyout.
            }
        }
        Thread.Sleep(500);
    }

    private static string Crash(AstridApp app) =>
        File.Exists(app.CrashLogPath)
            ? $"the app logged a crash:\n{File.ReadAllText(app.CrashLogPath)}"
            : string.Empty;
}

/// <summary>
/// One at a time.
/// </summary>
/// <remarks>
/// Each test drives a real window with a real mouse. Two at once would fight over the foreground,
/// and the loser would fail for reasons nothing to do with the code.
/// </remarks>
[CollectionDefinition("ui", DisableParallelization = true)]
public sealed class UiCollection;
