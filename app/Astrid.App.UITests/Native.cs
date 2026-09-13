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
        MouseEvent(LeftDown, 0, 0, 0, 0);
        Thread.Sleep(150);
        const int steps = 24;
        for (var step = 1; step <= steps; step++)
        {
            MoveTo(fromX + (toX - fromX) * step / steps, fromY + (toY - fromY) * step / steps);
            Thread.Sleep(30);
        }
        Thread.Sleep(300);
        MouseEvent(LeftUp, 0, 0, 0, 0);
        Thread.Sleep(600);
    }

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
}
