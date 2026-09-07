using System.IO;
using System.Threading;
using System.Linq;
using System.Runtime.InteropServices;

namespace Astrid.App.UITests;

/// <summary>
/// A real mouse click.
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

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll", EntryPoint = "mouse_event")]
    private static partial void MouseEvent(uint flags, uint dx, uint dy, uint data, int extra);
}
