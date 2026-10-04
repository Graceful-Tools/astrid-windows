using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Linq;
using System.Runtime.InteropServices;

namespace Astrid.App.UITests;

/// <summary>
/// A real mouse: a click, and a drag.
/// </summary>
/// <remarks>
/// UI Automation can invoke a button, but it cannot light-dismiss a flyout — that is what happens
/// when somebody clicks the window behind one, and there is no pattern for "click nothing in
/// particular". So the tests send a click the way the operating system does.
/// </remarks>
internal static partial class Native
{
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;

    internal static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        Thread.Sleep(100);
        MouseEvent(LeftDown, 0, 0, 0, 0);
        MouseEvent(LeftUp, 0, 0, 0, 0);
        Thread.Sleep(200);
    }

    private const uint Move = 0x0001;

    /// <summary>
    /// A real drag: press here, travel there in steps, let go.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In steps because that is what a drag is to the system — a press followed by movement past
    /// a threshold — and because the drop target only learns the pointer is over it from the
    /// moves.
    /// </para>
    /// <para>
    /// The moves are injected, not merely a repositioned cursor: while a button is held, WinUI
    /// takes its pointer's position from the input it receives, and <c>SetCursorPos</c> sends it
    /// none — every move it saw arrived at the press point, and the drag threshold was never
    /// crossed. Injected moves are relative and subject to the pointer's acceleration, so each
    /// step is corrected against where the cursor actually landed.
    /// </para>
    /// </remarks>
    internal static void Drag(int fromX, int fromY, int toX, int toY)
    {
        SetCursorPos(fromX, fromY);
        Thread.Sleep(150);
        GetCursorPos(out var pressed);
        MouseEvent(LeftDown, 0, 0, 0, 0);
        Thread.Sleep(150);
        const int steps = 24;
        for (var step = 1; step <= steps; step++)
        {
            MoveTo(fromX + (toX - fromX) * step / steps, fromY + (toY - fromY) * step / steps);
            Thread.Sleep(30);
        }
        Thread.Sleep(300);
        GetCursorPos(out var dropped);
        MouseEvent(LeftUp, 0, 0, 0, 0);
        Thread.Sleep(600);
        LastDrag =
            $"aimed ({fromX},{fromY})->({toX},{toY}); " +
            $"pressed at ({pressed.X},{pressed.Y}); dropped at ({dropped.X},{dropped.Y})";
    }

    /// <summary>
    /// Where the last drag actually put the pointer, against where it was aimed.
    /// </summary>
    /// <remarks>
    /// A drag that silently went somewhere else is indistinguishable, from the assertion's side,
    /// from a drop target that ignored it: both read as "the row did not move". The coordinates come
    /// from UI Automation, which answers in physical pixels, and are given to the pointer, which
    /// does not when the process is not per-monitor DPI aware — so "the pointer never got there" is
    /// a real failure mode and worth saying out loud rather than leaving to be guessed at.
    /// </remarks>
    internal static string LastDrag { get; private set; } = "no drag yet";

    /// <summary>
    /// Move the pointer to a point with injected moves.
    /// </summary>
    /// <remarks>
    /// In hops of a few pixels: Windows accelerates an injected move the way it does a fast
    /// mouse, and one big jump lands anywhere but where it was aimed. Below the acceleration
    /// threshold a move is taken as sent, and the cursor's actual position after each hop is
    /// what the next one is measured from.
    /// </remarks>
    private static void MoveTo(int x, int y)
    {
        const int hop = 4;
        for (var attempt = 0; attempt < 400; attempt++)
        {
            GetCursorPos(out var at);
            var dx = Math.Clamp(x - at.X, -hop, hop);
            var dy = Math.Clamp(y - at.Y, -hop, hop);
            if (dx == 0 && dy == 0)
            {
                return;
            }
            MouseEvent(Move, unchecked((uint)dx), unchecked((uint)dy), 0, 0);
            Thread.Sleep(3);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out Point point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll", EntryPoint = "mouse_event")]
    private static partial void MouseEvent(uint flags, uint dx, uint dy, uint data, int extra);

    /// <summary>The window the operating system is sending keyboard and activation to.</summary>
    internal static IntPtr Foreground() => GetForegroundWindow();

    /// <summary>
    /// What is in front, named: its title, its window class and the process that owns it.
    /// </summary>
    /// <remarks>
    /// "Something else is in front" is not an actionable failure; "the lock screen is in front" or
    /// "another Astrid.App is in front" is. Worth the three extra calls.
    /// </remarks>
    internal static string Describe(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return "nothing (no window has the foreground, which is what a locked session looks like)";
        }
        var title = new System.Text.StringBuilder(256);
        GetWindowText(window, title, title.Capacity);
        var className = new System.Text.StringBuilder(256);
        GetClassName(window, className, className.Capacity);
        GetWindowThreadProcessId(window, out var processId);
        var process = "an unreadable process";
        try
        {
            process = Process.GetProcessById((int)processId).ProcessName;
        }
        catch (ArgumentException)
        {
            // Gone between the two calls.
        }
        catch (InvalidOperationException)
        {
            // Likewise.
        }
        return $"{window} \"{title}\" (class {className}, {process} pid {processId})";
    }

    // DllImport rather than LibraryImport: the source generator does not marshal StringBuilder,
    // which is what these two fill.
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int count);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(IntPtr window, System.Text.StringBuilder text, int count);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    private const int Restore = 9;

    /// <summary>
    /// Put a window in front and make it the active one, and say whether it worked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A real mouse goes wherever the pointer is, to whatever window is there — which is only the
    /// window under test when that window is in front. UI Automation does not care: it reads the
    /// tree and the bounding rectangles of an occluded, inactive window exactly as it reads a
    /// visible one. So a test driven by UIA reads and a real pointer has one failure mode nothing
    /// in it reports: every element is found, every coordinate is right, the pointer travels
    /// exactly where it was aimed, and another window receives the click.
    /// </para>
    /// <para>
    /// That is what broke both drag tests (task f850514f). Nothing about the drag was wrong. The
    /// app simply was not in front, because nothing had ever asked it to be — these tests passed
    /// for as long as the app happened to come up activated, and stopped the moment something
    /// else on the machine held the foreground.
    /// </para>
    /// <para>
    /// <c>SetForegroundWindow</c> alone is not enough: Windows refuses it from a process that is
    /// not itself in the foreground, and silently — it returns false and nothing moves. So the
    /// restore comes first, <c>SwitchToThisWindow</c> is the fallback that is not subject to the
    /// lock, and the result is confirmed by reading the foreground back rather than assumed.
    /// </para>
    /// </remarks>
    internal static bool BringToForeground(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return false;
        }
        if (IsIconic(window))
        {
            ShowWindow(window, Restore);
        }
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (GetForegroundWindow() == window)
            {
                return true;
            }
            BringWindowToTop(window);
            SetForegroundWindow(window);
            if (GetForegroundWindow() != window)
            {
                // Not subject to the foreground lock, which is the whole reason it is here.
                SwitchToThisWindow(window, true);
            }
            Thread.Sleep(100);
        }
        return GetForegroundWindow() == window;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(IntPtr window);

    [LibraryImport("user32.dll")]
    private static partial void SwitchToThisWindow(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool altTab);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr window);
}
