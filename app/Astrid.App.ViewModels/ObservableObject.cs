using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Astrid.App.ViewModels;

/*
 * A note that applies to every view model in this project, kept here because it is the file
 * somebody opens first.
 *
 * NOTHING HERE USES ConfigureAwait(false), and that is deliberate.
 *
 * The usual advice — a library should always continue on the pool — is exactly wrong for a view
 * model. These types own ObservableCollections that a ListView is bound to, and mutating one from
 * a thread that is not the UI thread crashes WinUI inside CoreMessagingXP with E_NOINTERFACE and
 * no managed stack. The answers arrive from a Rust pool thread, so without the captured context
 * every continuation would be on the wrong thread; letting them come back to the dispatcher is
 * what makes the collection mutations legal.
 *
 * The tests have no synchronization context, so their continuations run on the pool, which is
 * correct there and is why the crash did not show up until the app was run.
 */

/// <summary>
/// The smallest thing XAML binding needs.
/// </summary>
/// <remarks>
/// Hand-written rather than taken from a toolkit. The whole surface is one method, the shell needs
/// nothing else from an MVVM framework, and a source generator that rewrites view models is a poor
/// trade for that — it makes the code that runs different from the code that is read, which is
/// exactly what this repo spends its comments avoiding.
/// </remarks>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Set a field and raise a change if it actually changed.
    /// </summary>
    /// <returns><c>true</c> when the value moved.</returns>
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            // Raising unconditionally is how a list rebinds on every keystroke somebody types
            // somewhere else. The equality check is the difference between a smooth list and a
            // flickering one.
            return false;
        }
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name ?? string.Empty));
}
