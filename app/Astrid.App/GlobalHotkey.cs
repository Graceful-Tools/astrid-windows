using Astrid.Core.Bindings;
using System.Runtime.InteropServices;
using System.Threading;

namespace Astrid.App;

/// <summary>
/// A key combination that works while another app has the keyboard.
/// </summary>
/// <remarks>
/// <para>
/// Ctrl+Shift+A brings Astrid forward with the quick-add box focused — the thing the Mac's global
/// hotkey does, and the reason a task app is worth having open at all: the thought arrives while
/// you are in something else.
/// </para>
/// <para>
/// <b>Registered on a thread of its own, with no window.</b> `RegisterHotKey(IntPtr.Zero, …)` posts
/// `WM_HOTKEY` to the calling *thread's* queue rather than to a window, so this needs a thread with
/// a message loop and nothing else — no window class, no subclassing of the WinUI window, and no
/// interference with XAML's own message handling.
/// </para>
/// <para>
/// <b>A chord can be taken.</b> Another app registering the same combination first wins, and
/// Windows says so by failing the registration rather than by telling anybody. That is reported
/// through <see cref="IsRegistered"/> so the app can say "that combination is in use" instead of
/// having a feature that silently does nothing — which is the failure mode this whole class exists
/// to avoid.
/// </para>
/// </remarks>
public sealed partial class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int WmQuit = 0x0012;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    /// <summary>Held down, it must not repeat: one press is one quick-add.</summary>
    private const uint ModNoRepeat = 0x4000;
    private const int HotkeyId = 1;

    private const uint ModWin = 0x0008;

    private readonly Action _pressed;
    private readonly Thread _thread;
    private readonly uint _modifiers;
    private readonly uint _key;
    private uint _threadId;
    private bool _disposed;

    /// <summary>The shipped chord, Ctrl+Shift+A. What is registered when nothing else was chosen.</summary>
    public GlobalHotkey(Action pressed)
        : this(pressed, new Hotkey { Chord = "Ctrl+Shift+A", Ctrl = true, Shift = true, Key = "A" })
    {
    }

    /// <summary>
    /// A chord the core accepted. It has already refused anything without a real modifier and
    /// anything whose key is not one letter or digit, so what arrives here can be registered.
    /// </summary>
    public GlobalHotkey(Action pressed, Hotkey chord)
    {
        _pressed = pressed;
        Chord = chord.Chord;
        _modifiers = ModNoRepeat
            | (chord.Ctrl ? ModControl : 0)
            | (chord.Alt ? ModAlt : 0)
            | (chord.Shift ? ModShift : 0)
            | (chord.Win ? ModWin : 0);
        // A letter's or a digit's virtual-key code is its upper-case ASCII code.
        _key = chord.Key.Length == 1 ? char.ToUpperInvariant(chord.Key[0]) : 'A';
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "Astrid global hotkey",
        };
    }

    /// <summary>Whether Windows accepted the combination.</summary>
    /// <remarks>False when another app got there first — which is a normal thing to happen.</remarks>
    public bool IsRegistered { get; private set; }

    /// <summary>What the combination is, for a message that has to name it.</summary>
    public string Chord { get; }

    public void Start()
    {
        _thread.Start();
        // Long enough for the registration to have happened or failed, so IsRegistered means
        // something to whoever asks straight afterwards.
        Thread.Sleep(200);
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        // Ctrl+Shift+A unless the person chose otherwise: 'A' for Astrid, with Ctrl+Shift, which
        // is the shape Windows apps use for a global chord and is free on a default install.
        IsRegistered = RegisterHotKey(IntPtr.Zero, HotkeyId, _modifiers, _key);
        if (!IsRegistered)
        {
            App.Log($"the global hotkey {Chord} is already in use by another app");
            return;
        }

        while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            if (message.message == WmHotkey)
            {
                try
                {
                    _pressed();
                }
                catch (Exception error)
                {
                    // A handler that throws here would kill the loop and take the hotkey with it,
                    // silently, for the rest of the session.
                    App.Log($"the global hotkey handler failed: {error}");
                }
            }
        }

        UnregisterHotKey(IntPtr.Zero, HotkeyId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_threadId != 0)
        {
            // Ends the loop, which unregisters on the way out — from the thread that registered,
            // which is the only thread allowed to.
            PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr window, int id);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessage(out Message message, IntPtr window, uint first, uint last);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint thread, uint message, IntPtr w, IntPtr l);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x;
        public int y;
    }
}
