namespace Astrid.App;

/// <summary>
/// Whether this shell may animate: the reader's "Animation effects" setting (task c2c185cb).
/// </summary>
/// <remarks>
/// <para>
/// astrid-core's register (DS17) names Windows as a client that must honour the system's
/// reduced-motion preference. WinUI's own theme transitions already do; a Composition animation
/// this shell starts itself does not, so every one asks here first.
/// </para>
/// <para>
/// Read on each ask rather than once, because the setting can change while the app is open, and
/// it is one property read on an animation that is about to run anyway. A failure to read it
/// leaves motion on, which is what the system's own default is.
/// </para>
/// </remarks>
internal static class Motion
{
    internal static bool Allowed
    {
        get
        {
            try
            {
                return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}
