using System.Runtime.InteropServices;

namespace Astrid.Core.Bindings;

/// <summary>
/// The raw C ABI from <c>crates/astrid-ffi/include/astrid.h</c>, one declaration per function and
/// nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Kept separate from <see cref="AstridClient"/> so the unsafe part is small enough to read in one
/// sitting and the safe part has no pointers in it at all. Every rule about who owns which string
/// is in the header; this file only restates the ones a C# reader would otherwise get wrong.
/// </para>
/// <para>
/// Strings cross as UTF-8. <c>LPUTF8Str</c> rather than the default, because the default on Windows
/// is ANSI and a task title with an accent in it would arrive at the core as mojibake — silently,
/// and only for the people whose language needs it.
/// </para>
/// </remarks>
internal static partial class NativeMethods
{
    /// <summary>
    /// The library name, without extension. Resolved from the application directory, where the
    /// build drops it beside the managed assemblies.
    /// </summary>
    internal const string Library = "astrid_ffi";

    /// <summary>
    /// Called when a command finishes, or when the cache changes.
    /// </summary>
    /// <remarks>
    /// <c>json</c> is borrowed for the duration of the call: copy it before returning. It arrives
    /// on a Rust pool thread, never the UI thread.
    /// </remarks>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void AstridCallback(IntPtr userData, IntPtr json);

    [LibraryImport(Library, EntryPoint = "astrid_version")]
    internal static partial IntPtr Version();

    [LibraryImport(Library, EntryPoint = "astrid_start", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr Start(string configJson, out IntPtr errorOut);

    [LibraryImport(Library, EntryPoint = "astrid_stop")]
    internal static partial void Stop(IntPtr handle);

    [LibraryImport(Library, EntryPoint = "astrid_call", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void Call(
        IntPtr handle,
        string requestJson,
        IntPtr callback,
        IntPtr userData);

    [LibraryImport(Library, EntryPoint = "astrid_call_blocking", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr CallBlocking(IntPtr handle, string requestJson);

    [LibraryImport(Library, EntryPoint = "astrid_subscribe")]
    internal static partial void Subscribe(IntPtr handle, IntPtr callback, IntPtr userData);

    [LibraryImport(Library, EntryPoint = "astrid_free_string")]
    internal static partial void FreeString(IntPtr text);

    /// <summary>
    /// Read a UTF-8 string the core returned, and free it.
    /// </summary>
    /// <remarks>
    /// The free is not optional and not deferrable: every returned string is a Rust allocation, and
    /// forgetting one leaks a few hundred bytes per command — which, at one command per keystroke
    /// in a search box, is a leak somebody notices in an afternoon.
    /// </remarks>
    internal static string TakeString(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            return Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
        }
        finally
        {
            FreeString(pointer);
        }
    }

    /// <summary>
    /// Read a UTF-8 string the core lent us. Does not free it.
    /// </summary>
    internal static string BorrowString(IntPtr pointer) =>
        pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
}
