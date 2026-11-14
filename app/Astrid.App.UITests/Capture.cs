using System.Runtime.InteropServices;

namespace Astrid.App.UITests;

/// <summary>
/// What the window actually drew, as pixels (task 82e4ad32).
/// </summary>
/// <remarks>
/// <para>
/// Every other assertion in this suite reads the accessibility tree, which says what a control is
/// and never what colour it came out. For the row mark that is the whole question: its colour is
/// resolved at runtime, against the theme the element is wearing, and both the value and the
/// lookup can be right in isolation while the wrong brush reaches the element. Only the rendered
/// pixel settles it.
/// </para>
/// <para>
/// <c>PrintWindow</c> with <c>PW_RENDERFULLCONTENT</c> rather than a screen grab: the flag exists
/// for exactly this class of window — one whose content is composed by DWM rather than painted
/// into its own DC — and unlike <c>CopyFromScreen</c> it does not need the window to be in front or
/// even unoccluded. A grab of the screen would read whatever is on top of the app.
/// </para>
/// <para>
/// Raw GDI through <c>LibraryImport</c> rather than <c>System.Drawing</c>, so the test project
/// gains no package: a DIB section is already a flat BGRA buffer, which is all a colour assertion
/// needs.
/// </para>
/// </remarks>
internal sealed partial class Capture : IDisposable
{
    private readonly uint[] _pixels;

    private Capture(uint[] pixels, int width, int height, int left, int top)
    {
        _pixels = pixels;
        Width = width;
        Height = height;
        Left = left;
        Top = top;
    }

    internal int Width { get; }

    internal int Height { get; }

    /// <summary>Where the captured bitmap's origin sits on the virtual screen.</summary>
    /// <remarks>
    /// UI Automation answers in physical screen pixels and this bitmap is in physical window
    /// pixels, so every rectangle that comes from the tree has to be moved by this before it can
    /// index the buffer.
    /// </remarks>
    internal int Left { get; }

    internal int Top { get; }

    /// <summary>The pixel at a point on the virtual screen, as 0xRRGGBB.</summary>
    internal int At(int screenX, int screenY)
    {
        var x = screenX - Left;
        var y = screenY - Top;
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(screenX),
                $"({screenX},{screenY}) is outside the captured window, which covers " +
                $"({Left},{Top}) to ({Left + Width},{Top + Height})");
        }
        var bgra = _pixels[(y * Width) + x];
        return (int)(bgra & 0x00FFFFFF);
    }

    /// <summary>Every distinct colour in a rectangle of the virtual screen, by how often it occurs.</summary>
    internal IReadOnlyList<(int Colour, int Count)> Histogram(int left, int top, int width, int height)
    {
        var counts = new Dictionary<int, int>();
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                if (x < Left || y < Top || x >= Left + Width || y >= Top + Height)
                {
                    continue;
                }
                var colour = At(x, y);
                counts[colour] = counts.TryGetValue(colour, out var seen) ? seen + 1 : 1;
            }
        }
        return counts.OrderByDescending(entry => entry.Value)
            .Select(entry => (entry.Key, entry.Value))
            .ToList();
    }

    /// <summary>Whether anything at all was drawn.</summary>
    /// <remarks>
    /// A capture that comes back a single flat colour is the signature of a session that is not
    /// rendering — locked, or the window never composed — rather than of an app that drew a blank
    /// screen. Worth distinguishing, because the two need completely different things done about
    /// them.
    /// </remarks>
    internal bool IsBlank() => Histogram(Left, Top, Width, Height).Count <= 1;

    /// <summary>Write the capture out, so a failure can be looked at rather than guessed at.</summary>
    /// <remarks>
    /// A colour that is wrong on screen is nearly impossible to diagnose from a histogram alone —
    /// the question is always "wrong where", and only the picture answers it.
    /// </remarks>
    internal string SavePng(string path)
    {
        using var bitmap = new System.Drawing.Bitmap(
            Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        var locked = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, Width, Height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            var raw = new int[_pixels.Length];
            for (var index = 0; index < raw.Length; index++)
            {
                raw[index] = unchecked((int)(_pixels[index] | 0xFF000000));
            }
            Marshal.Copy(raw, 0, locked.Scan0, raw.Length);
        }
        finally
        {
            bitmap.UnlockBits(locked);
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        return path;
    }

    private const uint RenderFullContent = 0x00000002;

    internal static Capture Window(IntPtr window)
    {
        if (!GetWindowRect(window, out var rect))
        {
            throw new InvalidOperationException("the window has no rectangle");
        }
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"the window measures {width}x{height}");
        }

        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            // Top-down, so row 0 is the top one and the arithmetic reads the way the screen does.
            Height = -height,
            Planes = 1,
            BitCount = 32,
            Compression = 0,
        };
        var bitmap = CreateDIBSection(memory, ref header, 0, out var bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
        {
            throw new InvalidOperationException("could not make a bitmap to draw the window into");
        }

        try
        {
            var previous = SelectObject(memory, bitmap);
            var drawn = PrintWindow(window, memory, RenderFullContent);
            SelectObject(memory, previous);
            if (!drawn)
            {
                throw new InvalidOperationException(
                    "PrintWindow refused to draw the window. On a locked or non-rendering session " +
                    "there is nothing to copy; see docs/TESTING.md.");
            }

            var pixels = new uint[width * height];
            var raw = new int[width * height];
            Marshal.Copy(bits, raw, 0, raw.Length);
            for (var index = 0; index < raw.Length; index++)
            {
                pixels[index] = unchecked((uint)raw[index]);
            }
            return new Capture(pixels, width, height, rect.Left, rect.Top);
        }
        finally
        {
            DeleteObject(bitmap);
            DeleteDC(memory);
            ReleaseDC(IntPtr.Zero, screen);
        }
    }

    public void Dispose()
    {
        // The buffers are released as soon as the capture is taken; this is here so a test can say
        // `using` and not have to know that.
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr window, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(IntPtr window, IntPtr deviceContext, uint flags);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr window);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr window, IntPtr deviceContext);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr deviceContext);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateDIBSection(
        IntPtr deviceContext,
        ref BitmapInfoHeader header,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr deviceContext, IntPtr handle);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr handle);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr deviceContext);
}
