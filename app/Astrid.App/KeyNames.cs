using Windows.System;

namespace Astrid.App;

/// <summary>
/// A Windows <see cref="VirtualKey"/>, as the name the shared shortcut table uses.
/// </summary>
/// <remarks>
/// <para>
/// The table is written in web's vocabulary — <c>"n"</c>, <c>"x"</c>, <c>"ArrowDown"</c> — because
/// it is the same table on three platforms and one of them is a browser. This is the translation,
/// and it is the whole of it: everything else about a key press is decided in
/// <c>astrid_core::keyboard</c>.
/// </para>
/// <para>
/// Anything not listed returns <c>null</c> and the key is left alone. That is the important half:
/// a translation that guessed would hand the core a key it might have a binding for, and a stray
/// keystroke would delete a task.
/// </para>
/// </remarks>
internal static class KeyNames
{
    public static string? For(VirtualKey key) => key switch
    {
        // Letters, lower-cased to match the table. The shift state is deliberately not consulted:
        // the shared scheme has no shifted bindings, and treating "X" as different from "x" would
        // make caps lock silently disable half the app.
        >= VirtualKey.A and <= VirtualKey.Z => ((char)('a' + (key - VirtualKey.A))).ToString(),

        // Digits, for the priority keys.
        >= VirtualKey.Number0 and <= VirtualKey.Number9 =>
            ((char)('0' + (key - VirtualKey.Number0))).ToString(),
        >= VirtualKey.NumberPad0 and <= VirtualKey.NumberPad9 =>
            ((char)('0' + (key - VirtualKey.NumberPad0))).ToString(),

        // The core accepts these names as well as the glyphs the web table stores, so no
        // translation to arrows is needed here.
        VirtualKey.Up => "ArrowUp",
        VirtualKey.Down => "ArrowDown",
        VirtualKey.Left => "ArrowLeft",
        VirtualKey.Right => "ArrowRight",

        VirtualKey.Escape => "Escape",
        VirtualKey.Enter => "Enter",
        VirtualKey.Tab => "Tab",
        VirtualKey.Delete => "Delete",
        VirtualKey.Back => "Backspace",

        _ => null,
    };
}
